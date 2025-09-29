package com.cashflowchallenge.ledger.infrastructure.repository;

import com.cashflowchallenge.ledger.domain.OutboxEvent;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.UUID;

public interface OutboxRepository extends JpaRepository<OutboxEvent, UUID> {
  // Convenience batch finder to drain outbox in small chunks
  List<OutboxEvent> findTop100ByOrderByCreatedAtAsc();

  @Modifying(clearAutomatically = true, flushAutomatically = true)
  @Transactional
  @Query("update OutboxEvent o set o.publishedAt = CURRENT_TIMESTAMP, o.updatedAt = CURRENT_TIMESTAMP where o.id = :id")
  int markPublished(@Param("id") UUID id);

  @Modifying
  @Query("update OutboxEvent o set o.attempts = o.attempts + 1, o.lastError = :err, o.updatedAt = CURRENT_TIMESTAMP where o.id = :id")
  void markFailed(@Param("id") UUID id, @Param("err") String error);

  @Modifying
  @Query(value = "update ledger.outbox set poisoned_at = now(), updated_at = now() where id = :id", nativeQuery = true)
  void markPoisoned(@Param("id") UUID id);

  @Query("select count(o) from OutboxEvent o where o.publishedAt is null")
  long countUnpublished();

  @Query(value = "select count(*) from ledger.outbox where poisoned_at is not null", nativeQuery = true)
  long countPoisoned();
}
