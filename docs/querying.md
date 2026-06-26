---
title: Querying
nav_order: 4
---

# Querying
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

All queries start from `db.Query<T>()`. The query is lazy — no SQL is executed until you call a terminal method (`ToListAsync`, `FirstOrDefaultAsync`, `CountAsync`, etc.).

---

## Basic fetch

```csharp
// All rows
var farms = await db.Query<Farm>().ToListAsync();

// As an array
var farms = await db.Query<Farm>().ToArrayAsync();

// Find by primary key
var farm = await db.FindAsync<Farm>(42);

// First match or null
var farm = await db.Query<Farm>()
    .Where(f => f.Location == "Paarl")
    .FirstOrDefaultAsync();

// Exactly one — throws if 0 or >1 results
var farm = await db.Query<Farm>()
    .Where(f => f.Name == "Riverside")
    .SingleAsync();
```

---

## Filtering

### Basic WHERE

```csharp
var large = await db.Query<Farm>()
    .Where(f => f.Hectares > 100)
    .ToListAsync();

// Multiple Where calls are combined with AND
var results = await db.Query<Farm>()
    .Where(f => f.Hectares > 50)
    .Where(f => f.Location == "Stellenbosch")
    .ToListAsync();
```

### OR conditions

```csharp
var results = await db.Query<Farm>()
    .Where(f => f.Hectares > 200)
    .OrWhere(f => f.Location == "Paarl")
    .ToListAsync();
```

### String operations

```csharp
// LIKE '%river%'
var contains = await db.Query<Farm>()
    .Where(f => f.Name.Contains("River"))
    .ToListAsync();

// LIKE 'River%'
var starts = await db.Query<Farm>()
    .Where(f => f.Name.StartsWith("River"))
    .ToListAsync();

// LIKE '%Farm'
var ends = await db.Query<Farm>()
    .Where(f => f.Name.EndsWith("Farm"))
    .ToListAsync();
```

### IN / NOT IN

```csharp
var locations = new[] { "Paarl", "Stellenbosch", "Franschhoek" };

var inList = await db.Query<Farm>()
    .WhereIn(f => f.Location, locations)
    .ToListAsync();

var notInList = await db.Query<Farm>()
    .WhereNotIn(f => f.Location, locations)
    .ToListAsync();
```

### BETWEEN

```csharp
var midSized = await db.Query<Farm>()
    .WhereBetween(f => f.Hectares, 50.0, 150.0)
    .ToListAsync();
```

### NULL checks

```csharp
var noNotes = await db.Query<Farm>()
    .WhereNull(f => f.Notes)
    .ToListAsync();

var hasNotes = await db.Query<Farm>()
    .WhereNotNull(f => f.Notes)
    .ToListAsync();
```

### Raw SQL predicate

Use when the expression translator can't handle a specific SQL function:

```csharp
var longNames = await db.Query<Farm>()
    .WhereRaw("length(name) > {0}", 10)
    .ToListAsync();
```

### EXISTS subquery

```csharp
// Farms that have at least one field
var withFields = await db.Query<Farm>()
    .WhereExists<Field>((farm, field) => farm.Id == field.FarmId)
    .ToListAsync();
```

---

## Ordering

```csharp
var ordered = await db.Query<Farm>()
    .OrderBy(f => f.Name)
    .ToListAsync();

var descending = await db.Query<Farm>()
    .OrderByDesc(f => f.Hectares)
    .ToListAsync();

// Multi-column sort
var multiSort = await db.Query<Farm>()
    .OrderBy(f => f.Location)
    .ThenByDesc(f => f.Hectares)
    .ToListAsync();
```

---

## Paging

```csharp
// Manual offset/limit
var page2 = await db.Query<Farm>()
    .OrderBy(f => f.Name)
    .Skip(10)
    .Take(10)
    .ToListAsync();

// PagedResult helper
PagedResult<Farm> result = await db.Query<Farm>()
    .OrderBy(f => f.Name)
    .ToPagedAsync(page: 1, pageSize: 10);

Console.WriteLine($"Page {result.Page} of {result.TotalPages}");
Console.WriteLine($"Showing {result.Items.Count} of {result.TotalCount} total");
```

---

## Joins

```csharp
// INNER JOIN
var joinSql = db.Query<Farm>()
    .Join<Field>((farm, field) => farm.Id == field.FarmId)
    .ToSql();

// LEFT JOIN — includes farms with no fields
var allFarms = await db.Query<Farm>()
    .LeftJoin<Field>((farm, field) => farm.Id == field.FarmId)
    .ToListAsync();

// Three-table join
var threeWay = db.Query<Farm>()
    .Join<Field, Inspection>((farm, field, inspection) =>
        farm.Id == field.FarmId && field.Id == inspection.FieldId)
    .ToSql();

// CROSS JOIN
var crossSql = db.Query<Farm>().CrossJoin<Field>().ToSql();
```

---

## Projections and SELECT

### Anonymous type projection

```csharp
// SELECT id, name, hectares FROM Farms WHERE ...
var summaries = await db.Query<Farm>()
    .Select(f => new { f.Id, f.Name, f.Hectares })
    .Where(f => f.Hectares > 50)
    .ToListAsync();
```

### Projection to a DTO

```csharp
public record FarmSummary(int Id, string Name, string Location);

var dtos = await db.Query<Farm>()
    .ProjectTo<FarmSummary>()
    .ToListAsync();

// Custom mapping
var dtos = await db.Query<Farm>()
    .ProjectTo<FarmSummary>(cfg => cfg
        .For(dto => dto.Name, f => f.Name + " Farm"))
    .ToListAsync();
```

### Two-table projection (after a join)

```csharp
var data = await db.Query<Farm>()
    .Join<Field>((f, fi) => f.Id == fi.FarmId)
    .Select<Field, FarmFieldDto>((farm, field) => new FarmFieldDto
    {
        FarmName = farm.Name,
        FieldName = field.Name,
        Hectares = farm.Hectares
    })
    .ToListAsync();
```

---

## Aggregates

```csharp
var count   = await db.Query<Farm>().CountAsync();
var exists  = await db.Query<Farm>().Where(f => f.Location == "Paarl").ExistsAsync();
var any     = await db.Query<Farm>().AnyAsync();
var avg     = await db.Query<Farm>().AverageAsync(f => f.Hectares);
var total   = await db.Query<Farm>().SumAsync(f => f.Hectares);
var biggest = await db.Query<Farm>().MaxAsync(f => f.Hectares);
var smallest= await db.Query<Farm>().MinAsync(f => f.Hectares);

// DISTINCT count
var distinctLocations = await db.Query<Farm>()
    .CountDistinctAsync(f => f.Location);
```

---

## GROUP BY / HAVING

```csharp
// Average inspection score per field
var scores = db.Query<Inspection>()
    .GroupBy(i => i.FieldId)
    .Having(i => i.SeverityScore > 3.0)
    .OrderByDesc(i => i.SeverityScore)
    .ToSql();

// Count per location
var countByLocation = db.Query<Farm>()
    .GroupBy(f => f.Location)
    .Select(f => new { f.Location, Count = f.Id })
    .ToSql();
```

---

## DISTINCT

```csharp
var locations = await db.Query<Farm>()
    .Select(f => new { f.Location })
    .Distinct()
    .ToListAsync();
```

---

## Soft-deleted records

```csharp
// Normal query — deleted rows excluded automatically
var active = await db.Query<Farm>().ToListAsync();

// Include soft-deleted rows
var all = await db.Query<Farm>().IncludeDeleted().ToListAsync();

// Only soft-deleted rows
var deleted = await db.Query<Farm>().OnlyDeleted().ToListAsync();
```

---

## Caching

```csharp
// Cache this query result for 5 minutes
var farms = await db.Query<Farm>()
    .Where(f => f.Location == "Paarl")
    .CacheFor(TimeSpan.FromMinutes(5))
    .ToListAsync();

// Cache but invalidate when Field table changes
var farms = await db.Query<Farm>()
    .CacheFor(TimeSpan.FromMinutes(5))
    .InvalidateOn<Field>()
    .ToListAsync();

// Manually invalidate the cache for a table
await db.Cache.InvalidateAsync<Farm>();
await db.Cache.InvalidateAsync<Farm>(f => f.Location == "Paarl");
```

---

## Query diagnostics

```csharp
// Print the SQL without executing
string sql = db.Query<Farm>()
    .Where(f => f.Hectares > 100)
    .OrderByDesc(f => f.Hectares)
    .Skip(0).Take(5)
    .ToSql();

Console.WriteLine(sql);
// SELECT * FROM Farms WHERE hectares > 100 ORDER BY hectares DESC LIMIT 5 OFFSET 0

// Execute and capture timing + row count
var (results, diag) = await db.Query<Farm>()
    .Where(f => f.Hectares > 50)
    .WithDiagnostics()
    .ToListWithDiagnosticsAsync();

Console.WriteLine($"SQL:  {diag.Sql}");
Console.WriteLine($"Time: {diag.ExecutionMs:F1} ms");
Console.WriteLine($"Rows: {diag.RowsRead}");
```

---

## Raw SQL

When the query builder doesn't cover your case:

```csharp
// Raw query returning typed results
var farms = await db.RawAsync<Farm>(
    "SELECT * FROM Farms WHERE length(name) > @min",
    new { min = 10 });

// Scalar
var count = await db.ScalarAsync<int>(
    "SELECT COUNT(*) FROM Farms WHERE location = @loc",
    new { loc = "Paarl" });

// Non-query (UPDATE, DELETE, DDL)
var affected = await db.ExecuteAsync(
    "UPDATE Farms SET location = @newLoc WHERE location = @oldLoc",
    new { newLoc = "Cape Winelands", oldLoc = "Paarl" });
```

---

## CTEs (Common Table Expressions)

```csharp
// Simple CTE
var db2 = db.WithCTE("large_farms",
    ctx => ctx.Query<Farm>().Where(f => f.Hectares > 100));

var result = await db2.RawAsync<Farm>("SELECT * FROM large_farms ORDER BY name");

// Recursive CTE (e.g., org hierarchy)
var db3 = db.WithRecursiveCTE<Category>("category_tree",
    anchor:    ctx => ctx.Query<Category>().Where(c => c.ParentId == null),
    recursive: (ctx, cte) => ctx.Query<Category>()
        .Join<Category>((c, parent) => c.ParentId == parent.Id));
```

---

## Multi-tenant queries

With multi-tenancy configured, `db.Query<T>()` always appends `WHERE tenant_id = @current`. To bypass this:

```csharp
// Scoped to a specific tenant (useful in admin code)
var tenantDb = db.ForTenant("tenant-abc");
var farms = await tenantDb.Query<Farm>().ToListAsync();

// Query across ALL tenants (super-admin only)
var allFarms = await db.QueryAllTenants<Farm>().ToListAsync();
```
