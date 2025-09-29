package com.cashflowchallenge.ledger.infrastructure.messaging;

import com.cashflowchallenge.ledger.domain.OutboxEvent;
import com.cashflowchallenge.ledger.infrastructure.repository.OutboxRepository;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import jakarta.annotation.PostConstruct;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.core.MessageProperties;
import org.springframework.amqp.rabbit.connection.CorrelationData;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.stereotype.Component;

import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentMap;
import org.slf4j.MDC;

import static com.cashflowchallenge.ledger.infrastructure.messaging.RabbitConfig.LEDGER_EXCHANGE;
import static com.cashflowchallenge.ledger.infrastructure.messaging.RabbitConfig.ROUTING_KEY;

@Component
public class OutboxPublisher {
  private static final Logger log = LoggerFactory.getLogger(OutboxPublisher.class);

  private final RabbitTemplate rabbitTemplate;
  private final OutboxRepository outboxRepository;
  private final Counter published;
  private final Counter returned;
  private final Counter nacked;
  private final Counter failed;
  // Track if a basic.return was observed for a given correlation id
  private final ConcurrentMap<String, Boolean> returnsSeen = new ConcurrentHashMap<>();

  public OutboxPublisher(RabbitTemplate rabbitTemplate, OutboxRepository outboxRepository, MeterRegistry registry) {
    this.rabbitTemplate = rabbitTemplate;
    this.outboxRepository = outboxRepository;
    this.published = registry.counter("outbox_published_total");
    this.returned = registry.counter("outbox_returned_total");
    this.nacked = registry.counter("outbox_nacked_total");
    this.failed = registry.counter("outbox_publish_failed_total");
  }

  @PostConstruct
  void initCallbacks() {
    rabbitTemplate.setReturnsCallback(ret -> {
      returned.increment();
      failed.increment();
      String corrId = ret.getMessage().getMessageProperties().getCorrelationId();
      if (corrId != null) returnsSeen.put(corrId, Boolean.TRUE);
      log.warn("Rabbit RETURNED (unroutable): replyCode={}, replyText={}, exchange={}, routingKey={}, corrId={}, message={}",
          ret.getReplyCode(), ret.getReplyText(), ret.getExchange(), ret.getRoutingKey(), corrId,
          new String(ret.getMessage().getBody(), StandardCharsets.UTF_8));
      // Keep outbox row; deletion is decided in the confirm callback
    });
    rabbitTemplate.setConfirmCallback((correlationData, ack, cause) -> {
      String corrId = correlationData != null ? correlationData.getId() : null;
      if (!ack) {
        nacked.increment();
        log.warn("Confirm NACK corrId={}, cause={}", corrId, cause);
      }
    });
  }

  // Synchronous publish with confirms. Returns true on ACK (and not returned), false otherwise
  public boolean publishAndConfirm(OutboxEvent evt) {
    UUID id = evt.getId();
    String idStr = id.toString();
    String payload = evt.getPayload();
    MessageProperties props = jsonProps();
    props.setCorrelationId(idStr);
    String rid = MDC.get("requestId");
    if (rid != null && !rid.isBlank()) {
      props.setHeader("X-Request-Id", rid);
    }
    props.setHeader("X-Event-Version", 1);
    props.setHeader("Idempotency-Key", idStr);
    Message msg = new Message(payload.getBytes(StandardCharsets.UTF_8), props);
    CorrelationData cd = new CorrelationData(idStr);
    try {
      // Avoid false positives from previous attempts: clear any stale RETURN flag
      returnsSeen.remove(idStr);
      rabbitTemplate.convertAndSend(LEDGER_EXCHANGE, ROUTING_KEY, msg, cd);
      CorrelationData.Confirm confirm = cd.getFuture().get();
      if (confirm != null && confirm.isAck()) {
        boolean wasReturned = Boolean.TRUE.equals(returnsSeen.remove(idStr));
        if (wasReturned) {
          nacked.increment();
          log.warn("ACK received but message was RETURNED earlier: {}", idStr);
          outboxRepository.markFailed(id, "returned earlier");
          return false;
        }
        published.increment();
        log.debug("Outbox ACK: {}", idStr);
        return true;
      } else {
        nacked.increment();
        log.warn("Outbox NACK: {}", idStr);
        outboxRepository.markFailed(id, "nack");
        failed.increment();
        return false;
      }
    } catch (Exception e) {
      nacked.increment();
      log.error("Outbox send threw: {}", idStr, e);
      outboxRepository.markFailed(id, e.getMessage());
      failed.increment();
      return false;
    }
  }

  // Backward-compat shim for legacy tests calling publishOne(...)
  public void publishOne(OutboxEvent event) {
    publishAndConfirm(event);
  }

  private static MessageProperties jsonProps() {
    MessageProperties p = new MessageProperties();
    p.setContentType(MessageProperties.CONTENT_TYPE_JSON);
    p.setContentEncoding(StandardCharsets.UTF_8.name());
    return p;
  }
}
