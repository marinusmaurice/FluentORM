---
title: Getting Started
nav_order: 2
---

# Getting Started
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

---

## Installation

### Option A — single meta-package (recommended)

```bash
dotnet add package FluentORM
```

This installs `FluentORM.Core`, `FluentORM.Migrations`, and both providers. If you want to keep your dependency surface small, install individual packages instead.

### Option B — à la carte

```bash
# Core query engine (always required)
dotnet add package FluentORM.Core

# Pick your database
dotnet add package FluentORM.Sqlite
dotnet add package FluentORM.SqlServer

# If you use migrations
dotnet add package FluentORM.Migrations

# Test helpers
dotnet add package FluentORM.Testing
```

### CLI tool

The `fluentorm` CLI scaffolds and applies migrations without touching application code.

```bash
# Install globally (recommended)
dotnet tool install -g FluentORM.Tools

# Or as a local project tool
dotnet new tool-manifest
dotnet tool install FluentORM.Tools
```

---

## Registering with ASP.NET Core DI

```csharp
// Program.cs
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("Data Source=app.db")           // provider
    .ScanAssemblies(typeof(Program).Assembly));  // entity discovery
```

Then inject `IFluentDb` wherever you need it:

```csharp
public class FarmService(IFluentDb db)
{
    public Task<Farm?> GetByIdAsync(int id) =>
        db.FindAsync<Farm>(id);
}
```

### SQL Server

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlServer(
        connectionString: builder.Configuration.GetConnectionString("Default")!,
        configure: sql => sql
            .CommandTimeout(30)
            .EnableRetry(attempts: 3))
    .ScanAssemblies(typeof(Program).Assembly));
```

### In-memory database (tests / local dev)

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqliteInMemory()
    .ScanAssemblies(typeof(Program).Assembly));
```

---

## Full configuration reference

```csharp
builder.Services.AddFluentOrm(orm => orm
    // ── Provider ──────────────────────────────────────────────
    .UseSqlite("Data Source=app.db")

    // ── Read replica (SELECT queries routed here) ──────────────
    .UseReadReplica("Data Source=app-read.db")

    // ── Connection pool ───────────────────────────────────────
    .Pool(min: 2, max: 50)

    // ── Retry on transient errors ──────────────────────────────
    .Retry(r => r
        .Attempts(3)
        .Backoff(BackoffStrategy.Exponential)
        .RetryOn<SqliteException>(ex => ex.SqliteErrorCode == 5))  // SQLITE_BUSY

    // ── Soft delete ───────────────────────────────────────────
    .SoftDeleteAll("DeletedAt")    // column name for the global soft-delete timestamp

    // ── Audit trail ───────────────────────────────────────────
    .AuditAll<Farm>()              // audit every mutation on Farm

    // ── Query caching ─────────────────────────────────────────
    .UseMemoryCache()
    .DefaultCacheTtl(TimeSpan.FromMinutes(5))

    // ── Slow query logging ────────────────────────────────────
    .SlowQueryThreshold(TimeSpan.FromMilliseconds(500))
    .LogTo(loggerFactory.CreateLogger("FluentORM"))

    // ── N+1 detection ─────────────────────────────────────────
    .DetectNPlusOne(WhenDetected.Throw)   // Warn | Throw | Disabled

    // ── Schema drift on startup ───────────────────────────────
    .ValidateSchemaOnStartup(DriftMode.Warn)   // Warn | Throw | Disabled

    // ── Entity scanning ───────────────────────────────────────
    .ScanAssemblies(typeof(Farm).Assembly));
```

---

## Manual wiring (without DI)

If you are not using a DI container you can wire the engine manually:

```csharp
var registry = new EntityMapRegistry();
registry.ScanAssembly(typeof(Farm).Assembly);

var dialect = new SqliteDialect();
var factory = new SqliteConnectionFactory("Data Source=app.db");

var db = new FluentDb(factory, dialect, registry);
```

---

## Your first migration

```bash
# In your project directory
fluentorm init                  # creates fluentorm.json
dotnet build
fluentorm migrations scaffold initial --output ./Migrations
# → creates _FluentORM_Snapshot.json (no migration yet)

# Add a property to an entity, rebuild, then:
fluentorm migrations scaffold add_notes --output ./Migrations
# → generates Migration_YYYYMMDDNNN_AddNotes.cs

fluentorm migrations apply      # applies to the database
```

See [Auto-Scaffold](auto-scaffold) and [CLI Tool](cli) for full details.
