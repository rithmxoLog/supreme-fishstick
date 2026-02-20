# Disaster Recovery Standards

**Version**: 1.0
**Last Updated**: 2026-02-16
**Audience**: DevOps, SRE, Platform Engineers, Service Owners

> ### Validation Required
>
> This document contains templates and estimates that require validation before production use:
>
> | Section | Needs Validation From | What to Validate |
> |---------|----------------------|------------------|
> | Recovery Objectives (RTO/RPO) | **Business / Product Owners** | All RTO/RPO numbers are engineering estimates, not contractual SLAs |
> | PostgreSQL Backup | **DBA** | DB version (paths assume v14), roles (`replication_user`, `app_user`), DB name (`rithmtemplate_db` vs `rithmtemplate`) |
> | Backup paths | **SRE / Infrastructure** | `/var/lib/rithm/backups/` directories don't exist yet — need provisioning |
> | WAL archiving config | **DBA** | Template for `postgresql.conf` — not applied anywhere |
> | MinIO backup | **Infrastructure / Storage** | MinIO endpoint (`minio.rithmxo.internal`) is a placeholder |
> | systemd timers | **SRE** | Backup timer/service units are templates, not deployed |
> | Backup schedule / retention | **Business / Compliance** | 02:00 UTC schedule and 7/30-day retention are arbitrary |
> | PITR procedures | **DBA** | Paths assume PostgreSQL 14, need actual version confirmation |
> | Datacenter failure scenario | **Infrastructure / DR Team** | Cross-DC WAL shipping is aspirational — no secondary DC configured |
> | Database replication failover | **DBA** | No streaming replication exists in codebase — scenario is future-state |
> | Escalation matrix | **Management / SRE** | Response times and contacts are placeholders |

---

## Table of Contents

1. [Recovery Objectives](#recovery-objectives)
2. [Component Classification](#component-classification)
3. [Backup Procedures](#backup-procedures)
4. [Restore Procedures](#restore-procedures)
5. [Failure Scenarios and Runbooks](#failure-scenarios-and-runbooks)
6. [DR Testing](#dr-testing)
7. [Escalation Matrix](#escalation-matrix)

---

## Recovery Objectives

### Definitions

| Term | Definition |
|------|-----------|
| **RTO** (Recovery Time Objective) | Maximum acceptable time to restore service after a failure |
| **RPO** (Recovery Point Objective) | Maximum acceptable data loss measured in time |

### Objectives by Component

| Component | RTO | RPO | Justification |
|-----------|-----|-----|---------------|
| **PostgreSQL** (primary data) | 30 minutes | 5 minutes | Core business data, PITR with WAL archiving |
| **Valkey/Redis** (cache + state) | 10 minutes | Ephemeral (0 RPO for cache, 5 min for operation state) | Cache is reconstructible; active operations tracked in Valkey need short RPO |
| **Application Service** (systemd) | 5 minutes | N/A (stateless) | systemd auto-restart + health checks; no local state |
| **Frontend** (Next.js) | 5 minutes | N/A (stateless) | Static build, systemd auto-restart |
| **Certificates** (mTLS) | 15 minutes | N/A (renewable) | Re-request from Service Identity Authority on restart |
| **InfraSoT Registration** | 5 minutes | N/A (re-registers on startup) | Service auto-registers on boot |

### Ecosystem-Wide Targets

For the rithmXO ecosystem managing hundreds of thousands of services:

| Scenario | Target RTO | Target RPO |
|----------|-----------|-----------|
| Single service failure | 5 minutes (auto-recovery) | Per-component above |
| Single host failure | 15 minutes | 5 minutes (PostgreSQL PITR) |
| Database failure (single instance) | 30 minutes | 5 minutes |
| Database failure (with replica) | 5 minutes (automatic failover) | ~0 (streaming replication) |
| Full datacenter failure | 4 hours | 1 hour (cross-DC WAL shipping) |

---

## Component Classification

### Tier 1: Stateful (Requires Backup)

| Component | Data Type | Backup Strategy |
|-----------|-----------|----------------|
| **PostgreSQL** | Business data, migrations, RLS policies | WAL archiving + periodic base backups |
| **Valkey/Redis** | Active operation state, distributed locks, idempotency keys | RDB snapshots (optional, data is reconstructible) |

### Tier 2: Stateless (No Backup Required)

| Component | Recovery Strategy |
|-----------|------------------|
| **Application binaries** | Redeploy from build artifacts |
| **Frontend build** | Redeploy from build artifacts |
| **Certificates** | Re-request from Service Identity Authority |
| **InfraSoT registration** | Auto-registers on startup |
| **Configuration** | Stored in version control + `/etc/rithm/*.env` |

### Tier 3: Infrastructure (Managed Separately)

| Component | Responsibility |
|-----------|---------------|
| **InfraSoT Registry** | Platform team |
| **Service Identity Authority** | Platform team |
| **Observability Collector** | Platform team |
| **PolicyEngine** | Platform team |
| **AuditAuthority** | Platform team |

---

## Backup Procedures

### PostgreSQL Backup

#### Strategy: WAL Archiving + Base Backups

PostgreSQL uses continuous WAL (Write-Ahead Log) archiving combined with periodic base backups to achieve the 5-minute RPO target.

#### Setup: WAL Archiving

**File**: `/etc/postgresql/14/main/postgresql.conf`

```ini
# WAL Archiving for Point-in-Time Recovery
wal_level = replica
archive_mode = on
archive_command = 'test ! -f /var/lib/rithm/backups/wal/%f && cp %p /var/lib/rithm/backups/wal/%f'
archive_timeout = 300  # Force WAL switch every 5 minutes (matches RPO)
```

#### Option A: Local Filesystem Backup

```bash
#!/bin/bash
# /opt/rithm/scripts/backup-postgresql.sh
# Schedule via systemd timer or cron: daily at 02:00 UTC

set -euo pipefail

SERVICE_NAME="rithm-template"
BACKUP_DIR="/var/lib/rithm/backups/postgresql"
WAL_DIR="/var/lib/rithm/backups/wal"
RETENTION_DAYS=7
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="${BACKUP_DIR}/${SERVICE_NAME}_${TIMESTAMP}.sql.gz"

# Create backup directory
mkdir -p "${BACKUP_DIR}" "${WAL_DIR}"

# Base backup (compressed)
pg_basebackup \
  -h localhost \
  -U replication_user \
  -D "${BACKUP_DIR}/base_${TIMESTAMP}" \
  -Ft -z \
  -P \
  --wal-method=stream

# Logical backup (for cross-version compatibility)
pg_dump \
  -h localhost \
  -U app_user \
  -d rithmtemplate_db \
  --format=custom \
  --compress=9 \
  -f "${BACKUP_FILE}"

# Cleanup old backups
find "${BACKUP_DIR}" -name "*.sql.gz" -mtime +${RETENTION_DAYS} -delete
find "${BACKUP_DIR}" -name "base_*" -mtime +${RETENTION_DAYS} -exec rm -rf {} +
find "${WAL_DIR}" -name "*.gz" -mtime +${RETENTION_DAYS} -delete

echo "[$(date)] Backup completed: ${BACKUP_FILE}"
```

#### Option B: MinIO/S3 Backup

When `FileStoreType: "minio"` is configured, backups can be stored in MinIO for off-host redundancy.

```bash
#!/bin/bash
# /opt/rithm/scripts/backup-postgresql-minio.sh

set -euo pipefail

SERVICE_NAME="rithm-template"
MINIO_ENDPOINT="${MINIO_ENDPOINT:-https://minio.rithmxo.internal}"
MINIO_BUCKET="${MINIO_BUCKET:-backups}"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
TMP_DIR=$(mktemp -d)
BACKUP_FILE="${TMP_DIR}/${SERVICE_NAME}_${TIMESTAMP}.sql.gz"

# Create backup
pg_dump \
  -h localhost \
  -U app_user \
  -d rithmtemplate_db \
  --format=custom \
  --compress=9 \
  -f "${BACKUP_FILE}"

# Upload to MinIO
mc cp "${BACKUP_FILE}" "${MINIO_ENDPOINT}/${MINIO_BUCKET}/postgresql/${SERVICE_NAME}/"

# Upload WAL segments
mc mirror /var/lib/rithm/backups/wal/ "${MINIO_ENDPOINT}/${MINIO_BUCKET}/wal/${SERVICE_NAME}/" --newer-than "24h"

# Cleanup temp
rm -rf "${TMP_DIR}"

# Cleanup old backups in MinIO (retain 30 days)
mc rm --recursive --force --older-than 30d "${MINIO_ENDPOINT}/${MINIO_BUCKET}/postgresql/${SERVICE_NAME}/"

echo "[$(date)] Backup uploaded to MinIO: ${MINIO_BUCKET}/postgresql/${SERVICE_NAME}/"
```

#### systemd Timer for Automated Backups

**File**: `/etc/systemd/system/rithm-template-backup.timer`

```ini
[Unit]
Description=Daily PostgreSQL backup for RithmTemplate

[Timer]
OnCalendar=*-*-* 02:00:00
RandomizedDelaySec=900
Persistent=true

[Install]
WantedBy=timers.target
```

**File**: `/etc/systemd/system/rithm-template-backup.service`

```ini
[Unit]
Description=PostgreSQL backup for RithmTemplate
After=postgresql.service

[Service]
Type=oneshot
User=rithm-template
ExecStart=/opt/rithm/scripts/backup-postgresql.sh
StandardOutput=journal
StandardError=journal
SyslogIdentifier=rithm-template-backup
```

```bash
# Enable automated backups
sudo systemctl daemon-reload
sudo systemctl enable rithm-template-backup.timer
sudo systemctl start rithm-template-backup.timer

# Verify timer is active
sudo systemctl list-timers | grep rithm-template
```

### Valkey/Redis Backup (Optional)

Valkey data is primarily ephemeral (cache, temporary locks). However, active operation state for batch processing may warrant periodic snapshots.

```bash
#!/bin/bash
# /opt/rithm/scripts/backup-valkey.sh

VALKEY_HOST="${VALKEY_HOST:-localhost}"
VALKEY_PORT="${VALKEY_PORT:-6379}"
BACKUP_DIR="/var/lib/rithm/backups/valkey"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

mkdir -p "${BACKUP_DIR}"

# Trigger RDB snapshot
redis-cli -h "${VALKEY_HOST}" -p "${VALKEY_PORT}" BGSAVE

# Wait for save to complete
while [ "$(redis-cli -h ${VALKEY_HOST} -p ${VALKEY_PORT} LASTSAVE)" == "$(redis-cli -h ${VALKEY_HOST} -p ${VALKEY_PORT} LASTSAVE)" ]; do
  sleep 1
done

# Copy dump file
cp /var/lib/redis/dump.rdb "${BACKUP_DIR}/dump_${TIMESTAMP}.rdb"

# Cleanup old snapshots (retain 3 days)
find "${BACKUP_DIR}" -name "dump_*.rdb" -mtime +3 -delete
```

### Configuration Backup

Service configuration is stored in two locations:

```bash
# 1. Version-controlled (already in git)
#    - appsettings.json, appsettings.Production.json
#    - systemd unit files
#    - Deployment scripts

# 2. Environment-specific secrets (NOT in git)
#    Backup these files:
sudo cp /etc/rithm/rithm-template.env /var/lib/rithm/backups/config/
sudo chmod 600 /var/lib/rithm/backups/config/rithm-template.env
```

---

## Restore Procedures

### PostgreSQL Restore

#### From pg_dump (Logical Backup)

```bash
#!/bin/bash
# Restore from logical backup
# Usage: ./restore-postgresql.sh <backup_file>

set -euo pipefail

BACKUP_FILE="${1:?Usage: restore-postgresql.sh <backup_file>}"
DB_NAME="rithmtemplate_db"
DB_USER="app_user"

echo "[$(date)] Starting restore from: ${BACKUP_FILE}"

# 1. Stop the application service
sudo systemctl stop rithm-template

# 2. Drop and recreate database
psql -U postgres -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${DB_NAME}';"
psql -U postgres -c "DROP DATABASE IF EXISTS ${DB_NAME};"
psql -U postgres -c "CREATE DATABASE ${DB_NAME} OWNER ${DB_USER};"

# 3. Restore from backup
pg_restore \
  -h localhost \
  -U "${DB_USER}" \
  -d "${DB_NAME}" \
  --no-owner \
  --no-privileges \
  --jobs=4 \
  "${BACKUP_FILE}"

# 4. Reapply RLS policies (if using Row-Level Security)
if [ -f /opt/rithm/scripts/apply-rls-migration.sh ]; then
  /opt/rithm/scripts/apply-rls-migration.sh
fi

# 5. Restart application
sudo systemctl start rithm-template

# 6. Verify health
sleep 10
curl -sf http://localhost:5000/health/ready || echo "WARNING: Health check failed after restore"

echo "[$(date)] Restore completed"
```

#### Point-in-Time Recovery (PITR)

For recovering to a specific point (e.g., just before accidental data deletion):

```bash
#!/bin/bash
# Point-in-Time Recovery
# Usage: ./restore-pitr.sh <target_time>
# Example: ./restore-pitr.sh "2026-02-16 14:30:00 UTC"

set -euo pipefail

TARGET_TIME="${1:?Usage: restore-pitr.sh '<target_time>'}"
BASE_BACKUP_DIR="/var/lib/rithm/backups/postgresql"
WAL_DIR="/var/lib/rithm/backups/wal"
RECOVERY_DIR="/var/lib/postgresql/14/recovery"

echo "[$(date)] Starting PITR to: ${TARGET_TIME}"

# 1. Stop PostgreSQL and application
sudo systemctl stop rithm-template
sudo systemctl stop postgresql

# 2. Find most recent base backup before target time
LATEST_BASE=$(ls -1d ${BASE_BACKUP_DIR}/base_* | sort -r | head -1)
echo "Using base backup: ${LATEST_BASE}"

# 3. Restore base backup
sudo rm -rf /var/lib/postgresql/14/main
sudo tar -xzf "${LATEST_BASE}/base.tar.gz" -C /var/lib/postgresql/14/main/

# 4. Configure recovery
cat > /var/lib/postgresql/14/main/recovery.signal << EOF
EOF

cat >> /var/lib/postgresql/14/main/postgresql.auto.conf << EOF
restore_command = 'cp ${WAL_DIR}/%f %p'
recovery_target_time = '${TARGET_TIME}'
recovery_target_action = 'promote'
EOF

# 5. Fix permissions
sudo chown -R postgres:postgres /var/lib/postgresql/14/main

# 6. Start PostgreSQL (will replay WAL segments)
sudo systemctl start postgresql

# 7. Wait for recovery
echo "Waiting for PITR recovery..."
until psql -U postgres -c "SELECT pg_is_in_recovery();" 2>/dev/null | grep -q "f"; do
  sleep 5
  echo "Still recovering..."
done

# 8. Start application
sudo systemctl start rithm-template

echo "[$(date)] PITR completed to: ${TARGET_TIME}"
```

### Valkey/Redis Restore

```bash
# 1. Stop application and Valkey
sudo systemctl stop rithm-template
sudo systemctl stop valkey

# 2. Replace dump file
sudo cp /var/lib/rithm/backups/valkey/dump_YYYYMMDD_HHMMSS.rdb /var/lib/redis/dump.rdb
sudo chown redis:redis /var/lib/redis/dump.rdb

# 3. Start Valkey and application
sudo systemctl start valkey
sudo systemctl start rithm-template
```

> **Note**: In most cases, Valkey restore is unnecessary. The application reconstructs cache on startup, and orphaned operations are automatically recovered by the BatchProcessing module.

### Full Service Restore (New Host)

When deploying to a completely new host:

```bash
#!/bin/bash
# Full service restore to new host
# Prerequisites: OS installed, network configured, DNS resolving

set -euo pipefail

echo "=== Step 1: Install prerequisites ==="
# (See DEPLOYMENT-SYSTEMD.md Prerequisites section)

echo "=== Step 2: Restore PostgreSQL ==="
# Restore from latest backup
./restore-postgresql.sh /path/to/latest/backup.sql.gz

echo "=== Step 3: Deploy application ==="
# Deploy from build artifacts
sudo cp -r /path/to/artifacts/backend/* /opt/rithm/rithm-template/
sudo cp -r /path/to/artifacts/frontend/* /opt/rithm/rithm-template-web/
sudo chown -R rithm-template:rithm-template /opt/rithm/rithm-template
sudo chown -R rithm-template:rithm-template /opt/rithm/rithm-template-web

echo "=== Step 4: Restore configuration ==="
sudo cp /path/to/backup/rithm-template.env /etc/rithm/rithm-template.env
sudo chmod 600 /etc/rithm/rithm-template.env

echo "=== Step 5: Install systemd units ==="
sudo cp deployment/systemd/*.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable rithm-template rithm-template-web

echo "=== Step 6: Start services ==="
sudo systemctl start rithm-template
sudo systemctl start rithm-template-web

echo "=== Step 7: Verify ==="
sleep 15
curl -sf http://localhost:5000/health/ready && echo "Backend: OK" || echo "Backend: FAILED"
curl -sf http://localhost:3000 && echo "Frontend: OK" || echo "Frontend: FAILED"
```

---

## Failure Scenarios and Runbooks

### Scenario 1: Application Crash

**Detection**: systemd detects process exit, health check fails
**Auto-Recovery**: systemd `Restart=always` with `RestartSec=10`

**Manual intervention required if**: Service crashes repeatedly (3+ times in 5 minutes)

```bash
# 1. Check crash logs
sudo journalctl -u rithm-template -n 100 --no-pager

# 2. Check resource usage
sudo systemctl status rithm-template
# Look for: Memory limit exceeded, OOM killed

# 3. If OOM, increase memory limit temporarily
sudo systemctl edit rithm-template
# Add: [Service]
#      MemoryLimit=4G
sudo systemctl daemon-reload
sudo systemctl restart rithm-template
```

### Scenario 2: Database Connection Lost

**Detection**: `/health/ready` returns Unhealthy for PostgreSQL component
**Symptoms**: HTTP 503 on API calls, error logs about connection failures

```bash
# 1. Verify PostgreSQL is running
sudo systemctl status postgresql

# 2. Check connectivity
psql -h localhost -U app_user -d rithmtemplate_db -c "SELECT 1;"

# 3. Check for connection pool exhaustion
psql -U postgres -c "SELECT count(*) FROM pg_stat_activity WHERE datname = 'rithmtemplate_db';"

# 4. If PostgreSQL is down, restart
sudo systemctl restart postgresql

# 5. Application will auto-reconnect (EF Core connection resilience)
# Verify via health check
curl http://localhost:5000/health/ready
```

### Scenario 3: Valkey/Redis Unavailable

**Detection**: `/health/ready` may show degraded state
**Impact**: Idempotency keys not verified, distributed locks unavailable, cache misses

```bash
# 1. Check Valkey status
sudo systemctl status valkey

# 2. Restart if needed
sudo systemctl restart valkey

# 3. Application handles Valkey absence gracefully:
#    - Idempotency: falls back to no deduplication (idempotent by design)
#    - Cache: falls back to database queries (slower but functional)
#    - Locks: operations may have brief contention window
```

### Scenario 4: Certificate Expiry / Service Identity Authority Down

**Detection**: mTLS handshake failures in logs, service-to-service calls fail
**Impact**: Inter-service communication blocked

```bash
# 1. Check certificate status in logs
sudo journalctl -u rithm-template | grep -i "certificate"

# 2. If Service Identity Authority is temporarily down:
#    Application caches last valid certificate
#    Wait for Service Identity Authority recovery

# 3. If extended outage, switch to LocalSecureStore contingency:
sudo systemctl edit rithm-template
# Add: Environment=Certificate__ProviderType=LocalSecureStore

# Place emergency certificate
sudo install -o rithm-template -g rithm-template -m 600 \
  /path/to/emergency-cert.pfx /run/rithm/certs/client.pfx

sudo systemctl restart rithm-template

# 4. Once Service Identity Authority is back, revert:
sudo systemctl revert rithm-template
sudo systemctl restart rithm-template
```

### Scenario 5: Host Failure (Complete Loss)

**Detection**: Host unreachable, all services down
**RTO Target**: 15 minutes

```bash
# 1. Provision new host (bare metal or VM)
# 2. Run full service restore (see "Full Service Restore" section above)
# 3. Update DNS/load balancer to point to new host
# 4. Verify InfraSoT registration
curl http://infrasot-registry.rithmxo.internal/api/services/rithm-template-service
```

### Scenario 6: Data Corruption / Accidental Deletion

**Detection**: Application errors, data inconsistency reported by users
**RPO Target**: 5 minutes (PITR)

```bash
# 1. Identify the time of corruption
sudo journalctl -u rithm-template --since "1 hour ago" | grep -i "error\|delete\|update"

# 2. Run Point-in-Time Recovery to just before the incident
./restore-pitr.sh "2026-02-16 14:25:00 UTC"

# 3. Verify data integrity
psql -U app_user -d rithmtemplate_db -c "SELECT count(*) FROM sample_entities WHERE is_deleted = false;"
```

---

## DR Testing

### Monthly Tests

| Test | Procedure | Success Criteria |
|------|-----------|-----------------|
| **Backup verification** | Restore latest backup to test environment | Data matches production record count |
| **Service restart** | `systemctl restart rithm-template` | Service healthy within 30 seconds |
| **PITR test** | Restore to specific timestamp in test env | Data matches expected state at timestamp |

### Quarterly Tests

| Test | Procedure | Success Criteria |
|------|-----------|-----------------|
| **Full host recovery** | Deploy to clean host from backups + artifacts | All services healthy, data restored |
| **Certificate failover** | Simulate Service Identity Authority outage | Service continues with cached cert, switches to LocalSecureStore |
| **Database failover** | Promote replica to primary (if using replication) | RTO < 5 minutes, RPO ~0 |

### Annual Tests

| Test | Procedure | Success Criteria |
|------|-----------|-----------------|
| **Full DR simulation** | Simulate datacenter failure, recover all services | All services recovered within 4-hour RTO |

### Test Logging

All DR tests should be documented with:

```
Date: YYYY-MM-DD
Test: [test name]
Operator: [name]
Start Time: HH:MM:SS
End Time: HH:MM:SS
Actual RTO: [minutes]
Actual RPO: [data loss if any]
Result: PASS / FAIL
Notes: [any issues encountered]
```

---

## Escalation Matrix

| Severity | Condition | Response Time | Escalation |
|----------|-----------|---------------|------------|
| **P1 - Critical** | Service down, no auto-recovery | 15 minutes | On-call SRE > Service Owner > Platform Lead |
| **P2 - Major** | Service degraded, partial functionality | 30 minutes | On-call SRE > Service Owner |
| **P3 - Minor** | Non-critical component failure | 4 hours | Service Owner |
| **P4 - Low** | Backup failure, monitoring gap | Next business day | Service Owner |

### Contact Channels

| Role | Primary | Secondary |
|------|---------|-----------|
| On-call SRE | [PagerDuty/AlertService] | [Internal chat] |
| Service Owner | [Internal chat] | [Email] |
| Platform Lead | [Internal chat] | [Phone] |
| Database Admin | [Internal chat] | [Email] |

---

## References

- [DEPLOYMENT-SYSTEMD.md](../backend/docs/DEPLOYMENT-SYSTEMD.md) - Deployment guide
- [MODULES.md](MODULES.md) - Module system documentation
- [PostgreSQL PITR Documentation](https://www.postgresql.org/docs/14/continuous-archiving.html)
- [systemd Timer Documentation](https://www.freedesktop.org/software/systemd/man/systemd.timer.html)

---

**Questions or Issues?**
File a ticket in the rithmXO Platform repository or contact the SRE team.
