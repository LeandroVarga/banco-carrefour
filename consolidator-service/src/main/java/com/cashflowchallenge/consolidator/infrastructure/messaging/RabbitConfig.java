package com.cashflowchallenge.consolidator.infrastructure.messaging;

import org.springframework.amqp.core.*;
import org.springframework.amqp.rabbit.config.SimpleRabbitListenerContainerFactory;
import org.springframework.amqp.rabbit.connection.ConnectionFactory;
import org.springframework.amqp.rabbit.retry.RejectAndDontRequeueRecoverer;
import org.springframework.amqp.rabbit.config.RetryInterceptorBuilder;
import org.springframework.amqp.core.AcknowledgeMode;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class RabbitConfig {

  public static final String LEDGER_EXCHANGE = "ledger.events";
  public static final String ROUTING_KEY = "ledger.entry-recorded";

  public static final String QUEUE = "report.ledger.entry-recorded.q";

  // DLQ setup
  public static final String DLX_EXCHANGE = "ledger.dlx";
  public static final String DLQ_ROUTING_KEY = "ledger.entry-recorded.dlq";
  public static final String DLQ_QUEUE = "report.ledger.entry-recorded.dlq";

  @Bean TopicExchange ledgerExchange() { return new TopicExchange(LEDGER_EXCHANGE, true, false); }
  @Bean TopicExchange deadLetterExchange() { return new TopicExchange(DLX_EXCHANGE, true, false); }

  @Bean
  Queue queue() {
    return QueueBuilder.durable(QUEUE)
        .withArgument("x-dead-letter-exchange", DLX_EXCHANGE)
        .withArgument("x-dead-letter-routing-key", DLQ_ROUTING_KEY)
        .withArgument("x-queue-type", "quorum")
        .withArgument("x-quorum-initial-group-size", 1)
        .build();
  }

  @Bean Queue dlq() { return QueueBuilder.durable(DLQ_QUEUE)
      .withArgument("x-queue-type", "quorum")
      .withArgument("x-quorum-initial-group-size", 1)
      .withArgument("x-message-ttl", 86400000)
      .withArgument("x-overflow", "reject-publish")
      .build(); }

  @Bean
  Binding binding(Queue queue, TopicExchange ledgerExchange) {
    return BindingBuilder.bind(queue).to(ledgerExchange).with(ROUTING_KEY);
  }

  @Bean
  Binding dlqBinding(Queue dlq, TopicExchange deadLetterExchange) {
    return BindingBuilder.bind(dlq).to(deadLetterExchange).with(DLQ_ROUTING_KEY);
  }

  /**
   * Group the topology into a single Declarables bean for explicit ownership
   * by consolidator-service (admin auto-startup declares at application start).
   */
  @Bean
  public Declarables ledgerTopology(
      @Qualifier("ledgerExchange") TopicExchange ledgerExchange,
      @Qualifier("deadLetterExchange") TopicExchange deadLetterExchange,
      @Qualifier("queue") Queue entryRecordedQueue,
      @Qualifier("dlq") Queue entryRecordedDlq,
      @Qualifier("binding") Binding entryRecordedBinding,
      @Qualifier("dlqBinding") Binding entryRecordedDlqBinding) {
    return new Declarables(
        ledgerExchange,
        deadLetterExchange,
        entryRecordedQueue,
        entryRecordedDlq,
        entryRecordedBinding,
        entryRecordedDlqBinding);
  }

  /**
   * Configure listener retry with exponential backoff and no infinite requeue.
   * After max attempts the message goes to DLQ via RejectAndDontRequeueRecoverer.
   */
  @Bean
  SimpleRabbitListenerContainerFactory rabbitListenerContainerFactory(ConnectionFactory cf) {
    SimpleRabbitListenerContainerFactory f = new SimpleRabbitListenerContainerFactory();
    f.setConnectionFactory(cf);
    f.setDefaultRequeueRejected(false); // hand control to DLQ after retries
    f.setAcknowledgeMode(AcknowledgeMode.AUTO);
    f.setPrefetchCount(100);
    f.setConcurrentConsumers(2);
    f.setMaxConcurrentConsumers(8);
    f.setAdviceChain(
        RetryInterceptorBuilder.stateless()
            .maxAttempts(5)
            .backOffOptions(200, 2.0, 5000) // 200ms -> 400 -> 800 -> 1600 -> 3200 .. capped 5s
            .recoverer(new RejectAndDontRequeueRecoverer())
            .build());
    return f;
  }
}
