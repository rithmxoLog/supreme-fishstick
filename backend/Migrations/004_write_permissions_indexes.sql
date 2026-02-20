-- GitXO Migration 004 — Write Permission Enforcement & ZIP Upload
-- Run: psql -U postgres -d gitxo -f backend/Migrations/004_write_permissions_indexes.sql
--
-- Changes introduced:
--   • UploadZip endpoint (POST /api/repos/{name}/upload-zip) now enforces
--     write access via UserCanWriteAsync before accepting any file data.
--   • ZIP_UPLOADED events are logged to activity_logs with fileCount,
--     strippedPrefix, commitHash, and branch details in the JSONB column.
--   • repo_collaborators (role: read | write | admin) is now actively queried
--     on every push/zip operation — indexes below make that fast.

-- ─────────────────────────────────────────
-- REPOSITORIES — fast lookup by name
-- (used on virtually every API call via GetRepoPathAsync / GetRepoMetaAsync)
-- ─────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_repos_name
    ON repositories(name);

-- ─────────────────────────────────────────
-- REPO_COLLABORATORS — optimized for write-access checks
--
-- UserCanWriteAsync query pattern:
--   SELECT 1 FROM repositories r
--   LEFT JOIN repo_collaborators rc
--          ON rc.repo_id = r.id AND rc.user_id = $2
--         AND rc.role IN ('write', 'admin')
--   WHERE r.name = $1
--     AND (r.owner_id = $2 OR rc.user_id IS NOT NULL)
-- ─────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_collab_write_lookup
    ON repo_collaborators(repo_id, user_id)
    WHERE role IN ('write', 'admin');

-- ─────────────────────────────────────────
-- ACTIVITY_LOGS — event type filtering
-- (used by LogsController and future audit queries for ZIP_UPLOADED,
--  FILES_PUSHED, FILE_CREATED, etc.)
-- ─────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_logs_event_type
    ON activity_logs(event_type);

CREATE INDEX IF NOT EXISTS idx_logs_user_id
    ON activity_logs(user_id)
    WHERE user_id IS NOT NULL;

-- ─────────────────────────────────────────
-- COMPLETE SCHEMA REFERENCE (after all 4 migrations)
-- ─────────────────────────────────────────
--
--   users               — accounts (display_name, bio, avatar_url, is_admin)
--   repositories        — git repo metadata (owner_id FK, is_public, default_branch)
--   repo_collaborators  — per-repo ACL (role: read | write | admin)
--   issues              — issue tracker (repo_id FK, author_id FK, status)
--   issue_comments      — comments on issues
--   activity_logs       — audit log (event_type, repo_name, details JSONB, user_id FK)
--   refresh_tokens      — JWT refresh sessions (token_hash, expires_at, revoked_at)
--   user_settings       — per-user preferences (theme, default_branch, email prefs)
--
-- Indexes added across all migrations:
--   idx_repos_owner         — repositories(owner_id)
--   idx_repos_public        — repositories(is_public) WHERE is_public = TRUE
--   idx_repos_name          — repositories(name)                          [004]
--   idx_issues_repo         — issues(repo_id)
--   idx_issues_status       — issues(status)
--   idx_collab_user         — repo_collaborators(user_id)
--   idx_collab_write_lookup — repo_collaborators(repo_id, user_id)
--                             WHERE role IN ('write', 'admin')             [004]
--   idx_logs_created        — activity_logs(created_at DESC)
--   idx_logs_repo           — activity_logs(repo_name)
--   idx_logs_event_type     — activity_logs(event_type)                   [004]
--   idx_logs_user_id        — activity_logs(user_id) WHERE NOT NULL       [004]
--   idx_refresh_tokens_user — refresh_tokens(user_id)
--   idx_refresh_tokens_hash — refresh_tokens(token_hash)
--   idx_refresh_tokens_user_active — refresh_tokens(user_id)
--                                    WHERE revoked_at IS NULL
--   idx_user_settings_user  — user_settings(user_id)
