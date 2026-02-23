# GitRipp / GitXO — Backup Guide

**Last Updated:** 2026-02-23

How to set up backups so you can recover from any crash. This guide covers what to back up, how often, and how to restore from backups.

---

## What Needs to Be Backed Up

| Item | Location | Contains | Priority |
|------|----------|----------|----------|
| PostgreSQL database | `localhost:5432/gitxo` | Users, repo metadata, issues, comments, activity logs, sessions, settings | **Critical** |
| Repository files | `backend/repositories/` | All git repos + `.meta.json` files | **Critical** |
| Private repo files | `backend/repositories-private/` | Private git repos + `.meta.json` files | **Critical** |
| Configuration | `backend/appsettings.json` | DB credentials, JWT secret, server settings | **Important** |

---

## 1. Database Backup

### One-time backup (manual)

```bash
pg_dump -U postgres -d gitxo -F c -f gitxo_backup.dump
```

- `-F c` = custom format (compressed, supports selective restore)
- Output: `gitxo_backup.dump`

### Restore from backup

```bash
# Drop and recreate the database
psql -U postgres -c "DROP DATABASE IF EXISTS gitxo;"
psql -U postgres -c "CREATE DATABASE gitxo;"

# Restore
pg_restore -U postgres -d gitxo gitxo_backup.dump
```

### Plain SQL backup (human-readable alternative)

```bash
# Backup
pg_dump -U postgres -d gitxo > gitxo_backup.sql

# Restore
psql -U postgres -d gitxo < gitxo_backup.sql
```

### Scheduled backup (Windows Task Scheduler)

Create a file `backup-db.bat`:
```batch
@echo off
set PGPASSWORD=Skrillex123#
set BACKUP_DIR=C:\Backups\GitXO\database
set TIMESTAMP=%date:~-4%%date:~4,2%%date:~7,2%_%time:~0,2%%time:~3,2%
set TIMESTAMP=%TIMESTAMP: =0%

if not exist "%BACKUP_DIR%" mkdir "%BACKUP_DIR%"

pg_dump -U postgres -d gitxo -F c -f "%BACKUP_DIR%\gitxo_%TIMESTAMP%.dump"

:: Delete backups older than 30 days
forfiles /p "%BACKUP_DIR%" /m "*.dump" /d -30 /c "cmd /c del @file" 2>nul

echo Backup completed: gitxo_%TIMESTAMP%.dump
```

Schedule it via Task Scheduler:
1. Open Task Scheduler (`taskschd.msc`)
2. Create Basic Task > Name: "GitXO DB Backup"
3. Trigger: Daily at 2:00 AM
4. Action: Start a program > `C:\path\to\backup-db.bat`

### Scheduled backup (Linux cron)

```bash
# Edit crontab
crontab -e

# Add this line (runs daily at 2:00 AM)
0 2 * * * PGPASSWORD='Skrillex123#' pg_dump -U postgres -d gitxo -F c -f /var/backups/gitxo/gitxo_$(date +\%Y\%m\%d).dump && find /var/backups/gitxo -name "*.dump" -mtime +30 -delete
```

---

## 2. Repository Files Backup

### One-time backup (manual)

```bash
# Windows (PowerShell)
Compress-Archive -Path "c:\Users\LoganCarpenter\Documents\GitXO\backend\repositories" -DestinationPath "C:\Backups\GitXO\repos_backup.zip"

# Linux/Mac
tar -czf /var/backups/gitxo/repos_$(date +%Y%m%d).tar.gz \
  -C c:/Users/LoganCarpenter/Documents/GitXO/backend \
  repositories/ repositories-private/
```

This backs up:
- All git repository directories (full history)
- All `.meta.json` metadata files (for disaster recovery)

### Restore from backup

```bash
# Windows (PowerShell)
Expand-Archive -Path "C:\Backups\GitXO\repos_backup.zip" -DestinationPath "c:\Users\LoganCarpenter\Documents\GitXO\backend\"

# Linux/Mac
tar -xzf /var/backups/gitxo/repos_20260223.tar.gz \
  -C c:/Users/LoganCarpenter/Documents/GitXO/backend/
```

### Scheduled backup (Windows)

Create `backup-repos.bat`:
```batch
@echo off
set BACKUP_DIR=C:\Backups\GitXO\repos
set REPOS_DIR=c:\Users\LoganCarpenter\Documents\GitXO\backend\repositories
set TIMESTAMP=%date:~-4%%date:~4,2%%date:~7,2%

if not exist "%BACKUP_DIR%" mkdir "%BACKUP_DIR%"

powershell -Command "Compress-Archive -Path '%REPOS_DIR%' -DestinationPath '%BACKUP_DIR%\repos_%TIMESTAMP%.zip' -Force"

:: Delete backups older than 14 days
forfiles /p "%BACKUP_DIR%" /m "*.zip" /d -14 /c "cmd /c del @file" 2>nul

echo Repos backup completed: repos_%TIMESTAMP%.zip
```

Schedule via Task Scheduler (same steps as above, daily at 3:00 AM).

---

## 3. Configuration Backup

### What to back up

The only critical config file is `backend/appsettings.json`. It contains:
- PostgreSQL connection credentials
- JWT signing secret
- Repository directory paths
- Server URL and port

### How to back up

```bash
cp backend/appsettings.json C:\Backups\GitXO\appsettings.json.backup
```

Store this in a **secure location** — it contains database passwords and the JWT secret.

### When to back up

- After any change to `appsettings.json`
- Before upgrading or migrating the system
- Monthly as a safety measure

---

## 4. Recommended Backup Schedule

| Item | Frequency | Retention | Storage |
|------|-----------|-----------|---------|
| Database dump | Daily at 2:00 AM | 30 days | `C:\Backups\GitXO\database\` |
| Repository files | Daily at 3:00 AM | 14 days | `C:\Backups\GitXO\repos\` |
| appsettings.json | On change + monthly | 12 months | Secure location |

### Storage estimate

- Database: Typically small (< 50 MB). 30 daily backups = ~1.5 GB.
- Repositories: Depends on repo sizes. If total repos = 1 GB, 14 daily backups = ~14 GB.
- Config: Negligible (< 1 KB).

---

## 5. Verifying Backups

Backups are useless if they're corrupted. Verify periodically.

### Verify database backup

```bash
# List contents without restoring
pg_restore -l gitxo_backup.dump

# Test restore to a temporary database
psql -U postgres -c "CREATE DATABASE gitxo_test;"
pg_restore -U postgres -d gitxo_test gitxo_backup.dump
psql -U postgres -d gitxo_test -c "SELECT COUNT(*) FROM users;"
psql -U postgres -c "DROP DATABASE gitxo_test;"
```

### Verify repository backup

```bash
# Windows (PowerShell) — test the ZIP
Expand-Archive -Path "C:\Backups\GitXO\repos\repos_20260223.zip" -DestinationPath "C:\temp\repos_test" -Force
ls "C:\temp\repos_test\repositories\*.meta.json"
rm -r "C:\temp\repos_test"

# Linux
tar -tzf repos_20260223.tar.gz | head -20
```

---

## 6. Recovery from Backups

### Full recovery using backups

If you have both a database dump and repository backup:

1. **Restore the database:**
   ```bash
   psql -U postgres -c "DROP DATABASE IF EXISTS gitxo;"
   psql -U postgres -c "CREATE DATABASE gitxo;"
   pg_restore -U postgres -d gitxo gitxo_backup.dump
   ```

2. **Restore repository files:**
   ```bash
   # Remove corrupted repos (if any)
   rm -rf backend/repositories/*

   # Restore from backup
   # (adjust path to your backup file)
   tar -xzf repos_backup.tar.gz -C backend/
   ```

3. **Restore config (if needed):**
   ```bash
   cp appsettings.json.backup backend/appsettings.json
   ```

4. **Start the application:**
   ```bash
   .\start.bat
   ```

5. **Verify:** Follow the [Post-Recovery Verification Checklist](RECOVERY-GUIDE.md#11-post-recovery-verification-checklist) in the Recovery Guide.

### Recovery without database backup (meta files only)

If you lost the database but have repo files with `.meta.json`:

Follow [Scenario I in the Recovery Guide](RECOVERY-GUIDE.md#10-scenario-i-full-system-recovery) — run migrations, register first admin, then use `POST /api/repos/recover` to rebuild from meta files.

---

## 7. What You Lose Without Backups

| No backup of... | What's lost |
|------------------|------------|
| Database | User accounts, repo metadata, issues, comments, activity logs, session data, user settings |
| Repository files | All git history, source code, branches, commits |
| Meta files | Ability to auto-recover repo ownership after DB wipe |
| appsettings.json | JWT secret (all tokens invalid), DB credentials |

The `.meta.json` files are your **last line of defense** — they're stored alongside repo files and contain enough info to rebuild DB records. But they cannot recover issues, comments, activity logs, or user settings.
