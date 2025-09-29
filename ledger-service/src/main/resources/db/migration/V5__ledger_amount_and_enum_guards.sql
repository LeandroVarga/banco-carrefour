DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='ledger' AND table_name='entries' AND column_name='amount_cents'
  ) THEN
    RAISE EXCEPTION 'ledger.entries.amount_cents column missing';
  END IF;

  BEGIN
    ALTER TABLE ledger.entries
      ADD CONSTRAINT ck_entries_amount_pos CHECK (amount_cents >= 1);
  EXCEPTION WHEN duplicate_object THEN
    -- already present, ignore
  END;

  BEGIN
    ALTER TABLE ledger.entries
      ADD CONSTRAINT ck_entries_type_valid CHECK (type IN ('CREDIT','DEBIT'));
  EXCEPTION WHEN duplicate_object THEN
  END;

  BEGIN
    ALTER TABLE ledger.entries
      ADD CONSTRAINT ck_entries_occurred_on_range CHECK (occurred_on BETWEEN DATE '2000-01-01' AND (CURRENT_DATE + INTERVAL '3650 days'));
  EXCEPTION WHEN duplicate_object THEN
  END;
END$$;

