package com.cashflowchallenge.ledger.domain;

public enum EntryType {
  CREDIT,
  DEBIT;

  public long signedAmount(long amountCents) {
    if (amountCents <= 0) throw new IllegalArgumentException("amountCents must be positive");
    return this == CREDIT ? amountCents : -amountCents;
  }
}

