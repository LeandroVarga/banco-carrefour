CREATE INDEX IF NOT EXISTS ix_entries_occurred_on ON ledger.entries(occurred_on);
CREATE INDEX IF NOT EXISTS ix_outbox_unpublished_created ON ledger.outbox (created_at) WHERE published_at IS NULL;

