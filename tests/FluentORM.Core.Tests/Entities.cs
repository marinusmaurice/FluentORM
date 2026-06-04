using System;
using FluentORM.Core.Attributes;

namespace FluentORM.Core.Tests;

[Table("Pests")]
public class Pest
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [Column("name"), NotNull]
    public string Name { get; set; } = string.Empty;

    public int RiskLevel { get; set; }

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;

    [RowVersion]
    public byte[]? Version { get; set; }

    [SoftDelete]
    public DateTime? DeletedAt { get; set; }

    [Audit]
    public int SeverityScore { get; set; }

    [Computed]
    public string? DisplayLabel { get; set; }
}

[Table("Fields")]
public class Field
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public int FarmId { get; set; }

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;
}

[Table("Scoutings")]
public class Scouting
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    public int PestId { get; set; }
    public int FieldId { get; set; }

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;

    public double SeverityScore { get; set; }
    public DateTime ObservedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
