ALTER TABLE IF EXISTS ledger.outbox
  ADD COLUMN IF NOT EXISTS attempts INT NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS last_error TEXT,
  ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT now();

CREATE INDEX IF NOT EXISTS idx_outbox_order ON ledger.outbox (published_at NULLS LAST, created_at, id);

