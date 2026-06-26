---
title: vs EF Core / Dapper
nav_order: 12
---

# FluentORM vs EF Core vs Dapper
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

---

## Feature matrix

| Feature | EF Core | Dapper | FluentORM |
|---|:---:|:---:|:---:|
| Type-safe LINQ queries | ✓ | ✗ | ✓ |
| Code-first entity mapping | ✓ | ✗ | ✓ |
| Attribute-based mapping | ✓ | ✗ | ✓ |
| Fluent mapping API | ✓ | ✗ | ✓ |
| Auto-generated migrations | ✓ | ✗ | ✓ |
| Migrations without live DB | ✗ | ✗ | **✓** |
| Standalone migration CLI | ✗¹ | ✗ | **✓** |
| Soft delete built-in | ✗² | ✗ | **✓** |
| Multi-tenancy built-in | ✗² | ✗ | **✓** |
| Query result caching | ✗ | ✗ | **✓** |
| Built-in audit trail | ✗² | ✗ | **✓** |
| Optimistic concurrency | ✓ | ✗ | ✓ |
| Raw SQL escape hatch | ✓ | ✓ | ✓ |
| Bulk insert/update/delete | ✗³ | ✗ | **✓** |
| Read replica routing | ✗ | ✗ | **✓** |
| N+1 detection | ✗ | ✗ | **✓** |
| In-memory test helpers | ✓ | ✗ | ✓ |
| Lazy loading | ✓ | ✗ | ✗ |
| Navigation properties | ✓ | ✗ | ✗ |
| Change tracking | ✓ | ✗ | ✗ |
| LINQ grouping/aggregates | ✓ | ✗ | ✓ |

¹ EF Core uses `dotnet ef` which requires a running app project; FluentORM.Tools is fully standalone.  
² EF Core has community packages for these features (e.g., EF Core Extensions).  
³ EF Core has `ExecuteUpdateAsync`/`ExecuteDeleteAsync` since EF 7; bulk insert requires EFCore.BulkExtensions.

---

## When to choose FluentORM

**Choose FluentORM when:**
- You want EF Core-style LINQ queries without change tracking overhead
- You want automatic migrations that don't need a live database connection
- You need multi-tenancy, soft delete, or caching without third-party packages
- You prefer a standalone CLI tool for deployments (Docker, CI, no full app startup)
- Your domain model should be free of ORM attributes (fluent API supports this)

**Choose EF Core when:**
- You need navigation properties and graph-based change tracking
- Your team is deeply familiar with EF Core and its tooling
- You rely on LINQ expressions that FluentORM doesn't yet translate
- You target multiple database providers beyond SQLite and SQL Server

**Choose Dapper when:**
- You want to write SQL yourself and map results manually
- You need maximum query transparency and control
- Performance headroom is critical and you prefer zero-magic

---

## Side-by-side examples

### Define an entity

**EF Core**
```csharp
public class Farm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double? Hectares { get; set; }
    public DateTime? DeletedAt { get; set; }  // manual soft-delete
}

// DbContext
public class AppDbContext : DbContext
{
    public DbSet<Farm> Farms => Set<Farm>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Farm>().HasQueryFilter(f => f.DeletedAt == null);
        mb.Entity<Farm>().Property(f => f.Name).HasMaxLength(200).IsRequired();
    }
}
```

**FluentORM**
```csharp
[Table("Farms")]
public class Farm
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [Column("name"), NotNull, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Column("hectares")]
    public double? Hectares { get; set; }

    [Column("deleted_at"), SoftDelete]   // automatic query filter
    public DateTime? DeletedAt { get; set; }
}
```

---

### Simple SELECT

**EF Core**
```csharp
var farms = await ctx.Farms
    .Where(f => f.Hectares > 100)
    .OrderByDescending(f => f.Hectares)
    .Take(10)
    .ToListAsync();
```

**Dapper**
```csharp
var farms = await conn.QueryAsync<Farm>(
    "SELECT * FROM Farms WHERE Hectares > @min ORDER BY Hectares DESC LIMIT 10",
    new { min = 100 });
```

**FluentORM**
```csharp
var farms = await db.Query<Farm>()
    .Where(f => f.Hectares > 100)
    .OrderByDesc(f => f.Hectares)
    .Take(10)
    .ToListAsync();
```

---

### Find by primary key

**EF Core**
```csharp
var farm = await ctx.Farms.FindAsync(42);
```

**Dapper**
```csharp
var farm = await conn.QuerySingleOrDefaultAsync<Farm>(
    "SELECT * FROM Farms WHERE Id = @id", new { id = 42 });
```

**FluentORM**
```csharp
var farm = await db.FindAsync<Farm>(42);
```

---

### INSERT and return generated ID

**EF Core**
```csharp
ctx.Farms.Add(farm);
await ctx.SaveChangesAsync();
int id = farm.Id;   // populated by EF's change tracker
```

**Dapper**
```csharp
var id = await conn.ExecuteScalarAsync<int>(
    "INSERT INTO Farms (Name, Location) VALUES (@Name, @Location); SELECT last_insert_rowid()",
    farm);
```

**FluentORM**
```csharp
int id = await db.InsertAndGetIdAsync<Farm, int>(farm);
```

---

### Update specific columns

**EF Core**
```csharp
// Must load, modify, and save — or use ExecuteUpdateAsync (EF7+)
var farm = await ctx.Farms.FindAsync(42);
farm.Hectares = 150;
await ctx.SaveChangesAsync();
// or:
await ctx.Farms
    .Where(f => f.Id == 42)
    .ExecuteUpdateAsync(s => s.SetProperty(f => f.Hectares, 150));
```

**Dapper**
```csharp
await conn.ExecuteAsync(
    "UPDATE Farms SET hectares = @h WHERE id = @id",
    new { h = 150, id = 42 });
```

**FluentORM**
```csharp
farm.Hectares = 150;
await db.UpdateAsync(farm, f => new { f.Hectares });
// UPDATE Farms SET hectares=@0 WHERE id=@1 — no load required
```

---

### Soft delete

**EF Core**
```csharp
// Manual — you write this
var farm = await ctx.Farms.FindAsync(42);
farm.DeletedAt = DateTime.UtcNow;
await ctx.SaveChangesAsync();
```

**FluentORM**
```csharp
// Built-in — the [SoftDelete] column is updated automatically
await db.DeleteAsync<Farm>(42);
```

---

### Transactions

**EF Core**
```csharp
using var tx = await ctx.Database.BeginTransactionAsync();
try
{
    ctx.Farms.Add(farm);
    ctx.Fields.Add(field);
    await ctx.SaveChangesAsync();
    await tx.CommitAsync();
}
catch { await tx.RollbackAsync(); throw; }
```

**Dapper**
```csharp
using var tx = conn.BeginTransaction();
try
{
    await conn.ExecuteAsync("INSERT INTO ...", farm, tx);
    await conn.ExecuteAsync("INSERT INTO ...", field, tx);
    tx.Commit();
}
catch { tx.Rollback(); throw; }
```

**FluentORM**
```csharp
await db.TransactionAsync(async tx =>
{
    var id = await tx.InsertAndGetIdAsync<Farm, int>(farm);
    field.FarmId = id;
    await tx.InsertAsync(field);
    // Auto-commits; auto-rolls-back on exception
});
```

---

### Add a migration

**EF Core**
```bash
dotnet ef migrations add AddFarmNotes
dotnet ef database update
```
Requires the app project to compile and a running EF context. The tooling starts your application.

**FluentORM**
```bash
# Auto-generate from entity diff (no live DB required):
fluentorm migrations scaffold add_farm_notes --output ./Migrations

# Or write manually:
fluentorm migrations new add_farm_notes --output ./Migrations

# Apply:
fluentorm migrations apply
```

---

### Testing

**EF Core**
```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase("test")
    .Options;
using var ctx = new AppDbContext(options);
ctx.Farms.Add(new Farm { Name = "Test" });
await ctx.SaveChangesAsync();
```

**FluentORM**
```csharp
var db = await DbTest.CreateAsync<Farm>();
await db.ApplyMigrationsAsync();
await db.SeedAsync(new Farm { Name = "Test" });

var svc = new FarmService(db.Db);
var result = await svc.GetAllAsync();
Assert.Single(result);
```

---

## Performance notes

FluentORM does not use change tracking. There is no `DbContext` state to maintain, no entity proxies, and no snapshot comparison at `SaveChanges`. Mutations are explicit — you call `InsertAsync`, `UpdateAsync`, `DeleteAsync` directly.

For read-heavy workloads, `.CacheFor(ttl)` on the query avoids round-trips to the database entirely.

For write-heavy workloads, `BulkInsertAsync`, `BulkUpdateAsync`, and `BulkDeleteAsync` batch operations without loading entities into memory.
