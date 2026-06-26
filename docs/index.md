---
title: Home
nav_order: 1
description: "FluentORM — a fast, expressive .NET ORM with LINQ queries, auto-migrations, and a standalone CLI tool."
permalink: /
---

# FluentORM

A fast, expressive .NET ORM for SQLite and SQL Server. Write type-safe queries in pure C#, manage your schema with migrations, and generate those migrations automatically from your entity classes — no live database required.

```csharp
var farms = await db.Query<Farm>()
    .Where(f => f.HectareSize > 100)
    .OrderByDesc(f => f.HectareSize)
    .Take(10)
    .ToListAsync();
```

---

## Why FluentORM?

| | EF Core | Dapper | **FluentORM** |
|---|:---:|:---:|:---:|
| Type-safe LINQ queries | ✓ | ✗ | ✓ |
| Auto-generated migrations | ✓ | ✗ | ✓ |
| Migrations without live DB | ✗ | ✗ | **✓** |
| Standalone migration CLI | ✗ | ✗ | **✓** |
| Raw SQL escape hatch | ✓ | ✓ | ✓ |
| Soft delete built-in | ✗ | ✗ | **✓** |
| Multi-tenancy built-in | ✗ | ✗ | **✓** |
| Query caching | ✗ | ✗ | **✓** |
| Built-in audit trail | ✗ | ✗ | **✓** |
| Connection pooling | ✓ | ✗ | ✓ |
| Read replica routing | ✗ | ✗ | **✓** |
| N+1 detection | ✗ | ✗ | **✓** |
| Test helpers (in-memory) | ✓ | ✗ | ✓ |

---

## Packages

| Package | Purpose |
|---|---|
| `FluentORM` | Meta-package — installs everything |
| `FluentORM.Core` | Query engine, entity mapping, DI |
| `FluentORM.Migrations` | Schema builder, migration engine, CLI scaffolding |
| `FluentORM.Sqlite` | SQLite provider |
| `FluentORM.SqlServer` | SQL Server provider |
| `FluentORM.Testing` | In-memory test helpers |
| `FluentORM.Tools` | `fluentorm` CLI (dotnet tool) |

---

## Quick install

```bash
dotnet add package FluentORM.Sqlite   # or FluentORM.SqlServer
dotnet add package FluentORM.Migrations

# Optional: standalone CLI
dotnet tool install -g FluentORM.Tools
```

---

## Five-minute example

**1. Define an entity**

```csharp
using FluentORM.Core.Attributes;

[Table("Farms")]
public class Farm
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [Column("name"), NotNull, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Column("location"), NotNull]
    public string Location { get; set; } = string.Empty;

    [Column("hectares")]
    public double? Hectares { get; set; }

    [Column("deleted_at")]
    [SoftDelete]
    public DateTime? DeletedAt { get; set; }
}
```

**2. Register with DI**

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("Data Source=app.db")
    .ScanAssemblies(typeof(Farm).Assembly));
```

**3. Query**

```csharp
public class FarmService(IFluentDb db)
{
    public Task<List<Farm>> GetLargeAsync(double minHectares) =>
        db.Query<Farm>()
          .Where(f => f.Hectares >= minHectares)
          .OrderByDesc(f => f.Hectares)
          .ToListAsync();
}
```

**4. Migrate**

```bash
# First run — creates the model snapshot
fluentorm migrations scaffold initial --output ./Migrations

# After adding a property — generates the migration file
fluentorm migrations scaffold add_hectares --output ./Migrations

# Apply to DB
fluentorm migrations apply
```

---

## Documentation

- [Getting Started](getting-started) — installation, DI setup, first query
- [Entity Mapping](entity-mapping) — attributes and fluent API
- [Querying](querying) — filters, joins, aggregates, paging
- [CRUD](crud) — insert, update, delete, upsert, bulk
- [Transactions](transactions) — commit, rollback, savepoints
- [Migrations](migrations) — writing migrations, schema builder reference
- [Auto-Scaffold](auto-scaffold) — generate migrations from your entity model
- [CLI Tool](cli) — `fluentorm` command reference
- [Advanced](advanced) — soft delete, multi-tenancy, caching, audit, raw SQL
- [Testing](testing) — `DbTest` in-memory test harness
- [vs EF Core / Dapper](comparisons) — side-by-side comparisons
