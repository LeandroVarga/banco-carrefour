package com.cashflowchallenge.gateway.web;

import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.bind.support.WebExchangeBindException;
import org.springframework.web.server.ResponseStatusException;
import org.springframework.web.server.ServerWebExchange;
import org.springframework.web.server.ServerWebInputException;

import java.time.Instant;
import java.util.HashMap;
import java.util.Map;

@RestControllerAdvice
public class GlobalExceptionHandler {

  @ExceptionHandler({ ServerWebInputException.class, WebExchangeBindException.class, IllegalArgumentException.class })
  public ResponseEntity<Map<String,Object>> handleBadRequest(Exception ex, ServerWebExchange exchange) {
    return body(HttpStatus.BAD_REQUEST, message(ex), exchange);
  }

  @ExceptionHandler(ResponseStatusException.class)
  public ResponseEntity<Map<String,Object>> handleResponseStatus(ResponseStatusException ex, ServerWebExchange exchange) {
    return body(ex.getStatusCode(), ex.getReason() == null ? ex.getStatusCode().toString() : ex.getReason(), exchange);
  }

  @ExceptionHandler(Throwable.class)
  public ResponseEntity<Map<String,Object>> handleAny(Throwable ex, ServerWebExchange exchange) {
    return body(HttpStatus.INTERNAL_SERVER_ERROR, "Internal Server Error", exchange);
  }

  private static ResponseEntity<Map<String,Object>> body(org.springframework.http.HttpStatusCode status, String msg, ServerWebExchange exchange) {
    Map<String,Object> map = new HashMap<>();
    map.put("timestamp", Instant.now().toString());
    map.put("status", status.value());
    map.put("error", (msg == null || msg.isBlank()) ? status.toString() : msg);
    map.put("path", exchange.getRequest().getPath().value());
    var headers = exchange.getRequest().getHeaders();
    map.put("requestId", headers.getFirst("X-Request-Id"));
    map.put("idempotencyKey", headers.getFirst("Idempotency-Key"));
    return ResponseEntity.status(status).body(map);
  }

  private static String message(Exception ex) {
    if (ex instanceof WebExchangeBindException be && !be.getAllErrors().isEmpty()) {
      var sb = new StringBuilder("Validation failed: ");
      be.getAllErrors().forEach(err -> sb.append(err.getDefaultMessage()).append("; "));
      return sb.toString();
    }
    return ex.getMessage();
  }
}
