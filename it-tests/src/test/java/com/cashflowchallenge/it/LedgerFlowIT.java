package com.cashflowchallenge.it;

import io.restassured.RestAssured;
import org.junit.jupiter.api.Test;

import java.time.LocalDate;
import java.util.concurrent.TimeUnit;

import static io.restassured.RestAssured.given;
import static org.awaitility.Awaitility.await;
import static org.hamcrest.Matchers.anyOf;
import static org.hamcrest.Matchers.greaterThanOrEqualTo;
import static org.hamcrest.Matchers.is;

public class LedgerFlowIT {
  @Test
  void endToEnd_entry_then_balance() {
    String base = System.getenv().getOrDefault("API_BASE_URL", "http://api-gateway:8080");
    RestAssured.baseURI = base;
    String day = LocalDate.now().toString();

    // Create entry via gateway -> ledger-service (through Rabbit) -> consolidator -> Postgres
    given()
      .header("Content-Type", "application/json")
      .header("Idempotency-Key", "it-demo-1")
      .body("{\"occurredOn\":\"" + day + "\",\"amountCents\":1234,\"type\":\"CREDIT\",\"description\":\"it\"}")
    .when()
      .post("/ledger/entries")
    .then()
      .statusCode(anyOf(is(200), is(201)));

    // Poll balance endpoint until the consumer processes the event
    await().atMost(30, TimeUnit.SECONDS).untilAsserted(() ->
      given()
        .when().get("/balances/daily?date=" + day)
        .then()
          .statusCode(200)
          .body("balanceCents", greaterThanOrEqualTo(1234))
    );
  }
}
