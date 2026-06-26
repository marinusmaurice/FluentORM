---
title: Auto-Scaffold
nav_order: 8
---

# Auto-Scaffold — Migrations from Your Entity Model
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

FluentORM's `scaffold` command works like EF Core's `Add-Migration` — but without requiring a live database connection. It compares your current C# entity classes against a stored JSON snapshot and generates a migration file for everything that changed.

---

## How it works

```
First run:  entity classes  →  _FluentORM_Snapshot.json   (no migration yet)
Next run:   entity classes  →  diff against snapshot  →  Migration_YYYYMMDDNNN_xxx.cs  +  updated snapshot
```

1. **First run (bootstrap):** no snapshot exists. The tool captures the current entity shape and writes `_FluentORM_Snapshot.json`. No migration is generated because there's nothing to diff against.

2. **Subsequent runs:** the tool loads the snapshot, compares it against the compiled entity classes in your assembly, generates a `.cs` migration file for every difference, and updates the snapshot.

The snapshot file lives alongside your migrations. **Commit it to source control** — it is the "last known state" baseline for the next scaffold.

---

## Workflow

```bash
# Step 1 — build your project
dotnet build

# Step 2 — first scaffold run (bootstrap)
fluentorm migrations scaffold initial --output ./Migrations --assembly ./bin/Debug/net8.0/MyApp.dll
# Output: No snapshot found — created initial snapshot
#         Captured 3 table(s): Farms, Fields, Inspections

# Step 3 — change an entity (add a property, remove a property, etc.)
# Step 4 — rebuild
dotnet build

# Step 5 — scaffold the migration
fluentorm migrations scaffold add_farm_notes --output ./Migrations --assembly ./bin/Debug/net8.0/MyApp.dll
# Output: Generated: ./Migrations/Migration_20240618001_AddFarmNotes.cs
#         1 change(s) — snapshot updated.

# Step 6 — review the generated file, then apply
fluentorm migrations apply
```

---

## What changes are detected

| Entity change | Generated migration code |
|---|---|
| New entity class | `schema.CreateTable<T>(...)` |
| Removed entity class | `schema.DropTable<T>()` ⚠ destructive |
| New property | `schema.AddColumn<T>(x => x.Prop)...` |
| Removed property | `schema.DropColumn<T>(x => x.Prop)` ⚠ destructive |
| Nullability changed (nullable → not null) | `schema.AlterColumn<T>(x => x.Prop).NotNull()` |
| Nullability changed (not null → nullable) | `schema.AlterColumn<T>(x => x.Prop).Nullable()` |
| `[MaxLength]` value changed | `schema.AlterColumn<T>(x => x.Prop).MaxLength(n)` |
| New `[Index]` attribute | `schema.AddIndex<T>(x => x.Prop)` |
| Removed `[Index]` attribute | `schema.DropIndex<T>("name")` |

Destructive changes are automatically annotated with `[Destructive("...")]` in the generated file.

---

## Example output

After adding `Notes` to `Farm`:

```csharp
// Migration_20240618001_AddFarmNotes.cs — auto-generated

using FluentORM.Core.Attributes;
using FluentORM.Core.Exceptions;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;
using MyApp.Entities;

namespace MyApp.Migrations;

[Migration(20240618001, "add_farm_notes")]
public sealed class AddFarmNotes : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.AddColumn<Farm>(x => x.Notes).Nullable();
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropColumn<Farm>(x => x.Notes);
    }
}
```

After adding a new `Inspection` entity class:

```csharp
[Migration(20240619001, "create_inspections_table")]
public sealed class CreateInspectionsTable : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.CreateTable<Inspection>(t =>
        {
            t.PrimaryKey(x => x.Id).AutoIncrement();
            t.Column(x => x.FieldId).NotNull();
            t.Column(x => x.InspectedAt).NotNull();
            t.Column(x => x.SeverityScore).Nullable();
            t.Column(x => x.Notes).Nullable();
        });
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropTable<Inspection>();
    }
}
```

---

## Dry-run (preview without writing)

```bash
fluentorm migrations scaffold my_change --output ./Migrations --dry-run
```

Prints the migration that would be generated without writing any files or updating the snapshot. Useful to review changes before committing.

---

## The snapshot file

```json
{
  "version": 20240618001,
  "generatedAt": "2024-06-18T10:22:00Z",
  "tables": {
    "Farms": {
      "tableName": "Farms",
      "entityTypeName": "Farm",
      "entityNamespace": "MyApp.Entities",
      "columns": [
        { "propertyName": "Id",    "columnName": "Id",    "clrType": "System.Int32",  "isNullable": false, "isPrimaryKey": true,  "isAutoIncrement": true },
        { "propertyName": "Name",  "columnName": "name",  "clrType": "System.String", "isNullable": false, "isPrimaryKey": false, "maxLength": 200 },
        { "propertyName": "Notes", "columnName": "notes", "clrType": "System.String?","isNullable": true,  "isPrimaryKey": false }
      ],
      "indexes": []
    }
  }
}
```

**Commit this file.** Without it, the next scaffold run will re-bootstrap from scratch and won't generate any diffs.

---

## Scaffold vs. `new`

| | `scaffold` | `new` |
|---|---|---|
| Requires live DB | No | No |
| Auto-fills Up/Down | Yes | No (blank template) |
| Works from entity changes | Yes | No |
| Best for | Adding/changing entity properties | Data migrations, raw SQL, renames |

Use `scaffold` for the typical case: you changed an entity and want the column change generated for you.  
Use `new` for data migrations, renaming columns, complex index changes, or anything that can't be inferred from entity shape alone.

---

## Limitations

- **Column renames** cannot be detected — a rename looks like a delete + add. Use `migrations new` and call `schema.RenameColumn<T>(old, new)` manually.
- **Default value changes** are not detected (only nullability and MaxLength are diffed).
- **SQLite DropColumn** is a no-op on SQLite. Rollbacks of `AddColumn` migrations will leave the column in place on SQLite.
- **Computed columns** and columns managed by the DB are not included in the snapshot.

---

## Using the snapshot API from code

```csharp
var registry = new EntityMapRegistry();
registry.ScanAssembly(typeof(Farm).Assembly);

var scaffolder = new SnapshotScaffolder(registry);

// Check if snapshot exists
bool exists = SnapshotScaffolder.SnapshotExists("./Migrations");

// Generate
string result = await scaffolder.ScaffoldAsync(
    description:         "add_farm_notes",
    outputDir:           "./Migrations",
    dryRun:              false,
    migrationsNamespace: "MyApp.Migrations");

Console.WriteLine(result);

// Load the current snapshot
ModelSnapshot? snap = await SnapshotScaffolder.LoadSnapshotAsync("./Migrations");
```
