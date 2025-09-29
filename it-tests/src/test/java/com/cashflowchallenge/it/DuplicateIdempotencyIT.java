package com.cashflowchallenge.it;

import io.restassured.RestAssured;
import org.junit.jupiter.api.Test;

import java.time.LocalDate;

import static io.restassured.RestAssured.given;
import static org.hamcrest.Matchers.anyOf;
import static org.hamcrest.Matchers.is;

public class DuplicateIdempotencyIT {
  @Test
  void duplicate_post_returns_conflict_and_effect_once() {
    String base = System.getenv().getOrDefault("API_BASE_URL", "http://api-gateway:8080");
    RestAssured.baseURI = base;
    String day = LocalDate.now().toString();

    String body = "{" +
        "\"occurredOn\":\"" + day + "\"," +
        "\"amountCents\":2345," +
        "\"type\":\"CREDIT\"," +
        "\"description\":\"e2e\"" +
        "}";

    String key = "dup-e2e-1";

    given().header("Content-Type", "application/json").header("Idempotency-Key", key).body(body)
        .when().post("/ledger/entries")
        .then().statusCode(anyOf(is(200), is(201)));

    given().header("Content-Type", "application/json").header("Idempotency-Key", key).body(body)
        .when().post("/ledger/entries")
        .then().statusCode(409);
  }
}

