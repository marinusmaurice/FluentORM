---
title: Entity Mapping
nav_order: 3
---

# Entity Mapping
{: .no_toc }

<details open markdown="block">
  <summary>On this page</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

FluentORM supports two mapping styles: **attribute-based** (decorators on the class) and **fluent API** (a separate configuration class). You can mix both — the fluent API fills in anything the attributes don't cover.

---

## Attribute mapping

The simplest approach. Decorate your entity class directly.

```csharp
using FluentORM.Core.Attributes;

[Table("Farms")]
public class Farm
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [Column("name"), NotNull, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Column("location"), NotNull, MaxLength(500)]
    public string Location { get; set; } = string.Empty;

    [Column("hectares")]
    public double? Hectares { get; set; }

    [Column("established_year"), DefaultValue(2000)]
    public int EstablishedYear { get; set; }

    // Soft-delete timestamp — filtered out of normal queries automatically
    [Column("deleted_at"), SoftDelete]
    public DateTime? DeletedAt { get; set; }

    // Optimistic concurrency token
    [Column("row_version"), RowVersion]
    public byte[]? RowVersion { get; set; }

    // Not mapped to a column
    [Ignore]
    public string DisplayLabel => $"{Name} ({Location})";
}
```

---

## Attribute reference

### `[Table(name)]`

Maps the class to a database table. Without this attribute, the class name is used.

```csharp
[Table("Farms")]
public class Farm { }
```

---

### `[Column(name)]`

Maps a property to a column with a specific name. Without this attribute, the property name is used.

```csharp
[Column("established_year")]
public int EstablishedYear { get; set; }
```

---

### `[PrimaryKey(autoIncrement = true)]`

Marks the primary key column.

```csharp
[PrimaryKey(autoIncrement: true)]
public int Id { get; set; }

// Natural key (no auto-increment)
[PrimaryKey(autoIncrement: false)]
public Guid Id { get; set; }
```

---

### `[NotNull]`

Marks the column as NOT NULL. String properties without this attribute are nullable by default.

```csharp
[NotNull]
public string Name { get; set; } = string.Empty;
```

---

### `[MaxLength(length)]`

Sets the maximum character/byte length for string or binary columns.

```csharp
[MaxLength(200)]
public string Name { get; set; } = string.Empty;
```

---

### `[DefaultValue(value)]`

Sets a column-level default value applied by the database on INSERT.

```csharp
[DefaultValue(2000)]
public int EstablishedYear { get; set; }

[DefaultValue("active")]
public string Status { get; set; } = "active";
```

---

### `[Index]` and `[UniqueIndex]`

Declares a database index on the column. Used by the snapshot scaffolder and schema builder.

```csharp
[Index]
public string Location { get; set; } = string.Empty;

[UniqueIndex]
public string Slug { get; set; } = string.Empty;

// Named index
[Index(Name = "IX_Farms_Owner")]
public int OwnerId { get; set; }

// Two indexes on the same property
[Index(Name = "IX_Farms_Status")]
[Index(Name = "IX_Farms_Status_Location", IsUnique = false)]
public string Status { get; set; } = string.Empty;
```

---

### `[ForeignKey<TRef>]`

Declares a foreign key constraint pointing to another entity's primary key.

```csharp
[ForeignKey<Farm>]
public int FarmId { get; set; }

// Named constraint
[ForeignKey<Farm>(constraintName: "FK_Fields_Farm")]
public int FarmId { get; set; }
```

---

### `[SoftDelete]`

Marks the column that stores the deletion timestamp. When a record is "deleted" via `db.DeleteAsync`, this column is set to `DateTime.UtcNow` instead of issuing a `DELETE`. Normal queries exclude soft-deleted rows automatically.

```csharp
[Column("deleted_at"), SoftDelete]
public DateTime? DeletedAt { get; set; }
```

---

### `[RowVersion]`

Marks a column as the optimistic concurrency token. Updated automatically on every write. Conflicts throw `ConcurrencyException`.

```csharp
[RowVersion]
public byte[]? RowVersion { get; set; }
```

---

### `[Audit]`

Marks a column to include in the automatic audit trail. Every change to this column is recorded in the `__FluentAudit` table.

```csharp
[Audit]
public string Status { get; set; } = string.Empty;
```

Use `[NoAudit]` on the class to opt the whole table out of global audit.

---

### `[TenantKey]`

Marks the column that holds the tenant identifier. In a multi-tenant setup, every query is automatically filtered by the current tenant.

```csharp
[TenantKey]
public string TenantId { get; set; } = string.Empty;
```

---

### `[Computed]`

Marks a column as database-computed. FluentORM will never try to INSERT or UPDATE it.

```csharp
[Computed]
public int TotalFields { get; set; }  // a computed column in the DB
```

---

### `[Encrypted]`

Marks a column for transparent encryption/decryption. Values are encrypted before writing and decrypted on read using the key configured in the builder.

```csharp
[Encrypted]
public string TaxNumber { get; set; } = string.Empty;
```

---

### `[Ignore]`

Excludes a property from all mapping. FluentORM will not read it from or write it to the database.

```csharp
[Ignore]
public string DisplayLabel => $"{Name} ({Location})";
```

---

### `[NoCacheAttribute]` (class-level)

Opt a table out of the global query cache even when `.CacheFor(ttl)` is configured globally.

```csharp
[NoCache]
[Table("AuditLogs")]
public class AuditLog { }
```

---

## Fluent API mapping

Create a class that extends `EntityMap<T>`. This is useful when you don't want to pollute your domain models with ORM attributes (e.g., in a clean architecture).

```csharp
using FluentORM.Core.Mapping;

public class FarmMap : EntityMap<Farm>
{
    public FarmMap()
    {
        ToTable("Farms");

        Key(f => f.Id).AutoIncrement();

        Column(f => f.Name).HasColumnName("name").NotNull().MaxLength(200);
        Column(f => f.Location).HasColumnName("location").NotNull().MaxLength(500);
        Column(f => f.Hectares).HasColumnName("hectares");
        Column(f => f.EstablishedYear).HasColumnName("established_year").Default(2000);
        Column(f => f.DeletedAt).HasColumnName("deleted_at").IsSoftDelete();
        Column(f => f.RowVersion).HasColumnName("row_version").IsRowVersion();
    }
}
```

Register the map during startup:

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("Data Source=app.db")
    .RegisterMap<FarmMap>());
```

### `ColumnMapBuilder` methods

| Method | Description |
|---|---|
| `.HasColumnName(name)` | Override the database column name |
| `.NotNull()` | Mark as NOT NULL |
| `.MaxLength(n)` | Set maximum length |
| `.Default(value)` | Set database default value |
| `.IsRowVersion()` | Optimistic concurrency token |
| `.IsSoftDelete()` | Soft-delete timestamp column |
| `.IsTenantKey()` | Multi-tenancy discriminator |
| `.IsAudited()` | Include in audit trail |
| `.IsComputed()` | Database-computed column (never written) |

---

## Complete example — clean domain model

Domain model with no ORM references:

```csharp
// Domain/Farm.cs — zero ORM references
public class Farm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public double? Hectares { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

Mapping in the infrastructure layer:

```csharp
// Infrastructure/Mappings/FarmMap.cs
public class FarmMap : EntityMap<Farm>
{
    public FarmMap()
    {
        ToTable("Farms");
        Key(f => f.Id).AutoIncrement();
        Column(f => f.Name).HasColumnName("name").NotNull().MaxLength(200);
        Column(f => f.Location).HasColumnName("location").NotNull();
        Column(f => f.Hectares).HasColumnName("hectares");
        Column(f => f.DeletedAt).HasColumnName("deleted_at").IsSoftDelete();
    }
}
```

Registration:

```csharp
builder.Services.AddFluentOrm(orm => orm
    .UseSqlite("Data Source=app.db")
    .RegisterMap<FarmMap>());
```

---

## Mixing attributes and fluent API

The fluent API always wins over attributes for the same property. You can use attributes as the default and override specific properties with the fluent API:

```csharp
[Table("Farms")]
public class Farm
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;  // overridden below
    public string Location { get; set; } = string.Empty;
}

public class FarmMap : EntityMap<Farm>
{
    public FarmMap()
    {
        // Only override what attributes don't cover
        Column(f => f.Name).HasColumnName("farm_name").NotNull().MaxLength(200);
    }
}
```
