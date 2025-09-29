package com.cashflowchallenge.ledger.infrastructure.repository;

import com.cashflowchallenge.ledger.domain.Entry;
import org.springframework.data.jpa.repository.JpaRepository;

import java.time.LocalDate;
import java.util.List;
import java.util.Optional;
import java.util.UUID;

public interface EntryRepository extends JpaRepository<Entry, UUID> {
  Optional<Entry> findByIdempotencyKey(String idempotencyKey);
  List<Entry> findByOccurredOn(LocalDate occurredOn);
}

