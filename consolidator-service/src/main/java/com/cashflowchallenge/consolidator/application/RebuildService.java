package com.cashflowchallenge.consolidator.application;

import com.cashflowchallenge.consolidator.infrastructure.repository.DailyBalanceRepository;
import com.cashflowchallenge.consolidator.infrastructure.repository.ProcessedEventRepository;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

@Service
public class RebuildService {
  private final JdbcTemplate jdbc;
  private final DailyBalanceRepository balanceRepo;
  private final ProcessedEventRepository processedRepo;
  private final BalanceApplicationService app;

  public RebuildService(JdbcTemplate jdbc, DailyBalanceRepository balanceRepo,
                        ProcessedEventRepository processedRepo, BalanceApplicationService app) {
    this.jdbc = jdbc;
    this.balanceRepo = balanceRepo;
    this.processedRepo = processedRepo;
    this.app = app;
  }

  record E(UUID id, LocalDate day, String type, long amount) {}

  @Transactional
  public void rebuildRange(LocalDate from, LocalDate to) {
    // Replace semantics: clear balances in range
    balanceRepo.deleteByDayBetween(from, to);

    // Load events
    List<E> events = jdbc.query(
        "select id, occurred_on, type, amount_cents from ledger.entries where occurred_on between ? and ? order by occurred_on, id",
        ps -> { ps.setObject(1, from); ps.setObject(2, to); },
        (rs, i) -> mapEvent(rs)
    );

    if (!events.isEmpty()) {
      // Optional: clear processed flags for these events to recompute cleanly
      List<UUID> ids = new ArrayList<>(events.size());
      for (E e : events) ids.add(e.id);
      processedRepo.deleteByIds(ids);

      // Re-apply using the same path as live consumption
      for (E e : events) {
        var t = "CREDIT".equals(e.type) ? BalanceApplicationService.EntryType.CREDIT : BalanceApplicationService.EntryType.DEBIT;
        app.applyEvent(e.id, e.day, t, e.amount);
      }
    }
  }

  private static E mapEvent(ResultSet rs) throws SQLException {
    return new E((UUID) rs.getObject(1), rs.getObject(2, LocalDate.class), rs.getString(3), rs.getLong(4));
  }
}

