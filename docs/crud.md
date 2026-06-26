---
title: CRUD
nav_order: 5
---

# CRUD — Insert, Update, Delete, Upsert
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

---

## INSERT

### Basic insert

```csharp
var farm = new Farm { Name = "Riverside", Location = "Paarl", Hectares = 120 };
await db.InsertAsync(farm);
```

### Insert and get the generated ID

```csharp
int id = await db.InsertAndGetIdAsync<Farm, int>(farm);
// farm.Id is NOT automatically populated — use the returned id
```

### Insert and return the full row

Returns the row as it exists in the database after insert (including defaults, computed columns, etc.).

```csharp
Farm inserted = await db.InsertAndReturnAsync(farm);
Console.WriteLine(inserted.Id);  // populated
```

### Insert only specific columns

```csharp
await db.InsertAsync(farm, f => new { f.Name, f.Location });
// Only Name and Location are included in the INSERT
```

### Insert or ignore (on conflict do nothing)

```csharp
await db.InsertOrIgnoreAsync(farm, conflictOn: f => new { f.Slug });
// If a row with the same Slug already exists, the insert is silently skipped
```

---

## UPDATE

### Update all mapped columns

```csharp
farm.Hectares = 150;
await db.UpdateAsync(farm);
// UPDATE Farms SET name=@0, location=@1, hectares=@2 WHERE id=@3
```

### Update only specific columns

```csharp
farm.Hectares = 150;
await db.UpdateAsync(farm, f => new { f.Hectares });
// UPDATE Farms SET hectares=@0 WHERE id=@1
```

### Bulk update by predicate

```csharp
int affected = await db.UpdateWhereAsync<Farm>(
    where:   f => f.Location == "Paarl",
    columns: f => new { f.Location },
    values:  new { Location = "Cape Winelands" });

Console.WriteLine($"Updated {affected} rows");
```

---

## UPSERT (INSERT OR UPDATE)

Inserts the row if it doesn't exist; updates it if it does. The conflict key determines what "already exists" means.

```csharp
// Upsert on Slug — update all other columns on conflict
await db.UpsertAsync(
    entity:     farm,
    conflictOn: f => new { f.Slug });

// Upsert but only update specific columns on conflict
await db.UpsertAsync(
    entity:     farm,
    conflictOn: f => new { f.Slug },
    updateOnly: f => new { f.Name, f.Hectares });
```

---

## DELETE

### Delete by primary key

```csharp
await db.DeleteAsync<Farm>(42);
```

This issues a soft-delete if the entity has a `[SoftDelete]` column, otherwise a hard `DELETE`.

### Delete by predicate

```csharp
int affected = await db.DeleteWhereAsync<Farm>(f => f.Location == "Paarl");
```

### Hard delete (bypass soft-delete)

```csharp
await db.HardDeleteAsync<Farm>(42);
// Always issues DELETE FROM Farms WHERE id=42
```

### Restore a soft-deleted row

```csharp
await db.RestoreAsync<Farm>(42);
// UPDATE Farms SET deleted_at=NULL WHERE id=42
```

---

## BULK operations

Bulk methods use batch SQL for efficiency. They do not load entities into memory first.

### Bulk insert

```csharp
var farms = Enumerable.Range(1, 1000).Select(i => new Farm
{
    Name = $"Farm {i}",
    Location = "Paarl"
});

await db.BulkInsertAsync(farms);

// Insert only specific columns
await db.BulkInsertAsync(farms, f => new { f.Name, f.Location });
```

### Bulk upsert

```csharp
await db.BulkUpsertAsync(
    items:      farms,
    conflictOn: f => new { f.Slug });
```

### Bulk update by predicate

```csharp
int affected = await db.BulkUpdateAsync<Farm>(
    where:   f => f.Location == "Paarl",
    columns: f => new { f.Location },
    values:  new { Location = "Cape Winelands" });
```

### Bulk delete by predicate

```csharp
int affected = await db.BulkDeleteAsync<Farm>(f => f.EstablishedYear < 1900);
```

---

## Concurrency and row versions

If your entity has a `[RowVersion]` property, FluentORM adds an optimistic concurrency check on every UPDATE. If the row was modified between the time you read it and the time you write it, a `ConcurrencyException` is thrown.

```csharp
[RowVersion]
public byte[]? RowVersion { get; set; }
```

```csharp
// Read
var farm = await db.FindAsync<Farm>(42);

// Modify (somebody else also modifies it here)
farm.Name = "New Name";

// Write — throws ConcurrencyException if row was changed in the meantime
try
{
    await db.UpdateAsync(farm);
}
catch (ConcurrencyException ex)
{
    Console.WriteLine($"Conflict on {ex.EntityType.Name} id={ex.Id}");
    // Reload and retry, or report to the user
}
```

---

## Audit history

When auditing is enabled (`[Audit]` attribute or `.AuditAll<T>()` in the builder), every mutation is recorded. Retrieve the history:

```csharp
var history = await db.AuditHistory<Farm>(id: 42);

foreach (var entry in history)
{
    Console.WriteLine($"{entry.Timestamp:u}  {entry.Operation}  {entry.Column}: {entry.OldValue} → {entry.NewValue}");
}
```
