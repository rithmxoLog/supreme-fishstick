# GitRipp / GitXO — Crash Recovery Guide

**Last Updated:** 2026-02-23

This guide covers step-by-step recovery procedures for every crash scenario. Pick the scenario that matches your situation and follow the steps in order.

---

## Table of Contents

1. [Quick Reference: Key Paths & Credentials](#1-quick-reference)
2. [Scenario A: Backend Crashed / Won't Start](#2-scenario-a-backend-crashed--wont-start)
3. [Scenario B: Frontend Crashed / Won't Start](#3-scenario-b-frontend-crashed--wont-start)
4. [Scenario C: PostgreSQL Down or Corrupted](#4-scenario-c-postgresql-down-or-corrupted)
5. [Scenario D: Complete Database Wipe (Tables Gone)](#5-scenario-d-complete-database-wipe)
6. [Scenario E: All Users Lost (Can't Log In)](#6-scenario-e-all-users-lost)
7. [Scenario F: Repository Files Missing or Corrupted](#7-scenario-f-repository-files-missing-or-corrupted)
8. [Scenario G: JWT Secret Changed (All Sessions Invalid)](#8-scenario-g-jwt-secret-changed)
9. [Scenario H: User Account Locked Out](#9-scenario-h-user-account-locked-out)
10. [Scenario I: Full System Recovery (Everything Lost)](#10-scenario-i-full-system-recovery)
11. [Post-Recovery Verification Checklist](#11-post-recovery-verification-checklist)

---

## 1. Quick Reference

### File Paths

| Item | Path |
|------|------|
| Project root | `c:\Users\LoganCarpenter\Documents\GitXO\` |
| Backend | `backend/` |
| Frontend | `frontend/` |
| Config | `backend/appsettings.json` |
| Migrations | `backend/Migrations/001_schema.sql` through `005_login_security.sql` |
| Public repos (on disk) | `backend/repositories/` |
| Private repos (on disk) | `backend/repositories-private/` |
| Repo metadata files | `backend/repositories/{repoName}.meta.json` |
| Start scripts | `start.bat` (Windows) / `start.sh` (Unix) |

### Default Credentials

| Service | Value |
|---------|-------|
| PostgreSQL user | `postgres` |
| PostgreSQL password | `Skrillex123#` |
| PostgreSQL database | `gitxo` |
| PostgreSQL host:port | `localhost:5432` |
| Backend URL | `http://localhost:3001` |
| Frontend URL | `http://localhost:3000` |

### Ports

| Service | Port |
|---------|------|
| Backend API | 3001 |
| Frontend dev server | 3000 |
| PostgreSQL | 5432 |

---

## 2. Scenario A: Backend Crashed / Won't Start

**Symptoms:** Backend process died, API calls fail, frontend shows network errors.

### Steps

1. **Check if the process is still running:**
   ```bash
   # Windows
   tasklist | findstr dotnet

   # Linux/Mac
   ps aux | grep dotnet
   ```

2. **Kill any zombie processes:**
   ```bash
   # Windows
   taskkill /f /im dotnet.exe

   # Linux/Mac
   pkill -f "dotnet run"
   ```

3. **Restart the backend:**
   ```bash
   cd c:\Users\LoganCarpenter\Documents\GitXO\backend
   dotnet run
   ```

4. **Check console output for errors.** Common issues:
   - **Port 3001 already in use:** Kill the old process first (step 2).
   - **PostgreSQL connection refused:** See [Scenario C](#4-scenario-c-postgresql-down-or-corrupted).
   - **Missing NuGet packages:** Run `dotnet restore` then `dotnet run`.
   - **Build errors:** Run `dotnet build` to see compile errors.

5. **Verify it's running:**
   ```bash
   curl http://localhost:3001/api/health
   # Expected: {"status":"ok","message":"GitXO backend is running"}
   ```

---

## 3. Scenario B: Frontend Crashed / Won't Start

**Symptoms:** Browser shows blank page or "cannot connect" on http://localhost:3000.

### Steps

1. **Kill any stuck Node processes:**
   ```bash
   # Windows
   taskkill /f /im node.exe

   # Linux/Mac
   pkill -f "react-scripts"
   ```

2. **Restart the frontend:**
   ```bash
   cd c:\Users\LoganCarpenter\Documents\GitXO\frontend
   npm start
   ```

3. **If npm start fails with dependency errors:**
   ```bash
   rm -rf node_modules package-lock.json
   npm install
   npm start
   ```

4. **Verify:** Open http://localhost:3000 in a browser. You should see the login page.

---

## 4. Scenario C: PostgreSQL Down or Corrupted

**Symptoms:** Backend starts but prints `[DB] WARNING: Could not connect to PostgreSQL`. API calls that touch the database fail.

### Steps

1. **Check if PostgreSQL is running:**
   ```bash
   # Windows
   sc query postgresql-x64-14
   # Or check Services app (services.msc)

   # Linux
   sudo systemctl status postgresql
   ```

2. **Start PostgreSQL if it's stopped:**
   ```bash
   # Windows
   net start postgresql-x64-14

   # Linux
   sudo systemctl start postgresql
   ```

3. **Test the connection:**
   ```bash
   psql -U postgres -d gitxo -c "SELECT 1;"
   ```

4. **If the database doesn't exist:**
   ```bash
   psql -U postgres -c "CREATE DATABASE gitxo;"
   ```
   Then follow [Scenario D](#5-scenario-d-complete-database-wipe) to run migrations.

5. **If PostgreSQL data is corrupted:**
   - Check PostgreSQL logs (usually in `pg_log/` or `/var/log/postgresql/`).
   - If unrecoverable, you may need to recreate the cluster and follow [Scenario I](#10-scenario-i-full-system-recovery).

6. **Restart the backend** after fixing PostgreSQL:
   ```bash
   cd c:\Users\LoganCarpenter\Documents\GitXO\backend
   dotnet run
   ```

---

## 5. Scenario D: Complete Database Wipe

**Symptoms:** Database exists but all tables are gone, or you dropped and recreated the database.

### Steps

1. **Run all 5 migrations in order:**
   ```bash
   psql -U postgres -d gitxo -f backend/Migrations/001_schema.sql
   psql -U postgres -d gitxo -f backend/Migrations/002_refresh_tokens.sql
   psql -U postgres -d gitxo -f backend/Migrations/003_user_settings.sql
   psql -U postgres -d gitxo -f backend/Migrations/004_write_permissions_indexes.sql
   psql -U postgres -d gitxo -f backend/Migrations/005_login_security.sql
   ```

   All migrations are idempotent (`IF NOT EXISTS`) — safe to re-run.

2. **Verify tables were created:**
   ```bash
   psql -U postgres -d gitxo -c "\dt"
   ```
   You should see: `users`, `repositories`, `repo_collaborators`, `issues`, `issue_comments`, `activity_logs`, `refresh_tokens`, `user_settings`.

3. **Restart the backend:**
   ```bash
   cd c:\Users\LoganCarpenter\Documents\GitXO\backend
   dotnet run
   ```

4. **At this point the database is empty.** Continue to:
   - [Scenario E](#6-scenario-e-all-users-lost) to create the first admin user.
   - [Scenario F](#7-scenario-f-repository-files-missing-or-corrupted) to recover repos from `.meta.json` files.

---

## 6. Scenario E: All Users Lost (Can't Log In)

**Symptoms:** No users exist in the database. Nobody can log in. The login page rejects all credentials.

### Steps

1. **Register a new admin user.** The first user registered always becomes admin automatically.

   ```bash
   curl -X POST http://localhost:3001/api/auth/register \
     -H "Content-Type: application/json" \
     -d '{"username":"admin","email":"admin@example.com","password":"YourPassword8+"}'
   ```

   Response:
   ```json
   {
     "user": { "id": 1, "username": "admin", "email": "admin@example.com", "isAdmin": true }
   }
   ```

2. **Log in with the new admin account** at http://localhost:3000/login.

3. **If repositories had previous owners**, run the recovery endpoint to restore them — see [Scenario F step 3](#7-scenario-f-repository-files-missing-or-corrupted).

4. **Reset passwords for placeholder users** (created during repo recovery):
   ```sql
   -- Connect to the database
   psql -U postgres -d gitxo

   -- List placeholder users (they have random passwords and can't log in)
   SELECT id, username, email, is_admin FROM users WHERE id > 1;

   -- Reset a user's password (replace the hash with a BCrypt hash of the desired password)
   -- You can generate a hash at: https://bcrypt-generator.com/
   UPDATE users
   SET password_hash = '<bcrypt_hash_here>',
       failed_login_attempts = 0,
       locked_until = NULL
   WHERE username = 'alice';
   ```

5. **Notify recovered users** of their temporary passwords so they can log in and change them via Settings > Account.

---

## 7. Scenario F: Repository Files Missing or Corrupted

### If repos are on disk but not in the database

**Symptoms:** Repo folders exist in `backend/repositories/` but don't appear in the UI.

1. **Check that `.meta.json` files exist** alongside the repo directories:
   ```bash
   ls backend/repositories/*.meta.json
   ```

2. **If meta files exist**, each one looks like:
   ```json
   {
     "name": "my-project",
     "ownerUsername": "alice",
     "ownerEmail": "alice@example.com",
     "ownerId": 42,
     "isPublic": true,
     "description": "A cool project",
     "defaultBranch": "main",
     "ownerIsAdmin": false,
     "savedAt": "2025-02-23T10:30:00Z"
   }
   ```

3. **Run the recovery endpoint:**

   If **no users exist** in the database (bootstrap mode — no auth needed):
   ```bash
   curl -X POST http://localhost:3001/api/repos/recover
   ```

   If **users exist** (requires admin JWT):
   ```bash
   # First, log in to get a token
   curl -X POST http://localhost:3001/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"admin@example.com","password":"YourPassword8+"}'

   # Use the accessToken from the response
   curl -X POST http://localhost:3001/api/repos/recover \
     -H "Authorization: Bearer <accessToken>"
   ```

4. **Check the response:**
   ```json
   {
     "recovered": [{"repo": "my-project", "owner": "alice"}],
     "skipped": ["already-registered-repo"],
     "failed": [],
     "placeholdersCreated": ["alice"],
     "bootstrapMode": false,
     "note": "Placeholder accounts were created..."
   }
   ```

   - **recovered** — repos successfully re-linked to the database.
   - **skipped** — repos already in the database (no action needed).
   - **failed** — repos that couldn't be recovered (check the reason).
   - **placeholdersCreated** — new user accounts with locked random passwords.

5. **If meta files are missing**, you must manually re-register the repo:
   ```bash
   # Log in as admin, then create the repo entry
   curl -X POST http://localhost:3001/api/repos \
     -H "Authorization: Bearer <accessToken>" \
     -H "Content-Type: application/json" \
     -d '{"name":"my-project","description":"Recovered repo","isPublic":true}'
   ```
   Note: This creates a fresh git repo. If the old repo directory already exists with data, the init step is skipped and existing files are preserved.

### If repo files are deleted from disk

**Bad news:** If the actual git repository directory (`backend/repositories/my-project/`) is gone, the git history is lost. There is no built-in mechanism to recover git data from the database — the database only stores metadata (ownership, visibility), not file contents.

**Prevention:** Set up regular backups of the `backend/repositories/` directory. See [BACKUP-GUIDE.md](BACKUP-GUIDE.md).

---

## 8. Scenario G: JWT Secret Changed

**Symptoms:** All users are suddenly logged out. Refresh tokens fail. Access tokens are rejected with 401.

### What happened

The JWT secret in `backend/appsettings.json` was changed. All tokens signed with the old secret are now invalid.

### Steps

1. **This is expected behavior.** Users simply need to log in again.

2. **If the secret was changed accidentally**, restore the original value in `backend/appsettings.json`:
   ```json
   "Jwt": {
     "Secret": "<original-secret-value>"
   }
   ```
   Then restart the backend.

3. **If the change was intentional** (secret rotation), no recovery needed — users log in again and get new tokens.

4. **Clean up old sessions** (optional):
   ```sql
   -- Revoke all refresh tokens (they're invalid anyway)
   UPDATE refresh_tokens SET revoked_at = NOW() WHERE revoked_at IS NULL;
   ```

---

## 9. Scenario H: User Account Locked Out

**Symptoms:** User sees "Account temporarily locked. Try again in X minute(s)." after too many failed login attempts.

### Automatic recovery

The lockout expires automatically after **15 minutes**. The user just needs to wait.

### Manual recovery (admin)

```sql
psql -U postgres -d gitxo -c "
  UPDATE users
  SET failed_login_attempts = 0, locked_until = NULL
  WHERE username = 'alice';
"
```

The user can now log in immediately.

---

## 10. Scenario I: Full System Recovery (Everything Lost)

**Symptoms:** Database wiped, users gone, but repo files still on disk. Starting from scratch.

This is the worst case. Follow these steps in exact order.

### Phase 1: Restore the database (2 min)

```bash
# Create the database if needed
psql -U postgres -c "CREATE DATABASE gitxo;"

# Run all migrations
psql -U postgres -d gitxo -f backend/Migrations/001_schema.sql
psql -U postgres -d gitxo -f backend/Migrations/002_refresh_tokens.sql
psql -U postgres -d gitxo -f backend/Migrations/003_user_settings.sql
psql -U postgres -d gitxo -f backend/Migrations/004_write_permissions_indexes.sql
psql -U postgres -d gitxo -f backend/Migrations/005_login_security.sql
```

### Phase 2: Start the application (2 min)

```bash
cd c:\Users\LoganCarpenter\Documents\GitXO
.\start.bat
```

Wait for both servers to be running:
- Backend: http://localhost:3001/api/health should return `{"status":"ok"}`
- Frontend: http://localhost:3000 should show the login page

### Phase 3: Create the first admin (1 min)

```bash
curl -X POST http://localhost:3001/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","email":"admin@example.com","password":"YourPassword8+"}'
```

The first registered user is automatically admin.

### Phase 4: Recover repositories (1 min)

If `.meta.json` files exist alongside repo directories:

```bash
# Log in
TOKEN=$(curl -s -X POST http://localhost:3001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"YourPassword8+"}' | \
  python -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

# Run recovery
curl -X POST http://localhost:3001/api/repos/recover \
  -H "Authorization: Bearer $TOKEN"
```

This will:
- Scan all `.meta.json` files in `backend/repositories/` and `backend/repositories-private/`
- Recreate repo entries in the database
- Create placeholder user accounts for missing owners (with locked random passwords)

### Phase 5: Reset placeholder user passwords (5 min)

For each placeholder user created during recovery:

```sql
psql -U postgres -d gitxo

-- List all users
SELECT id, username, email, is_admin FROM users ORDER BY id;

-- Reset password for each placeholder (generate hash at bcrypt-generator.com)
UPDATE users
SET password_hash = '<bcrypt_hash>',
    failed_login_attempts = 0,
    locked_until = NULL
WHERE username = '<username>';
```

### Phase 6: Verify everything works

Follow the [Post-Recovery Verification Checklist](#11-post-recovery-verification-checklist) below.

---

## 11. Post-Recovery Verification Checklist

Run through this after any recovery to confirm the system is healthy.

- [ ] **Backend running:** `curl http://localhost:3001/api/health` returns OK
- [ ] **Frontend loading:** http://localhost:3000 shows the login page
- [ ] **Database connected:** Backend console shows `[DB] Connected to PostgreSQL successfully.`
- [ ] **Admin can log in:** Log in with the admin account at http://localhost:3000/login
- [ ] **Repos visible:** Navigate to http://localhost:3000 after login — repos should appear
- [ ] **Repo files accessible:** Click into a repo — code tab should show files
- [ ] **Commits visible:** Click the Commits tab in a repo — history should load
- [ ] **Branches visible:** Click the Branches tab — branch list should load
- [ ] **Issues accessible:** Click the Issues tab — should load (may be empty)
- [ ] **Add user works:** Go to Settings > Add User — create a test account
- [ ] **New user can log in:** Log out, log in with the test account
- [ ] **Activity log recording:** Go to /activity (admin) — should show recent events
- [ ] **Push works:** Upload a file via the Push tab to a repo you own

### Quick SQL health check

```sql
psql -U postgres -d gitxo -c "
  SELECT 'users' AS tbl, COUNT(*) FROM users
  UNION ALL
  SELECT 'repositories', COUNT(*) FROM repositories
  UNION ALL
  SELECT 'issues', COUNT(*) FROM issues
  UNION ALL
  SELECT 'activity_logs', COUNT(*) FROM activity_logs;
"
```

---

## Recovery Flowchart

```
System crashed — what's broken?
│
├── Can't reach http://localhost:3001?
│   ├── PostgreSQL down? ──────────► Scenario C
│   └── Backend process died? ─────► Scenario A
│
├── Can't reach http://localhost:3000?
│   └── Frontend process died? ────► Scenario B
│
├── Can log in but repos missing?
│   └── Repos on disk but not in DB? ► Scenario F
│
├── Can't log in at all?
│   ├── "Account locked" message? ──► Scenario H
│   ├── No users in database? ──────► Scenario E
│   └── Tokens all invalid? ────────► Scenario G
│
├── Database completely empty?
│   └── Tables gone? ──────────────► Scenario D → E → F
│
└── Everything gone?
    └── Full wipe ─────────────────► Scenario I
```
