package com.cashflowchallenge.consolidator.it;

import com.cashflowchallenge.consolidator.ConsolidatorApplication;
import com.cashflowchallenge.consolidator.infrastructure.messaging.LedgerEventConsumer;
import io.micrometer.core.instrument.MeterRegistry;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;

import java.time.LocalDate;
import java.util.Map;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(classes = ConsolidatorApplication.class)
@ActiveProfiles("it")
public class DoubleDeliveryIT {

  @Autowired LedgerEventConsumer consumer;
  @Autowired JdbcTemplate jdbc;
  @Autowired MeterRegistry registry;

  @Test
  void sameEventTwice_updatesOnce_andCountsDuplicate() throws Exception {
    UUID eventId = UUID.randomUUID();
    LocalDate day = LocalDate.now();
    long amount = 777;
    String eventJson = "{" +
        "\"id\":\"" + eventId + "\"," +
        "\"occurredOn\":\"" + day + "\"," +
        "\"amountCents\":" + amount + "," +
        "\"type\":\"CREDIT\"," +
        "\"description\":\"it\"" +
        "}";

    jdbc.update("DELETE FROM report.processed_events WHERE id = ?", eventId);
    jdbc.update("DELETE FROM report.daily_balances WHERE day = ?", day);

    double dupBefore = registry.counter("app_entries_duplicate_total").count();

    consumer.handle(eventJson);
    consumer.handle(eventJson);

    Long bal = jdbc.queryForObject("SELECT balance_cents FROM report.daily_balances WHERE day = ?", Long.class, day);
    assertThat(bal).isEqualTo(amount);

    double dupAfter = registry.counter("app_entries_duplicate_total").count();
    assertThat(dupAfter).isEqualTo(dupBefore + 1.0d);
  }
}

