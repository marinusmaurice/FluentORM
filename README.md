<div align="center">

# FluentORM

**A developer-first C# ORM for .NET 8**

Explicit mutations · Readable SQL · Structural multi-tenancy · No magic

[![.NET](https://img.shields.io/badge/.NET-8.0+-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)
[![Status](https://img.shields.io/badge/status-in%20development-orange?style=flat-square)]()
[![SQL Server](https://img.shields.io/badge/SQL%20Server-2019+-CC2927?style=flat-square&logo=microsoftsqlserver)](https://www.microsoft.com/sql-server)
[![SQLite](https://img.shields.io/badge/SQLite-3.35+-003B57?style=flat-square&logo=sqlite)](https://sqlite.org)

</div>

---

## The Problem With Existing ORMs

**EF Core** generates SQL that is difficult to read, silently tracks everything in memory, and makes multi-tenancy a convention rather than a guarantee. Miss one `WHERE TenantId =` clause and you have a data breach.

**Dapper** gives you raw control but no migrations, no bulk operations, and forces you to manage SQL strings that break silently when your schema changes.

**FluentORM** takes the best of both — and fixes what neither gets right.

---

## What Makes FluentORM Different

### 1. SQL You Can Actually Read

Every query FluentORM generates is formatted, aliased, and structured exactly as a senior developer would write it by hand.

```sql
-- FluentORM output
SELECT
    p.Id,
    p.Name,
    p.RiskLevel,
    COUNT(s.Id)          AS ScoutingCount,
    MAX(s.SeverityScore) AS MaxSeverity
FROM
    Pests p
    INNER JOIN Scouting s ON p.Id = s.PestId
WHERE
    p.TenantId  = @tenantId
    AND p.RiskLevel > @minRisk
    AND p.DeletedAt IS NULL
GROUP BY
    p.Id,
    p.Name
HAVING
    COUNT(s.Id) > @minScoutings
ORDER BY
    MaxSeverity DESC
OFFSET @skip ROWS
FETCH NEXT @take ROWS ONLY
```

No more deciphering machine-generated SQL at 2am when production is on fire.

### 2. Explicit Mutations — You Control What Gets Written

No change tracking. No background state. Every mutation names exactly which columns are written.

```csharp
// Only RiskLevel and UpdatedAt are written — nothing else, ever
await db.UpdateAsync(pest, p => new { p.RiskLevel, p.UpdatedAt });
```

```sql
UPDATE Pests
SET    RiskLevel = @0,
       UpdatedAt = @1
WHERE  Id       = @2
  AND  TenantId = @3
```

### 3. Multi-Tenancy That Cannot Be Forgotten

TenantId is injected structurally by the framework on every single query. There is no API to accidentally omit it.

```csharp
// TenantId is automatically injected — you cannot forget it
await db.Query<Pest>().Where(p => p.RiskLevel > 3).ToListAsync();

// Cross-tenant access requires an explicit opt-in call
await db.QueryAllTenants<Pest>().ToListAsync(); // admin only
```

---

## Quick Start

```bash
dotnet add package FluentORM.Core
dotnet add package FluentORM.SqlServer   # or FluentORM.Sqlite
dotnet add package FluentORM.Migrations
```

### Register

```csharp
services.AddFluentOrm(cfg =>
{
    cfg.UseSqlServer(connectionString);
    cfg.Pool(min: 2, max: 100);
    cfg.SlowQueryThreshold(TimeSpan.FromMilliseconds(500));
    cfg.LogTo(logger);
    cfg.AuditAll<AuditEntry>();
    cfg.SoftDeleteAll(col: "DeletedAt");

    cfg.MultiTenant(mt =>
    {
        mt.Column("TenantId");
        mt.ResolveFrom<IHttpContextAccessor>(ctx =>
            ctx.HttpContext?.User.FindFirst("tid")?.Value
        );
    });
});
```

### Define an Entity

```csharp
[Table("Pests")]
public class Pest
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int RiskLevel { get; set; }

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;

    [RowVersion]
    public byte[] Version { get; set; } = [];

    [SoftDelete]
    public DateTime? DeletedAt { get; set; }
}
```

### Query

```csharp
// Basic query — TenantId auto-injected
var pests = await db.Query<Pest>()
    .Where(p => p.RiskLevel > 3)
    .OrderByDesc(p => p.RiskLevel)
    .Take(20)
    .ToListAsync();

// Join
var results = await db.Query<Pest>()
    .Join<Scouting>((p, s) => p.Id == s.PestId)
    .Select((p, s) => new { p.Name, p.RiskLevel, s.ObservedAt })
    .ToListAsync();

// Group By + Having
var hotspots = await db.Query<Scouting>()
    .GroupBy(s => s.FieldId)
    .Having(g => g.Average(s => s.SeverityScore) > 3.5)
    .Select(g => new { FieldId = g.Key, AvgSeverity = g.Average(s => s.SeverityScore) })
    .ToListAsync();

// CTE
var results = await db
    .WithCTE("recent", cte => cte
        .Query<Scouting>()
        .Where(s => s.ObservedAt > DateTime.UtcNow.AddDays(-7))
    )
    .Query<Field>()
    .JoinCTE("recent", (f, cte) => f.Id == cte.FieldId)
    .ToListAsync();
```

### Mutate

```csharp
// Insert
await db.InsertAsync(pest);
int newId = await db.InsertAndGetIdAsync<Pest, int>(pest);

// Update — explicit columns only
await db.UpdateAsync(pest, p => new { p.RiskLevel, p.UpdatedAt });

// Upsert
await db.UpsertAsync(pest, conflictOn: p => p.ExternalId);

// Delete (soft if [SoftDelete] configured)
await db.DeleteAsync<Pest>(id);

// Bulk
await db.BulkInsertAsync(records);
await db.BulkUpsertAsync(records, conflictOn: r => r.ExternalId);
```

### Transactions

```csharp
await db.TransactionAsync(async tx =>
{
    var farmId = await tx.InsertAndGetIdAsync<Farm, int>(farm);
    field.FarmId = farmId;
    await tx.InsertAsync(field);
    await tx.InsertAsync(auditLog);
    // Auto-commit on success, auto-rollback on exception
});
```

### Migrations

```csharp
[Migration(20240115_001, "create_pest_table")]
public class CreatePestTable : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.CreateTable<Pest>(t =>
        {
            t.PrimaryKey(p => p.Id).AutoIncrement();
            t.Column(p => p.Name).NotNull().MaxLength(200);
            t.Column(p => p.TenantId).NotNull().Indexed();
            t.Column(p => p.RiskLevel).NotNull().Default(0);
            t.Column(p => p.DeletedAt).Nullable();
            t.Column(p => p.Version).IsRowVersion();
        });
    }

    public override void Down(SchemaBuilder schema) =>
        schema.DropTable<Pest>();
}

// Apply
await db.Migrations.PreviewAsync();  // see SQL first
await db.Migrations.ApplyAsync();    // then run it
```

---

## Feature Overview

| Feature | FluentORM | EF Core | Dapper |
|---|---|---|---|
| Readable SQL output | ✅ Always | ❌ Machine-generated | ✅ You write it |
| Explicit column mutations | ✅ Required | ❌ Change tracking | ✅ You write it |
| Structural multi-tenancy | ✅ Framework-enforced | ⚠️ Global filters (bypassable) | ❌ Manual |
| Optimistic concurrency | ✅ Built-in | ✅ Built-in | ❌ Manual |
| Soft deletes | ✅ Built-in | ⚠️ Extension required | ❌ Manual |
| Audit trail | ✅ Built-in | ⚠️ Extension required | ❌ Manual |
| Bulk operations | ✅ Built-in | ⚠️ Limited | ❌ Manual |
| Migrations | ✅ Built-in | ✅ Built-in | ❌ None |
| N+1 detection | ✅ Dev mode | ❌ | ❌ |
| In-memory test provider | ✅ Built-in | ✅ Built-in | ❌ |
| Query SQL capture | ✅ Built-in | ⚠️ Logging only | ❌ |
| Window functions | ✅ Fluent API | ⚠️ Raw SQL required | ⚠️ Raw SQL |
| CTEs | ✅ Fluent API | ⚠️ Raw SQL required | ⚠️ Raw SQL |

---

## Testing

```csharp
// Spin up a real in-memory DB that mirrors your schema
await using var testDb = await DbTest.CreateAsync<AppDbContext>();

// Seed
await testDb.SeedAsync(new Pest { Name = "Aphid", RiskLevel = 3, TenantId = "t1" });

// Assert on generated SQL — catches N+1 in CI
var monitor = testDb.MonitorQueries();
var results = await testDb.Query<Pest>().ToListAsync();

monitor.AssertQueryCount(1);
monitor.AssertNoFullTableScans();
monitor.AssertSqlContains("WHERE p.TenantId = @p0");

// Verify tenant isolation
await testDb.SeedAsync(new Pest { Name = "Locust", TenantId = "t2" });
var forT1 = await testDb.ForTenant("t1").Query<Pest>().ToListAsync();
Assert.DoesNotContain(forT1, p => p.TenantId == "t2");
```

---

## Packages

| Package | Purpose |
|---|---|
| `FluentORM.Core` | Interfaces, query builder, base abstractions |
| `FluentORM.SqlServer` | SQL Server 2019+ dialect & connection factory |
| `FluentORM.Sqlite` | SQLite 3.35+ dialect & connection factory |
| `FluentORM.Migrations` | Schema migration engine |
| `FluentORM.Testing` | InMemory provider, query capture, assertion helpers |

---

## Project Structure

```
FluentORM/
├── docs/
│   └── FluentORM_Specification.docx   # Complete technical specification
├── src/
│   ├── FluentORM.Core/
│   ├── FluentORM.SqlServer/
│   ├── FluentORM.Sqlite/
│   ├── FluentORM.Migrations/
│   └── FluentORM.Testing/
└── tests/
    ├── FluentORM.Core.Tests/
    ├── FluentORM.SqlServer.Tests/
    ├── FluentORM.Sqlite.Tests/
    └── FluentORM.Migrations.Tests/
```

---

## Design Principles

- **Explicit over implicit** — every mutation names its columns; nothing is tracked in the background
- **Readable SQL always** — generated SQL is formatted, aliased, and structured for humans
- **Tenant safety is structural** — enforced by the framework, not by developer convention
- **No magic, full escape hatch** — raw SQL always available but never required for standard operations
- **Fail loudly in development** — N+1 queries, slow queries, and missing indexes produce warnings or exceptions in dev mode
- **Testable by default** — every query is inspectable; in-memory provider mirrors production behaviour

---

## Status

> 📄 **Specification complete** — see [`docs/FluentORM_Specification.docx`](docs/FluentORM_Specification.docx)
>
> 🔧 **Implementation in progress**

---

## Contributing

Contributions are welcome. Please read the specification document before submitting a PR to understand the design intent. Open an issue first for any significant changes.

---

## License

MIT — see [LICENSE](LICENSE)
