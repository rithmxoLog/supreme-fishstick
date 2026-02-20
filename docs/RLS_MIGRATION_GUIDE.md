# PostgreSQL RLS (Row-Level Security) Migration Guide

**Date**: 2026-02-09
**Purpose**: Enable PostgreSQL RLS policies for defense-in-depth tenant isolation

---

## Overview

This guide explains how to apply PostgreSQL Row-Level Security (RLS) policies to enforce tenant isolation at the database level. RLS provides a second layer of defense on top of EF Core global filters.

### Why RLS?

1. **Defense-in-Depth**: Protects against bugs in application-layer filters
2. **Database-Level Enforcement**: Cannot be bypassed by application code
3. **Compliance**: Required for SOC 2, ISO 27001 multi-tenant compliance
4. **Zero-Trust Architecture**: Database enforces boundaries even if app is compromised

---

## Prerequisites

- PostgreSQL 12+ (RLS policies support)
- Database user with `ALTER TABLE` and `CREATE POLICY` permissions
- Existing `sample_entities` table with `tenant_id` column
- TenantConnectionInterceptor configured to set `app.current_tenant` session variable

---

## Migration Files

| File | Purpose |
|------|---------|
| `backend/src/RithmTemplate.DAL/Migrations/SQL/20260209_EnableRLSPolicies.sql` | SQL migration script |
| `backend/scripts/apply-rls-migration.sh` | Bash helper script |
| `docs/RLS_MIGRATION_GUIDE.md` | This guide |

---

## Application Methods

### Method 1: Using Helper Script (Recommended for Local/Dev)

```bash
cd backend/scripts

# Set environment variables
export DB_HOST=localhost
export DB_PORT=5432
export DB_NAME=rithm_template
export DB_USER=postgres
export DB_PASSWORD=your_password

# Run migration script
./apply-rls-migration.sh
```

The script will:
1. Verify migration file exists
2. Display connection info
3. Prompt for confirmation
4. Apply migration
5. Show verification commands

### Method 2: Using psql Directly (Recommended for Production)

```bash
# Navigate to migrations directory
cd backend/src/RithmTemplate.DAL/Migrations/SQL

# Apply migration
psql -h localhost -p 5432 -U postgres -d rithm_template -f 20260209_EnableRLSPolicies.sql
```

### Method 3: EF Core Migration with Raw SQL (Future)

For integration with EF Core migrations:

```csharp
// In future EF Core migration
public partial class EnableRLSPolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var sql = File.ReadAllText("../SQL/20260209_EnableRLSPolicies.sql");
        migrationBuilder.Sql(sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            DROP POLICY IF EXISTS sample_entities_tenant_isolation_policy ON sample_entities;
            ALTER TABLE sample_entities DISABLE ROW LEVEL SECURITY;
        ");
    }
}
```

---

## Verification

### 1. Verify RLS is Enabled

```sql
SELECT schemaname, tablename, rowsecurity
FROM pg_tables
WHERE tablename = 'sample_entities';
```

**Expected Output**:
```
 schemaname |    tablename    | rowsecurity
------------+-----------------+-------------
 public     | sample_entities | t
```

### 2. Verify Policy Exists

```sql
SELECT schemaname, tablename, policyname, permissive, cmd
FROM pg_policies
WHERE tablename = 'sample_entities';
```

**Expected Output**:
```
 schemaname |    tablename    |          policyname                | permissive | cmd
------------+-----------------+------------------------------------+------------+-----
 public     | sample_entities | sample_entities_tenant_isolation_policy | PERMISSIVE | ALL
```

### 3. Test Tenant Isolation

#### Test A: Query with Tenant Context

```sql
-- Set tenant context (simulating TenantConnectionInterceptor)
SET app.current_tenant = '00000000-0000-0000-0000-000000000001';

-- Query should only return tenant 1's data
SELECT id, tenant_id, name FROM sample_entities;
```

**Expected**: Only rows with `tenant_id = '00000000-0000-0000-0000-000000000001'`

#### Test B: Switch Tenant Context

```sql
-- Switch to different tenant
SET app.current_tenant = '00000000-0000-0000-0000-000000000002';

-- Query should only return tenant 2's data
SELECT id, tenant_id, name FROM sample_entities;
```

**Expected**: Only rows with `tenant_id = '00000000-0000-0000-0000-000000000002'`

#### Test C: Attempt Cross-Tenant Insert (Should Fail)

```sql
-- Set tenant context
SET app.current_tenant = '00000000-0000-0000-0000-000000000001';

-- Try to insert data for different tenant
INSERT INTO sample_entities (id, tenant_id, name, created_at)
VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000002', 'Cross-tenant test', NOW());
```

**Expected**: Error message:
```
ERROR: new row violates row-level security policy for table "sample_entities"
```

#### Test D: Query Without Tenant Context (Should Return Empty)

```sql
-- Reset tenant context
RESET app.current_tenant;

-- Query should return nothing (safe failure mode)
SELECT id, tenant_id, name FROM sample_entities;
```

**Expected**: Empty result set (RLS policy blocks all rows when tenant context not set)

---

## Rollback

**⚠️ WARNING**: Only rollback in non-production environments. Disabling RLS removes critical security layer.

```sql
-- Remove RLS policy
DROP POLICY IF EXISTS sample_entities_tenant_isolation_policy ON sample_entities;

-- Disable RLS
ALTER TABLE sample_entities DISABLE ROW LEVEL SECURITY;
```

---

## Adding RLS to New Tables

When creating new tenant-scoped tables (inheriting from `TenantEntity`), follow this pattern:

```sql
-- 1. Enable RLS on the table
ALTER TABLE your_new_table ENABLE ROW LEVEL SECURITY;

-- 2. Create tenant isolation policy
CREATE POLICY your_new_table_tenant_isolation_policy
    ON your_new_table
    USING (tenant_id = current_setting('app.current_tenant', true)::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant', true)::uuid);
```

### Policy Explanation

- **USING**: Controls which rows are visible in SELECT queries
- **WITH CHECK**: Controls which rows can be inserted/updated
- **current_setting('app.current_tenant', true)**: Reads session variable set by TenantConnectionInterceptor
  - Second parameter `true` = return NULL if variable not set (instead of error)
- **::uuid**: Casts string to UUID for comparison

---

## Troubleshooting

### Issue: "ERROR: invalid input syntax for type uuid"

**Cause**: Session variable `app.current_tenant` not set or invalid format

**Solution**: Ensure TenantConnectionInterceptor is executing `SET app.current_tenant = '{tenantId}'`

```csharp
// Verify in TenantConnectionInterceptor.cs
var setTenantCommand = connection.CreateCommand();
setTenantCommand.CommandText = $"SET app.current_tenant = '{tenantId}'";
await setTenantCommand.ExecuteNonQueryAsync();
```

### Issue: "ERROR: permission denied for table sample_entities"

**Cause**: Database user lacks permissions

**Solution**: Grant necessary permissions

```sql
GRANT SELECT, INSERT, UPDATE, DELETE ON sample_entities TO rithm_app_user;
```

### Issue: All queries return empty results

**Cause**: Session variable not set correctly

**Solution**: Verify tenant context is set before queries

```sql
-- Check current setting
SHOW app.current_tenant;

-- If empty, manually set for testing
SET app.current_tenant = '00000000-0000-0000-0000-000000000001';
```

### Issue: RLS policy blocks legitimate queries

**Cause**: Policy definition mismatch

**Solution**: Verify policy uses correct column and session variable

```sql
-- Check policy definition
SELECT pg_get_expr(qual, 'sample_entities'::regclass) AS using_clause,
       pg_get_expr(with_check, 'sample_entities'::regclass) AS with_check_clause
FROM pg_policy
WHERE polname = 'sample_entities_tenant_isolation_policy';
```

---

## Performance Considerations

### Indexes

RLS policies benefit from indexes on tenant_id:

```sql
-- Already created in table configuration
CREATE INDEX ix_sample_entities_tenant_id ON sample_entities(tenant_id);
```

### Query Planning

Verify PostgreSQL uses index for RLS filtering:

```sql
SET app.current_tenant = '00000000-0000-0000-0000-000000000001';
EXPLAIN ANALYZE SELECT * FROM sample_entities WHERE status = 1;
```

**Expected**: Plan should include "Index Scan using ix_sample_entities_tenant_id"

---

## Security Best Practices

1. **Always Enable RLS in Production**: Never skip this migration
2. **Verify After Deployment**: Run verification queries post-deploy
3. **Monitor Policy Violations**: Set up alerts for RLS policy errors
4. **Test Cross-Tenant Access**: Include RLS bypass tests in security audit
5. **Document Exceptions**: If bypassing RLS (e.g., admin queries), document why and how

---

## References

- PostgreSQL RLS Documentation: https://www.postgresql.org/docs/current/ddl-rowsecurity.html
- Validation Report: `VALIDATION_REPORT.md` (Fase 2 compliance)
- CEO Feedback Analysis: `CEO_FEEDBACK_ANALYSIS.md` (Item #3)
- Backlog Item: `BACKLOG.md` (#1 - ALTA prioridad)

---

## Deployment Checklist

- [ ] Review migration SQL file
- [ ] Test migration in development environment
- [ ] Verify RLS enabled (`SELECT rowsecurity FROM pg_tables WHERE tablename = 'sample_entities'`)
- [ ] Verify policy exists (`SELECT * FROM pg_policies WHERE tablename = 'sample_entities'`)
- [ ] Run Test A: Query with tenant context
- [ ] Run Test B: Switch tenant context
- [ ] Run Test C: Attempt cross-tenant insert (should fail)
- [ ] Run Test D: Query without tenant context (should return empty)
- [ ] Check query performance with EXPLAIN ANALYZE
- [ ] Apply migration to staging environment
- [ ] Verify application functionality in staging
- [ ] Apply migration to production (during maintenance window)
- [ ] Post-deployment verification queries
- [ ] Update BACKLOG.md to mark item #1 as completed

---

**Migration Created**: 2026-02-09
**Last Updated**: 2026-02-09
**Status**: Ready for Application
