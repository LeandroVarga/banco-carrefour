DO $$
BEGIN
  BEGIN
    ALTER TABLE report.daily_balances
      ALTER COLUMN balance_cents SET NOT NULL,
      ALTER COLUMN updated_at SET NOT NULL;
  EXCEPTION WHEN undefined_table THEN
    -- table created earlier migrations; ignore if not present
  END;
END$$;

