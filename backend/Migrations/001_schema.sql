-- GitXO Full Schema Migration
-- Run this against your PostgreSQL database: psql -U postgres -d gitxo -f 001_schema.sql

-- ─────────────────────────────────────────
-- USERS
-- ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS users (
    id            BIGSERIAL    PRIMARY KEY,
    username      TEXT         NOT NULL UNIQUE,
    email         TEXT         NOT NULL UNIQUE,
    password_hash TEXT         NOT NULL,
    display_name  TEXT,
    avatar_url    TEXT,
    bio           TEXT,
    is_admin      BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ─────────────────────────────────────────
-- REPOSITORIES (metadata — git data lives on disk)
-- ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS repositories (
    id             BIGSERIAL    PRIMARY KEY,
    name           TEXT         NOT NULL UNIQUE,
    description    TEXT,
    owner_id       BIGINT       NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    is_public      BOOLEAN      NOT NULL DEFAULT TRUE,
    default_branch TEXT         NOT NULL DEFAULT 'main',
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ─────────────────────────────────────────
-- REPO COLLABORATORS
-- ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS repo_collaborators (
    repo_id    BIGINT  NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
    user_id    BIGINT  NOT NULL REFERENCES users(id)        ON DELETE CASCADE,
    role       TEXT    NOT NULL CHECK (role IN ('read', 'write', 'admin')),
    granted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (repo_id, user_id)
);

-- ─────────────────────────────────────────
-- ISSUES
-- ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS issues (
    id         BIGSERIAL   PRIMARY KEY,
    repo_id    BIGINT      NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
    number     INT         NOT NULL,
    author_id  BIGINT      NOT NULL REFERENCES users(id),
    title      TEXT        NOT NULL,
    body       TEXT,
    status     TEXT        NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'closed')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(repo_id, number)
);

CREATE TABLE IF NOT EXISTS issue_comments (
    id         BIGSERIAL   PRIMARY KEY,
    issue_id   BIGINT      NOT NULL REFERENCES issues(id)  ON DELETE CASCADE,
    author_id  BIGINT      NOT NULL REFERENCES users(id),
    body       TEXT        NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ─────────────────────────────────────────
-- ACTIVITY LOGS (existing table — extend it)
-- ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS activity_logs (
    id         BIGSERIAL    PRIMARY KEY,
    event_type TEXT         NOT NULL,
    repo_name  TEXT,
    details    JSONB        NOT NULL DEFAULT '{}',
    created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    user_id    BIGINT       REFERENCES users(id)
);

-- If activity_logs already exists, just add user_id column
ALTER TABLE activity_logs ADD COLUMN IF NOT EXISTS user_id BIGINT REFERENCES users(id);

-- ─────────────────────────────────────────
-- INDEXES
-- ─────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_repos_owner    ON repositories(owner_id);
CREATE INDEX IF NOT EXISTS idx_repos_public   ON repositories(is_public) WHERE is_public = TRUE;
CREATE INDEX IF NOT EXISTS idx_issues_repo    ON issues(repo_id);
CREATE INDEX IF NOT EXISTS idx_issues_status  ON issues(status);
CREATE INDEX IF NOT EXISTS idx_collab_user    ON repo_collaborators(user_id);
CREATE INDEX IF NOT EXISTS idx_logs_created   ON activity_logs(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_logs_repo      ON activity_logs(repo_name);
