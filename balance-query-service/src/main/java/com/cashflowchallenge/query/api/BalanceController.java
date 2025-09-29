package com.cashflowchallenge.query.api;

import com.cashflowchallenge.query.api.dto.BalancePoint;
import com.cashflowchallenge.query.domain.DailyBalance;
import com.cashflowchallenge.query.infrastructure.repository.DailyBalanceRepository;
import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.headers.Header;
import io.swagger.v3.oas.annotations.responses.ApiResponse;
import io.swagger.v3.oas.annotations.tags.Tag;
import org.springframework.http.CacheControl;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

import java.time.LocalDate;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@RestController
@RequestMapping("/balances")
@Tag(name = "Balance Query API")
public class BalanceController {
  private final DailyBalanceRepository repo;
  public BalanceController(DailyBalanceRepository repo) { this.repo = repo; }

  @GetMapping("/daily")
  @Operation(summary = "Get balance for a specific date",
      responses = {
          @ApiResponse(responseCode = "200",
              headers = {
                  @Header(name = "X-Request-Id", description = "Echoed/generated request id"),
                  @Header(name = "Idempotency-Key", description = "Echoed if provided on request")
              })
      })
  public ResponseEntity<Map<String, Object>> daily(@RequestParam("date") LocalDate date) {
    Map<String, Object> body = repo.findById(date)
        .map(db -> Map.<String,Object>of("day", db.getDay().toString(), "balanceCents", db.getBalanceCents()))
        .orElseGet(() -> {
          Map<String, Object> m = new HashMap<>();
          m.put("day", date.toString());
          m.put("balanceCents", 0);
          return m;
        });
    return ResponseEntity.ok().cacheControl(CacheControl.maxAge(java.time.Duration.ofSeconds(30)).cachePublic()).body(body);
  }

  @GetMapping("/range")
  @Operation(summary = "Get balances for a date range [from,to]",
      responses = {
          @ApiResponse(responseCode = "200",
              headers = {
                  @Header(name = "X-Request-Id", description = "Echoed/generated request id"),
                  @Header(name = "Idempotency-Key", description = "Echoed if provided on request")
              })
      })
  public ResponseEntity<List<BalancePoint>> range(@RequestParam("from") LocalDate from,
                                       @RequestParam("to") LocalDate to,
                                       @RequestParam(value = "page", required = false, defaultValue = "0") int page,
                                       @RequestParam(value = "size", required = false, defaultValue = "366") int size) {
    if (from.isAfter(to)) {
      return ResponseEntity.badRequest().body(List.of());
    }
    long span = java.time.temporal.ChronoUnit.DAYS.between(from, to) + 1;
    if (span > 366) {
      return ResponseEntity.badRequest().body(List.of());
    }
    List<DailyBalance> all = repo.findRange(from, to);
    int fromIndex = Math.max(0, Math.min(all.size(), page * size));
    int toIndex = Math.max(fromIndex, Math.min(all.size(), fromIndex + size));
    List<BalancePoint> pts = all.subList(fromIndex, toIndex).stream()
        .map(db -> new BalancePoint(db.getDay().toString(), db.getBalanceCents()))
        .toList();
    return ResponseEntity.ok().cacheControl(CacheControl.maxAge(java.time.Duration.ofSeconds(30)).cachePublic()).body(pts);
  }
}
