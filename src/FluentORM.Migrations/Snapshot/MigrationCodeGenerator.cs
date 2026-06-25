using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FluentORM.Migrations.Snapshot;

public static class MigrationCodeGenerator
{
    private const string Indent = "        ";  // 8 spaces (inside method body)

    public static (string up, string down, bool hasDestructive) Generate(IReadOnlyList<ModelChange> changes)
    {
        var up   = new StringBuilder();
        var down = new StringBuilder();
        bool hasDestructive = changes.Any(c => c.IsDestructive);

        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case ChangeKind.CreateTable:
                    AppendCreateTable(up, change);
                    AppendDropTable(down, change);
                    break;

                case ChangeKind.DropTable:
                    AppendDropTable(up, change);
                    AppendCreateTable(down, change);
                    break;

                case ChangeKind.AddColumn:
                    AppendAddColumn(up, change);
                    AppendDropColumn(down, change);
                    break;

                case ChangeKind.DropColumn:
                    AppendDropColumn(up, change);
                    AppendAddColumn(down, change);
                    break;

                case ChangeKind.AlterColumnNullability:
                    AppendAlterNullability(up, change, change.Column!);
                    AppendAlterNullability(down, change, change.OldColumn!);
                    break;

                case ChangeKind.AlterColumnMaxLength:
                    AppendAlterMaxLength(up, change, change.Column!);
                    AppendAlterMaxLength(down, change, change.OldColumn!);
                    break;

                case ChangeKind.AddIndex:
                    AppendAddIndex(up, change);
                    AppendDropIndex(down, change);
                    break;

                case ChangeKind.DropIndex:
                    AppendDropIndex(up, change);
                    AppendAddIndex(down, change);
                    break;
            }
        }

        return (up.ToString(), down.ToString(), hasDestructive);
    }

    // ── Up/Down helpers ───────────────────────────────────────────────────────

    private static void AppendCreateTable(StringBuilder sb, ModelChange change)
    {
        var t = change.Table!;
        sb.AppendLine($"{Indent}schema.CreateTable<{change.EntityName}>(t =>");
        sb.AppendLine($"{Indent}{{");

        foreach (var col in t.Columns)
        {
            if (col.IsPrimaryKey)
            {
                var ai = col.IsAutoIncrement ? ".AutoIncrement()" : "";
                sb.AppendLine($"{Indent}    t.PrimaryKey(x => x.{col.PropertyName}){ai};");
            }
            else
            {
                sb.Append($"{Indent}    t.Column(x => x.{col.PropertyName})");
                AppendColumnChain(sb, col);
                sb.AppendLine(";");
            }
        }

        foreach (var idx in t.Indexes)
        {
            if (idx.IsUnique)
                sb.AppendLine($"{Indent}    t.UniqueIndex(x => x.{idx.PropertyName});");
            else
                sb.AppendLine($"{Indent}    t.Index(x => x.{idx.PropertyName});");
        }

        sb.AppendLine($"{Indent}}});");
        sb.AppendLine();
    }

    private static void AppendDropTable(StringBuilder sb, ModelChange change)
    {
        sb.AppendLine($"{Indent}schema.DropTable<{change.EntityName}>();");
        sb.AppendLine();
    }

    private static void AppendAddColumn(StringBuilder sb, ModelChange change)
    {
        var col = change.Column!;
        sb.Append($"{Indent}schema.AddColumn<{change.EntityName}>(x => x.{col.PropertyName})");
        AppendColumnChain(sb, col);
        sb.AppendLine(";");
        sb.AppendLine();
    }

    private static void AppendDropColumn(StringBuilder sb, ModelChange change)
    {
        var col = change.Column!;
        sb.AppendLine($"{Indent}schema.DropColumn<{change.EntityName}>(x => x.{col.PropertyName});");
        sb.AppendLine();
    }

    private static void AppendAlterNullability(StringBuilder sb, ModelChange change, SnapshotColumn col)
    {
        var nullability = col.IsNullable ? ".Nullable()" : ".NotNull()";
        sb.AppendLine($"{Indent}schema.AlterColumn<{change.EntityName}>(x => x.{col.PropertyName}){nullability};");
        sb.AppendLine();
    }

    private static void AppendAlterMaxLength(StringBuilder sb, ModelChange change, SnapshotColumn col)
    {
        var chain = col.MaxLength.HasValue ? $".MaxLength({col.MaxLength})" : "";
        sb.AppendLine($"{Indent}schema.AlterColumn<{change.EntityName}>(x => x.{col.PropertyName}){chain};");
        sb.AppendLine();
    }

    private static void AppendAddIndex(StringBuilder sb, ModelChange change)
    {
        var idx = change.Index!;
        if (idx.IsUnique)
            sb.AppendLine($"{Indent}schema.AddUniqueIndex<{change.EntityName}>(x => x.{idx.PropertyName});");
        else
            sb.AppendLine($"{Indent}schema.AddIndex<{change.EntityName}>(x => x.{idx.PropertyName});");
        sb.AppendLine();
    }

    private static void AppendDropIndex(StringBuilder sb, ModelChange change)
    {
        var idx = change.Index!;
        sb.AppendLine($"{Indent}schema.DropIndex<{change.EntityName}>(\"{idx.Name}\");");
        sb.AppendLine();
    }

    // ── Column fluent-chain builder ───────────────────────────────────────────

    private static void AppendColumnChain(StringBuilder sb, SnapshotColumn col)
    {
        if (col.IsRowVersion)  { sb.Append(".IsRowVersion()"); return; }
        if (!col.IsNullable)   sb.Append(".NotNull()");
        else                   sb.Append(".Nullable()");
        if (col.MaxLength.HasValue)    sb.Append($".MaxLength({col.MaxLength})");
        if (col.DefaultValue != null)  sb.Append($".Default({FormatDefault(col)})");
    }

    private static string FormatDefault(SnapshotColumn col)
    {
        // Wrap strings in quotes; leave numbers/booleans bare
        var val = col.DefaultValue!;
        if (col.ClrType.Contains("String") || col.ClrType == "string")
            return $"\"{val}\"";
        return val;
    }

    // ── Namespace collection (for using statements) ───────────────────────────

    public static IEnumerable<string> CollectNamespaces(IReadOnlyList<ModelChange> changes) =>
        changes
            .Select(c => c.EntityNamespace)
            .Where(ns => !string.IsNullOrWhiteSpace(ns))
            .Distinct()
            .OrderBy(ns => ns);
}
