package com.cashflowchallenge.ledger.api.dto;

import com.cashflowchallenge.ledger.domain.EntryType;
import com.fasterxml.jackson.annotation.JsonFormat;
import io.swagger.v3.oas.annotations.media.Schema;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;

import java.time.LocalDate;

public class LedgerEntryRequest {
  @NotNull
  @JsonFormat(pattern = "yyyy-MM-dd")
  @Schema(example = "2025-01-01")
  private LocalDate occurredOn;

  @NotNull
  private EntryType type;

  @Min(1)
  private long amountCents;

  @jakarta.validation.constraints.Size(max = 255, message = "description must be up to 255 chars")
  private String description;

  public LocalDate getOccurredOn() { return occurredOn; }
  public EntryType getType() { return type; }
  public long getAmountCents() { return amountCents; }
  public String getDescription() { return description; }
}
