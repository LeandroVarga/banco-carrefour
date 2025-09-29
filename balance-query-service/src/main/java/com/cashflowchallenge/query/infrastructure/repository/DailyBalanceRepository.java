package com.cashflowchallenge.query.infrastructure.repository;

import com.cashflowchallenge.query.domain.DailyBalance;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;

import java.time.LocalDate;
import java.util.List;

public interface DailyBalanceRepository extends JpaRepository<DailyBalance, LocalDate> {
  @Query("select d from DailyBalance d where d.day between ?1 and ?2 order by d.day asc")
  List<DailyBalance> findRange(LocalDate from, LocalDate to);
}

