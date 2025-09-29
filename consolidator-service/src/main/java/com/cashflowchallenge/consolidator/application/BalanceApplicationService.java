package com.cashflowchallenge.consolidator.application;

import com.cashflowchallenge.consolidator.infrastructure.repository.DailyBalanceRepository;
import com.cashflowchallenge.consolidator.infrastructure.repository.ProcessedEventRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDate;
import java.util.UUID;

@Service
public class BalanceApplicationService {
  private final DailyBalanceRepository balanceRepo;
  private final ProcessedEventRepository processedRepo;

  public BalanceApplicationService(DailyBalanceRepository balanceRepo, ProcessedEventRepository processedRepo) {
    this.balanceRepo = balanceRepo;
    this.processedRepo = processedRepo;
  }

  public static enum EntryType { CREDIT, DEBIT }

  @Transactional
  public boolean applyEvent(UUID id, LocalDate occurredOn, EntryType type, long amountCents) {
    long delta = (type == EntryType.CREDIT) ? amountCents : -amountCents;
    int ins = processedRepo.insertIgnore(id);
    if (ins == 1) {
      balanceRepo.upsertAdd(occurredOn, delta);
      return true;
    }
    return false;
  }
}

