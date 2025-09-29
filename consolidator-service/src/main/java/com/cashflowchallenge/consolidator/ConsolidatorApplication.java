package com.cashflowchallenge.consolidator;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.amqp.rabbit.annotation.EnableRabbit;

@SpringBootApplication
@EnableRabbit
public class ConsolidatorApplication {
  public static void main(String[] args) {
    SpringApplication.run(ConsolidatorApplication.class, args);
  }
}
