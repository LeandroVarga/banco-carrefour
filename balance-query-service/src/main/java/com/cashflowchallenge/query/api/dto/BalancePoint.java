package com.cashflowchallenge.query.api.dto;

public class BalancePoint {
  public final String day;
  public final long balanceCents;
  public BalancePoint(String day, long balanceCents) {
    this.day = day;
    this.balanceCents = balanceCents;
  }
}

