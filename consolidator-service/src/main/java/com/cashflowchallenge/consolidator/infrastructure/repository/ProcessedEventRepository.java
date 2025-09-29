package com.cashflowchallenge.consolidator.infrastructure.repository;

import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;

import java.util.List;
import java.util.UUID;

@Repository
public class ProcessedEventRepository {
  private final JdbcTemplate jdbc;

  public ProcessedEventRepository(JdbcTemplate jdbc) {
    this.jdbc = jdbc;
  }

  public int insertIgnore(UUID id) {
    return jdbc.update("INSERT INTO report.processed_events(id, processed_at) VALUES (?, now()) ON CONFLICT DO NOTHING", id);
  }

  public int deleteByIds(List<UUID> ids) {
    if (ids == null || ids.isEmpty()) return 0;
    String inSql = ids.stream().map(x -> "?").reduce((a,b) -> a + "," + b).orElse("?");
    String sql = "DELETE FROM report.processed_events WHERE id IN (" + inSql + ")";
    return jdbc.update(sql, ids.toArray());
  }
}

