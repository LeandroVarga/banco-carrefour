package com.cashflowchallenge.ledger.infrastructure.messaging;

import org.springframework.amqp.rabbit.connection.ConnectionFactory;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class RabbitConfig {

  // Constants used by publisher/tests; topology is owned by consolidator-service.
  public static final String LEDGER_EXCHANGE = "ledger.events";
  public static final String ROUTING_KEY = "ledger.entry-recorded";
  public static final String QUEUE = "report.ledger.entry-recorded.q";
  public static final String DLX_EXCHANGE = "ledger.dlx";
  public static final String DLQ_ROUTING_KEY = "ledger.entry-recorded.dlq";
  public static final String DLQ_QUEUE = "report.ledger.entry-recorded.dlq";

  @Bean
  public RabbitTemplate rabbitTemplate(ConnectionFactory connectionFactory) {
    RabbitTemplate tpl = new RabbitTemplate(connectionFactory);
    tpl.setMandatory(true); // honor returns
    return tpl;
  }
}
