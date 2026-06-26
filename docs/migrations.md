---
title: Migrations
nav_order: 7
---

# Migrations
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

---

## Overview

A migration is a C# class that describes a reversible database schema change. Every migration has an `Up()` method (apply the change) and a `Down()` method (undo it). Migrations are stored in your project, compiled into your assembly, and executed in version order.

---

## Writing a migration

```csharp
using FluentORM.Core.Attributes;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;
using MyApp.Entities;

namespace MyApp.Migrations;

[Migration(20240601001, "create_farms_table")]
public sealed class CreateFarmsTable : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.CreateTable<Farm>(t =>
        {
            t.PrimaryKey(f => f.Id).AutoIncrement();
            t.Column(f => f.Name).NotNull().MaxLength(200);
            t.Column(f => f.Location).NotNull().MaxLength(500);
            t.Column(f => f.Hectares).Nullable();
            t.Column(f => f.DeletedAt).Nullable();
            t.UniqueIndex(f => f.Slug);
        });
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropTable<Farm>();
    }
}
```

### Version numbers

Use the pattern `yyyyMMddNNN` — a date prefix plus a three-digit sequence number:

```
20240601001   first migration on 1 June 2024
20240601002   second migration on 1 June 2024
20240615001   first migration on 15 June 2024
```

The CLI's `migrations new` command assigns the next available number automatically. Version numbers must be unique across all migrations in the assembly.

---

## SchemaBuilder reference

### Create table

```csharp
schema.CreateTable<Farm>(t =>
{
    // Primary key
    t.PrimaryKey(f => f.Id).AutoIncrement();

    // Columns
    t.Column(f => f.Name).NotNull().MaxLength(200);
    t.Column(f => f.Hectares).Nullable();
    t.Column(f => f.Status).NotNull().Default("active");
    t.Column(f => f.RowVersion).IsRowVersion();
    t.Column(f => f.TenantId).IsTenantKey();

    // Indexes
    t.Index(f => f.Location);
    t.UniqueIndex(f => f.Slug);
});
```

### Drop table

```csharp
schema.DropTable<Farm>();
schema.DropTable("legacy_farms");   // by name, when no entity class exists
```

### Rename table

```csharp
schema.RenameTable<Farm>("farms_v2");
```

### Truncate table

```csharp
schema.TruncateTable<Farm>();
```

---

### Add column

```csharp
schema.AddColumn<Farm>(f => f.Notes).Nullable();
schema.AddColumn<Farm>(f => f.Rating).NotNull().Default(0);
schema.AddColumn<Farm>(f => f.Slug).NotNull().MaxLength(100);
```

### Drop column

```csharp
schema.DropColumn<Farm>(f => f.LegacyRef);
```

{: .warning }
**SQLite limitation:** `DropColumn` is emitted as a SQL comment on SQLite — no column is actually removed. Use SQL Server for reliable `DROP COLUMN`, or perform a manual table rebuild on SQLite.

### Rename column

```csharp
schema.RenameColumn<Farm>(old: "farm_ref", @new: "external_id");
```

### Alter column

```csharp
// Change nullability
schema.AlterColumn<Farm>(f => f.Notes).NotNull();
schema.AlterColumn<Farm>(f => f.Notes).Nullable();

// Set default
schema.AlterColumn<Farm>(f => f.Status).Default("pending");
```

---

### Add index

```csharp
schema.AddIndex<Farm>(f => f.Location);
schema.AddIndex<Farm>(f => f.Location).Clustered();   // SQL Server only
schema.AddUniqueIndex<Farm>(f => f.Slug);
```

### Drop index

```csharp
schema.DropIndex<Farm>("IX_Farms_Location");
```

---

### Foreign keys

```csharp
schema.AddForeignKey<Field, Farm>(
    child:    field => field.FarmId,
    parent:   farm  => farm.Id,
    onDelete: CascadeRule.Cascade);

schema.DropForeignKey<Field>("FK_Fields_Farm");
```

| `CascadeRule` | SQL |
|---|---|
| `Restrict` (default) | `ON DELETE RESTRICT` |
| `Cascade` | `ON DELETE CASCADE` |
| `SetNull` | `ON DELETE SET NULL` |
| `NoAction` | `ON DELETE NO ACTION` |

---

### Raw SQL escape hatch

```csharp
schema.Sql("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\"");
schema.Sql("UPDATE Settings SET Version = 2 WHERE Version = 1");
```

### Preview generated SQL

```csharp
schema.CreateTable<Farm>(...);
Console.WriteLine(schema.ToSql());
```

---

## Destructive migrations

Mark any migration that loses data with `[Destructive]`. The CLI and runtime both refuse to apply it without an explicit opt-in (`--allow-destructive`).

```csharp
[Migration(20240702001, "drop_legacy_notes")]
[Destructive("Drops Farm.LegacyNotes — export to cold storage before applying.")]
public sealed class DropLegacyNotes : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.DropColumn<Farm>(f => f.LegacyNotes);
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.AddColumn<Farm>(f => f.LegacyNotes).Nullable();
    }
}
```

---

## Irreversible migrations

If a migration cannot be rolled back, throw `IrreversibleMigrationException` in `Down()`:

```csharp
public override void Down(SchemaBuilder schema)
{
    throw new IrreversibleMigrationException(
        "Truncated data cannot be restored. Restore from backup.");
}
```

The CLI returns exit code 4 if rollback is attempted on such a migration.

---

## Running migrations in code

```csharp
// Status
MigrationStatus status = await db.Migrations.StatusAsync();
Console.WriteLine($"Applied: {status.Applied.Count}");
Console.WriteLine($"Pending: {status.Pending.Count}");

// Preview SQL without executing
string sql = await db.Migrations.PreviewAsync();

// Apply all safe pending migrations
await db.Migrations.ApplyAsync();

// Apply including destructive
await db.Migrations.ApplyAsync(allowDestructive: true);

// Apply up to a specific version
await db.Migrations.ApplyToAsync(20240601005);

// Rollback last
await db.Migrations.RollbackAsync();

// Rollback to specific version
await db.Migrations.RollbackToAsync(20240601003);
```

---

## Apply on startup

```csharp
// Program.cs — apply all pending migrations when the app starts
var db = app.Services.GetRequiredService<IFluentDb>();
await db.Migrations.ApplyAsync();
```

---

## The `__FluentMigrations` table

FluentORM creates this table automatically on first run:

| Column | Description |
|---|---|
| `Version` | Migration version (primary key) |
| `Description` | Human-readable name |
| `AppliedAt` | UTC timestamp |
| `AppliedBy` | Machine name |
| `DurationMs` | Execution time in milliseconds |
| `Checksum` | SHA-256 hash of the generated SQL — tamper detection |

Never modify this table manually. Use `validate --check-checksums` to verify integrity.

---

## Version compatibility table

| FluentORM.Migrations | FluentORM.Tools CLI |
|---|---|
| 1.0.2 | 1.0.2 |
| 1.0.1 | 1.0.1 |
