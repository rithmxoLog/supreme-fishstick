# Gitripp — Project Context for Claude

## Standing Instruction
**After every Claude message that makes a change, append an entry to the Changelog at the bottom of this file.** Log every file created or modified, every bug fixed, every feature added, every schema change, and any new issues discovered. Each entry should be grouped under the current date and include a short description of what changed and which files were affected. Be sure to document this comprehensively. 

---

## What is Gitripp?
Gitripp (this repo is named GitXO) is a **self-hosted, GitHub-like web application**.
Security is not a current priority — focus on functionality first.

---

## Project Layout

```
GitXO/
├── backend/          ASP.NET Core 8 API  — http://localhost:3001
├── frontend/         React 18 SPA        — http://localhost:3000
├── docs/             Architecture/planning docs
├── start.bat / start.sh   Start both servers
└── CLAUDE.md         This file
```

---

## Tech Stack & Libraries

### Backend (C# / ASP.NET Core 8)
| Package | Version | Purpose |
|---------|---------|---------|
| Npgsql | 8.0.5 | PostgreSQL driver |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.0 | JWT auth middleware |
| BCrypt.Net-Next | 4.0.3 | Password hashing |
| Git CLI (system) | — | All git operations via `GitRunner.cs` |

**Services:**
- `Services/GitRunner.cs` — static wrapper around git CLI process calls
- `Services/ActivityLogger.cs` — fire-and-forget PostgreSQL audit logging
- `Services/AuthService.cs` — JWT generation (1hr access + 30d refresh), BCrypt hashing, user CRUD
- `Services/RepoDbService.cs` — repos table CRUD, owner/collaborator access checks, `GetUserIdByUsernameAsync` for recovery
- `Services/RepoMetaWriter.cs` — static helper: writes `{repoName}.meta.json` next to each repo dir for DB-loss recovery; `ScanAll` reads them back

**Controllers:**
- `AuthController` — POST /api/auth/register|login|refresh|logout, GET /api/auth/me
- `ReposController` — CRUD repos, ZIP upload/download; `POST /api/repos/recover` (admin) re-inserts repos from `.meta.json` files
- `FilesController` — browse files/dirs, save file content (`[Authorize]` on writes)
- `BranchesController` — list/create/delete branches (`[Authorize]` on writes)
- `CommitsController` — commit history + unified diff (for diff2html)
- `IssuesController` — issues CRUD + comments
- `LogsController` — activity log queries
- `GitController` — git-related passthrough endpoints
- `HealthController` — GET /api/health

### Frontend (React 18)
| Package | Version | Purpose |
|---------|---------|---------|
| react / react-dom | 18.2.0 | UI framework |
| react-router-dom | 6.21.1 | Client-side routing |
| react-syntax-highlighter | 15.5.0 | Syntax highlighting (vscDarkPlus theme) |
| diff2html | 3.4.56 | Side-by-side diff rendering |
| timeago.js | 4.0.2 | Relative timestamps |
| @craco/craco | 7.1.0 | CRA config override (craco.config.js) |
| jszip | ^3.x | Client-side ZIP compression before upload |

**Key source files:**
- `src/App.js` — AuthProvider wrapper, all routes
- `src/api/index.js` — all API calls, auto JWT Bearer header injection
- `src/contexts/AuthContext.js` — JWT in localStorage, login/logout/register
- `src/components/RepoView.js` — tabs: Code, Commits, Branches, Issues, Push
- `src/components/FileBrowser.js` — directory/file tree
- `src/components/FileViewer.js` — syntax-highlighted file content
- `src/components/CommitHistory.js` — diff2html side-by-side diffs
- `src/components/PushPanel.js` — unified upload UI: drag-drop zone, JSZip client compress, two-phase progress bar, collapsible tree preview
- `src/components/BranchManager.js` — branch list/create/delete
- `src/pages/ExplorePage.js` — public repo browser (homepage, no auth required)
- `src/pages/LoginPage.js` / `RegisterPage.js` — auth forms
- `src/pages/IssuesPage.js` / `IssueDetail.js` — issues + comments
- `src/pages/SettingsPage.js` — user settings

---

## Version Control Model (How Gitripp Works)

Gitripp does **not** use `git push` over HTTP. Instead:

1. **Upload**: User clicks a button → file dialog opens → user selects a **folder** (not a zip).
   - Frontend sends the folder contents to `POST /api/repos/{name}/upload-zip`.
   - Backend receives the files, zips them server-side (ignoring `node_modules`), then unzips into the repo directory and runs `git add . && git commit`.
2. **Download / "Pull"**: User clicks Download → receives a `.zip` of the repo at the selected branch/commit via `GET /api/repos/{name}/download?branch=main`.
3. **Actual git clone via HTTP is not yet implemented.** The clone URL shown in the UI is a placeholder.

Repositories live on disk at:
- **Public repos**: `backend/repositories/`  (flat by repo name)
- **Private repos**: `backend/repositories-private/` (flat by repo name)

Both directories are created automatically on backend startup.

---

## Authentication System

- **Access token**: JWT, 1-hour expiry, signed with HS256 (`Jwt:Secret` in appsettings.json)
- **Refresh token**: stored hashed in `refresh_tokens` table, 30-day expiry, can be revoked
- JWT validation: issuer + audience + lifetime + signing key all validated; ClockSkew = 1 min
- First registered user is automatically `is_admin = true`
- Token stored in `localStorage` on the frontend; injected as `Authorization: Bearer <token>` on every API call via `src/api/index.js`

---

## PostgreSQL Database Schema (Complete — All 4 Migrations)

Run migrations in order:
```
psql -U postgres -d gitxo -f backend/Migrations/001_schema.sql
psql -U postgres -d gitxo -f backend/Migrations/002_refresh_tokens.sql
psql -U postgres -d gitxo -f backend/Migrations/003_user_settings.sql
psql -U postgres -d gitxo -f backend/Migrations/004_write_permissions_indexes.sql
```

### Tables

```sql
-- User accounts
users (
    id            BIGSERIAL PK,
    username      TEXT UNIQUE NOT NULL,
    email         TEXT UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,
    display_name  TEXT,
    avatar_url    TEXT,
    bio           TEXT,
    is_admin      BOOLEAN DEFAULT FALSE,
    created_at    TIMESTAMPTZ,
    updated_at    TIMESTAMPTZ
)

-- Git repo metadata (actual git data lives on disk)
repositories (
    id             BIGSERIAL PK,
    name           TEXT UNIQUE NOT NULL,
    description    TEXT,
    owner_id       BIGINT FK → users(id) CASCADE,
    is_public      BOOLEAN DEFAULT TRUE,
    default_branch TEXT DEFAULT 'main',
    created_at     TIMESTAMPTZ,
    updated_at     TIMESTAMPTZ
)

-- Per-repo access control
repo_collaborators (
    repo_id    BIGINT FK → repositories(id) CASCADE,
    user_id    BIGINT FK → users(id) CASCADE,
    role       TEXT CHECK IN ('read', 'write', 'admin'),
    granted_at TIMESTAMPTZ,
    PRIMARY KEY (repo_id, user_id)
)

-- Issue tracker
issues (
    id         BIGSERIAL PK,
    repo_id    BIGINT FK → repositories(id) CASCADE,
    number     INT NOT NULL,
    author_id  BIGINT FK → users(id),
    title      TEXT NOT NULL,
    body       TEXT,
    status     TEXT CHECK IN ('open', 'closed') DEFAULT 'open',
    created_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ,
    UNIQUE(repo_id, number)
)

issue_comments (
    id         BIGSERIAL PK,
    issue_id   BIGINT FK → issues(id) CASCADE,
    author_id  BIGINT FK → users(id),
    body       TEXT NOT NULL,
    created_at TIMESTAMPTZ
)

-- Audit/activity log
activity_logs (
    id         BIGSERIAL PK,
    event_type TEXT NOT NULL,
    repo_name  TEXT,
    details    JSONB DEFAULT '{}',
    user_id    BIGINT FK → users(id),
    created_at TIMESTAMPTZ
)

-- JWT refresh sessions
refresh_tokens (
    id          BIGSERIAL PK,
    user_id     BIGINT FK → users(id) CASCADE,
    token_hash  TEXT UNIQUE NOT NULL,
    expires_at  TIMESTAMPTZ NOT NULL,
    revoked_at  TIMESTAMPTZ,
    user_agent  TEXT,
    created_at  TIMESTAMPTZ
)

-- Per-user preferences
user_settings (
    user_id        BIGINT PK FK → users(id) CASCADE,
    theme          TEXT DEFAULT 'dark',
    default_branch TEXT DEFAULT 'main',
    show_email     BOOLEAN DEFAULT FALSE,
    email_on_push  BOOLEAN DEFAULT FALSE,
    email_on_issue BOOLEAN DEFAULT FALSE,
    updated_at     TIMESTAMPTZ
)
```

### Indexes (all migrations combined)
```
idx_repos_owner              — repositories(owner_id)
idx_repos_public             — repositories(is_public) WHERE is_public = TRUE
idx_repos_name               — repositories(name)
idx_issues_repo              — issues(repo_id)
idx_issues_status            — issues(status)
idx_collab_user              — repo_collaborators(user_id)
idx_collab_write_lookup      — repo_collaborators(repo_id, user_id) WHERE role IN ('write','admin')
idx_logs_created             — activity_logs(created_at DESC)
idx_logs_repo                — activity_logs(repo_name)
idx_logs_event_type          — activity_logs(event_type)
idx_logs_user_id             — activity_logs(user_id) WHERE user_id IS NOT NULL
idx_refresh_tokens_user      — refresh_tokens(user_id)
idx_refresh_tokens_hash      — refresh_tokens(token_hash)
idx_refresh_tokens_user_active — refresh_tokens(user_id) WHERE revoked_at IS NULL
idx_user_settings_user       — user_settings(user_id)
```

---

## Goals / Feature Roadmap

### Currently Working
- User registration + login (JWT access + refresh token)
- JWT auto-expiry and logout
- Repo creation, deletion (with `[Authorize]`)
- Public repo browsing (ExplorePage — no auth required)
- File/directory browser with inline "New file" editor (canWrite users only)
- File viewer with syntax highlighting
- Commit history + unified diff (diff2html)
- Branch list, create, delete
- Issues + comments UI
- Push tab: unified drop zone (files/folder/ZIP), client-side JSZip compression, two-phase progress bar, collapsible file tree preview
- ZIP download ("pull") of any branch
- Activity log
- User settings page

### Planned / In Progress
- Branch comparison diff (`GET /api/repos/{name}/diff?from=X&to=Y`)
- Pull requests + merge UI (PR table + endpoints + frontend tab)
- Real git clone via HTTP (git http-backend CGI or soft-serve sidecar)
- SSH key management (`ssh_keys` table + UI)
- Private repos: confirm persistent permission enforcement in both UI and backend

---

## Known Issues / Priorities
1. `/` route should NOT be behind `PrivateRoute` — public users must browse repos
2. File write ops should call `UserCanWriteAsync` in `FilesController`
3. Push tab should be hidden for non-owners/non-collaborators
4. Clone URL in UI is non-functional — should be labeled "coming soon" or removed
5. Branch comparison diff endpoint not yet implemented

---

## Dev Notes
- Do NOT add `System.IdentityModel.Tokens.Jwt` to .csproj — pulled in transitively by JwtBearer
- `File.SetAttributes` in `ReposController` must be qualified as `System.IO.File` (conflict with `ControllerBase.File`)
- JWT secret is in `appsettings.json` — change before any production deployment
- DB password is in `appsettings.json` — `Skrillex123#` (dev only)
- Proxy in `frontend/package.json` routes all `/api/*` calls to `http://localhost:3001`

---

## Changelog

### 2026-02-20

#### Context & Documentation
- **[ClaudeContextGitXO.md]** Created with full project context: tech stack, DB schema (all 4 migrations), version control model, auth system, feature roadmap, known issues, and dev notes. Established standing instruction to append changelog entries after every Claude session.

---

#### Feature: Unified Push Panel with Client-Side Compression & Progress Bar

**Problem:** The old Push tab had three separate sub-tabs (Files / Folder / ZIP) that used multipart form-data, sending every file as a separate field in one massive HTTP request. No progress feedback was shown, and the flat file list was unmanageable for large uploads.

**Changes:**

- **[frontend/package.json]** Added `jszip ^3.10.1` — browser-native ZIP compression library.

- **[frontend/src/api/index.js]** Added `uploadZipWithProgress(repo, zipBlob, message, branch, onProgress, authorName, authorEmail)`. Uses `XMLHttpRequest` (not `fetch`) so `xhr.upload.onprogress` events fire during the HTTP upload phase. Handles 401 token expiry the same way as all other API calls (calls `tryRefresh`, retries once).

- **[frontend/src/components/PushPanel.js]** Full rewrite. Replaced the three-tab layout with a single unified workflow:
  - **One drop zone** accepts any combination of individual files, folders (via `webkitdirectory`), or a `.zip` archive — by drag-and-drop or via three selection buttons (Select Files / Select Folder / Select ZIP).
  - **Dropped `.zip`** is sent directly to `/api/repos/{name}/upload-zip` with no re-compression (zero overhead).
  - **Files and folders** are compressed client-side using JSZip (DEFLATE level 6) into a single zip blob, then posted to the same endpoint — eliminates multipart overhead entirely.
  - **Two-phase progress bar**: "Compressing… N%" covers 0–50%, "Uploading… N%" covers 50–100%, derived from JSZip's `onUpdate` callback and `xhr.upload.onprogress` respectively. Label and percentage displayed above the bar.
  - **Collapsible file tree preview** of all staged files: directories expand/collapse on click, individual files show name + size + a `×` remove button. Folder nodes show a file count badge. Built from a recursive `buildTree()` helper that sorts dirs before files.
  - **Staged file controls**: file count + total size shown in header; `+ Files`, `+ Folder`, and `Clear all` buttons available once staging has begun (no need to re-open the drop zone).
  - **Commit form** (target branch selector, commit message input, collapsible advanced author name/email) only renders once at least one file or zip is staged.
  - `node_modules` directories are filtered out at ingestion time.

---

#### Feature: Inline "New File" Editor in File Browser

**Problem:** Creating a new file required navigating away from the Code tab to the Push tab. No quick in-place creation existed.

**Changes:**

- **[frontend/src/components/FileBrowser.js]** Added `canWrite` prop (boolean). When true:
  - A `+ New file` button appears in the branch-selector toolbar at all times while browsing.
  - An empty directory shows a `+ Create a file` call-to-action in place of the empty-state message.
  - Clicking either opens an inline editor panel (replacing the file table, consistent with the `FileViewer` pattern):
    - Filename input pre-scoped to the current directory path (e.g. showing `src/components/` as a prefix).
    - Full-height `editor-textarea` for file content.
    - Commit message input with placeholder defaulting to `Create <filename>`.
    - `Commit new file` and `Cancel` buttons in the file-viewer header row.
    - On success, calls `api.saveFile`, returns to the directory listing, and triggers `onRefresh`.
    - Inline error banner for validation failures (empty filename, empty commit message) or API errors.

- **[frontend/src/components/RepoView.js]** Passes `canWrite={canWrite}` prop down to `FileBrowser`.

---

#### Feature: Filesystem Metadata for DB-Loss Recovery

**Problem:** All repository ownership data (owner, visibility, description) lived only in PostgreSQL. A database wipe would leave all repos on disk with no way to know who owned them or whether they were public/private.

**Changes:**

- **[backend/Services/RepoMetaWriter.cs]** New static helper class. Writes a `{repoName}.meta.json` file **alongside** (not inside) each repository directory whenever a repo is created or deleted.
  - File location example: `repositories/my-project.meta.json`
  - Fields stored: `name`, `ownerUsername`, `ownerEmail`, `ownerId`, `isPublic`, `description`, `defaultBranch`, `savedAt` (UTC timestamp).
  - `WriteAsync(reposBaseDir, repoName, ownerUsername, ownerEmail, ownerId, isPublic, description)` — creates or overwrites the file.
  - `Delete(reposBaseDir, repoName)` — removes the file on repo deletion.
  - `ScanAll(dir)` — enumerates all `*.meta.json` files in a directory and deserialises them; silently skips unparseable files.

- **[backend/Services/RepoDbService.cs]**
  - `GetRepoMetaAsync` query extended: now selects `u.email` in addition to `u.username`; the `RepoMeta` model gains an `OwnerEmail` property.
  - Added `GetUserIdByUsernameAsync(username)` — looks up `users.id` by username; used during recovery to re-link repos to existing user accounts.
  - Added `CreatePlaceholderUserAsync(username, email)` — for the full DB+users wipe scenario: inserts a new `users` row with the original username and email but a cryptographically random, unguessable BCrypt password hash. The account is functional but locked until an admin sets a real password. Falls back to `{username}@recovery.gitxo.local` if the meta file had no email. Uses `ON CONFLICT (username) DO NOTHING` to avoid duplicates.

- **[backend/Controllers/ReposController.cs]**
  - `CreateRepo`: after `CreateRepoMetaAsync` succeeds, immediately calls `GetRepoMetaAsync` (to get the resolved username + email) and writes the metadata file via `RepoMetaWriter.WriteAsync`.
  - `DeleteRepo`: derives the repos base directory from `Path.GetDirectoryName(repoPath)` and calls `RepoMetaWriter.Delete` after the DB row is removed.
  - New `POST /api/repos/recover` endpoint (admin JWT required):
    - Scans both `repositories/` and `repositories-private/` for `*.meta.json` files.
    - For each: checks if already in DB (→ `skipped`), else looks up owner by username.
    - If owner exists: re-inserts the repo row (→ `recovered`).
    - If owner missing (full wipe): calls `CreatePlaceholderUserAsync`, then re-inserts the repo row (→ `recovered` + `placeholdersCreated`).
    - If placeholder creation or DB insert fails: adds to `failed` with a reason string.
    - Response shape: `{ recovered, skipped, failed, placeholdersCreated, note? }`. The `note` field is populated when placeholder accounts were created, explaining that those users need a password reset.

---

#### Bug Fixes: Authorization Gaps in BranchesController, FilesController, IssuesController

**Problem:** Several write endpoints lacked per-repo permission checks; any authenticated user could mutate branches or issues they didn't own. Private-repo read access was also gated on write permission instead of read permission.

**Changes:**

- **[backend/Services/RepoDbService.cs]** Added `UserCanReadAsync(repoName, userId)` — same shape as `UserCanWriteAsync` but allows any collaborator role (read/write/admin), not just write/admin. Used by read-gating logic for private repos.

- **[backend/Controllers/FilesController.cs]** Fixed `CanReadRepoAsync`: was calling `UserCanWriteAsync` for private repos, now correctly calls `UserCanReadAsync`. Collaborators with `read` role can now browse files in private repos.

- **[backend/Controllers/BranchesController.cs]**
  - Added `using System.Security.Claims` import and `GetUserId()` helper.
  - Added `UserCanWriteAsync` permission check at the top of `CreateBranch`, `Checkout`, `Merge`, and `DeleteBranch`. Returns 403 if the authenticated user does not own or have write/admin collaborator access to the repo.

- **[backend/Controllers/IssuesController.cs]**
  - `UpdateIssue (PATCH)`: now fetches the issue's `author_id` before updating. Returns 403 unless the caller is the issue author OR passes `UserCanWriteAsync`. Also surfaces a proper 404 if the issue doesn't exist at the auth-check stage.
  - `AddComment (POST)`: for private repos, calls `UserCanReadAsync` and returns 403 if the user has no read access to the repository.

---

#### Bug Fix: CS8126 Tuple Element Name `Rest` Reserved in C#

**Problem:** `GitController.cs` declared a named tuple return type `(string? RepoName, string? Rest)` — `Rest` is a reserved identifier in `ValueTuple` (used internally for tuple chaining beyond 7 elements) and is disallowed as a named member, causing a compile error.

**Changes:**

- **[backend/Controllers/GitController.cs]** Renamed tuple element `Rest` → `Service` in the `ParseGitPath` helper method signature (line 180). No callers referenced `.Rest` by name, so no other changes were needed. Build now succeeds with 0 errors.

---

#### Recovery System Hardening (Meta File Gaps)

**Problem:** Analysis of the recovery system identified five gaps: (1) stale meta files if repo metadata changed post-creation, (2) admin chicken-and-egg — `POST /api/repos/recover` required an admin JWT but a full DB wipe leaves no users to log in as, (3) `CreatePlaceholderUserAsync` could silently return `null` on `ON CONFLICT` (race condition between two concurrent recover calls), (4) `ownerIsAdmin` flag not preserved in the meta file so the admin role could not be restored after a full wipe, (5) no way to update description or default branch via API.

**Changes:**

- **[backend/Services/RepoMetaWriter.cs]**
  - Added `ownerIsAdmin` parameter to `WriteAsync` (default `false`).
  - Added `[JsonPropertyName("ownerIsAdmin")] public bool OwnerIsAdmin` field to `RepoMetaFile`. All newly written meta files now record whether the owner was an admin at creation time.

- **[backend/Services/RepoDbService.cs]**
  - `GetRepoMetaAsync` SQL extended to also `SELECT u.is_admin, r.default_branch`; `RepoMeta` model gains `OwnerIsAdmin` and `DefaultBranch` properties.
  - `CreatePlaceholderUserAsync`: added `bool isAdmin = false` parameter; `is_admin` is now included in the `INSERT`; added a fallback `SELECT id FROM users WHERE username = $1` after the `ON CONFLICT DO NOTHING` so a race condition no longer returns `null`.
  - Added `HasAnyUsersAsync()` — returns `true` when at least one row exists in `users`; fails safe to `true` if the DB is unreachable (prevents inadvertent auth bypass).
  - Added `UpdateRepoAsync(name, description?, defaultBranch?)` — uses `COALESCE` to update only the supplied fields, sets `updated_at = NOW()`.

- **[backend/Controllers/ReposController.cs]**
  - `CreateRepo`: reads the `is_admin` claim from the JWT and passes it as `ownerIsAdmin` to `RepoMetaWriter.WriteAsync`.
  - `RecoverFromMetaFiles`:
    - Removed `[Authorize]`, added `[AllowAnonymous]`. Auth is now checked manually inside the method.
    - **Bootstrap mode**: if `HasAnyUsersAsync()` returns `false` (full DB wipe), the endpoint is accessible without a JWT so recovery can run before any login is possible. Once at least one user exists, an admin JWT is required as before.
    - Passes `meta.OwnerIsAdmin` to `CreatePlaceholderUserAsync` so the admin flag is restored for placeholder accounts.
    - Response now includes `bootstrapMode: bool` field so callers can tell which path was taken.
  - Added `PATCH /api/repos/{name}` endpoint (`[Authorize]`, owner or admin only):
    - Accepts `{ description?, defaultBranch? }` body (`UpdateRepoRequest` record).
    - Calls `UpdateRepoAsync` to write the updated values to the DB.
    - Re-fetches the row via `GetRepoMetaAsync` and calls `RepoMetaWriter.WriteAsync` with the refreshed values, keeping the meta file permanently in sync with the database.
    - If `description` is provided, also overwrites `.git/description` to keep git's internal description in sync.
    - Logs a `REPO_UPDATED` activity event.
