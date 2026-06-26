---
title: Transactions
nav_order: 6
---

# Transactions
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

---

## Auto-commit transaction

The simplest option. Pass an async delegate — it commits on success and rolls back on any exception.

```csharp
await db.TransactionAsync(async tx =>
{
    var farmId = await tx.InsertAndGetIdAsync<Farm, int>(farm);

    var field = new Field { FarmId = farmId, Name = "North block" };
    await tx.InsertAsync(field);

    // Commits here if no exception was thrown
    // Any exception → automatic rollback
});
```

This covers the vast majority of use cases. Prefer it over manual transaction management unless you need savepoints or a specific isolation level.

---

## Isolation levels

```csharp
await db.TransactionAsync(
    async tx => { /* ... */ },
    isolation: IsolationLevel.Serializable);
```

Available values (from `System.Data.IsolationLevel`):

| Level | Prevents |
|---|---|
| `ReadUncommitted` | Nothing — dirty reads possible |
| `ReadCommitted` (default) | Dirty reads |
| `RepeatableRead` | Dirty reads, non-repeatable reads |
| `Serializable` | Dirty reads, non-repeatable reads, phantom reads |
| `Snapshot` | All anomalies (SQL Server only) |

---

## Manual transaction

Use when you need to conditionally commit, rollback, or set savepoints across multiple code paths.

```csharp
await using var tx = await db.BeginTransactionAsync();

try
{
    var farmId = await tx.InsertAndGetIdAsync<Farm, int>(farm);
    await tx.InsertAsync(field);

    await tx.CommitAsync();
}
catch
{
    await tx.RollbackAsync();
    throw;
}
```

`IFluentTransaction` implements `IFluentDb` — use `tx` exactly as you'd use `db`.

---

## Savepoints

Savepoints let you partially roll back a transaction without losing work done before the savepoint.

```csharp
await using var tx = await db.BeginTransactionAsync();

var farmId = await tx.InsertAndGetIdAsync<Farm, int>(farm);

await tx.SavepointAsync("before_fields");

try
{
    foreach (var field in fields)
        await tx.InsertAsync(field);
}
catch (Exception ex)
{
    // Roll back only the field inserts; the farm insert is preserved
    await tx.RollbackToAsync("before_fields");
    Console.WriteLine($"Fields failed: {ex.Message}. Farm saved.");
}

await tx.CommitAsync();
```

{: .warning }
SQLite supports savepoints. SQL Server supports them via `SAVE TRANSACTION`.

---

## Nested transactions (savepoint pattern)

FluentORM automatically promotes nested `TransactionAsync` calls to savepoints when already inside a transaction:

```csharp
await db.TransactionAsync(async outer =>
{
    await outer.InsertAsync(farm);

    // This becomes a savepoint, not a new connection-level transaction
    await outer.TransactionAsync(async inner =>
    {
        await inner.InsertAsync(field);
        // If this throws, only the inner work is rolled back
    });

    await outer.InsertAsync(inspection);
    // Commits the outer transaction
});
```

---

## Transactions across services

Pass `IFluentDb` (the transaction handle) down to other services so they participate in the same transaction:

```csharp
await db.TransactionAsync(async tx =>
{
    await farmService.CreateAsync(farm, tx);      // tx is an IFluentDb
    await fieldService.CreateAsync(field, tx);
    await invoiceService.CreateAsync(invoice, tx);
});
```

Services that accept `IFluentDb` rather than field-injecting it are naturally testable and transaction-aware.

---

## Pool statistics

```csharp
var stats = db.PoolStats();
Console.WriteLine($"Active: {stats.Active}, Idle: {stats.Idle}, Total: {stats.Total}");
```
