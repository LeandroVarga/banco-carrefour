package com.cashflowchallenge.ledger.infrastructure.security;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.HttpMethod;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

import java.io.IOException;

@Component
public class ApiKeyFilter extends OncePerRequestFilter {
  private final String apiKey;

  public ApiKeyFilter(@Value("${api.key:}") String apiKey) {
    this.apiKey = apiKey;
  }

  @Override
  protected boolean shouldNotFilter(HttpServletRequest request) {
    // Only enforce on write endpoints if API key is configured
    if (apiKey == null || apiKey.isBlank()) return true;
    String path = request.getRequestURI();
    boolean isWrite = request.getMethod().equals(HttpMethod.POST.name());
    return !isWrite || !path.startsWith("/ledger");
  }

  @Override
  protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain filterChain)
      throws ServletException, IOException {
    String provided = request.getHeader("X-API-Key");
    if (provided == null || !provided.equals(apiKey)) {
      response.setStatus(HttpServletResponse.SC_UNAUTHORIZED);
      return;
    }
    filterChain.doFilter(request, response);
  }
}

