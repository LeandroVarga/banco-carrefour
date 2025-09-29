ALTER TABLE IF EXISTS ledger.entries
  ALTER COLUMN idempotency_key SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_entries_idem_key
  ON ledger.entries (idempotency_key);

