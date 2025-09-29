package com.cashflowchallenge.consolidator.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;
import java.time.LocalDate;

@Entity
@Table(schema = "report", name = "daily_balances")
public class DailyBalance {
  @Id
  @Column(name = "day", nullable = false)
  private LocalDate day;

  @Column(name = "balance_cents", nullable = false)
  private long balanceCents;

  @Column(name = "updated_at", nullable = false)
  private Instant updatedAt;

  protected DailyBalance() {}

  public DailyBalance(LocalDate day, long balanceCents) {
    this.day = day;
    this.balanceCents = balanceCents;
    this.updatedAt = Instant.now();
  }

  public LocalDate getDay() { return day; }
  public long getBalanceCents() { return balanceCents; }
  public Instant getUpdatedAt() { return updatedAt; }
}

