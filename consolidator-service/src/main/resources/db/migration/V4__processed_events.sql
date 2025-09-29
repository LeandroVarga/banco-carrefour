CREATE TABLE IF NOT EXISTS report.processed_events (
  id UUID PRIMARY KEY,
  processed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

