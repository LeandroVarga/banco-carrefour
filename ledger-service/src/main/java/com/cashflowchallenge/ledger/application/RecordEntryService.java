package com.cashflowchallenge.ledger.application;

import com.cashflowchallenge.ledger.domain.EntryType;
import com.cashflowchallenge.ledger.domain.OutboxEvent;
import com.cashflowchallenge.ledger.infrastructure.repository.OutboxRepository;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDate;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

@Service
public class RecordEntryService {
  private final NamedParameterJdbcTemplate jdbc;
  private final OutboxRepository outboxRepository;
  private final ObjectMapper objectMapper;
  private final Counter createdCounter;
  private final Counter conflictCounter;

  public RecordEntryService(NamedParameterJdbcTemplate jdbc, OutboxRepository outboxRepository, ObjectMapper objectMapper, MeterRegistry registry) {
    this.jdbc = jdbc;
    this.outboxRepository = outboxRepository;
    this.objectMapper = objectMapper;
    this.createdCounter = registry.counter("entries_created_total");
    this.conflictCounter = registry.counter("entries_conflict_total");
  }

  public static class Result {
    public final UUID id;
    public final boolean created;
    public Result(UUID id, boolean created) { this.id = id; this.created = created; }
  }

  @Transactional
  public Result record(LocalDate occurredOn, long amountCents, EntryType type, String description, String idempotencyKey) {
    UUID id = UUID.randomUUID();
    String sql = """
      INSERT INTO ledger.entries (id, occurred_on, type, amount_cents, description, idempotency_key)
      VALUES (:id, :day, :type, :amt, :desc, :idem)
      ON CONFLICT (idempotency_key) DO UPDATE
        SET idempotency_key = EXCLUDED.idempotency_key
      RETURNING id, (xmax = 0) AS created
      """;
    Map<String, Object> params = new HashMap<>();
    params.put("id", id);
    params.put("day", occurredOn);
    params.put("type", type.name());
    params.put("amt", amountCents);
    params.put("desc", description);
    params.put("idem", idempotencyKey);

    Map<String, Object> row = jdbc.queryForMap(sql, params);
    UUID returnedId = UUID.fromString(row.get("id").toString());
    boolean created = (Boolean) row.get("created");

    if (created) {
      // Only create outbox when newly inserted
      Map<String, Object> payload = Map.of(
          "id", returnedId.toString(),
          "occurredOn", occurredOn.toString(),
          "amountCents", amountCents,
          "type", type.name(),
          "description", description
      );
      String json;
      try {
        json = objectMapper.writeValueAsString(payload);
      } catch (Exception e) {
        throw new RuntimeException("Failed to serialize outbox payload", e);
      }
      OutboxEvent evt = new OutboxEvent(UUID.randomUUID(), "Entry", "ledger.entry-recorded", json);
      outboxRepository.save(evt);
      createdCounter.increment();
    } else {
      conflictCounter.increment();
    }
    return new Result(returnedId, created);
  }
}
