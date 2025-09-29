package com.cashflowchallenge.ledger.infrastructure.messaging;

import com.cashflowchallenge.ledger.domain.OutboxEvent;
import com.cashflowchallenge.ledger.infrastructure.repository.OutboxRepository;
import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.amqp.rabbit.connection.CorrelationData;

import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.*;

public class OutboxPublisherCorrelationIdTest {

  @Test
  void setsCorrelationIdOnMessage() {
    RabbitTemplate template = mock(RabbitTemplate.class);
    OutboxRepository repo = mock(OutboxRepository.class);
    var registry = new SimpleMeterRegistry();

    OutboxPublisher publisher = new OutboxPublisher(template, repo, registry);
    // We don't need returns callback for this test

    UUID id = UUID.randomUUID();
    OutboxEvent evt = new OutboxEvent(id, "Entry", "ledger.entry-recorded", "{\"k\":1}");

    // Capture message argument
    ArgumentCaptor<Message> msgCaptor = ArgumentCaptor.forClass(Message.class);

    doAnswer(inv -> null)
        .when(template)
        .convertAndSend(eq(RabbitConfig.LEDGER_EXCHANGE), eq(RabbitConfig.ROUTING_KEY), msgCaptor.capture(), any(CorrelationData.class));

    publisher.publishAndConfirm(evt);

    Message sent = msgCaptor.getValue();
    assertThat(sent).isNotNull();
    assertThat(sent.getMessageProperties().getCorrelationId()).isEqualTo(id.toString());
  }
}
