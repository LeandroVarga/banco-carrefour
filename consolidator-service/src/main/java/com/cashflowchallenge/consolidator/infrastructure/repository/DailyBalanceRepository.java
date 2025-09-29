package com.cashflowchallenge.consolidator.infrastructure.repository;

import com.cashflowchallenge.consolidator.domain.DailyBalance;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDate;

public interface DailyBalanceRepository extends JpaRepository<DailyBalance, LocalDate> {
  @Modifying
  @Transactional
  @Query(value = "INSERT INTO report.daily_balances(day, balance_cents, updated_at) " +
      "VALUES (?1, ?2, now()) " +
      "ON CONFLICT (day) DO UPDATE SET balance_cents = report.daily_balances.balance_cents + EXCLUDED.balance_cents, updated_at = now()", nativeQuery = true)
  void upsertAdd(LocalDate day, long deltaCents);

  @Modifying
  @Transactional
  @Query("delete from DailyBalance d where d.day between ?1 and ?2")
  void deleteByDayBetween(LocalDate from, LocalDate to);
}
