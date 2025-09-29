package com.cashflowchallenge.gateway.filters;

import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.cloud.gateway.filter.GlobalFilter;
import org.springframework.core.Ordered;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Component;
import org.springframework.web.server.ServerWebExchange;
import reactor.core.publisher.Mono;

import java.time.Instant;
import java.util.Arrays;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

@Component
public class RateLimitFilter implements GlobalFilter, Ordered {
  private final int limitPerSecond;
  private final String[] pathPrefixes;
  private final Map<String, AtomicInteger> counters = new ConcurrentHashMap<>();
  private volatile long windowEpochSecond = Instant.now().getEpochSecond();

  private final MeterRegistry registry;

  public RateLimitFilter(MeterRegistry registry,
                         @Value("${gateway.rps.limit:50}") int limitPerSecond,
                         @Value("${gateway.rps.paths:/balances/*,/ledger/*}") String paths) {
    this.registry = registry;
    this.limitPerSecond = limitPerSecond;
    this.pathPrefixes = Arrays.stream(paths.split(","))
        .map(String::trim)
        .filter(s -> !s.isBlank())
        .map(s -> s.replace("*", ""))
        .map(s -> s.endsWith("/") ? s : s + "/")
        .toArray(String[]::new);
    for (String p : this.pathPrefixes) counters.put(p, new AtomicInteger(0));
  }

  @Override
  public Mono<Void> filter(ServerWebExchange exchange, org.springframework.cloud.gateway.filter.GatewayFilterChain chain) {
    String path = exchange.getRequest().getPath().value();
    String key = matchPrefix(path);
    if (key == null) return chain.filter(exchange);
    long nowSec = Instant.now().getEpochSecond();
    if (nowSec != windowEpochSecond) {
      windowEpochSecond = nowSec;
      counters.values().forEach(c -> c.set(0));
    }
    int current = counters.get(key).incrementAndGet();
    if (current > limitPerSecond) {
      Counter.builder("gateway_ratelimit_rejected_total")
          .tag("path", key)
          .tag("application", "api-gateway")
          .register(registry)
          .increment();
      exchange.getResponse().setStatusCode(HttpStatus.TOO_MANY_REQUESTS);
      exchange.getResponse().getHeaders().set("Retry-After", "1");
      return exchange.getResponse().setComplete();
    }
    Counter.builder("gateway_ratelimit_allowed_total")
        .tag("path", key)
        .tag("application", "api-gateway")
        .register(registry)
        .increment();
    return chain.filter(exchange);
  }

  private String matchPrefix(String path) {
    for (String p : pathPrefixes) {
      if (path.startsWith(p)) return p;
    }
    return null;
  }

  @Override
  public int getOrder() { return -50; }
}
