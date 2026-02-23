# Gitripp — Project Context for Claude

## Standing Instructions

**1. Changelog** — After every Claude message that makes a change, append an entry to the Changelog at the bottom of this file. Log every file created or modified, every bug fixed, every feature added, every schema change, and any new issues discovered. Each entry should be grouped under the current date and include a short description of what changed and which files were affected. Be thorough.

**2. "What you need to do" section** — Every changelog entry must end with a **"What you need to do"** block. This block should list every manual action the user must take as a result of the changes: running migrations, installing packages, restarting servers, setting environment variables, running commands, updating config files, etc. Format it as a numbered list. If nothing is required, write "Nothing — changes are code-only." Example format:

> #### What you need to do
> 1. Run the migration: `psql -U postgres -d gitxo -f backend/Migrations/005_login_security.sql`
> 2. Restart the backend server.
> 3. Nothing else — no new packages required.

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

## File Reference

A complete, annotated inventory of every source file in the project. Grouped by layer and directory.

---

### Root Level

| File | Purpose |
|------|---------|
| `start.bat` | Windows launcher — checks for .NET + Node, restores deps, opens two cmd windows (backend port 3001, frontend port 3000) |
| `start.sh` | Unix/macOS launcher — same logic as start.bat but uses background processes; traps SIGINT/SIGTERM to cleanly kill both |
| `ClaudeContextGitXO.md` | This file. Full project context, schema, roadmap, changelog, and standing instructions for Claude |

---

### Backend — Configuration & Entry Point

| File | Purpose |
|------|---------|
| `backend/GitXO.Api.csproj` | MSBuild project file. Target: `net8.0`. NuGet deps: Npgsql 8.0.5, JwtBearer 8.0.0, BCrypt.Net-Next 4.0.3 |
| `backend/appsettings.json` | Runtime config: Postgres connection string, `ReposDirectory`, JWT secret/issuer/audience/expiry, port `http://localhost:3001` |
| `backend/Program.cs` | App entry point. Registers DI (ActivityLogger, AuthService, RepoDbService as Singletons). Configures middleware: CORS (all origins), security headers, rate limiter, authentication, authorization, controller routing. Resolves repo dirs and prints startup status. |

---

### Backend — Services

#### `backend/Services/AuthService.cs`
Full authentication and user management service.

| Method | Description |
|--------|-------------|
| `RegisterAsync` | Validates, BCrypt-hashes password, inserts user. First user becomes admin. |
| `LoginAsync` | Email+password login with account lockout (10 bad attempts → 15-min lock). |
| `CreateRefreshTokenAsync` | 32-byte random refresh token, SHA256-hashed before DB insert. |
| `RefreshAsync` | Validates refresh token, issues new access+refresh pair (token rotation), revokes old token. |
| `RevokeRefreshTokenAsync` | Single token revocation. |
| `RevokeAllSessionsAsync` | Revoke all sessions for a user. |
| `GetActiveSessionsAsync` | Lists non-revoked, non-expired sessions with user-agent info. |
| `RevokeSessionByIdAsync` | User can revoke a specific session by ID (own sessions only). |
| `UpdateProfileAsync` | Update display_name, bio, avatar_url. |
| `ChangePasswordAsync` | Verifies current password, sets new hash, revokes all sessions, clears lockout state. |
| `ChangeEmailAsync` | Verifies current password before changing email. |
| `GetUserSettingsAsync` | Returns preferences, auto-creates row on first access. |
| `UpdateUserSettingsAsync` | Upserts theme, default_branch, email notification preferences. |
| `GetTotalUsersAsync` | Count of all registered users. |
| `ListUsersAsync` | Admin-only full user list. |
| `DeleteUserAsync` | Admin-only user deletion. |
| `VerifyUsernamePasswordAsync` | Username+password check used for Git HTTP Basic auth. |
| `GetUserByIdAsync` | Fetch user record by ID. |
| `GenerateAccessToken` | Creates 1-hour HS256 JWT with claims: sub, email, username, is_admin, jti. |

**Data types:** `UserInfo`, `SessionInfo`, `UserSettings`

---

#### `backend/Services/ActivityLogger.cs`
Singleton fire-and-forget audit logger. `LogEvent` spawns a background Task to INSERT into `activity_logs` (event_type, repo_name, details JSONB, user_id). Errors are swallowed and printed as warnings — never propagate to callers.

---

#### `backend/Services/GitRunner.cs`
Static wrapper around the `git` CLI process.

| Method | Description |
|--------|-------------|
| `RunAsync` | Executes any git command; returns `(stdout, stderr, exitCode)`. |
| `StartStreaming` | Starts a git process with streaming stdout — used for `git archive` downloads. |
| `GetBranchesAsync` | Parses `git branch` output; returns `(currentBranch, List<string> allBranches)`. |
| `ParseLogLine` | Parses `%H\|%h\|%an\|%ae\|%ai\|%s` format → `CommitInfo` record. |

**Data type:** `CommitInfo` (Hash, ShortHash, Author, Email, Date, Message)

---

#### `backend/Services/RepoDbService.cs`
Database service for repository metadata and access control.

| Method | Description |
|--------|-------------|
| `GetRepoPathAsync` | Resolves filesystem path for a repo by name. Returns null if not in DB. |
| `GetTargetDir` | Returns the base directory for new repos. |
| `GetRepoMetaAsync` | Fetches repo + owner info from DB. Returns `RepoMeta` or null. |
| `CreateRepoMetaAsync` | Inserts new repo row (ON CONFLICT DO NOTHING). |
| `DeleteRepoMetaAsync` | Deletes repo row. |
| `UserCanWriteAsync` | True if user is owner or collaborator with write/admin role. |
| `UserCanReadAsync` | True if user is owner or collaborator with any role (read/write/admin). |
| `GetRepoIdAsync` | Gets numeric repo ID by name. |
| `GetUserIdByUsernameAsync` | Gets user ID by username — used during recovery. |
| `CreatePlaceholderUserAsync` | Inserts locked placeholder user (random BCrypt hash). Handles ON CONFLICT race via fallback SELECT. Accepts `isAdmin` flag. |
| `HasAnyUsersAsync` | Returns true if any users exist; fails safe to true if DB is unreachable. |
| `UpdateRepoAsync` | Updates description and/or default_branch (COALESCE semantics). |

**Data type:** `RepoMeta` (Id, OwnerId, IsPublic, Description, OwnerUsername, OwnerEmail, OwnerIsAdmin, DefaultBranch)

---

#### `backend/Services/RepoMetaWriter.cs`
Static helper for disaster recovery. Writes `{repoName}.meta.json` alongside each repo directory whenever a repo is created or updated; removes it on deletion.

| Method | Description |
|--------|-------------|
| `WriteAsync` | Creates/overwrites `.meta.json` with name, ownerUsername, ownerEmail, ownerId, isPublic, description, defaultBranch, ownerIsAdmin, savedAt. |
| `Delete` | Removes `.meta.json` for a deleted repo. |
| `ScanAll` | Enumerates all `*.meta.json` in a directory; silently skips unparseable files. |

**Data type:** `RepoMetaFile` (all fields JSON-serialized)

---

### Backend — Controllers

#### `backend/Controllers/AuthController.cs` — `/api/auth`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `POST /api/auth/register` | Rate-limited | Register new user (requires admin JWT if users already exist) |
| `POST /api/auth/login` | Rate-limited | Email+password login; returns accessToken + refreshToken |
| `POST /api/auth/refresh` | None | Exchange refreshToken for new token pair |
| `POST /api/auth/logout` | JWT | Revoke one or all refresh tokens |
| `GET /api/auth/me` | JWT | Current user profile |
| `PUT /api/auth/profile` | JWT | Update displayName, bio, avatarUrl |
| `PUT /api/auth/password` | JWT | Change password (revokes all sessions) |
| `PUT /api/auth/email` | JWT | Change email (requires current password) |
| `GET /api/auth/settings` | JWT | Fetch user preferences |
| `PUT /api/auth/settings` | JWT | Update user preferences |
| `GET /api/auth/sessions` | JWT | List active sessions |
| `DELETE /api/auth/sessions/{id}` | JWT | Revoke specific session |
| `GET /api/auth/users` | JWT admin | List all users |
| `DELETE /api/auth/users/{id}` | JWT admin | Delete user (cannot delete self) |

---

#### `backend/Controllers/ReposController.cs` — `/api/repos`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /api/repos` | None | List repos; supports `?search=` and `?publicOnly=true` |
| `GET /api/repos/{name}` | None | Repo details: branches, last commit, write-access flag |
| `POST /api/repos` | JWT | Create repo (init git, write README, write `.meta.json`) |
| `DELETE /api/repos/{name}` | JWT owner/admin | Delete repo from disk and DB |
| `PATCH /api/repos/{name}` | JWT owner/admin | Update description / defaultBranch; keeps `.meta.json` in sync |
| `POST /api/repos/recover` | Anonymous (bootstrap) / JWT admin | Scan `.meta.json` files and re-insert repos into DB; creates placeholder users if DB was fully wiped |

---

#### `backend/Controllers/FilesController.cs` — `/api/repos/{name}/...`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /{name}/files?path=` | None (public repos) | Directory listing with last-commit info per file |
| `GET /{name}/file?path=` | None (public repos) | File content; detects binary files |
| `POST /{name}/file` | JWT + write | Save/create file and commit |
| `DELETE /{name}/file` | JWT + write | Delete file and commit |
| `POST /{name}/push` | JWT + write | Bulk multipart upload and commit (100 MB limit) |
| `POST /{name}/upload-zip` | JWT + write | Extract ZIP and commit (200 MB limit); strips `node_modules` |
| `GET /{name}/download` | None (public repos) | Download full repo as ZIP |
| `GET /{name}/download/file` | None (public repos) | Download single file |
| `GET /{name}/download/folder` | None (public repos) | Download folder as ZIP via `git archive` |

Path traversal is blocked via `SafeJoin` validation. Binary detection scans first 512 bytes.

---

#### `backend/Controllers/BranchesController.cs` — `/api/repos/{name}/...`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /{name}/branches` | None | List branches with current-branch indicator |
| `POST /{name}/branches` | JWT + write | Create branch |
| `POST /{name}/checkout` | JWT + write | Checkout branch |
| `POST /{name}/merge` | JWT + write | Merge source into target; reports conflicts |
| `DELETE /{name}/branches/{branch}` | JWT + write | Delete branch (cannot delete currently checked-out branch) |

Branch names validated against `^[a-zA-Z0-9_\-\/\.]+$`.

---

#### `backend/Controllers/CommitsController.cs` — `/api/repos/{name}/...`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /{name}/commits?branch=&limit=` | None | Commit list (default 50, max 500) |
| `GET /{name}/commits/{hash}` | None | Commit detail with full unified diff (`git show --patch`) |
| `GET /{name}/diff?from=&to=` | None | Diff between two refs + commit list between them |

Output is unified diff format, consumed by diff2html on the frontend.

---

#### `backend/Controllers/IssuesController.cs` — `/api/repos/{repoName}/issues`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET` | None (public repos) | List issues; filter by `?status=open\|closed` |
| `GET /{number}` | None (public repos) | Issue detail with all comments |
| `POST` | JWT | Create issue (auto-increments number per repo) |
| `PATCH /{number}` | JWT (author or write collaborator) | Update title/body/status |
| `POST /{number}/comments` | JWT + read access | Add comment |

---

#### `backend/Controllers/LogsController.cs` — `/api/logs`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /api/logs` | None | Query activity log; filters: repo, event_type, from, to, limit (max 500), offset |
| `GET /api/logs/event-types` | None | List distinct event types for UI filter dropdown |

---

#### `backend/Controllers/HealthController.cs` — `/api/health`

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /api/health` | None | Returns `{ status: "ok", message: "GitXO backend is running" }` |

---

#### `backend/Controllers/GitController.cs` — `/api/git`

Implements the **Git Smart HTTP protocol** so native `git clone` and `git push` work against GitXO.

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /{*path}?service=git-upload-pack` | None (public) / Basic (private) | Advertise refs for clone/fetch |
| `POST /{*path}/git-upload-pack` | None (public) / Basic (private) | Pack protocol for clone/fetch |
| `POST /{*path}/git-receive-pack` | Basic (always) | Pack protocol for push |

Public repos allow unauthenticated clone. All pushes require HTTP Basic auth (GitXO username + password). Streams large payloads directly without buffering. Helper `ParseGitPath` extracts repo name and service name from the URL path (tuple element named `Service`, not `Rest` — reserved in C# ValueTuple).

---

### Backend — Migrations

| File | Description |
|------|-------------|
| `backend/Migrations/001_schema.sql` | Initial schema: `users`, `repositories`, `repo_collaborators`, `issues`, `issue_comments`, `activity_logs` with all base indexes |
| `backend/Migrations/002_refresh_tokens.sql` | Adds `refresh_tokens` table for JWT session management |
| `backend/Migrations/003_user_settings.sql` | Adds `user_settings` table; adds optimized active-sessions index on `refresh_tokens` |
| `backend/Migrations/004_write_permissions_indexes.sql` | Adds indexes: `idx_repos_name`, `idx_collab_write_lookup`, `idx_logs_event_type`, `idx_logs_user_id` |
| `backend/Migrations/005_login_security.sql` | Adds `failed_login_attempts` and `locked_until` columns to `users`; sparse index on `locked_until` |

Run all in order: `001` → `002` → `003` → `004` → `005`.

---

### Frontend — Core

| File | Purpose |
|------|---------|
| `frontend/src/index.js` | React entry point. Renders `<App />` in `React.StrictMode` into `#root`. |
| `frontend/src/App.js` | Root component. Wraps app in `AuthProvider`. Defines all client routes (see below). Renders `<Navbar />` on every page. |
| `frontend/craco.config.js` | CRA config override (via `@craco/craco`). Currently minimal. |

**Routes defined in App.js:**

| Path | Component | Guard |
|------|-----------|-------|
| `/login` | LoginPage | Public |
| `/` | ExplorePage | PrivateRoute |
| `/repos/:repoName/*` | RepoView | PrivateRoute |
| `/activity` | ActivityLog | PrivateRoute |
| `/settings` | SettingsPage | PrivateRoute |

---

### Frontend — Context & Auth

#### `frontend/src/contexts/AuthContext.js`
Global auth state. Manages `user`, `token`, and `refreshToken` in localStorage.

| Export | Description |
|--------|-------------|
| `AuthProvider` | Wraps app; loads persisted tokens on boot, auto-refreshes on startup |
| `useAuth()` | Hook returning `{ user, token, loading, login, logout, refreshUser, setUser }` |

On 401 responses, dispatches `gitxoAuthExpired` custom event; `AuthContext` listens and calls `logout()` automatically. LocalStorage keys: `gitxo_access_token`, `gitxo_refresh_token`.

---

### Frontend — API Client

#### `frontend/src/api/index.js`
Centralized HTTP client. Auto-injects `Authorization: Bearer <token>` on every request. On 401, calls `tryRefresh()` and retries once before emitting `gitxoAuthExpired`.

**Exported `api` object — all methods:**

| Category | Methods |
|----------|---------|
| Auth | `login`, `register`, `refresh`, `logout`, `getMe`, `updateProfile`, `changePassword`, `changeEmail`, `getSettings`, `updateSettings`, `getSessions`, `revokeSession`, `listUsers`, `deleteUser` |
| Repos | `listRepos`, `getRepo`, `createRepo`, `deleteRepo` |
| Files | `listFiles`, `getFile`, `saveFile`, `deleteFile`, `pushFiles`, `uploadZip`, `uploadZipWithProgress` |
| Branches | `listBranches`, `createBranch`, `checkoutBranch`, `mergeBranch`, `deleteBranch` |
| Commits | `listCommits`, `getCommit`, `getBranchDiff` |
| Issues | `listIssues`, `getIssue`, `createIssue`, `updateIssue`, `addComment` |
| Logs | `getLogs`, `getLogEventTypes` |
| URL builders | `getFileDownloadUrl`, `getRepoDownloadUrl`, `getFolderDownloadUrl` |

`uploadZipWithProgress` uses `XMLHttpRequest` (not fetch) so `xhr.upload.onprogress` fires during upload.

---

### Frontend — Components

#### `frontend/src/components/Navbar.js`
Top navigation bar. Shows GitXO logo, Explore link, Activity link (admin only), and a user dropdown (avatar → username/email, Settings, Sign out). Shows "Sign in" button if unauthenticated. Dropdown closes on outside click.

---

#### `frontend/src/components/PrivateRoute.js`
Route guard. Shows loading spinner while auth resolves. Redirects to `/login` (with `location.state.from`) if unauthenticated. Renders children if authenticated.

---

#### `frontend/src/components/RepoView.js`
Repository detail page shell. Fetches repo metadata and renders tabbed interface:

| Tab | Component | Visibility |
|-----|-----------|------------|
| Code | FileBrowser | All |
| Commits | CommitHistory | All |
| Branches | BranchManager | All |
| Issues | IssuesPage | All |
| Push | PushPanel | Write access only |

Also renders: repo name + description header, owner badge, public/private badge, clone URL (copy-to-clipboard), ZIP download button, delete button (owner/admin only), git push instructions for write users.

---

#### `frontend/src/components/FileBrowser.js`
File/directory explorer. Features: breadcrumb navigation, file icons by extension, last-commit message+date per entry, relative timestamps, click-through navigation. If `canWrite=true`: shows `+ New file` button that opens an inline editor (filename scoped to current path, content textarea, commit message, Commit/Cancel). Empty-dir CTA also shown for write users.

---

#### `frontend/src/components/FileViewer.js`
Single file viewer/editor. Syntax highlighting via `react-syntax-highlighter` (vscDarkPlus, 30+ languages). Edit mode opens a textarea; saving commits the change. Delete file commits deletion. Download button available. Binary files detected and handled gracefully. Breadcrumb back-navigation included.

---

#### `frontend/src/components/CommitHistory.js`
Commit log viewer. Lists commits for the selected branch (newest first) with message, author, and date. "View diff" toggle loads and renders the patch with diff2html (side-by-side). Diffs are fetched lazily on demand.

---

#### `frontend/src/components/BranchManager.js`
Branch management UI. Lists all branches with current-branch indicator. Actions: create (from any existing branch), checkout, delete (blocked if currently checked out). Compare tab: select from/to branches, view commit count ahead, list of commits between them, rendered side-by-side diff via diff2html.

---

#### `frontend/src/components/PushPanel.js`
Advanced upload interface. Single drop zone accepts files, folders (via `webkitdirectory`), or a `.zip`. Three staging buttons: Select Files / Select Folder / Select ZIP. Staged files shown in collapsible tree (dirs expand/collapse, files show size + remove button). `node_modules` filtered at ingestion. Client-side JSZip DEFLATE compression for files/folders before upload. Two-phase progress bar: compress 0–50%, upload 50–100%. Commit form: branch selector, commit message, optional author name/email. ZIP files are sent raw without re-compression.

---

#### `frontend/src/components/ActivityLog.js`
Admin audit log viewer. Table of system events with filters: repo name, event type, date range, page size. Pagination. Auto-refresh toggle (10-second interval). Expandable rows for JSONB detail metadata. Event type options populated from server. Write events highlighted.

---

### Frontend — Pages

#### `frontend/src/pages/ExplorePage.js`
Homepage / repo discovery. Hero section with search input. Repo grid cards showing name, description, owner, public/private badge, current branch, last commit. Create repo dialog (name, description, public/private). Delete button visible to repo owner. Empty state with call-to-action.

---

#### `frontend/src/pages/LoginPage.js`
Login form. Email + password fields, loading state, error display. On success, redirects to `location.state.from` or `/`.

---

#### `frontend/src/pages/IssuesPage.js`
Issue list for a repo. Open/Closed filter tabs. Create issue dialog (title, body). List rows show issue number, title, author, creation date, comment count. Click to navigate to IssueDetail.

---

#### `frontend/src/pages/IssueDetail.js`
Single issue view. Shows title, body, status, author, dates. Comment thread below. Add comment form. Status toggle (open/close) for author or write collaborators.

---

#### `frontend/src/pages/SettingsPage.js`
User account settings. Sections: profile (displayName, bio, avatarUrl), password change, email change, preferences (theme, defaultBranch, email notification toggles), session management (list active sessions, revoke individual sessions or all at once).

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

---

#### Feature: Login-First Navigation & Security Hardening

**Problem:** The app root (`/`) loaded `ExplorePage` without requiring authentication. No rate limiting, no account lockout, and no security headers were in place.

**Changes:**

- **[frontend/src/App.js]** Wrapped `ExplorePage` in `<PrivateRoute>` so `/` now redirects unauthenticated visitors to `/login`. After login, `PrivateRoute` automatically redirects back to the originally requested path via `location.state.from`.

- **[backend/Program.cs]**
  - Added `using System.Threading.RateLimiting` and `using Microsoft.AspNetCore.RateLimiting` (both built into .NET 8 — no new NuGet packages needed).
  - Registered a `"auth"` rate-limit policy using `AddRateLimiter` + `RateLimitPartition.GetFixedWindowLimiter`: max **10 requests per IP per 5 minutes** on auth endpoints; returns HTTP 429 when exceeded.
  - Added `app.UseRateLimiter()` in the middleware pipeline (before `UseAuthentication`).
  - Added a security-headers middleware before the rate limiter:
    - `X-Content-Type-Options: nosniff` — prevents MIME-type sniffing
    - `X-Frame-Options: DENY` — blocks clickjacking via iframes
    - `Referrer-Policy: strict-origin-when-cross-origin`
    - `X-XSS-Protection: 1; mode=block`
    - `Permissions-Policy: geolocation=(), camera=(), microphone=()`

- **[backend/Controllers/AuthController.cs]**
  - Added `using Microsoft.AspNetCore.RateLimiting`.
  - Applied `[EnableRateLimiting("auth")]` to both `Register` and `Login` action methods.

- **[backend/Services/AuthService.cs]**
  - **Password minimum length raised from 8 → 12 characters** in `RegisterAsync`.
  - **Account lockout logic added to `LoginAsync`**:
    - Reads new `failed_login_attempts` (int) and `locked_until` (timestamptz) columns from the `users` row.
    - If `locked_until > NOW()`, rejects login immediately with remaining time in minutes.
    - On wrong password: increments `failed_login_attempts`; if it reaches **10**, sets `locked_until = NOW() + 15 minutes`.
    - On correct password: resets both columns to `0` / `NULL`.
  - **`ChangePasswordAsync`**: reset also clears `failed_login_attempts = 0` and `locked_until = NULL` so an admin password reset always unlocks the account.

- **[backend/Migrations/005_login_security.sql]** New migration:
  - `ALTER TABLE users ADD COLUMN IF NOT EXISTS failed_login_attempts INT NOT NULL DEFAULT 0`
  - `ALTER TABLE users ADD COLUMN IF NOT EXISTS locked_until TIMESTAMPTZ`
  - Sparse index on `locked_until WHERE locked_until IS NOT NULL` for efficient lockout queries.

#### What you need to do
1. Run the migration: `psql -U postgres -d gitxo -f backend/Migrations/005_login_security.sql`
2. Restart the backend server so the new rate limiter and security headers middleware take effect.
3. Nothing else — rate limiting is built into .NET 8, no new NuGet packages required.

---

#### Documentation: Full File Reference Section Added

**Changes:**

- **[ClaudeContextGitXO.md]** Added new **"File Reference"** section between "Dev Notes" and "Changelog". Covers all 43 source files across root, backend, and frontend layers. Each file is documented with: purpose, key methods/endpoints/components, auth requirements, data types, and notable implementation details. Structured as tables and subsections for fast lookup. Written by reading every actual source file, not inferred from filenames.

#### What you need to do
Nothing — documentation-only change, no code modified.

---

#### Rebrand: Rename "GitXO" → "GitRipp" in UI Display Text

**Changes:**

- **[frontend/src/components/Navbar.js]** Brand name in nav bar: `GitXO` → `GitRipp`
- **[frontend/src/pages/LoginPage.js]** Login page heading: `Sign in to GitXO` → `Sign in to GitRipp`
- **[frontend/src/components/ActivityLog.js]** Empty-state message: `...start using GitXO` → `...start using GitRipp`
- **[frontend/src/pages/SettingsPage.js]** Two strings updated: profile helper text and Preferences section subtitle
- **[frontend/src/components/PushPanel.js]** Three placeholder strings updated: commit message input, author name input, author email input (`gitxo@local` → `gitripp@local`)
- **[frontend/src/components/RepoView.js]** Git push credential hint: `your GitXO username + password` → `your GitRipp username + password`

Internal identifiers left unchanged (localStorage keys `gitxo_access_token`/`gitxo_refresh_token`, custom event name `gitxoAuthExpired`) — these are implementation details, not visible to users.

#### What you need to do
Nothing — frontend will hot-reload automatically if the dev server is running.

---

### 2026-02-23

#### Registration: Any Signed-In User Can Add New Users, No Public Registration

**Problem:** A standalone `RegisterPage.js` existed as dead code (no route in `App.js`), the backend password minimum (12 characters) was inconsistent with the frontend forms (8 characters), and the "Add user" form was hidden inside the admin-only tab — only admins could create new users.

**Changes:**

- **[frontend/src/pages/RegisterPage.js]** Deleted. This file was orphaned — it had no route in `App.js` and was never reachable by users.

- **[backend/Services/AuthService.cs]** Changed password minimum in `RegisterAsync` from 12 → 8 characters to match the frontend validation and the `ChangePasswordAsync` minimum.

- **[backend/Controllers/AuthController.cs]** Changed `POST /api/auth/register` authorization: now allows **any authenticated user** to create new users (previously required admin). The check changed from `callerIsAdmin` to `callerIsAuthenticated`. Unauthenticated requests are still rejected when users exist.

- **[frontend/src/pages/SettingsPage.js]**
  - Extracted the "Add user" form out of `AdminTab` into a new `AddUserTab` component.
  - Added "Add User" as a sidebar tab visible to **all signed-in users** (not just admins).
  - `AdminTab` now only contains the user list with delete buttons (admin-only).

**Registration flow summary:** The first user to register becomes admin (bootstrap). After that, any signed-in user can create new users via **Settings → Add User**. There is no public-facing registration page. Admins additionally see **Settings → Admin — Users** for listing/deleting users.

#### What you need to do
1. Restart the backend server to pick up the auth and password changes.
2. Nothing else — frontend will hot-reload automatically.

---

#### Documentation: Recovery & Backup Guides

**Changes:**

- **[docs/RECOVERY-GUIDE.md]** Created. Step-by-step recovery procedures for 9 crash scenarios: backend crash, frontend crash, PostgreSQL down, database wipe, all users lost, repos missing, JWT secret changed, account lockout, and full system recovery. Includes quick reference table (paths, credentials, ports), recovery flowchart, and post-recovery verification checklist.

- **[docs/BACKUP-GUIDE.md]** Created. Covers what to back up (database, repos, config), how to do manual and scheduled backups on both Windows and Linux, how to verify backup integrity, restore from backups, and what data is lost without backups. Includes recommended backup schedule and storage estimates.

#### What you need to do
Nothing — documentation-only change, no code modified.
