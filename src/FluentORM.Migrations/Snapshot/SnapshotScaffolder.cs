using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Mapping;

namespace FluentORM.Migrations.Snapshot;

/// <summary>
/// Generates migration files by diffing the current entity model against a stored snapshot.
/// No live database connection required.
/// </summary>
public sealed class SnapshotScaffolder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private const string SnapshotFileName = "_FluentORM_Snapshot.json";

    private readonly EntityMapRegistry _registry;

    public SnapshotScaffolder(EntityMapRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Returns the path where the snapshot file would live for a given output directory.
    /// </summary>
    public static string SnapshotPath(string outputDir) =>
        Path.Combine(outputDir, SnapshotFileName);

    /// <summary>
    /// Returns true if a snapshot file already exists in <paramref name="outputDir"/>.
    /// </summary>
    public static bool SnapshotExists(string outputDir) =>
        File.Exists(SnapshotPath(outputDir));

    /// <summary>
    /// Scaffolds a migration file by diffing the current entity model against the stored snapshot.
    /// If no snapshot exists, writes an initial snapshot from the current model and returns an
    /// informational message (nothing to migrate on first run).
    /// </summary>
    public async Task<string> ScaffoldAsync(
        string description,
        string outputDir,
        bool dryRun = false,
        string? migrationsNamespace = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var snapshotPath = SnapshotPath(outputDir);
        var currentModel = SnapshotBuilder.Build(_registry);

        // ── Bootstrap: no snapshot yet ────────────────────────────────────────
        if (!File.Exists(snapshotPath))
        {
            if (!dryRun)
                await WriteSnapshotAsync(snapshotPath, currentModel, version: 0, ct);

            return
                $"No snapshot found — created initial snapshot at {snapshotPath}\n" +
                $"  Captured {currentModel.Tables.Count} table(s): " +
                string.Join(", ", currentModel.Tables.Keys) + "\n" +
                $"  Add or change entity properties, then run scaffold again to generate a migration.";
        }

        // ── Load previous snapshot and diff ───────────────────────────────────
        var previousModel = await ReadSnapshotFileAsync(snapshotPath, ct);
        var changes = ModelDiffer.Diff(previousModel, currentModel);

        if (changes.Count == 0)
            return "No changes detected between the current model and the last snapshot. Nothing to scaffold.";

        // ── Generate migration code ────────────────────────────────────────────
        var version = NextVersion(outputDir);
        var (upBody, downBody, hasDestructive) = MigrationCodeGenerator.Generate(changes);
        var namespaces = MigrationCodeGenerator.CollectNamespaces(changes).ToList();
        var ns = migrationsNamespace ?? "Migrations";
        var className = ToPascalCase(description);

        var content = BuildMigrationFile(version, description, className, ns, namespaces, upBody, downBody, hasDestructive);

        if (dryRun)
            return content;

        // Write migration file
        var fileName = Path.Combine(outputDir, $"Migration_{version}_{className}.cs");
        await File.WriteAllTextAsync(fileName, content, ct);

        // Update snapshot to reflect the new model state
        currentModel.Version = version;
        await WriteSnapshotAsync(snapshotPath, currentModel, version, ct);

        return $"Generated: {fileName}\n  {changes.Count} change(s) — snapshot updated.";
    }

    /// <summary>
    /// Reads and returns the current snapshot without generating a migration.
    /// Returns null if no snapshot exists.
    /// </summary>
    public static async Task<ModelSnapshot?> LoadSnapshotAsync(
        string outputDir, CancellationToken ct = default)
    {
        var path = SnapshotPath(outputDir);
        if (!File.Exists(path)) return null;
        return await ReadSnapshotFileAsync(path, ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<ModelSnapshot> ReadSnapshotFileAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ModelSnapshot>(json, JsonOpts)
               ?? new ModelSnapshot();
    }

    private static async Task WriteSnapshotAsync(
        string path, ModelSnapshot snapshot, long version, CancellationToken ct)
    {
        snapshot.Version = version;
        snapshot.GeneratedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(snapshot, JsonOpts);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private static string BuildMigrationFile(
        long version, string description, string className, string ns,
        System.Collections.Generic.List<string> entityNamespaces,
        string upBody, string downBody, bool hasDestructive)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using FluentORM.Core.Attributes;");
        sb.AppendLine("using FluentORM.Core.Exceptions;");
        sb.AppendLine("using FluentORM.Migrations.Engine;");
        sb.AppendLine("using FluentORM.Migrations.Schema;");

        foreach (var entityNs in entityNamespaces)
            sb.AppendLine($"using {entityNs};");

        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"[Migration({version}, \"{description}\")]");

        if (hasDestructive)
            sb.AppendLine("[Destructive(\"Auto-scaffolded migration contains destructive operations — review before applying.\")]");

        sb.AppendLine($"public sealed class {className} : Migration");
        sb.AppendLine("{");
        sb.AppendLine("    public override void Up(SchemaBuilder schema)");
        sb.AppendLine("    {");
        sb.Append(upBody.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public override void Down(SchemaBuilder schema)");
        sb.AppendLine("    {");
        sb.Append(downBody.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static long NextVersion(string outputDir)
    {
        var prefix = $"{DateTime.UtcNow:yyyyMMdd}";
        var existing = Directory.EnumerateFiles(outputDir, "Migration_*.cs")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(n => { var p = n.Split('_'); return p.Length >= 2 ? p[1] : null; })
            .Where(v => v?.StartsWith(prefix) == true)
            .Select(v => long.TryParse(v, out var n) ? n : 0L)
            .Where(n => n > 0)
            .ToList();

        if (existing.Count == 0) return long.Parse($"{prefix}001");
        return existing.Max() + 1;
    }

    private static string ToPascalCase(string s) =>
        string.Concat(s.Split('_', ' ', '-')
            .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : ""));
}
