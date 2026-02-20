-- GitXO Migration 002 — Refresh Token Sessions
-- Run against gitxo database: psql -U postgres -d gitxo -f 002_refresh_tokens.sql

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id          BIGSERIAL    PRIMARY KEY,
    user_id     BIGINT       NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash  TEXT         NOT NULL UNIQUE,
    expires_at  TIMESTAMPTZ  NOT NULL,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    revoked_at  TIMESTAMPTZ,
    user_agent  TEXT
);

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user ON refresh_tokens(user_id);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_hash ON refresh_tokens(token_hash);
