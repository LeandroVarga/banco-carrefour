package com.cashflowchallenge.ledger.infra;

import com.cashflowchallenge.ledger.LedgerApplication;
import com.cashflowchallenge.ledger.application.RecordEntryService;
import com.cashflowchallenge.ledger.domain.EntryType;
import com.cashflowchallenge.ledger.infrastructure.repository.OutboxRepository;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.ActiveProfiles;

import java.time.LocalDate;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(classes = LedgerApplication.class, webEnvironment = SpringBootTest.WebEnvironment.NONE)
@ActiveProfiles("it")
public class JpaOutboxIT {
  @Autowired RecordEntryService service;
  @Autowired OutboxRepository outbox;

  @Test
  void savingEntryCreatesOutbox() {
    var result = service.record(LocalDate.now(), 1000, EntryType.CREDIT, "t", "ik-1");
    assertThat(result.created).isTrue();
    assertThat(outbox.findTop100ByOrderByCreatedAtAsc()).isNotEmpty();
  }
}
