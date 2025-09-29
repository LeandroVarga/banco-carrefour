package com.cashflowchallenge.query.infrastructure.security;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.slf4j.MDC;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;
import java.util.UUID;

@Component
public class RequestIdFilter extends OncePerRequestFilter {
  @Override
  protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
      throws ServletException, IOException {
    String rid = request.getHeader("X-Request-Id");
    if (rid == null || rid.isBlank()) rid = UUID.randomUUID().toString();
    MDC.put("requestId", rid);
    response.setHeader("X-Request-Id", rid);
    try { chain.doFilter(request, response); }
    finally { MDC.remove("requestId"); }
  }
}

