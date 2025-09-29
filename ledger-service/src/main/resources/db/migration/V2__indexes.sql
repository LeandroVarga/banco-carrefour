-- Speeds up listing and range aggregations by date
CREATE INDEX IF NOT EXISTS idx_entries_occurred_on ON ledger.entries(occurred_on);

