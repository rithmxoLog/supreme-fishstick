# GitXO

Lightweight local GitHub-like web app for browsing and editing repositories.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — for the backend
- [Node.js v14+](https://nodejs.org) — for the frontend
- [Git](https://git-scm.com) — must be on your PATH (used by the backend)
- [PostgreSQL](https://www.postgresql.org) — for activity logging (optional; the app runs without it, logging is just silently skipped)

## Quick Start

Use the included helper scripts to install dependencies and launch both services.

**Windows:**
```
start.bat
```

**macOS / Linux:**
```bash
./start.sh
```

Both scripts will:
1. Restore C# NuGet packages (`dotnet restore`) if not already done
2. Install frontend npm packages (`npm install`) if not already done
3. Start the C# backend on http://localhost:3001
4. Start the React frontend on http://localhost:3000

## Manual Start

**Backend (C#):**
```bash
cd backend-csharp
dotnet restore       # first time only
dotnet run
```

**Frontend (React):**
```bash
cd frontend
npm install          # first time only
npm start
```

After starting:

- Frontend: http://localhost:3000
- Backend API: http://localhost:3001 (health check: `GET /api/health`)

## Configuration

Backend settings are in [backend-csharp/appsettings.json](backend-csharp/appsettings.json):

| Key | Default | Description |
|-----|---------|-------------|
| `Urls` | `http://localhost:3001` | Port the backend listens on |
| `ReposDirectory` | `repositories` | Path where git repos are stored (relative to working dir, or absolute) |
| `Postgres.Host` | `localhost` | PostgreSQL host |
| `Postgres.Port` | `5432` | PostgreSQL port |
| `Postgres.Database` | `gitxo` | Database name |
| `Postgres.Username` | `postgres` | Database user |
| `Postgres.Password` | *(set this)* | Database password |

## Storage

Repositories are stored at `backend-csharp/repositories/` (created automatically on first run). The path is configurable via `ReposDirectory` in `appsettings.json`.

## PostgreSQL Setup

The activity log requires a `activity_logs` table. Run this once against your `gitxo` database:

```sql
CREATE TABLE IF NOT EXISTS activity_logs (
    id         BIGSERIAL PRIMARY KEY,
    event_type TEXT        NOT NULL,
    repo_name  TEXT,
    details    JSONB       NOT NULL DEFAULT '{}',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

If PostgreSQL is unavailable, the app starts normally — git operations and file browsing work as usual, only activity logging is skipped.

## API Reference

Base path: `/api`

**Repositories**
- `GET /api/repos` — list all repositories
- `GET /api/repos/{name}` — repo metadata
- `POST /api/repos` — create repo (body: `name`, `description`)
- `DELETE /api/repos/{name}` — delete repo

**Files**
- `GET /api/repos/{name}/files?path=subdir` — list directory contents
- `GET /api/repos/{name}/file?path=file.txt` — get file content
- `POST /api/repos/{name}/file` — save a file (body: `filePath`, `content`, `message`, `branch`)
- `DELETE /api/repos/{name}/file` — delete a file (body: `filePath`, `message`)
- `GET /api/repos/{name}/download/file?path=file.txt` — download a single file
- `GET /api/repos/{name}/download?branch=main` — download repo as ZIP

**Push**
- `POST /api/repos/{name}/push` — upload and commit files (multipart form: `files`, `message`, `branch`, `authorName`, `authorEmail`)

**Branches**
- `GET /api/repos/{name}/branches` — list branches
- `POST /api/repos/{name}/branches` — create branch (body: `branchName`, `fromBranch`)
- `POST /api/repos/{name}/checkout` — checkout branch (body: `branchName`)
- `POST /api/repos/{name}/merge` — merge branches (body: `sourceBranch`, `targetBranch`, `message`)
- `DELETE /api/repos/{name}/branches/{branch}` — delete branch

**Commits**
- `GET /api/repos/{name}/commits?branch=main&limit=50` — list commits
- `GET /api/repos/{name}/commits/{hash}` — get commit details and diff

**Activity Logs**
- `GET /api/logs?repo=&event_type=&from=&to=&limit=100&offset=0` — query activity log
- `GET /api/logs/event-types` — list distinct event type values

## Key Files

| Path | Description |
|------|-------------|
| `backend-csharp/Program.cs` | Backend entry point, DI setup, CORS |
| `backend-csharp/appsettings.json` | Backend configuration |
| `backend-csharp/Services/GitRunner.cs` | Git CLI wrapper used by all controllers |
| `backend-csharp/Services/ActivityLogger.cs` | Fire-and-forget PostgreSQL logging |
| `backend-csharp/Controllers/` | One controller per API group |
| `frontend/src/api/index.js` | Fetch wrapper used by all React components |
| `frontend/package.json` | Frontend scripts; `proxy` forwards API calls to port 3001 |
| `start.bat` / `start.sh` | One-command launcher for both services |

## Troubleshooting

- **`dotnet` not found** — install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and ensure `dotnet` is on your PATH.
- **`git` not found** — install [Git](https://git-scm.com) and ensure `git` is on your PATH; the backend shells out to git for all repository operations.
- **Frontend cannot reach the API** — verify the backend is running on port `3001`. The `proxy` in `frontend/package.json` forwards `/api` calls during development.
- **Activity log empty** — check PostgreSQL is running, credentials in `appsettings.json` are correct, and the `activity_logs` table exists (see setup SQL above).
