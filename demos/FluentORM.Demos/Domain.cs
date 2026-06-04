using System;
using FluentORM.Core.Attributes;

namespace FluentORM.Demos;

// ── Entities ─────────────────────────────────────────────────────────────────

[Table("Farms")]
public class Farm
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;
    public bool Active { get; set; } = true;

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;

    [SoftDelete]
    public DateTime? DeletedAt { get; set; }

    [Audit]
    public int HectareSize { get; set; }
}

[Table("Fields")]
public class Field
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public int FarmId { get; set; }
    public int AreaHectares { get; set; }
    public string CropType { get; set; } = string.Empty;

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;

    [SoftDelete]
    public DateTime? DeletedAt { get; set; }
}

[Table("Pests")]
public class Pest
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public int RiskLevel { get; set; }
    public string? Category { get; set; }

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;
}

[Table("Inspections")]
public class Inspection
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    public int FieldId { get; set; }
    public int PestId { get; set; }
    public double SeverityScore { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime InspectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;
}

[Table("SprayEvents")]
public class SprayEvent
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    public int FieldId { get; set; }
    public string Chemical { get; set; } = string.Empty;
    public decimal CostZAR { get; set; }
    public DateTime AppliedAt { get; set; }

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;
}

[Table("Products")]
public class Product
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [NotNull]
    public string ExternalId { get; set; } = string.Empty;

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }
    public int Stock { get; set; }

    [RowVersion]
    public int Version { get; set; }

    [TenantKey]
    public string TenantId { get; set; } = string.Empty;
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class FarmSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public double AvgSeverity { get; set; }
}

public class FieldInspectionDto
{
    public string FieldName { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public string PestName { get; set; } = string.Empty;
    public double SeverityScore { get; set; }
    public DateTime InspectedAt { get; set; }
}
