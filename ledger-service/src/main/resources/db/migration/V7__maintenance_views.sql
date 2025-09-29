CREATE OR REPLACE VIEW ledger.v_outbox_stuck AS
  SELECT id, attempts, created_at, updated_at, last_error
  FROM ledger.outbox
  WHERE published_at IS NULL AND attempts >= 10;

CREATE OR REPLACE VIEW ledger.v_idempotency_old AS
  SELECT id, idempotency_key, occurred_on, created_at
  FROM ledger.entries
  WHERE created_at < (now() - INTERVAL '90 days');

