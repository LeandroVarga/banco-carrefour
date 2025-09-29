package com.cashflowchallenge.ledger.infrastructure.messaging;

import com.cashflowchallenge.ledger.domain.OutboxEvent;
import com.cashflowchallenge.ledger.infrastructure.repository.OutboxRepository;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

import java.util.UUID;

@Component
public class OutboxDrainScheduler {
  private static final Logger log = LoggerFactory.getLogger(OutboxDrainScheduler.class);

  private final OutboxRepository repo;
  private final OutboxPublisher publisher;
  private final JdbcTemplate jdbc;
  private final int batchSize;
  private final int maxAttempts;
  private final io.micrometer.core.instrument.Gauge unpublishedGauge;
  private final io.micrometer.core.instrument.Gauge poisonedGauge;

  public OutboxDrainScheduler(OutboxRepository repo, OutboxPublisher publisher, JdbcTemplate jdbc,
                              @Value("${outbox.batchSize:200}") int batchSize,
                              @Value("${outbox.maxAttempts:20}") int maxAttempts,
                              io.micrometer.core.instrument.MeterRegistry registry) {
    this.repo = repo;
    this.publisher = publisher;
    this.jdbc = jdbc;
    this.batchSize = batchSize;
    this.maxAttempts = maxAttempts;
    this.unpublishedGauge = io.micrometer.core.instrument.Gauge.builder("outbox_unpublished_count", repo, OutboxRepository::countUnpublished).register(registry);
    this.poisonedGauge = io.micrometer.core.instrument.Gauge.builder("outbox_poisoned_count", repo, OutboxRepository::countPoisoned).register(registry);
  }

  @Scheduled(fixedDelayString = "${outbox.drainDelay:1000}")
  public void drain() {
    int processed = 0;
    for (int i = 0; i < batchSize; i++) {
      if (!processOne()) break;
      processed++;
    }
    if (processed > 0) log.debug("Outbox processed {} events", processed);
  }

  @Transactional
  protected boolean processOne() {
    UUID id = jdbc.query(
        """
        SELECT id
        FROM ledger.outbox
        WHERE published_at IS NULL
          AND (attempts = 0 OR now() - updated_at > LEAST((attempts * attempts) * interval '5 seconds', interval '5 minutes'))
          AND (poisoned_at IS NULL)
        ORDER BY created_at ASC
        LIMIT 1
        FOR UPDATE SKIP LOCKED
        """,
        rs -> rs.next() ? (UUID) rs.getObject(1) : null);
    if (id == null) return false;
    OutboxEvent evt = repo.findById(id).orElse(null);
    if (evt == null) return true; // someone deleted/published meanwhile
    if (evt.getAttempts() >= maxAttempts) {
      repo.markPoisoned(id);
      org.slf4j.MDC.put("outboxId", id.toString());
      log.warn("Outbox event poisoned after max attempts: {}", id);
      org.slf4j.MDC.remove("outboxId");
      return true;
    }
    boolean ok = publisher.publishAndConfirm(evt);
    if (ok) {
      repo.markPublished(id);
    } else {
      repo.markFailed(id, "nack/exception during publish");
      // If exceeded attempts after failure, poison it
      OutboxEvent nowEvt = repo.findById(id).orElse(null);
      if (nowEvt != null && nowEvt.getAttempts() >= maxAttempts) {
        repo.markPoisoned(id);
        org.slf4j.MDC.put("outboxId", id.toString());
        log.warn("Outbox event poisoned after failure threshold: {}", id);
        org.slf4j.MDC.remove("outboxId");
      }
    }
    return true;
  }
}
