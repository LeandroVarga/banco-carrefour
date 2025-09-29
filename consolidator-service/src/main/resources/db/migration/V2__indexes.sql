-- Explicit index on report.daily_balances primary key column
CREATE INDEX IF NOT EXISTS idx_daily_balances_day ON report.daily_balances(day);

