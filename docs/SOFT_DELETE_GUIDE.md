# Soft Delete & Hard Delete Guide

**Last Updated**: 2026-02-09
**Applies To**: RithmTemplate v2.0+

---

## Overview

This guide explains how soft delete and hard delete work in RithmTemplate, including:
- How soft delete operates automatically
- When and how to use hard delete
- Handling foreign key relationships
- Unique constraints with soft delete
- Compliance considerations (GDPR, data retention)

---

## How Soft Delete Works

### Automatic Soft Delete

**Soft delete is the DEFAULT behavior**. When you call `context.Remove(entity)` or `dbSet.Remove(entity)`, the entity is NOT actually deleted from the database.

**Implementation** (`RithmTemplateDbContext.cs`, lines 308-316):
```csharp
case EntityState.Deleted:
    // Convert hard delete to soft delete
    if (entry.Entity is ISoftDeletable softDeletable)
    {
        entry.State = EntityState.Modified;
        softDeletable.IsDeleted = true;
        softDeletable.DeletedAt = now;
    }
    break;
```

**What happens**:
1. EF Core marks entity as `EntityState.Deleted`
2. `SaveChanges()` intercepts this
3. Changes state to `EntityState.Modified`
4. Sets `IsDeleted = true`, `DeletedAt = DateTime.UtcNow`
5. SQL: `UPDATE ... SET is_deleted = true, deleted_at = NOW() WHERE id = ...`

### Query Filtering

Soft-deleted entities are **automatically excluded** from queries via global filters (`RithmTemplateDbContext.cs`, lines 186-194):

```csharp
// For BaseEntity types (non-tenant)
var condition = Expression.Equal(
    Expression.Property(parameter, nameof(BaseEntity.IsDeleted)),
    Expression.Constant(false));

// For TenantEntity types
Expression<Func<TEntity, bool>> filter = e =>
    !e.IsDeleted &&
    e.TenantId == context.GetCurrentTenantId();
```

**Result**: All queries automatically filter `WHERE is_deleted = false`.

### Bypassing Filters

To query soft-deleted entities (for admin, audit, or restore):
```csharp
// Include soft-deleted entities
var allUsers = await context.Users
    .IgnoreQueryFilters()
    .ToListAsync();

// Or use extension method
var deletedUser = await context.FindIncludingDeletedAsync<User>(userId);
```

---

## Hard Delete (Permanent Deletion)

**Hard delete must be EXPLICIT**. Use extension methods from `DbContextExtensions.cs`.

### When to Use Hard Delete

| Use Case | Reason | Example |
|----------|--------|---------|
| **GDPR Right to Erasure** | Legal requirement | User requests account deletion |
| **PII Purging** | Compliance | Remove personal data after retention period |
| **Test Data Cleanup** | Development | Clean up test database |
| **Explicit User Action** | Business logic | "Permanently delete" button |
| **Data Retention Enforcement** | Policy | Purge data older than X days |

### Usage Examples

#### Single Entity Hard Delete
```csharp
using RithmTemplate.DAL.Extensions;

// Hard delete a user
var success = await context.HardDeleteAsync<User>(userId, logger);

if (success)
{
    logger.LogInformation("User {UserId} permanently deleted", userId);
}
```

#### Batch Purge (Retention Policy)
```csharp
// Purge users soft-deleted more than 90 days ago
var retentionDate = DateTime.UtcNow.AddDays(-90);
var purgedCount = await context.PurgeDeletedAsync<User>(retentionDate, logger);

logger.LogInformation(
    "Purged {Count} users deleted before {Date}",
    purgedCount, retentionDate);
```

#### Restore Soft-Deleted Entity
```csharp
// Restore a soft-deleted user
var restored = await context.RestoreAsync<User>(userId, logger);

if (restored)
{
    logger.LogInformation("User {UserId} restored", userId);
}
```

---

## Foreign Key Relationships

**CRITICAL**: Soft delete does NOT handle cascading deletes automatically. You must handle related entities manually.

### The Problem

When parent entity is soft-deleted, child entities remain visible:

```csharp
// Soft delete user
context.Users.Remove(user);
await context.SaveChangesAsync();
// Result: user.IsDeleted = true

// Child entities (orders) are STILL VISIBLE
var orphanedOrders = await context.Orders
    .Where(o => o.UserId == userId)
    .ToListAsync();
// Returns orders because they are NOT soft-deleted
```

### Solution 1: Manual Cascading (Recommended)

```csharp
public async Task SoftDeleteUserWithCascadeAsync(Guid userId)
{
    var user = await context.Users
        .Include(u => u.Orders)
        .Include(u => u.Addresses)
        .FirstOrDefaultAsync(u => u.Id == userId);

    if (user == null) return false;

    // Soft delete related entities first
    foreach (var order in user.Orders)
    {
        context.Orders.Remove(order); // Triggers soft delete
    }

    foreach (var address in user.Addresses)
    {
        context.Addresses.Remove(address);
    }

    // Then soft delete parent
    context.Users.Remove(user);

    await context.SaveChangesAsync();
    return true;
}
```

### Solution 2: Configure Cascade Behavior

```csharp
// In entity configuration
modelBuilder.Entity<Order>(entity =>
{
    entity.HasOne(o => o.User)
        .WithMany(u => u.Orders)
        .HasForeignKey(o => o.UserId)
        .OnDelete(DeleteBehavior.Restrict); // Prevent accidental cascade
});
```

**Options**:
- `DeleteBehavior.Restrict`: Throw error if parent deleted with children (SAFE)
- `DeleteBehavior.SetNull`: Set FK to null (only if nullable)
- `DeleteBehavior.Cascade`: Database cascade delete (DANGEROUS with soft delete)
- `DeleteBehavior.NoAction`: No action (default)

**Recommendation**: Use `Restrict` to force explicit handling.

### Solution 3: Query Filter with Parent Check

```csharp
// Filter child entities when parent is soft-deleted
modelBuilder.Entity<Order>(entity =>
{
    entity.HasQueryFilter(o =>
        !o.IsDeleted &&
        !o.User.IsDeleted); // Also exclude if parent deleted
});
```

**Warning**: This can cause performance issues (requires JOIN).

### Hard Delete with Foreign Keys

```csharp
public async Task HardDeleteUserWithCascadeAsync(Guid userId)
{
    var user = await context.Users
        .Include(u => u.Orders)
        .Include(u => u.Addresses)
        .FirstOrDefaultAsync(u => u.Id == userId);

    if (user == null) return false;

    // Hard delete related entities first (order matters!)
    foreach (var order in user.Orders)
    {
        await context.HardDeleteAsync<Order>(order.Id, logger);
    }

    foreach (var address in user.Addresses)
    {
        await context.HardDeleteAsync<Address>(address.Id, logger);
    }

    // Then hard delete parent
    await context.HardDeleteAsync<User>(userId, logger);

    return true;
}
```

**IMPORTANT**: Delete children before parent to avoid FK constraint violations.

---

## Unique Constraints with Soft Delete

**The Problem**: Soft-deleted rows can break unique constraints.

### Example
```sql
-- User with email "john@example.com" soft-deleted
UPDATE users SET is_deleted = true WHERE id = '...';

-- Try to register new user with same email
INSERT INTO users (email) VALUES ('john@example.com');
-- ERROR: duplicate key value violates unique constraint "users_email_key"
```

### Solution 1: Partial Unique Index (Recommended)

```sql
-- Only enforce uniqueness on NON-deleted rows
CREATE UNIQUE INDEX users_email_active_unique
    ON users (email)
    WHERE is_deleted = false;

-- Allow multiple soft-deleted rows with same email
CREATE INDEX users_email_deleted_idx
    ON users (email, is_deleted)
    WHERE is_deleted = true;
```

**EF Core Configuration**:
```csharp
modelBuilder.Entity<User>(entity =>
{
    entity.HasIndex(e => e.Email)
        .IsUnique()
        .HasFilter("is_deleted = false") // PostgreSQL syntax
        .HasDatabaseName("users_email_active_unique");
});
```

### Solution 2: Composite Unique Index

```sql
-- Include is_deleted in unique constraint
CREATE UNIQUE INDEX users_email_is_deleted_unique
    ON users (email, is_deleted);
```

**Trade-off**: Allows only ONE soft-deleted row per email (not ideal).

### Solution 3: Conditional Validation

```csharp
public class User : BaseEntity
{
    public required string Email { get; set; }

    public async Task ValidateUniqueEmailAsync(DbContext context)
    {
        var existingActive = await context.Users
            .Where(u => u.Email == Email && !u.IsDeleted && u.Id != Id)
            .AnyAsync();

        if (existingActive)
        {
            throw new ValidationException("Email already in use");
        }
    }
}
```

### Migration Example

```sql
-- Create partial unique index for sample_entities.name
CREATE UNIQUE INDEX sample_entities_name_tenant_active_unique
    ON sample_entities (name, tenant_id)
    WHERE is_deleted = false;

-- Drop old unique constraint if exists
ALTER TABLE sample_entities DROP CONSTRAINT IF EXISTS sample_entities_name_key;
```

---

## Best Practices

### 1. Default to Soft Delete

✅ **DO**: Use soft delete by default for user-facing data
```csharp
context.Users.Remove(user);
await context.SaveChangesAsync(); // Automatic soft delete
```

❌ **DON'T**: Use hard delete unless explicitly required

### 2. Document Hard Delete Use Cases

```csharp
// ✅ Good: Documented reason
// GDPR: Hard delete user data after right-to-erasure request
await context.HardDeleteAsync<User>(userId, logger);

// ❌ Bad: No justification
await context.HardDeleteAsync<User>(userId, logger);
```

### 3. Handle Foreign Keys Explicitly

```csharp
// ✅ Good: Explicit cascade handling
await SoftDeleteUserWithCascadeAsync(userId);

// ❌ Bad: Forgetting child entities
context.Users.Remove(user); // Orphans orders!
```

### 4. Use Partial Unique Indexes

```sql
-- ✅ Good: Only enforce on active rows
CREATE UNIQUE INDEX ... WHERE is_deleted = false;

-- ❌ Bad: Breaks on soft delete
CREATE UNIQUE INDEX ...;
```

### 5. Log Hard Deletes

```csharp
// Extension methods automatically log with ⚠️ warning
await context.HardDeleteAsync<User>(userId, logger);
// Logs: "⚠️ HARD DELETED User with ID ... This action is IRREVERSIBLE."
```

### 6. Test Restore Functionality

```csharp
[Fact]
public async Task Should_Restore_SoftDeleted_Entity()
{
    // Arrange: Soft delete user
    context.Users.Remove(user);
    await context.SaveChangesAsync();

    // Act: Restore user
    var restored = await context.RestoreAsync<User>(userId, logger);

    // Assert
    Assert.True(restored);
    var restoredUser = await context.Users.FindAsync(userId);
    Assert.NotNull(restoredUser);
    Assert.False(restoredUser.IsDeleted);
}
```

### 7. Implement Retention Policies

```csharp
// Background job: Purge old soft-deleted data
public class DataRetentionJob : IHostedService
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var retentionDate = DateTime.UtcNow.AddDays(-90);

        // Purge users
        await context.PurgeDeletedAsync<User>(retentionDate, logger);

        // Purge orders
        await context.PurgeDeletedAsync<Order>(retentionDate, logger);
    }
}
```

---

## Compliance Considerations

### GDPR "Right to Erasure"

**Requirement**: Users can request permanent deletion of personal data.

**Implementation**:
```csharp
public async Task ProcessErasureRequestAsync(Guid userId)
{
    // 1. Soft delete first (reversible, for audit trail)
    var user = await context.Users.FindAsync(userId);
    context.Users.Remove(user);
    await context.SaveChangesAsync();

    // 2. Log erasure request
    await auditLog.LogAsync("GDPR_ERASURE_REQUEST", userId);

    // 3. Wait compliance period (e.g., 30 days)
    // 4. Hard delete after period (automated job)
    var scheduledDate = DateTime.UtcNow.AddDays(30);
    await scheduler.ScheduleHardDeleteAsync(userId, scheduledDate);
}
```

### Data Retention Policies

**Example Policy**:
- Soft-deleted data retained for 90 days
- After 90 days, permanently purged
- Audit logs retained for 7 years

**Implementation**:
```csharp
// appsettings.json
"DataRetention": {
  "SoftDeleteRetentionDays": 90,
  "AuditLogRetentionYears": 7
}

// Background job
var retentionDays = configuration.GetValue<int>("DataRetention:SoftDeleteRetentionDays");
var purgeDate = DateTime.UtcNow.AddDays(-retentionDays);
await context.PurgeDeletedAsync<User>(purgeDate, logger);
```

---

## Performance Considerations

### Indexes on IsDeleted

```sql
-- Composite index for soft delete queries
CREATE INDEX sample_entities_is_deleted_idx
    ON sample_entities (is_deleted);

-- Partial index (more efficient)
CREATE INDEX sample_entities_active_idx
    ON sample_entities (tenant_id, status)
    WHERE is_deleted = false;
```

### Query Performance

```csharp
// ❌ Slow: Forces table scan
var activeUsers = await context.Users
    .Where(u => !u.IsDeleted) // Redundant, global filter does this
    .ToListAsync();

// ✅ Fast: Uses global filter + index
var activeUsers = await context.Users.ToListAsync();
```

### Archival Strategy

For high-volume tables, consider archiving soft-deleted data:

```sql
-- Create archive table
CREATE TABLE users_archive AS SELECT * FROM users WHERE false;

-- Move old soft-deleted rows to archive
INSERT INTO users_archive
SELECT * FROM users
WHERE is_deleted = true AND deleted_at < NOW() - INTERVAL '1 year';

-- Hard delete from main table
DELETE FROM users
WHERE is_deleted = true AND deleted_at < NOW() - INTERVAL '1 year';
```

---

## Troubleshooting

### Issue: FK Constraint Violation on Hard Delete

**Error**: `violates foreign key constraint`

**Cause**: Trying to hard delete parent with child entities

**Solution**: Delete children first
```csharp
// Load related entities
var user = await context.Users
    .Include(u => u.Orders)
    .FirstAsync(u => u.Id == userId);

// Delete children first
foreach (var order in user.Orders)
    await context.HardDeleteAsync<Order>(order.Id, logger);

// Then parent
await context.HardDeleteAsync<User>(userId, logger);
```

### Issue: Unique Constraint Violation After Soft Delete

**Error**: `duplicate key value violates unique constraint`

**Cause**: Unique index includes soft-deleted rows

**Solution**: Use partial unique index
```sql
CREATE UNIQUE INDEX users_email_active_unique
    ON users (email)
    WHERE is_deleted = false;
```

### Issue: Queries Return Too Many Rows

**Symptom**: Aggregate counts include soft-deleted entities

**Cause**: Using `IgnoreQueryFilters()` unnecessarily

**Solution**: Remove `IgnoreQueryFilters()` unless needed
```csharp
// ❌ Includes soft-deleted
var count = await context.Users
    .IgnoreQueryFilters()
    .CountAsync();

// ✅ Excludes soft-deleted
var count = await context.Users.CountAsync();
```

---

## Migration Checklist

When adding new entities with soft delete:

- [ ] Entity inherits from `BaseEntity` or `TenantEntity`
- [ ] Unique constraints use partial indexes (`WHERE is_deleted = false`)
- [ ] FK relationships configured (`OnDelete` behavior)
- [ ] Indexes include `is_deleted` column if needed
- [ ] Cascading delete logic implemented if applicable
- [ ] Hard delete authorization rules defined
- [ ] Data retention policy documented
- [ ] Tests cover soft delete, restore, and hard delete scenarios

---

## References

- DbContext soft delete implementation: [`RithmTemplateDbContext.cs:308-316`](../backend/src/RithmTemplate.DAL/Persistence/RithmTemplateDbContext.cs)
- Extension methods: [`DbContextExtensions.cs`](../backend/src/RithmTemplate.DAL/Extensions/DbContextExtensions.cs)
- Base entities: [`BaseEntity.cs`](../backend/src/RithmTemplate.DAL/Entities/BaseEntity.cs)
- CEO Feedback Analysis: [`CEO_FEEDBACK_ANALYSIS.md`](CEO_FEEDBACK_ANALYSIS.md) (Item D)

---

**Created**: 2026-02-09
**Status**: Production-Ready
