package com.cashflowchallenge.ledger.domain;

import jakarta.persistence.*;
import org.hibernate.annotations.JdbcTypeCode;
import org.hibernate.type.SqlTypes;
import java.time.Instant;
import java.util.UUID;

@Entity
@Table(schema = "ledger", name = "outbox")
public class OutboxEvent {
  @Id
  @Column(nullable = false)
  private UUID id;

  @Column(nullable = false, length = 32)
  private String aggregate;

  @Column(name = "event_type", nullable = false, length = 64)
  private String eventType;

  @Column(columnDefinition = "jsonb", nullable = false)
  @JdbcTypeCode(SqlTypes.JSON)
  private String payload;

  @Column(name = "created_at", nullable = false)
  private Instant createdAt;

  @Column(name = "published_at")
  private Instant publishedAt;

  @Column(name = "attempts", nullable = false)
  private int attempts = 0;

  @Column(name = "last_error")
  private String lastError;

  @Column(name = "updated_at", nullable = false)
  private Instant updatedAt;

  protected OutboxEvent() {}

  public OutboxEvent(UUID id, String aggregate, String eventType, String payload) {
    this.id = id;
    this.aggregate = aggregate;
    this.eventType = eventType;
    this.payload = payload;
    this.createdAt = Instant.now();
    this.updatedAt = this.createdAt;
  }

  public UUID getId() { return id; }
  public String getAggregate() { return aggregate; }
  public String getEventType() { return eventType; }
  public String getPayload() { return payload; }
  public Instant getCreatedAt() { return createdAt; }
  public Instant getPublishedAt() { return publishedAt; }
  public int getAttempts() { return attempts; }
  public String getLastError() { return lastError; }
  public Instant getUpdatedAt() { return updatedAt; }
  public void markPublished() { this.publishedAt = Instant.now(); }
  public boolean isPublished() { return publishedAt != null; }
}
