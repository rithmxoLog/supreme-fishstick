# GitXO (GitRipp) — Complete Project Context

> Self-hosted GitHub-like web application for managing Git repositories, issues, and collaboration.

---

## Table of Contents

1. [Tech Stack & Dependencies](#1-tech-stack--dependencies)
2. [Project Structure](#2-project-structure)
3. [Startup & Configuration](#3-startup--configuration)
4. [Database Schema](#4-database-schema)
5. [Backend — Program.cs Setup](#5-backend--programcs-setup)
6. [Backend — API Endpoints](#6-backend--api-endpoints)
7. [Backend — Services](#7-backend--services)
8. [Backend — Security Features](#8-backend--security-features)
9. [Frontend — Routing & Layout](#9-frontend--routing--layout)
10. [Frontend — Auth & API Client](#10-frontend--auth--api-client)
11. [Frontend — Components](#11-frontend--components)
12. [Frontend — Pages](#12-frontend--pages)
13. [Frontend — Styling](#13-frontend--styling)
14. [Git Integration](#14-git-integration)
15. [Deployment](#15-deployment)

---

## 1. Tech Stack & Dependencies

### Backend (C# / ASP.NET Core 8)

| Package | Version | Purpose |
|---------|---------|---------|
| .NET 8.0 | net8.0 | Runtime/framework |
| Npgsql | 8.0.5 | PostgreSQL driver |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.0 | JWT authentication |
| BCrypt.Net-Next | 4.0.3 | Password hashing |

### Frontend (React 18)

| Package | Version | Purpose |
|---------|---------|---------|
| react | 18.2.0 | UI framework |
| react-dom | 18.2.0 | DOM rendering |
| react-router-dom | 6.21.1 | Client-side routing |
| react-syntax-highlighter | 15.5.0 | Code syntax highlighting (vscDarkPlus) |
| diff2html | 3.4.56 | Side-by-side diff rendering |
| jszip | 3.10.1 | Client-side ZIP compression |
| timeago.js | 4.0.2 | Relative time formatting |
| yaml | 2.8.2 | YAML parsing |
| @craco/craco | 7.1.0 | CRA webpack override |
| tailwindcss | (via craco) | Utility CSS framework |

---

## 2. Project Structure

```
GitXO/
├── backend/
│   ├── GitXO.Api.csproj
│   ├── Program.cs                        # App setup, middleware, JWT, CORS, rate limiting
│   ├── appsettings.json                  # DB, JWT, port config
│   ├── Controllers/
│   │   ├── AuthController.cs             # Auth endpoints (register, login, sessions, profile)
│   │   ├── ReposController.cs            # Repo CRUD + recovery
│   │   ├── FilesController.cs            # File ops, push, download, ZIP upload
│   │   ├── BranchesController.cs         # Branch CRUD, merge, checkout
│   │   ├── CommitsController.cs          # Commit history + diffs
│   │   ├── IssuesController.cs           # Issues + comments
│   │   ├── GitController.cs              # Git Smart HTTP protocol (clone/push)
│   │   ├── LogsController.cs             # Activity log queries
│   │   └── HealthController.cs           # Health checks
│   ├── Services/
│   │   ├── AuthService.cs                # JWT, BCrypt, user CRUD, sessions
│   │   ├── GitRunner.cs                  # Static git CLI wrapper
│   │   ├── ActivityLogger.cs             # Fire-and-forget PostgreSQL logging
│   │   ├── RepoDbService.cs              # Repo DB CRUD, access control
│   │   └── RepoMetaWriter.cs             # .meta.json backup/recovery files
│   ├── Migrations/
│   │   ├── 001_schema.sql                # Core schema (embedded resource)
│   │   ├── 002_refresh_tokens.sql        # Refresh token sessions
│   │   ├── 003_user_settings.sql         # User settings & preferences
│   │   ├── 004_indexes.sql               # Performance indexes
│   │   └── 005_account_lockout.sql       # Login lockout columns
│   └── repositories/                     # Git repos on disk (public)
│
├── frontend/
│   ├── package.json
│   ├── tailwind.config.js
│   ├── craco.config.js
│   └── src/
│       ├── App.js                        # Router + AuthProvider wrapper
│       ├── App.css                       # Global dark theme styles
│       ├── api/
│       │   └── index.js                  # Unified API client (auto JWT injection)
│       ├── contexts/
│       │   └── AuthContext.js             # JWT auth provider + token refresh
│       ├── components/
│       │   ├── Navbar.js                 # Top nav + user dropdown
│       │   ├── PrivateRoute.js           # Auth guard
│       │   ├── RepoView.js              # Repo detail shell (tab switcher)
│       │   ├── FileBrowser.js            # Directory tree + file creation
│       │   ├── FileViewer.js             # Syntax-highlighted file viewer/editor
│       │   ├── CommitHistory.js          # Commit list with expandable diffs
│       │   ├── BranchManager.js          # Branch list, create, merge, compare
│       │   ├── PushPanel.js              # Drag-drop upload with progress
│       │   ├── RepoList.js              # Repo list + create modal
│       │   └── ActivityLog.js            # Event log with filters + pagination
│       └── pages/
│           ├── LoginPage.js              # Sign-in form
│           ├── ExplorePage.js            # Browse & create repos
│           ├── IssuesPage.js             # Issues list + create
│           ├── IssueDetail.js            # Issue detail + comments
│           └── SettingsPage.js           # Multi-tab settings dashboard
│
├── start.bat / start.sh                  # One-command launcher (both servers)
├── .ad.json                              # Deployment config
├── GitXO.sln                             # Visual Studio solution
├── README.md                             # Setup guide
├── GitXO-BACKUP-GUIDE.md                 # Backup procedures
├── GitXO-RECOVERY-GUIDE.md               # Disaster recovery
└── .gitignore                            # node_modules/
```

---

## 3. Startup & Configuration

### Ports

| Service | Default Port | Override |
|---------|-------------|---------|
| Backend | 3001 | `APP_PORT` env var |
| Frontend | 3000 | (CRA default) |

### appsettings.json

```
Jwt:Secret              — HMAC-SHA256 signing key (min 32 chars)
Jwt:Issuer              — "GitXO"
Jwt:Audience            — "GitXO"
Jwt:ExpiryHours         — 1 (access token lifetime)
Jwt:RefreshTokenExpiryDays — 30 (refresh token lifetime)
Postgres:Host           — localhost
Postgres:Port           — 5432
Postgres:Database       — gitxo
Postgres:Username       — postgres
Postgres:Password       — (configured per environment)
ReposDirectory          — "repositories"
```

### Environment Variables

| Variable | Purpose |
|----------|---------|
| `APP_PORT` | Override backend port (default 3001) |
| `RITHM_DATA_DIR` | Writable data directory (fallback: `../data` relative to binary) |

### Startup Scripts

- **start.bat** (Windows) / **start.sh** (Linux/macOS): Check prerequisites (.NET SDK, Node.js), restore dependencies, launch both servers.
- Frontend `proxy` in package.json forwards `/api` to `http://localhost:3001`.

---

## 4. Database Schema

### Tables

#### users
| Column | Type | Notes |
|--------|------|-------|
| id | BIGSERIAL PK | |
| username | TEXT UNIQUE NOT NULL | 3-30 chars, alphanumeric + dashes/underscores |
| email | TEXT UNIQUE NOT NULL | |
| password_hash | TEXT NOT NULL | BCrypt |
| display_name | TEXT | |
| avatar_url | TEXT | |
| bio | TEXT | |
| is_admin | BOOLEAN DEFAULT FALSE | First user auto-admin |
| failed_login_attempts | INT DEFAULT 0 | Account lockout counter |
| locked_until | TIMESTAMPTZ | Lockout expiry |
| created_at | TIMESTAMPTZ | |
| updated_at | TIMESTAMPTZ | |

#### repositories
| Column | Type | Notes |
|--------|------|-------|
| id | BIGSERIAL PK | |
| name | TEXT UNIQUE NOT NULL | Alphanumeric + dashes/underscores/dots |
| description | TEXT | |
| owner_id | BIGINT FK users(id) CASCADE | |
| is_public | BOOLEAN DEFAULT TRUE | |
| default_branch | TEXT DEFAULT 'main' | |
| created_at | TIMESTAMPTZ | |
| updated_at | TIMESTAMPTZ | |

#### repo_collaborators
| Column | Type | Notes |
|--------|------|-------|
| repo_id | BIGINT FK repositories(id) CASCADE | |
| user_id | BIGINT FK users(id) CASCADE | |
| role | TEXT CHECK ('read', 'write', 'admin') | |
| granted_at | TIMESTAMPTZ | |
| PK | (repo_id, user_id) | |

#### issues
| Column | Type | Notes |
|--------|------|-------|
| id | BIGSERIAL PK | |
| repo_id | BIGINT FK repositories(id) CASCADE | |
| number | INT NOT NULL | Sequential per repo |
| author_id | BIGINT FK users(id) | |
| title | TEXT NOT NULL | |
| body | TEXT | |
| status | TEXT DEFAULT 'open' CHECK ('open', 'closed') | |
| created_at | TIMESTAMPTZ | |
| updated_at | TIMESTAMPTZ | |
| UNIQUE | (repo_id, number) | |

#### issue_comments
| Column | Type | Notes |
|--------|------|-------|
| id | BIGSERIAL PK | |
| issue_id | BIGINT FK issues(id) CASCADE | |
| author_id | BIGINT FK users(id) | |
| body | TEXT NOT NULL | |
| created_at | TIMESTAMPTZ | |

#### refresh_tokens
| Column | Type | Notes |
|--------|------|-------|
| id | BIGSERIAL PK | |
| user_id | BIGINT FK users(id) CASCADE | |
| token_hash | TEXT UNIQUE NOT NULL | SHA256 of raw token |
| expires_at | TIMESTAMPTZ NOT NULL | |
| created_at | TIMESTAMPTZ | |
| revoked_at | TIMESTAMPTZ | null = active |
| user_agent | TEXT | Session identification |

#### user_settings
| Column | Type | Notes |
|--------|------|-------|
| user_id | BIGINT PK FK users(id) CASCADE | |
| theme | TEXT DEFAULT 'dark' | |
| default_branch | TEXT DEFAULT 'main' | |
| show_email | BOOLEAN DEFAULT FALSE | |
| email_on_push | BOOLEAN DEFAULT FALSE | |
| email_on_issue | BOOLEAN DEFAULT FALSE | |
| updated_at | TIMESTAMPTZ | |

#### activity_logs
| Column | Type | Notes |
|--------|------|-------|
| id | BIGSERIAL PK | |
| event_type | TEXT NOT NULL | |
| repo_name | TEXT | |
| details | JSONB DEFAULT '{}' | |
| created_at | TIMESTAMPTZ | |
| user_id | BIGINT FK users(id) | nullable |

### Key Indexes

- `idx_repos_owner` ON repositories(owner_id)
- `idx_repos_public` ON repositories(is_public) WHERE is_public = TRUE
- `idx_repos_name` ON repositories(name)
- `idx_issues_repo` ON issues(repo_id)
- `idx_issues_status` ON issues(status)
- `idx_collab_user` ON repo_collaborators(user_id)
- `idx_collab_write_lookup` ON repo_collaborators(repo_id, user_id) WHERE role IN ('write', 'admin')
- `idx_logs_created` ON activity_logs(created_at DESC)
- `idx_logs_repo` ON activity_logs(repo_name)
- `idx_logs_event_type` ON activity_logs(event_type)
- `idx_refresh_tokens_hash` ON refresh_tokens(token_hash)
- `idx_refresh_tokens_user_active` ON refresh_tokens(user_id) WHERE revoked_at IS NULL

---

## 5. Backend — Program.cs Setup

### Middleware Pipeline (order)
1. CORS (AllowAnyOrigin, AllowAnyMethod, AllowAnyHeader)
2. Security Headers (X-Content-Type-Options, X-Frame-Options, Referrer-Policy, X-XSS-Protection, Permissions-Policy)
3. Rate Limiting ("auth" policy: 10 req/5 min per IP on auth endpoints)
4. Authentication (JWT Bearer, HMAC-SHA256, 1 min clock skew)
5. Authorization
6. Controller Mapping

### Registered Services
- `AuthService` (singleton) — user CRUD, JWT, BCrypt, sessions
- `RepoDbService` (singleton) — repo DB CRUD, access control
- `ActivityLogger` (singleton) — fire-and-forget logging

### Startup Behavior
- Creates `repositories/` and `repositories-private/` directories
- Runs embedded 001_schema.sql migration (idempotent with IF NOT EXISTS)
- Kestrel + multipart form limit: 200 MB

---

## 6. Backend — API Endpoints

### Authentication (`/api/auth`)

| Method | Route | Auth | Rate Limited | Description |
|--------|-------|------|-------------|-------------|
| POST | /register | Required (unless first user) | Yes | Create user. First user = admin. BCrypt hash. Validates: username 3-30 chars, email has @, password 8+ chars. |
| POST | /login | None | Yes | Authenticate, return access + refresh tokens. Account lockout: 10 failures = 15 min lock. |
| POST | /refresh | None | No | Exchange refresh token for new access + refresh pair. Rotates tokens. |
| POST | /logout | Required | No | Revoke single refresh token or all sessions. |
| GET | /me | Required | No | Current user profile (id, username, email, isAdmin, displayName, bio, avatarUrl). |
| PUT | /profile | Required | No | Update displayName, bio, avatarUrl. |
| PUT | /password | Required | No | Change password (verify current first). Revokes all sessions. |
| PUT | /email | Required | No | Change email (verify password first). |
| GET | /settings | Required | No | User preferences (theme, defaultBranch, showEmail, notifications). |
| PUT | /settings | Required | No | Update preferences (upsert). |
| GET | /sessions | Required | No | List active sessions (not revoked, not expired). |
| DELETE | /sessions/{id} | Required | No | Revoke a specific session (own sessions only). |
| GET | /users | Required + Admin | No | List all users. |
| DELETE | /users/{id} | Required + Admin | No | Delete user (cannot delete self). |

### Repositories (`/api/repos`)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | / | None | List repos. Query: `search`, `publicOnly`. Private repos filtered by ownership/collaboration. Returns name, description, currentBranch, branches, lastCommit, isPublic, owner. |
| GET | /{name} | None | Repo detail. Returns canWrite flag. 404 if private and no access. |
| POST | / | Required | Create repo. Inits git, creates README, inserts DB record, writes .meta.json. |
| DELETE | /{name} | Required | Delete repo directory + DB record + .meta.json. |
| PATCH | /{name} | Required (owner/admin) | Update description, defaultBranch. |
| POST | /recover | Admin (or unauthenticated if 0 users) | Recover repos from .meta.json files. Creates placeholder users if needed. |

### Files (`/api/repos/{name}`)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | /files?path= | None (read check) | List directory contents. Sorted: dirs first, then alphabetical. Includes last commit per entry. |
| GET | /file?path= | None (read check) | Get file content. Binary detection (null bytes in first 512 bytes). Returns content or null if binary. |
| POST | /file | Required + write | Create/update file with commit message. Supports branch targeting. |
| DELETE | /file | Required + write | Delete file with commit message. |
| POST | /push | Required + write | Multi-file upload (FormData, 100 MB limit). Supports author override. Skips node_modules. |
| POST | /upload-zip | Required + write | ZIP upload with server-side extraction (200 MB limit). Strips common prefix. Skips node_modules. |
| GET | /download?branch= | None | Download entire repo as ZIP (`git archive`). |
| GET | /download/file?path=&branch= | None (read check) | Download single file (application/octet-stream). |
| GET | /download/folder?path=&branch= | None | Download folder as ZIP (`git archive`). |

### Branches (`/api/repos/{name}`)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | /branches | None | List branches + current branch. |
| POST | /branches | Required + write | Create branch (optional fromBranch). Name regex: `^[a-zA-Z0-9_\-\/\.]+$`. |
| POST | /checkout | Required + write | Switch to branch. |
| POST | /merge | Required + write | Merge source into target (`--no-ff`). Returns 409 on conflict. |
| DELETE | /branches/{branch} | Required + write | Force-delete branch (`git branch -D`). |

### Commits (`/api/repos/{name}`)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | /commits?branch=&limit=50 | None | Commit history (1-500 limit). Format: hash, shortHash, author, email, date, message. |
| GET | /commits/{hash} | None | Single commit detail + unified diff. |
| GET | /diff?from=&to= | None | Diff between two refs. Returns diff text + commits list. |

### Issues (`/api/repos/{repoName}/issues`)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | / | None | List issues. Filter by status (open/closed). Includes commentCount. Repo must exist in DB. |
| GET | /{number} | None | Issue detail + all comments. |
| POST | / | Required | Create issue (title required, body optional). Auto-increments number per repo. |
| PATCH | /{number} | Required (author or collaborator) | Update status, title, body (all optional). |
| POST | /{number}/comments | Required | Add comment. Updates issue's updated_at. Private repos require read access. |

### Git Smart HTTP (`/api/git`)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | /{*path} | Basic Auth (private repos or push) | Git info/refs advertisement for clone/fetch/push. |
| POST | /{*path} | Basic Auth (private repos or push) | Data transfer for clone/fetch/push (500 MB limit). Streams stdin/stdout. |

Supports `git clone`, `git fetch`, and `git push` via Smart HTTP protocol. Public repos allow anonymous clone. Private repos and all pushes require HTTP Basic Auth (username + password).

### Activity Logs (`/api/logs`)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | / | None | Query logs. Filters: repo, event_type, from, to, limit (1-500), offset. Returns total count. |
| GET | /event-types | None | List distinct event types. |

### Health (`/api/health`)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | /api/health, /health, /health/ready, /health/live | None | Returns `{ status: "ok" }`. |

---

## 7. Backend — Services

### AuthService (`Services/AuthService.cs`)

**User Management:**
- `RegisterAsync(username, email, password)` — BCrypt hash, first user = admin
- `LoginAsync(email, password)` — Verify password, account lockout (10 failures = 15 min)
- `GetUserByIdAsync(id)` — Fetch user
- `ListUsersAsync()` — All users (admin panel)
- `DeleteUserAsync(userId)` — Delete user (cascades)
- `VerifyUsernamePasswordAsync(username, password)` — For Git HTTP Basic Auth

**Profile:**
- `UpdateProfileAsync(userId, displayName, bio, avatarUrl)`
- `ChangePasswordAsync(userId, currentPassword, newPassword)` — Revokes all sessions
- `ChangeEmailAsync(userId, currentPassword, newEmail)`

**Tokens & Sessions:**
- `GenerateAccessToken(user)` — JWT with HMAC-SHA256
- `CreateRefreshTokenAsync(userId, userAgent)` — SHA256 hashed in DB
- `RefreshAsync(rawRefreshToken, userAgent)` — Rotate tokens (old revoked)
- `RevokeRefreshTokenAsync(rawRefreshToken)` — Single token
- `RevokeAllSessionsAsync(userId)` — All user sessions
- `GetActiveSessionsAsync(userId)` — List active (not revoked, not expired)
- `RevokeSessionByIdAsync(sessionId, requestingUserId)` — Own sessions only

**Settings:**
- `GetUserSettingsAsync(userId)` — Upserts default row
- `UpdateUserSettingsAsync(userId, settings)` — Upsert

**Utility:**
- `GetTotalUsersAsync()` — Count users (bootstrap detection)

### GitRunner (`Services/GitRunner.cs`) — Static

- `RunAsync(workingDir, args[])` — Execute git command, returns (stdout, stderr, exitCode)
- `StartStreaming(workingDir, args[])` — Start git process for I/O streaming
- `GetBranchesAsync(repoPath)` — Parse `git branch` output → (current, all)
- `ParseLogLine(line)` — Parse `%H|%h|%an|%ae|%ai|%s` format → CommitInfo

Uses `ProcessStartInfo` with `ArgumentList.Add()` (safe from injection). UTF-8 encoding.

### ActivityLogger (`Services/ActivityLogger.cs`)

- `LogEvent(eventType, repoName, details, userId)` — Fire-and-forget, JSONB details
- `TestConnectionAsync()` — Verify DB connection at startup

Logged event types: USER_CREATED, USER_LOGIN, USER_LOGOUT, PROFILE_UPDATED, PASSWORD_CHANGED, EMAIL_CHANGED, SESSION_REVOKED, USER_DELETED, REPOS_LISTED, REPO_ACCESSED, REPO_CREATED, REPO_DELETED, REPO_UPDATED, REPOS_RECOVERED, FILES_LISTED, FILE_ACCESSED, FILE_CREATED, FILE_UPDATED, FILE_DELETED, FILES_PUSHED, ZIP_UPLOADED, FILE_DOWNLOADED, FOLDER_DOWNLOADED, REPO_DOWNLOADED, BRANCHES_LISTED, BRANCH_CREATED, BRANCH_SWITCHED, BRANCH_MERGED, BRANCH_DELETED, COMMITS_LISTED, COMMIT_ACCESSED, BRANCH_DIFF, ISSUE_CREATED, ISSUE_UPDATED, GIT_PUSH

### RepoDbService (`Services/RepoDbService.cs`)

- `GetRepoPathAsync(name)` — Resolve filesystem path
- `GetTargetDir(isPublic)` — Base directory for new repos
- `GetRepoMetaAsync(name)` — Fetch RepoMeta (id, ownerId, isPublic, description, ownerUsername, defaultBranch)
- `CreateRepoMetaAsync(name, ownerId, isPublic, description)` — Insert (ON CONFLICT DO NOTHING)
- `DeleteRepoMetaAsync(name)` — Delete record
- `UpdateRepoAsync(name, description, defaultBranch)` — Update mutable fields
- `UserCanWriteAsync(repoName, userId)` — Owner OR collaborator with write/admin role
- `UserCanReadAsync(repoName, userId)` — Owner OR any collaborator
- `GetRepoIdAsync(name)` — Numeric repo ID
- `GetUserIdByUsernameAsync(username)` — For recovery
- `CreatePlaceholderUserAsync(username, email, isAdmin)` — Locked user during recovery
- `HasAnyUsersAsync()` — Bootstrap detection

### RepoMetaWriter (`Services/RepoMetaWriter.cs`) — Static

- `WriteAsync(...)` — Write `{repoName}.meta.json` for backup
- `Delete(reposBaseDir, repoName)` — Delete .meta.json
- `ScanAll(dir)` — Scan directory for all .meta.json files (recovery)

---

## 8. Backend — Security Features

### Authentication
- **JWT Access Tokens**: 1-hour expiry, HMAC-SHA256
- **Refresh Tokens**: 30-day expiry, SHA256 hashed in DB, rotated on use
- **Git HTTP Basic Auth**: Username + password for clone/push
- **Rate Limiting**: 10 requests per 5 minutes per IP on /api/auth/*
- **Account Lockout**: 10 failed login attempts = 15 minute lock

### Access Control
- **Public repos**: Readable by anyone (anonymous or authenticated)
- **Private repos**: Owner + collaborators only
- **Write access**: Owner OR collaborator with 'write'/'admin' role
- **Admin endpoints**: Require is_admin flag

### HTTP Security Headers
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `X-XSS-Protection: 1; mode=block`
- `Permissions-Policy: geolocation=(), camera=(), microphone=()`

### Input Validation
- Username: 3-30 chars, alphanumeric + dashes/underscores
- Email: must contain @
- Password: min 8 chars
- Repo name: alphanumeric + dashes/underscores/dots
- Branch name: `^[a-zA-Z0-9_\-\/\.]+$`
- File paths: SafeJoin() prevents path traversal

### Upload Limits
- Kestrel global: 200 MB
- /push endpoint: 100 MB
- /upload-zip: 200 MB
- /api/git POST: 500 MB

---

## 9. Frontend — Routing & Layout

### App.js Routes

| Path | Component | Auth Required | Notes |
|------|-----------|--------------|-------|
| `/login` | LoginPage | No | |
| `/` | ExplorePage | Yes (PrivateRoute) | Homepage, browse repos |
| `/repos/:repoName/*` | RepoView | Yes (PrivateRoute) | Repo detail with tabs |
| `/activity` | ActivityLog | Yes (PrivateRoute) | Admin-visible in navbar |
| `/settings` | SettingsPage | Yes (PrivateRoute) | Multi-tab settings |

### Layout
- `AuthProvider` wraps entire app (JWT context)
- `BrowserRouter` for client-side routing
- `Navbar` renders at top of every page
- `app-body` div contains route content

---

## 10. Frontend — Auth & API Client

### AuthContext (`contexts/AuthContext.js`)

**State**: `user`, `loading`, `token`

**LocalStorage Keys**: `gitxo_access_token`, `gitxo_refresh_token`

**Functions**:
- `login(email, password)` — POST /api/auth/login, store tokens, set user
- `logout()` — POST /api/auth/logout (best-effort), clear tokens
- `refreshUser()` — GET /api/auth/me, update user state
- Auto-refresh on boot via refresh token
- Listens for `gitxoAuthExpired` event to clear user

### API Client (`api/index.js`)

**Core**: `request(method, path, body, isFormData, _retry)` — unified HTTP client with:
- Auto `Authorization: Bearer <token>` injection
- Auto-retry on 401 (attempts token refresh)
- Dispatches `gitxoAuthExpired` on auth failure

**API Functions (38 total)**:

| Category | Functions |
|----------|-----------|
| Auth | `login`, `register`, `refresh`, `logout`, `getMe` |
| Profile | `updateProfile`, `changePassword`, `changeEmail`, `getSettings`, `updateSettings` |
| Sessions | `getSessions`, `revokeSession` |
| Admin | `listUsers`, `deleteUser` |
| Repos | `listRepos`, `getRepo`, `createRepo`, `deleteRepo` |
| Files | `listFiles`, `getFile`, `saveFile`, `deleteFile` |
| Upload | `pushFiles`, `uploadZip`, `uploadZipWithProgress` (XHR for progress tracking) |
| Branches | `listBranches`, `createBranch`, `checkoutBranch`, `mergeBranch`, `deleteBranch` |
| Commits | `listCommits`, `getCommit`, `getBranchDiff` |
| Issues | `listIssues`, `getIssue`, `createIssue`, `updateIssue`, `addComment` |
| Logs | `getLogs`, `getLogEventTypes` |
| Downloads | `getFileDownloadUrl`, `getRepoDownloadUrl`, `getFolderDownloadUrl` (URL builders) |

---

## 11. Frontend — Components

### Navbar (`components/Navbar.js`)
- Brand logo + "GitRipp" text (links to home)
- "Explore" button (always visible)
- "Activity" button (admin-only)
- User avatar (first letter) + username + dropdown:
  - User info header
  - "Settings" link
  - "Sign out" button
- Click-outside closes dropdown

### PrivateRoute (`components/PrivateRoute.js`)
- Shows loading spinner while authenticating
- Redirects to `/login` if unauthenticated (preserves intended location)
- Renders children if authenticated

### RepoView (`components/RepoView.js`)
- Repo header: breadcrumb, visibility badge, owner, delete button (owner-only), description
- Clone URL: `{origin}/api/git/{repoName}.git` with copy-to-clipboard
- ZIP download button for current branch
- Push instructions (if canWrite)
- Tab navigation: Code, Commits, Branches, Issues, Push (if canWrite)
- Branch selector dropdown
- Delegates to child components per tab

### FileBrowser (`components/FileBrowser.js`)
- Directory listing table: Name (with emoji icon by extension), Last commit, Date
- Breadcrumb navigation with clickable segments
- ".." parent directory row
- New file creation panel (filename + content + commit message)
- Folder ZIP download button
- Branch selector dropdown
- Clicking file → opens FileViewer; clicking dir → navigates into it

### FileViewer (`components/FileViewer.js`)
- Breadcrumb with clickable path parts
- Language tag (auto-detected from extension)
- Syntax highlighting via react-syntax-highlighter (vscDarkPlus theme, line numbers)
- Edit mode: textarea + commit message + save button
- Binary file handling: "Binary file (X bytes) — cannot display"
- Download button (direct link)
- Delete button (prompts for commit message)

### CommitHistory (`components/CommitHistory.js`)
- Commit count on current branch
- Each commit: message, author, date, short hash badge
- "View diff" / "Hide diff" toggle per commit
- Lazy-loads diff via API, renders with diff2html (side-by-side, dark theme)

### BranchManager (`components/BranchManager.js`)
- Branch list with icons, names, "current" badge
- Checkout / Delete buttons per branch
- Create Branch modal: name + optional fromBranch
- Merge Branches modal: source + target + optional message (409 on conflict)
- Compare Branches: from/to selector, shows commits ahead + diff2html visualization

### PushPanel (`components/PushPanel.js`)
- Drag-and-drop zone: files, folders, or .zip
- Buttons: "Select Files", "Select Folder", "Select ZIP"
- Recursive TreeNode file tree visualization (expand/collapse, remove, sizes)
- ZIP mode: shows ZIP info, server-side extraction
- Commit form: branch selector, commit message, advanced (author name/email)
- Two-phase progress bar: compression (0-50%) + upload (50-100%)
- Auto-filters node_modules, strips top-level folder

### RepoList (`components/RepoList.js`)
- Page header + "New repository" button
- Repo items: name link, description, branch tag, last commit, date, delete button
- Create modal: name + description

### ActivityLog (`components/ActivityLog.js`)
- Header: "Activity Log" + auto-refresh toggle (10s interval) + manual refresh
- Filter bar (sticky): repo name, event type dropdown, from/to date, per-page selector, clear filters
- Results summary: "Showing X-Y of Z events"
- Event table: timestamp, event type (colored badge), repository link, expandable JSON details
- Pagination: prev/next + page display

---

## 12. Frontend — Pages

### LoginPage (`pages/LoginPage.js`)
- GitHub logo SVG + "Sign in to GitRipp" title
- Email + password form
- Error banner
- Redirects to previous location on success

### ExplorePage (`pages/ExplorePage.js`)
- Hero: "Explore Repositories" title + search form (text input + search/clear buttons)
- Results header: repo count + "New repository" button (if authenticated)
- Repo grid: name link, visibility badge, owner, description, branch tag, last commit, date, delete button (owner/admin)
- Create modal: name + description + visibility (public/private radio buttons)
- Empty state: "No repositories yet" with sign-in or create prompt

### IssuesPage (`pages/IssuesPage.js`)
- Filter tabs: Open / Closed (green active state)
- "New issue" button
- Issue items: status dot, title, number + date + author + comment count
- Create modal: title + description
- Clicking issue → renders IssueDetail inline

### IssueDetail (`pages/IssueDetail.js`)
- Back button
- Issue header: title, status badge (Open/Closed), meta info
- Issue body in comment box (author + date)
- Comments list (same comment box style)
- Close/Reopen toggle button (author/collaborator)
- Comment form: textarea + submit button

### SettingsPage (`pages/SettingsPage.js`)
- Two-column layout: sidebar tabs + content area
- **Profile tab**: Display name, bio, avatar URL inputs
- **Account tab**: Change email (with password confirm), change password (current + new + confirm, logs out on success)
- **Security tab**: Active sessions list with user-agent, dates, revoke buttons
- **Preferences tab**: Default branch, email privacy checkbox, push/issue notification checkboxes
- **Add User tab** (visible to all): Username + email + password form → creates account via register API
- **Admin — Users tab** (admin-only): User list with username, admin badge, email, delete button

---

## 13. Frontend — Styling

### Approach
- **Primary**: Custom CSS in App.css with CSS custom properties (dark theme)
- **Secondary**: Tailwind CSS via globals.css (`@tailwind base/components/utilities`)

### Color Palette (CSS Variables)
```
--bg-base:       #0d1117    (main background)
--bg-canvas:     #161b22    (card/surface)
--bg-overlay:    #21262d    (hover state)
--border:        #30363d
--text-primary:  #c9d1d9
--text-secondary:#8b949e
--text-link:     #58a6ff
--accent:        #238636    (green, success)
--danger:        #da3633    (red, destructive)
```

### Design Elements
- GitHub-like dark theme (midnight blue/black)
- 6px border-radius, flexbox layouts
- Loading spinners (CSS keyframe animation)
- Modal overlays (fixed, centered, backdrop click to close)
- diff2html dark theme overrides
- Responsive buttons (.btn, .btn-primary, .btn-danger, .btn-sm)
- File table with icons, commit messages, dates
- Breadcrumb navigation
- Status badges (green=open, gray=closed)

---

## 14. Git Integration

### Repo Initialization (on create)
```
git init
git config user.name "GitXO User"
git config user.email "gitxo@local"
git config receive.denyCurrentBranch updateInstead
```
`receive.denyCurrentBranch updateInstead` allows pushing to checked-out branch (updates working tree).

### Git Commands Used
- `git init` — Create repo
- `git branch` — List branches
- `git checkout` / `git checkout -b` — Switch/create branch
- `git merge --no-ff -m` — Merge with commit
- `git branch -D` — Force-delete branch
- `git log --format=%H|%h|%an|%ae|%ai|%s` — Commit history
- `git show` — Commit detail + unified diff
- `git diff` — Diff between refs
- `git add` / `git commit` / `git rm` — Stage/commit/remove files
- `git upload-pack --stateless-rpc` — Smart HTTP clone/fetch
- `git receive-pack --stateless-rpc` — Smart HTTP push
- `git archive --format=zip` — ZIP downloads
- `git rev-parse --verify` — Validate refs

### Smart HTTP Protocol
Clone URL format: `http(s)://{host}/api/git/{repoName}.git`

Supports standard `git clone`, `git fetch`, `git push` commands. Public repos allow anonymous clone. Private repos and all pushes require HTTP Basic Auth.

### Path Safety
All file paths validated via `Path.GetFullPath()` + prefix check to prevent path traversal attacks.

---

## 15. Deployment

### .ad.json (Artifact Deployment)
```json
{
  "name": "gitripp",
  "framework": "dotnet",
  "buildCommand": "dotnet publish backend/GitXO.Api.csproj -c Release -o ./publish",
  "startCommand": "cd publish && ./GitXO.Api"
}
```

### Requirements
- .NET 8.0 runtime
- Node.js v14+ (frontend build)
- Git CLI in PATH
- PostgreSQL 12+

### Data Directories
```
{RITHM_DATA_DIR}/repositories/          (public repos)
{RITHM_DATA_DIR}/repositories-private/  (private repos)
```

### No Containerization
No Dockerfile or docker-compose.yml — intended for bare-metal or VM deployment.

### Backup Targets
- PostgreSQL database (`pg_dump`)
- `repositories/` and `repositories-private/` directories
- `appsettings.json`
- `.meta.json` files (auto-recovery mechanism)
