---
title: Testing
nav_order: 11
---

# Testing
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

`FluentORM.Testing` provides `DbTest` — an in-memory SQLite test harness that spins up a fresh, isolated database for each test. No mocking required.

---

## Installation

```bash
dotnet add package FluentORM.Testing
```

---

## Basic usage

```csharp
using FluentORM.Testing;
using Xunit;

public class FarmServiceTests : IAsyncLifetime
{
    private DbTest _db = null!;

    public async Task InitializeAsync()
    {
        _db = await DbTest.CreateAsync<Farm>();
        await _db.ApplyMigrationsAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task GetLarge_ReturnsOnlyFarmsAboveThreshold()
    {
        // Arrange
        await _db.SeedAsync(new Farm { Name = "Big Farm",   Hectares = 200, Location = "Paarl" });
        await _db.SeedAsync(new Farm { Name = "Small Farm", Hectares = 10,  Location = "Paarl" });

        var svc = new FarmService(_db.Db);

        // Act
        var result = await svc.GetLargeAsync(minHectares: 50);

        // Assert
        Assert.Single(result);
        Assert.Equal("Big Farm", result[0].Name);
    }
}
```

---

## `DbTest` API

### `CreateAsync`

```csharp
// Scan a specific assembly
var db = await DbTest.CreateAsync<Farm>();

// Pass additional configuration
var db = await DbTest.CreateAsync<Farm>(configure: orm =>
    orm.SoftDeleteAll("DeletedAt").AuditAll<Farm>());
```

### `SeedAsync` / `SeedManyAsync`

```csharp
await _db.SeedAsync(new Farm { Name = "Riverside", Location = "Paarl" });

await _db.SeedManyAsync(new[]
{
    new Farm { Name = "Riverside", Location = "Paarl"      },
    new Farm { Name = "Hillside",  Location = "Stellenbosch"},
    new Farm { Name = "Lakeside",  Location = "Franschhoek" },
});
```

### `ApplyMigrationsAsync`

Applies all pending migrations to the in-memory database.

```csharp
await _db.ApplyMigrationsAsync();
```

### `ForTenant`

Returns a tenant-scoped `IFluentDb`:

```csharp
var tenantDb = _db.ForTenant("tenant-abc");
var farms = await tenantDb.Query<Farm>().ToListAsync();
```

### `MonitorQueries`

Captures every SQL query executed during the test. Useful for asserting query count (N+1 detection) or inspecting generated SQL.

```csharp
using var monitor = _db.MonitorQueries();

await svc.GetLargeAsync(50);

Assert.Equal(1, monitor.QueryCount);
Assert.Contains("SELECT", monitor.Queries[0].Sql);
```

### `FreezeTime`

Fix the clock for deterministic timestamp assertions:

```csharp
_db.FreezeTime(new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc));

// Now DateTime.UtcNow inside FluentORM returns 2024-06-01 12:00:00
var farm = await _db.Db.InsertAndReturnAsync(new Farm { ... });
Assert.Equal(new DateTime(2024, 6, 1, 12, 0, 0), farm.CreatedAt);
```

---

## Testing migrations

```csharp
[Fact]
public async Task Migration_20240618001_AddsNotesColumn()
{
    var db = await DbTest.CreateAsync<Farm>();

    // Apply only up to this version
    // (requires running the migration engine manually — use db.Db.Migrations)
    await db.Db.Migrations.ApplyToAsync(20240618001);

    // Verify the column exists by inserting a row that uses it
    await db.SeedAsync(new Farm { Name = "Test", Location = "Paarl", Notes = "hello" });
    var farm = await db.Db.Query<Farm>().FirstAsync();

    Assert.Equal("hello", farm.Notes);
}
```

---

## Testing with xUnit collection fixtures

Share one database across all tests in a class (faster, but tests must not interfere):

```csharp
public class FarmDatabaseFixture : IAsyncLifetime
{
    public DbTest Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Db = await DbTest.CreateAsync<Farm>();
        await Db.ApplyMigrationsAsync();

        // Seed read-only reference data once
        await Db.SeedManyAsync(new[]
        {
            new Region { Name = "Winelands" },
            new Region { Name = "Overberg"  },
        });
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();
}

[Collection("FarmDatabase")]
public class FarmQueryTests(FarmDatabaseFixture fixture) : IClassFixture<FarmDatabaseFixture>
{
    [Fact]
    public async Task Query_ReturnsAllRegions()
    {
        var regions = await fixture.Db.Db.Query<Region>().ToListAsync();
        Assert.Equal(2, regions.Count);
    }
}
```

---

## Testing against a real SQLite file

For integration tests that need to survive process restarts:

```csharp
var db = await DbTest.CreateAsync<Farm>(configure: orm =>
    orm.UseSqlite("Data Source=integration_test.db"));

await db.ApplyMigrationsAsync();
```

Delete the file in test teardown to get a clean state next run.

---

## Tips

- **One `DbTest` per test method** gives full isolation. It's fast because SQLite in-memory databases are created in microseconds.
- **Use `SeedAsync` instead of `InsertAsync`** in test setup — it bypasses soft-delete filtering and always inserts the raw row.
- **`MonitorQueries`** is the best way to catch accidental N+1 queries before they reach production.
- **`FreezeTime`** eliminates timing-related flakiness in tests that assert on `CreatedAt`, `UpdatedAt`, or `DeletedAt` fields.
