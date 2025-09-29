package com.cashflowchallenge.consolidator.infrastructure.web;

import java.time.Instant;
import java.util.HashMap;
import java.util.Map;

import jakarta.servlet.http.HttpServletRequest;
import jakarta.validation.ConstraintViolationException;

import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.validation.BindException;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.HttpMediaTypeNotSupportedException;
import org.springframework.web.HttpRequestMethodNotSupportedException;
import org.springframework.web.bind.MissingRequestHeaderException;
import org.springframework.web.bind.MissingServletRequestParameterException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

@RestControllerAdvice
public class GlobalExceptionHandler {

  @ExceptionHandler({
      MethodArgumentNotValidException.class,
      BindException.class,
      ConstraintViolationException.class,
      MissingServletRequestParameterException.class,
      MissingRequestHeaderException.class,
      IllegalArgumentException.class
  })
  public ResponseEntity<Map<String, Object>> handleBadRequest(Exception ex, HttpServletRequest request) {
    return body(HttpStatus.BAD_REQUEST, message(ex), request);
  }


  @ExceptionHandler({
      HttpRequestMethodNotSupportedException.class,
      HttpMediaTypeNotSupportedException.class
  })
  public ResponseEntity<Map<String, Object>> handleMethodOrMedia(Exception ex, HttpServletRequest request) {
    return body(HttpStatus.BAD_REQUEST, message(ex), request);
  }

  @ExceptionHandler(Throwable.class)
  public ResponseEntity<Map<String, Object>> handleAny(Throwable ex, HttpServletRequest request) {
    return body(HttpStatus.INTERNAL_SERVER_ERROR, "Internal Server Error", request);
  }

  private static ResponseEntity<Map<String, Object>> body(HttpStatus status, String msg, HttpServletRequest request) {
    Map<String, Object> map = new HashMap<>();
    map.put("timestamp", Instant.now().toString());
    map.put("status", status.value());
    map.put("error", (msg == null || msg.isBlank()) ? status.getReasonPhrase() : msg);
    map.put("path", request.getRequestURI());
    map.put("requestId", request.getHeader("X-Request-Id"));
    map.put("idempotencyKey", request.getHeader("Idempotency-Key"));
    return ResponseEntity.status(status).body(map);
  }

  private static String message(Exception ex) {
    if (ex instanceof MethodArgumentNotValidException manv && manv.getBindingResult() != null) {
      var sb = new StringBuilder("Validation failed: ");
      manv.getBindingResult().getAllErrors().forEach(err -> sb.append(err.getDefaultMessage()).append("; "));
      return sb.toString();
    }
    if (ex instanceof BindException be && be.getBindingResult() != null) {
      var sb = new StringBuilder("Validation failed: ");
      be.getBindingResult().getAllErrors().forEach(err -> sb.append(err.getDefaultMessage()).append("; "));
      return sb.toString();
    }
    if (ex instanceof ConstraintViolationException cve && !cve.getConstraintViolations().isEmpty()) {
      var sb = new StringBuilder("Validation failed: ");
      cve.getConstraintViolations().forEach(v -> sb.append(v.getMessage()).append("; "));
      return sb.toString();
    }
    return ex.getMessage();
  }
}
