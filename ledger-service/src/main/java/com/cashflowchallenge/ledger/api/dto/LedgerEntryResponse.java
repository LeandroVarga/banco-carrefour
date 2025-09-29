package com.cashflowchallenge.ledger.api.dto;

import java.util.UUID;

public class LedgerEntryResponse {
  public UUID id;
  public LedgerEntryResponse(UUID id) { this.id = id; }
}

