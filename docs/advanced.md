---
title: Advanced Features
nav_order: 10
---

# Advanced Features
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

---

## Soft delete

Soft delete marks records as "deleted" by setting a timestamp column instead of issuing a `DELETE` statement. Normal queries exclude soft-deleted rows automatically.

### Setup

Add a `[SoftDelete]` column to your entity:

```csharp
[Column("deleted_at"), SoftDelete]
public DateTime? DeletedAt { get; set; }
```

Or configure globally in the builder:

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .SoftDeleteAll("DeletedAt"));  // applies to all entities with this column name
```

### Usage

```csharp
// "Delete" — sets deleted_at = now
await db.DeleteAsync<Farm>(id);

// Normal query — deleted rows are invisible
var active = await db.Query<Farm>().ToListAsync();

// Include soft-deleted rows
var all = await db.Query<Farm>().IncludeDeleted().ToListAsync();

// Only soft-deleted rows
var deleted = await db.Query<Farm>().OnlyDeleted().ToListAsync();

// Hard delete (bypass soft-delete)
await db.HardDeleteAsync<Farm>(id);

// Restore
await db.RestoreAsync<Farm>(id);
```

---

## Multi-tenancy

FluentORM can automatically scope every query and mutation to the current tenant.

### Setup

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .MultiTenant(mt => mt
        .Column("TenantId")
        .ResolveFrom<IHttpContextAccessor>(
            ctx => ctx.HttpContext?.User.FindFirst("tid")?.Value)));
```

Add the tenant key column to your entities:

```csharp
[TenantKey]
public string TenantId { get; set; } = string.Empty;
```

### Usage

```csharp
// Scoped to the current tenant automatically
var farms = await db.Query<Farm>().ToListAsync();
// → SELECT * FROM Farms WHERE TenantId = @current

// Scope to a specific tenant (admin code)
var tenantDb = db.ForTenant("tenant-abc");
var farms = await tenantDb.Query<Farm>().ToListAsync();

// Cross-tenant query (super-admin only)
var allFarms = await db.QueryAllTenants<Farm>().ToListAsync();
```

---

## Query caching

Cache expensive queries in memory or Redis without changing your query code.

### Setup

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    // In-memory cache (single instance)
    .UseMemoryCache()
    .DefaultCacheTtl(TimeSpan.FromMinutes(5))
    
    // OR distributed cache (multi-instance)
    // .UseDistributedCache("localhost:6379")
);
```

### Per-query caching

```csharp
// Cache for 10 minutes
var farms = await db.Query<Farm>()
    .Where(f => f.Location == "Paarl")
    .CacheFor(TimeSpan.FromMinutes(10))
    .ToListAsync();

// Cache but invalidate when the Field table is mutated
var farms = await db.Query<Farm>()
    .CacheFor(TimeSpan.FromMinutes(5))
    .InvalidateOn<Field>()
    .ToListAsync();
```

### Manual invalidation

```csharp
// Invalidate all cached queries for Farm
await db.Cache.InvalidateAsync<Farm>();

// Invalidate cached queries for a specific predicate
await db.Cache.InvalidateAsync<Farm>(f => f.Location == "Paarl");
```

### Opt out a table

```csharp
[NoCache]
[Table("AuditLogs")]
public class AuditLog { }
```

---

## Audit trail

FluentORM can record every INSERT, UPDATE, and DELETE to an `__FluentAudit` table.

### Setup

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .AuditAll<Farm>()        // audit every mutation on Farm
    .AuditAll<Field>());
```

Or per-column with `[Audit]`:

```csharp
[Audit]
public string Status { get; set; } = string.Empty;

[Audit]
public decimal Price { get; set; }
```

Opt out a table entirely with `[NoAudit]`:

```csharp
[NoAudit]
[Table("Sessions")]
public class Session { }
```

### Retrieving history

```csharp
var history = await db.AuditHistory<Farm>(id: 42);

foreach (var entry in history)
{
    Console.WriteLine(
        $"{entry.Timestamp:u}  {entry.Operation}  " +
        $"{entry.Column}: {entry.OldValue} → {entry.NewValue}");
}

// Output:
// 2024-06-01 09:00:00Z  UPDATE  Status: pending → active
// 2024-06-02 14:22:00Z  UPDATE  Status: active → suspended
```

---

## Read replicas

Route read queries to a replica automatically:

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("Data Source=primary.db")
    .UseReadReplica("Data Source=replica.db"));
```

`db.Query<T>()` is routed to the replica. All mutations (`InsertAsync`, `UpdateAsync`, etc.) go to the primary.

---

## Connection pooling

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .Pool(configure: pool => pool
        .Min(2)
        .Max(50)
        .ConnectionTimeout(TimeSpan.FromSeconds(10))
        .CommandTimeout(TimeSpan.FromSeconds(30))
        .IdleTimeout(TimeSpan.FromMinutes(5))));
```

```csharp
// Runtime stats
var stats = db.PoolStats();
Console.WriteLine($"Active: {stats.Active}, Idle: {stats.Idle}, Total: {stats.Total}");
```

---

## Retry on transient errors

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .Retry(r => r
        .Attempts(3)
        .Backoff(BackoffStrategy.Exponential)
        .RetryOn<SqliteException>(ex => ex.SqliteErrorCode == 5))); // SQLITE_BUSY
```

For SQL Server, retry on common transient error codes (timeout, deadlock):

```csharp
.Retry(r => r
    .Attempts(3)
    .Backoff(BackoffStrategy.Exponential)
    .RetryOn<SqlException>(ex => ex.Number is 1205 or -2 or 49918))
```

---

## N+1 detection

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .DetectNPlusOne(WhenDetected.Throw));   // Warn | Throw | Disabled
```

When an N+1 pattern is detected (the same query executed many times in a loop), FluentORM either logs a warning or throws `NPlusOneException`, helping you catch the issue at development time.

---

## Slow query logging

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .SlowQueryThreshold(TimeSpan.FromMilliseconds(200))
    .LogTo(loggerFactory.CreateLogger("FluentORM.SlowQuery")));
```

Queries exceeding the threshold are logged at Warning level with the SQL, parameters, and execution time.

---

## Schema validation on startup

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .ValidateSchemaOnStartup(DriftMode.Warn));
    // DriftMode.Disabled — skip validation
    // DriftMode.Warn     — log warnings on drift
    // DriftMode.Throw    — throw on drift (good for CI)
```

---

## Lifecycle callbacks

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .OnQueryExecuted((sql, parameters, elapsed, rowsRead) =>
    {
        metrics.TrackQuery(elapsed.TotalMilliseconds, rowsRead);
    })
    .OnMutationExecuted((table, operation, rowsAffected, elapsed) =>
    {
        metrics.TrackMutation(table, operation, rowsAffected);
    })
    .OnConcurrencyConflict((table, id) =>
    {
        logger.LogWarning("Concurrency conflict on {Table} id={Id}", table, id);
    })
    .OnConnectionPoolExhausted(() =>
    {
        alerts.Trigger("ConnectionPoolExhausted");
    }));
```

---

## Custom type mapping

Map C# types that aren't supported natively by the provider:

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("...")
    .MapType<Guid>(
        toDb:   guid  => guid.ToString(),
        fromDb: value => Guid.Parse(value.ToString()!))
    .MapType<JsonDocument>(
        toDb:   doc   => doc.RootElement.GetRawText(),
        fromDb: value => JsonDocument.Parse(value.ToString()!)));
```

---

## Raw SQL

```csharp
// Typed results
var farms = await db.RawAsync<Farm>(
    "SELECT * FROM Farms WHERE length(name) > @min",
    new { min = 10 });

// Scalar value
var count = await db.ScalarAsync<int>(
    "SELECT COUNT(*) FROM Farms WHERE location = @loc",
    new { loc = "Paarl" });

// Non-query
var affected = await db.ExecuteAsync(
    "UPDATE Farms SET status = @status WHERE last_audit < @cutoff",
    new { status = "overdue", cutoff = DateTime.UtcNow.AddYears(-1) });
```
