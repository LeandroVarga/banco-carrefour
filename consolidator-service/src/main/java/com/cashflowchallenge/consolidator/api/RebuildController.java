package com.cashflowchallenge.consolidator.api;

import com.cashflowchallenge.consolidator.application.RebuildService;
import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.headers.Header;
import io.swagger.v3.oas.annotations.responses.ApiResponse;
import io.swagger.v3.oas.annotations.tags.Tag;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.time.LocalDate;
import java.time.temporal.ChronoUnit;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

@RestController
@RequestMapping("/consolidator")
@Tag(name = "Consolidator Admin")
public class RebuildController {
  private final RebuildService rebuildService;
  private final ConcurrentHashMap<String, String> jobs = new ConcurrentHashMap<>();

  public RebuildController(RebuildService rebuildService) { this.rebuildService = rebuildService; }

  @PostMapping("/rebuild")
  @Operation(summary = "Backfill balances for range [from,to]",
      responses = {
          @ApiResponse(responseCode = "202",
              headers = {
                  @Header(name = "X-Request-Id", description = "Echoed/generated request id"),
                  @Header(name = "Idempotency-Key", description = "Echoed if provided on request")
              })
      })
  public ResponseEntity<Map<String, Object>> rebuild(@RequestParam("from") LocalDate from,
                                                     @RequestParam("to") LocalDate to) {
    if (from.isAfter(to)) {
      return ResponseEntity.badRequest().body(Map.of("error", "from must be <= to"));
    }
    long span = ChronoUnit.DAYS.between(from, to) + 1;
    if (span > 366) {
      return ResponseEntity.badRequest().body(Map.of("error", "max range is 366 days"));
    }
    String jobId = UUID.randomUUID().toString();
    jobs.put(jobId, "PENDING");
    new Thread(() -> {
      try {
        jobs.put(jobId, "RUNNING");
        rebuildService.rebuildRange(from, to);
        jobs.put(jobId, "DONE");
      } catch (Exception e) {
        jobs.put(jobId, "FAILED:" + e.getMessage());
      }
    }, "rebuild-" + jobId).start();
    return ResponseEntity.status(HttpStatus.ACCEPTED).body(Map.of("jobId", jobId));
  }

  @GetMapping("/rebuild/status/{jobId}")
  @Operation(summary = "Get rebuild job status",
      responses = {
          @ApiResponse(responseCode = "200",
              headers = {
                  @Header(name = "X-Request-Id", description = "Echoed/generated request id"),
                  @Header(name = "Idempotency-Key", description = "Echoed if provided on request")
              })
      })
  public Map<String, Object> status(@PathVariable String jobId) {
    return Map.of("jobId", jobId, "status", jobs.getOrDefault(jobId, "UNKNOWN"));
  }
}
