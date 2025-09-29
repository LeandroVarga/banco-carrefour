package com.cashflowchallenge.ledger.domain;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

public class EntryTypeTest {
  @Test
  void creditIsPositive() {
    assertEquals(100, EntryType.CREDIT.signedAmount(100));
  }

  @Test
  void debitIsNegative() {
    assertEquals(-100, EntryType.DEBIT.signedAmount(100));
  }

  @Test
  void rejectsNonPositive() {
    assertThrows(IllegalArgumentException.class, () -> EntryType.CREDIT.signedAmount(0));
  }
}

