package com.cashflowchallenge.ledger.api;

import com.cashflowchallenge.ledger.LedgerApplication;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.client.TestRestTemplate;
import org.springframework.http.*;
import org.springframework.core.ParameterizedTypeReference;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;

import java.time.LocalDate;
import java.util.Map;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(classes = LedgerApplication.class, webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@ActiveProfiles("it")
public class LedgerIdempotencyIT {

  @Autowired TestRestTemplate rest;
  @Autowired JdbcTemplate jdbc;

  @Test
  void firstIsCreated_thenConflict_withSameIdempotencyKey() {
    String key = "it-idem-" + UUID.randomUUID();
    String day = LocalDate.now().toString();
    String body = "{" +
        "\"occurredOn\":\"" + day + "\"," +
        "\"amountCents\":1234," +
        "\"type\":\"CREDIT\"," +
        "\"description\":\"it\"" +
        "}";

    HttpHeaders h = new HttpHeaders();
    h.setContentType(MediaType.APPLICATION_JSON);
    h.set("Idempotency-Key", key);

    ParameterizedTypeReference<Map<String, Object>> mapType = new ParameterizedTypeReference<>() {};
    ResponseEntity<Map<String, Object>> r1 = rest.exchange("/ledger/entries", HttpMethod.POST, new HttpEntity<>(body, h), mapType);
    assertThat(r1.getStatusCode()).isIn(HttpStatus.CREATED, HttpStatus.OK); // created
    assertThat(r1.getBody()).isNotNull();
    String id = r1.getBody().get("id").toString();

    ResponseEntity<Map<String, Object>> r2 = rest.exchange("/ledger/entries", HttpMethod.POST, new HttpEntity<>(body, h), mapType);
    assertThat(r2.getStatusCode()).isEqualTo(HttpStatus.CONFLICT);
    assertThat(r2.getBody()).isNotNull();
    assertThat(r2.getBody().get("id").toString()).isEqualTo(id);

    Integer cntEntries = jdbc.queryForObject("select count(*) from ledger.entries where idempotency_key = ?", Integer.class, key);
    assertThat(cntEntries).isEqualTo(1);

    Integer cntOutbox = jdbc.queryForObject("select count(*) from ledger.outbox where payload->>'id' = ?", Integer.class, id);
    assertThat(cntOutbox).isEqualTo(1);
  }
}
