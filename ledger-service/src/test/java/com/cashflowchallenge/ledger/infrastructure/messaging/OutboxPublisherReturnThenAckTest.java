package com.cashflowchallenge.ledger.infrastructure.messaging;

import com.cashflowchallenge.ledger.domain.OutboxEvent;
import com.cashflowchallenge.ledger.infrastructure.repository.OutboxRepository;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.core.ReturnedMessage;
import org.springframework.amqp.rabbit.connection.CorrelationData;
import org.springframework.amqp.rabbit.core.RabbitTemplate;

import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.*;

public class OutboxPublisherReturnThenAckTest {

  @Test
  void returnThenAck_doesNotDelete_thenNextAck_deletes() {
    RabbitTemplate template = mock(RabbitTemplate.class);
    OutboxRepository repo = mock(OutboxRepository.class);
    var registry = new SimpleMeterRegistry();

    OutboxPublisher publisher = new OutboxPublisher(template, repo, registry);
    // capture ReturnsCallback registered in @PostConstruct
    var returnsCbCaptor = ArgumentCaptor.forClass(RabbitTemplate.ReturnsCallback.class);
    doAnswer(inv -> { return null; })
        .when(template).setReturnsCallback(returnsCbCaptor.capture());
    publisher.initCallbacks();

    // Prepare first event
    UUID id1 = UUID.randomUUID();
    OutboxEvent evt1 = new OutboxEvent(id1, "Entry", "ledger.entry-recorded", "{\"k\":1}");

    // capture message & correlation for first send
    ArgumentCaptor<Message> msgCaptor = ArgumentCaptor.forClass(Message.class);
    ArgumentCaptor<CorrelationData> cdCaptor = ArgumentCaptor.forClass(CorrelationData.class);
    doAnswer(inv -> null)
        .when(template)
        .convertAndSend(eq(RabbitConfig.LEDGER_EXCHANGE), eq(RabbitConfig.ROUTING_KEY), msgCaptor.capture(), cdCaptor.capture());

    // --- First publish: simulate RETURN then ACK ---
    publisher.publishAndConfirm(evt1);

    Message sent1 = msgCaptor.getAllValues().get(0);
    CorrelationData cd1 = cdCaptor.getAllValues().get(0);

    // simulate broker RETURN (unroutable) using the same message
    ReturnedMessage rm = new ReturnedMessage(
        sent1, 312, "NO_ROUTE", RabbitConfig.LEDGER_EXCHANGE, RabbitConfig.ROUTING_KEY);
    returnsCbCaptor.getValue().returnedMessage(rm);

    // simulate broker ACK (true)
    cd1.getFuture().complete(new CorrelationData.Confirm(true, null));

    // verify: NOT deleted due to prior RETURN
    verify(repo, never()).deleteById(id1);

    // metrics after first attempt
    assertThat(registry.counter("outbox_returned_total").count()).isEqualTo(1.0d);
    assertThat(registry.counter("outbox_published_total").count()).isEqualTo(0.0d);
    assertThat(registry.counter("outbox_nacked_total").count()).isEqualTo(1.0d);

    // --- Second publish: clean path (ACK only) ---
    reset(template);
    msgCaptor = ArgumentCaptor.forClass(Message.class);
    cdCaptor  = ArgumentCaptor.forClass(CorrelationData.class);
    doAnswer(inv -> null)
        .when(template)
        .convertAndSend(eq(RabbitConfig.LEDGER_EXCHANGE), eq(RabbitConfig.ROUTING_KEY), msgCaptor.capture(), cdCaptor.capture());

    UUID id2 = UUID.randomUUID();
    OutboxEvent evt2 = new OutboxEvent(id2, "Entry", "ledger.entry-recorded", "{\"k\":2}");
    publisher.publishAndConfirm(evt2);

    CorrelationData cd2 = cdCaptor.getAllValues().get(0);
    cd2.getFuture().complete(new CorrelationData.Confirm(true, null));

    // verify: deleted on success
    verify(repo).deleteById(id2);
    assertThat(registry.counter("outbox_published_total").count()).isEqualTo(1.0d);
  }
}
