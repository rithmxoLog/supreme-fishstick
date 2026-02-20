-- GitXO Migration 003 — User Settings & Preferences
-- Run: psql -U postgres -d gitxo -f backend/Migrations/003_user_settings.sql

-- ─────────────────────────────────────────
-- USER SETTINGS (per-user preferences)
-- ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS user_settings (
    user_id         BIGINT      PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    theme           TEXT        NOT NULL DEFAULT 'dark',
    default_branch  TEXT        NOT NULL DEFAULT 'main',
    show_email      BOOLEAN     NOT NULL DEFAULT FALSE,
    email_on_push   BOOLEAN     NOT NULL DEFAULT FALSE,
    email_on_issue  BOOLEAN     NOT NULL DEFAULT FALSE,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_user_settings_user ON user_settings(user_id);

-- ─────────────────────────────────────────
-- Tighten refresh_tokens index (idempotent)
-- ─────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_active
    ON refresh_tokens(user_id)
    WHERE revoked_at IS NULL;

-- ─────────────────────────────────────────
-- COMPLETE SCHEMA REFERENCE (informational)
-- ─────────────────────────────────────────
-- After running all 3 migrations, the schema consists of:
--
--   users               — accounts (display_name, bio, avatar_url, is_admin)
--   repositories        — git repo metadata (owner_id FK, is_public, description)
--   repo_collaborators  — per-repo ACL (role: read | write | admin)
--   issues              — issue tracker (repo_id FK, author_id FK, status)
--   issue_comments      — comments on issues
--   activity_logs       — audit log (event_type, repo_name, details JSONB, user_id FK)
--   refresh_tokens      — JWT refresh sessions (user_id FK, token_hash, expires_at, revoked_at)
--   user_settings       — per-user preferences (theme, default_branch, email prefs)
