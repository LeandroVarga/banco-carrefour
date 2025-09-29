package com.cashflowchallenge.ledger.api;

import com.cashflowchallenge.ledger.api.dto.LedgerEntryRequest;
import com.cashflowchallenge.ledger.api.dto.LedgerEntryResponse;
import com.cashflowchallenge.ledger.application.RecordEntryService;
import com.cashflowchallenge.ledger.domain.Entry;
import com.cashflowchallenge.ledger.domain.EntryType;
import com.cashflowchallenge.ledger.infrastructure.repository.EntryRepository;
import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.enums.ParameterIn;
import io.swagger.v3.oas.annotations.headers.Header;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.responses.ApiResponse;
import io.swagger.v3.oas.annotations.tags.Tag;
import jakarta.validation.Valid;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.net.URI;
import java.time.LocalDate;
import java.util.List;
import java.util.UUID;

@RestController
@RequestMapping("/ledger")
@Tag(name = "Ledger API")
public class LedgerController {
  private final RecordEntryService recordEntryService;
  private final EntryRepository entryRepository;

  public LedgerController(RecordEntryService recordEntryService, EntryRepository entryRepository) {
    this.recordEntryService = recordEntryService;
    this.entryRepository = entryRepository;
  }

  @PostMapping("/entries")
  @Operation(
      summary = "Create a ledger entry with idempotency",
      parameters = {
          @Parameter(name = "X-Request-Id", in = ParameterIn.HEADER, required = false,
              description = "Optional client-supplied request id (UUID); generated if absent"),
          @Parameter(name = "Idempotency-Key", in = ParameterIn.HEADER, required = false,
              description = "Recommended for writes; echoed back if provided")
      },
      responses = {
          @ApiResponse(responseCode = "201", description = "Created",
              headers = {
                  @Header(name = "X-Request-Id", description = "Echoed/generated request id"),
                  @Header(name = "Idempotency-Key", description = "Echoed if provided")
              }),
          @ApiResponse(responseCode = "200", description = "Replayed (idempotent)",
              headers = {
                  @Header(name = "X-Request-Id", description = "Echoed/generated request id"),
                  @Header(name = "Idempotency-Key", description = "Echoed if provided")
              })
      }
  )
  public ResponseEntity<LedgerEntryResponse> create(
      @RequestHeader("Idempotency-Key") String idempotencyKey,
      @Valid @RequestBody LedgerEntryRequest req) {
    var result = recordEntryService.record(req.getOccurredOn(), req.getAmountCents(), req.getType(), req.getDescription(), idempotencyKey);
    URI location = URI.create("/ledger/entries/" + result.id);
    if (result.created) {
      return ResponseEntity.created(location).body(new LedgerEntryResponse(result.id));
    } else {
      return ResponseEntity.ok().location(location).body(new LedgerEntryResponse(result.id));
    }
  }

  @GetMapping("/entries")
  @Operation(summary = "List entries by date")
  public List<Entry> list(@RequestParam("date") LocalDate date) {
    return entryRepository.findByOccurredOn(date);
  }
}
