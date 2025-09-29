CREATE SCHEMA IF NOT EXISTS report;

CREATE TABLE IF NOT EXISTS report.daily_balances (
    day DATE PRIMARY KEY,
    balance_cents BIGINT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

