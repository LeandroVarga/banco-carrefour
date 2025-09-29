package com.cashflowchallenge.consolidator.repo;

import com.cashflowchallenge.consolidator.ConsolidatorApplication;
import com.cashflowchallenge.consolidator.infrastructure.repository.DailyBalanceRepository;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.ActiveProfiles;

import java.time.LocalDate;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(classes = ConsolidatorApplication.class, webEnvironment = SpringBootTest.WebEnvironment.NONE)
@ActiveProfiles("it")
public class DailyBalanceRepositoryIT {
  @Autowired DailyBalanceRepository repo;

  @Test
  void upsertAddsDelta() {
    LocalDate day = LocalDate.now();
    repo.upsertAdd(day, 1000);
    repo.upsertAdd(day, -300);
    var found = repo.findById(day);
    assertThat(found).isPresent();
    assertThat(found.get().getBalanceCents()).isEqualTo(700);
  }
}
