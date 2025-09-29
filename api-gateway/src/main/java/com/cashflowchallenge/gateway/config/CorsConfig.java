package com.cashflowchallenge.gateway.config;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.cors.CorsConfiguration;
import org.springframework.web.cors.reactive.CorsWebFilter;
import org.springframework.web.cors.reactive.UrlBasedCorsConfigurationSource;

import java.util.Arrays;
import java.util.List;

@Configuration
public class CorsConfig {
  @Bean
  public CorsWebFilter corsWebFilter(@Value("${app.cors.allowed-origins:http://localhost:3000}") String origins) {
    CorsConfiguration cfg = new CorsConfiguration();
    List<String> allowed = Arrays.stream(origins.split(",")).map(String::trim).filter(s -> !s.isBlank()).toList();
    cfg.setAllowedOrigins(allowed);
    cfg.setAllowedMethods(List.of("GET", "POST"));
    cfg.setAllowedHeaders(List.of("Content-Type", "X-API-Key", "Idempotency-Key", "X-Request-Id"));
    cfg.setExposedHeaders(List.of("X-Request-Id", "Idempotency-Key"));
    cfg.setAllowCredentials(false);
    UrlBasedCorsConfigurationSource src = new UrlBasedCorsConfigurationSource();
    src.registerCorsConfiguration("/**", cfg);
    return new CorsWebFilter(src);
  }
}
