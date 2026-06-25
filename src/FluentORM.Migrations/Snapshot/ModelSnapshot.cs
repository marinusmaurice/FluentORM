using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentORM.Migrations.Snapshot;

/// <summary>
/// A point-in-time snapshot of the entity model as seen by FluentORM.
/// Serialised to <c>_FluentORM_Snapshot.json</c> alongside migration files and used
/// by <see cref="ModelDiffer"/> to compute what changed since the last scaffold run.
/// </summary>
public sealed class ModelSnapshot
{
    /// <summary>Migration version number of the last scaffold run (0 for the bootstrap snapshot).</summary>
    [JsonPropertyName("version")]
    public long Version { get; set; }

    /// <summary>UTC timestamp of when this snapshot was written.</summary>
    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Tables keyed by table name (case-insensitive).</summary>
    [JsonPropertyName("tables")]
    public Dictionary<string, SnapshotTable> Tables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Snapshot of a single table as mapped by a FluentORM entity class.
/// </summary>
public sealed class SnapshotTable
{
    /// <summary>The database table name (from <c>[Table("...")]</c> or the entity class name).</summary>
    [JsonPropertyName("tableName")]
    public string TableName { get; set; } = string.Empty;

    /// <summary>The short name of the C# entity type (e.g. <c>"Farm"</c>).</summary>
    [JsonPropertyName("entityTypeName")]
    public string EntityTypeName { get; set; } = string.Empty;

    /// <summary>The CLR namespace of the entity type, used to emit <c>using</c> statements in generated migrations.</summary>
    [JsonPropertyName("entityNamespace")]
    public string EntityNamespace { get; set; } = string.Empty;

    /// <summary>Column snapshots, ordered as discovered from the entity map.</summary>
    [JsonPropertyName("columns")]
    public List<SnapshotColumn> Columns { get; set; } = new();

    /// <summary>Index snapshots derived from <c>[Index]</c> and <c>[UniqueIndex]</c> property attributes.</summary>
    [JsonPropertyName("indexes")]
    public List<SnapshotIndex> Indexes { get; set; } = new();
}

/// <summary>
/// Snapshot of a single mapped column.
/// </summary>
public sealed class SnapshotColumn
{
    /// <summary>C# property name (e.g. <c>"Notes"</c>).</summary>
    [JsonPropertyName("propertyName")]
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>Database column name (e.g. <c>"notes"</c>).</summary>
    [JsonPropertyName("columnName")]
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>Friendly CLR type string, e.g. <c>"System.String"</c> or <c>"System.Int32?"</c>.</summary>
    [JsonPropertyName("clrType")]
    public string ClrType { get; set; } = string.Empty;

    /// <summary><c>true</c> if the column allows NULL.</summary>
    [JsonPropertyName("isNullable")]
    public bool IsNullable { get; set; }

    /// <summary><c>true</c> if the column is the primary key of the table.</summary>
    [JsonPropertyName("isPrimaryKey")]
    public bool IsPrimaryKey { get; set; }

    /// <summary><c>true</c> if the column is an auto-incrementing primary key.</summary>
    [JsonPropertyName("isAutoIncrement")]
    public bool IsAutoIncrement { get; set; }

    /// <summary><c>true</c> if the column is a row-version / concurrency token.</summary>
    [JsonPropertyName("isRowVersion")]
    public bool IsRowVersion { get; set; }

    /// <summary>Maximum string/binary length, or <c>null</c> if unconstrained.</summary>
    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    /// <summary>String-serialised default value, or <c>null</c> if none.</summary>
    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }
}

/// <summary>
/// Snapshot of an index declared on an entity property.
/// </summary>
public sealed class SnapshotIndex
{
    /// <summary>Index name (auto-derived as <c>IX_TableName_PropName</c> if not specified).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>C# property name the index is defined on.</summary>
    [JsonPropertyName("propertyName")]
    public string PropertyName { get; set; } = string.Empty;

    /// <summary><c>true</c> for a unique index.</summary>
    [JsonPropertyName("isUnique")]
    public bool IsUnique { get; set; }
}
