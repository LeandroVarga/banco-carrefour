package com.cashflowchallenge.gateway.web;

import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RestController;

import java.util.Map;

@RestController
public class FallbackController {
  @GetMapping("/fallback/{svc}")
  public Map<String,Object> fb(@PathVariable String svc) {
    return Map.of("service", svc, "status", "degraded");
  }
}

