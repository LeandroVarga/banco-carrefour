package com.cashflowchallenge.gateway.filters;

import org.slf4j.MDC;
import org.springframework.cloud.gateway.filter.GlobalFilter;
import org.springframework.core.Ordered;
import org.springframework.http.server.reactive.ServerHttpResponse;
import org.springframework.stereotype.Component;
import org.springframework.web.server.ServerWebExchange;
import reactor.core.publisher.Mono;

import java.util.UUID;

@Component
public class RequestIdFilter implements GlobalFilter, Ordered {
  @Override
  public Mono<Void> filter(ServerWebExchange exchange, org.springframework.cloud.gateway.filter.GatewayFilterChain chain) {
    String rid = exchange.getRequest().getHeaders().getFirst("X-Request-Id");
    if (rid == null || rid.isBlank()) rid = UUID.randomUUID().toString();
    MDC.put("requestId", rid);
    ServerHttpResponse response = exchange.getResponse();
    response.getHeaders().set("X-Request-Id", rid);
    // Propagate Idempotency-Key if present (for downstream logs)
    String idem = exchange.getRequest().getHeaders().getFirst("Idempotency-Key");
    if (idem != null && !idem.isBlank()) {
      response.getHeaders().set("Idempotency-Key", idem);
    }
    return chain.filter(exchange).doFinally(s -> MDC.remove("requestId"));
  }

  @Override
  public int getOrder() { return -100; }
}
