package com.cashflowchallenge.consolidator.infrastructure.messaging;

import com.cashflowchallenge.consolidator.application.BalanceApplicationService;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.amqp.rabbit.annotation.RabbitListener;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.messaging.handler.annotation.Payload;
import org.springframework.stereotype.Component;

import java.time.LocalDate;
import java.util.UUID;

@Component
public class LedgerEventConsumer {
  private static final Logger log = LoggerFactory.getLogger(LedgerEventConsumer.class);
  private final BalanceApplicationService app;
  private final ObjectMapper mapper;
  private final Counter processed;
  private final Counter duplicates;

  public LedgerEventConsumer(BalanceApplicationService app, ObjectMapper mapper, MeterRegistry registry) {
    this.app = app;
    this.mapper = mapper;
    this.processed = registry.counter("app_entries_processed_total");
    this.duplicates = registry.counter("app_entries_duplicate_total");
  }

  @RabbitListener(queues = RabbitConfig.QUEUE)
  @Transactional
  public void handle(@Payload String json) {
    try {
      JsonNode n = mapper.readTree(json);
      UUID eventId = UUID.fromString(n.get("id").asText());
      LocalDate day = LocalDate.parse(n.get("occurredOn").asText());
      long amount = n.get("amountCents").asLong();
      String type = n.get("type").asText();
      boolean applied = app.applyEvent(eventId, day,
          "CREDIT".equals(type) ? BalanceApplicationService.EntryType.CREDIT : BalanceApplicationService.EntryType.DEBIT,
          amount);
      if (applied) {
        processed.increment();
      } else {
        log.debug("Duplicate event ignored: {}", eventId);
        duplicates.increment();
      }
    } catch (Exception e) {
      log.error("Failed to process event; will retry and may be dead-lettered", e);
      throw new RuntimeException(e);
    }
  }
}
