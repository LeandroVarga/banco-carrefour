CREATE SCHEMA IF NOT EXISTS ledger;

CREATE TABLE IF NOT EXISTS ledger.entries (
    id UUID PRIMARY KEY,
    occurred_on DATE NOT NULL,
    amount_cents BIGINT NOT NULL,
    type VARCHAR(10) NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    idempotency_key VARCHAR(64) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS ledger.outbox (
    id UUID PRIMARY KEY,
    aggregate VARCHAR(32) NOT NULL,
    event_type VARCHAR(64) NOT NULL,
    payload JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_outbox_unpublished ON ledger.outbox(published_at) WHERE published_at IS NULL;

