using System;
using FluentORM.Core.Attributes;

namespace FluentORM.MigrationsSample;

// These entity shapes represent the END STATE of the schema, after every
// migration below has been applied. The migrations build up to this
// incrementally — exactly like a real project evolving over time.

[Table("Farms")]
public class Farm
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    // Added by migration 3
    public string IrrigationType { get; set; } = "None";

    // Added by migration 6, then dropped again by migration 7 (destructive)
    public string? LegacyNotes { get; set; }
}

[Table("Fields")]
public class Field
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public int FarmId { get; set; }
    public string CropType { get; set; } = string.Empty;
}
