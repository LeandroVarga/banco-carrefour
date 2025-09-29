package com.cashflowchallenge.gateway.filters;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.cloud.gateway.filter.GlobalFilter;
import org.springframework.core.Ordered;
import org.springframework.http.HttpMethod;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Component;
import org.springframework.web.server.ServerWebExchange;
import reactor.core.publisher.Mono;

@Component
public class ApiKeyWriteGuardFilter implements GlobalFilter, Ordered {
  private final String apiKey;
  public ApiKeyWriteGuardFilter(@Value("${api.key:}") String apiKey) {
    this.apiKey = apiKey;
  }

  @Override
  public Mono<Void> filter(ServerWebExchange exchange, org.springframework.cloud.gateway.filter.GatewayFilterChain chain) {
    if (apiKey == null || apiKey.isBlank()) return chain.filter(exchange);
    String path = exchange.getRequest().getPath().value();
    boolean isWrite = exchange.getRequest().getMethod() == HttpMethod.POST;
    boolean protectedPath = path.startsWith("/ledger/") || path.startsWith("/consolidator/");
    if (isWrite && protectedPath) {
      String provided = exchange.getRequest().getHeaders().getFirst("X-API-Key");
      if (provided == null || !provided.equals(apiKey)) {
        exchange.getResponse().setStatusCode(HttpStatus.FORBIDDEN);
        return exchange.getResponse().setComplete();
      }
    }
    return chain.filter(exchange);
  }

  @Override
  public int getOrder() { return -10; }
}
