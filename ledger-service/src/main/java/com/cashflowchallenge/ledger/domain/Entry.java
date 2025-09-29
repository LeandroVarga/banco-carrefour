package com.cashflowchallenge.ledger.domain;

import jakarta.persistence.*;
import java.time.Instant;
import java.time.LocalDate;
import java.util.UUID;

@Entity
@Table(schema = "ledger", name = "entries", uniqueConstraints = {
    @UniqueConstraint(name = "uk_entries_idempotency_key", columnNames = {"idempotency_key"})
})
public class Entry {
  @Id
  @Column(nullable = false)
  private UUID id;

  @Column(name = "occurred_on", nullable = false)
  private LocalDate occurredOn;

  @Column(name = "amount_cents", nullable = false)
  private long amountCents;

  @Enumerated(EnumType.STRING)
  @Column(nullable = false, length = 10)
  private EntryType type;

  @Column(columnDefinition = "text")
  private String description;

  @Column(name = "created_at", nullable = false)
  private Instant createdAt;

  @Column(name = "idempotency_key", nullable = false, length = 64)
  private String idempotencyKey;

  protected Entry() {}

  public Entry(UUID id, LocalDate occurredOn, long amountCents, EntryType type, String description, String idempotencyKey) {
    if (amountCents <= 0) throw new IllegalArgumentException("amountCents must be positive");
    this.id = id;
    this.occurredOn = occurredOn;
    this.amountCents = amountCents;
    this.type = type;
    this.description = description;
    this.createdAt = Instant.now();
    this.idempotencyKey = idempotencyKey;
  }

  public UUID getId() { return id; }
  public LocalDate getOccurredOn() { return occurredOn; }
  public long getAmountCents() { return amountCents; }
  public EntryType getType() { return type; }
  public String getDescription() { return description; }
  public Instant getCreatedAt() { return createdAt; }
  public String getIdempotencyKey() { return idempotencyKey; }
}

