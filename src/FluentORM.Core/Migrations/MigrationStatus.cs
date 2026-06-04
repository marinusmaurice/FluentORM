using System.Collections.Generic;

namespace FluentORM.Core.Migrations;

public sealed class MigrationStatus
{
    public IReadOnlyList<MigrationInfo> Applied { get; init; } = [];
    public IReadOnlyList<MigrationInfo> Pending { get; init; } = [];
    public IReadOnlyList<MigrationInfo> DestructivePending { get; init; } = [];
}

public sealed class MigrationInfo
{
    public long Version { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsDestructive { get; init; }
    public string? DestructiveReason { get; init; }
    public System.DateTime? AppliedAt { get; init; }
}
