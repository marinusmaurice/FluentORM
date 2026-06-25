using System.Collections.Generic;
using System.Linq;

namespace FluentORM.Migrations.Snapshot;

public enum ChangeKind
{
    CreateTable,
    DropTable,
    AddColumn,
    DropColumn,
    AlterColumnNullability,
    AlterColumnMaxLength,
    AddIndex,
    DropIndex
}

public sealed class ModelChange
{
    public ChangeKind Kind { get; init; }
    public bool IsDestructive { get; init; }

    // Table context
    public string TableName    { get; init; } = string.Empty;
    public string EntityName   { get; init; } = string.Empty;
    public string EntityNamespace { get; init; } = string.Empty;

    // Column context (AddColumn / DropColumn / Alter*)
    public SnapshotColumn? Column    { get; init; }
    public SnapshotColumn? OldColumn { get; init; }   // for Alter* — the before state

    // Index context
    public SnapshotIndex? Index { get; init; }

    // Full table snapshot (for CreateTable / DropTable)
    public SnapshotTable? Table { get; init; }
}

public static class ModelDiffer
{
    /// <summary>
    /// Diffs <paramref name="from"/> (previous snapshot) against <paramref name="to"/> (current model).
    /// Returns the changes needed to bring the database from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    public static IReadOnlyList<ModelChange> Diff(ModelSnapshot from, ModelSnapshot to)
    {
        var changes = new List<ModelChange>();

        // Tables added in current model
        foreach (var (key, toTable) in to.Tables)
        {
            if (!from.Tables.TryGetValue(key, out var fromTable))
            {
                changes.Add(new ModelChange
                {
                    Kind      = ChangeKind.CreateTable,
                    TableName = toTable.TableName,
                    EntityName = toTable.EntityTypeName,
                    EntityNamespace = toTable.EntityNamespace,
                    Table     = toTable
                });
                continue;
            }

            DiffTable(fromTable, toTable, changes);
        }

        // Tables removed from current model
        foreach (var (key, fromTable) in from.Tables)
        {
            if (!to.Tables.ContainsKey(key))
            {
                changes.Add(new ModelChange
                {
                    Kind         = ChangeKind.DropTable,
                    IsDestructive = true,
                    TableName    = fromTable.TableName,
                    EntityName   = fromTable.EntityTypeName,
                    EntityNamespace = fromTable.EntityNamespace,
                    Table        = fromTable
                });
            }
        }

        return changes;
    }

    private static void DiffTable(SnapshotTable from, SnapshotTable to, List<ModelChange> changes)
    {
        var fromCols = from.Columns.ToDictionary(c => c.PropertyName);
        var toCols   = to.Columns.ToDictionary(c => c.PropertyName);

        // Added columns
        foreach (var (name, col) in toCols)
        {
            if (!fromCols.ContainsKey(name))
                changes.Add(new ModelChange
                {
                    Kind      = ChangeKind.AddColumn,
                    TableName = to.TableName,
                    EntityName = to.EntityTypeName,
                    EntityNamespace = to.EntityNamespace,
                    Column    = col
                });
        }

        // Removed columns
        foreach (var (name, col) in fromCols)
        {
            if (!toCols.ContainsKey(name))
                changes.Add(new ModelChange
                {
                    Kind         = ChangeKind.DropColumn,
                    IsDestructive = true,
                    TableName    = to.TableName,
                    EntityName   = to.EntityTypeName,
                    EntityNamespace = to.EntityNamespace,
                    Column       = col
                });
        }

        // Altered columns
        foreach (var (name, toCol) in toCols)
        {
            if (!fromCols.TryGetValue(name, out var fromCol)) continue;

            if (fromCol.IsNullable != toCol.IsNullable)
                changes.Add(new ModelChange
                {
                    Kind      = ChangeKind.AlterColumnNullability,
                    TableName = to.TableName,
                    EntityName = to.EntityTypeName,
                    EntityNamespace = to.EntityNamespace,
                    Column    = toCol,
                    OldColumn = fromCol
                });

            if (fromCol.MaxLength != toCol.MaxLength)
                changes.Add(new ModelChange
                {
                    Kind      = ChangeKind.AlterColumnMaxLength,
                    TableName = to.TableName,
                    EntityName = to.EntityTypeName,
                    EntityNamespace = to.EntityNamespace,
                    Column    = toCol,
                    OldColumn = fromCol
                });
        }

        // Indexes
        var fromIdxs = from.Indexes.ToDictionary(i => i.Name);
        var toIdxs   = to.Indexes.ToDictionary(i => i.Name);

        foreach (var (name, idx) in toIdxs)
        {
            if (!fromIdxs.ContainsKey(name))
                changes.Add(new ModelChange
                {
                    Kind      = ChangeKind.AddIndex,
                    TableName = to.TableName,
                    EntityName = to.EntityTypeName,
                    EntityNamespace = to.EntityNamespace,
                    Index     = idx
                });
        }

        foreach (var (name, idx) in fromIdxs)
        {
            if (!toIdxs.ContainsKey(name))
                changes.Add(new ModelChange
                {
                    Kind      = ChangeKind.DropIndex,
                    TableName = to.TableName,
                    EntityName = to.EntityTypeName,
                    EntityNamespace = to.EntityNamespace,
                    Index     = idx
                });
        }
    }
}
