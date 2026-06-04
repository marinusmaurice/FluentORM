using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Attributes;
using FluentORM.Core.Exceptions;
using FluentORM.Core.Migrations;
using FluentORM.Migrations.Schema;

namespace FluentORM.Migrations.Engine;

public sealed class MigrationEngine : IMigrationRunner
{
    private readonly IConnectionFactory _factory;
    private readonly ISqlDialect _dialect;
    private readonly Core.Mapping.EntityMapRegistry _registry;
    private readonly MigrationHistory _history;
    private readonly List<Assembly> _assemblies;

    public MigrationEngine(
        IConnectionFactory factory,
        ISqlDialect dialect,
        Core.Mapping.EntityMapRegistry registry,
        List<Assembly>? assemblies = null)
    {
        _factory = factory;
        _dialect = dialect;
        _registry = registry;
        _history = new MigrationHistory(factory, dialect);
        _assemblies = assemblies ?? [Assembly.GetEntryAssembly()!];
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task ApplyAsync(bool allowDestructive = false, CancellationToken ct = default)
    {
        await _history.EnsureTableExistsAsync(ct);
        await ValidateChecksumsAsync(ct);

        var applied = (await _history.GetAppliedAsync(ct)).ToDictionary(e => e.Version);
        var lastApplied = applied.Values.OrderByDescending(e => e.Version).FirstOrDefault();
        var pending = DiscoverMigrations()
            .Where(m => !applied.ContainsKey(m.Version))
            .OrderBy(m => m.Version)
            .ToList();

        bool anyAppliedThisRun = false;
        foreach (var meta in pending)
        {
            // Guard: version ordering — no migration older than last applied
            if (lastApplied != null && meta.Version < lastApplied.Version)
                throw new MigrationOrderException(meta.Version, lastApplied.Version);

            if (meta.IsDestructive && !allowDestructive)
            {
                // If no safe migrations have been applied yet in this run, throw immediately.
                // If some safe migrations ran first, stop silently — the destructive one waits for --allow-destructive.
                if (!anyAppliedThisRun)
                    throw new DestructiveMigrationException(meta.Version, meta.Description,
                        meta.DestructiveReason ?? "No reason provided.");
                break; // safe migrations done; destructive ones require explicit opt-in
            }

            await ApplyMigrationAsync(meta, ct);
            anyAppliedThisRun = true;
            lastApplied = (await _history.GetAppliedAsync(ct))
                .OrderByDescending(e => e.Version).First();
        }
    }

    public async Task ApplyToAsync(long version, bool allowDestructive = false, CancellationToken ct = default)
    {
        await _history.EnsureTableExistsAsync(ct);
        var applied = (await _history.GetAppliedAsync(ct)).ToDictionary(e => e.Version);
        var lastApplied = applied.Values.OrderByDescending(e => e.Version).FirstOrDefault();

        var pending = DiscoverMigrations()
            .Where(m => !applied.ContainsKey(m.Version) && m.Version <= version)
            .OrderBy(m => m.Version)
            .ToList();

        foreach (var meta in pending)
        {
            if (lastApplied != null && meta.Version < lastApplied.Version)
                throw new MigrationOrderException(meta.Version, lastApplied.Version);
            if (meta.IsDestructive && !allowDestructive)
                throw new DestructiveMigrationException(meta.Version, meta.Description, meta.DestructiveReason ?? "");
            await ApplyMigrationAsync(meta, ct);
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _history.EnsureTableExistsAsync(ct);
        var applied = await _history.GetAppliedAsync(ct);
        var last = applied.OrderByDescending(e => e.Version).FirstOrDefault();
        if (last == null) return;

        var migration = DiscoverMigrations().FirstOrDefault(m => m.Version == last.Version);
        if (migration == null) return;

        await RollbackMigrationAsync(migration, ct);
        await _history.RemoveAsync(last.Version, ct);
    }

    public async Task RollbackToAsync(long version, CancellationToken ct = default)
    {
        await _history.EnsureTableExistsAsync(ct);
        var toRoll = (await _history.GetAppliedAsync(ct))
            .Where(e => e.Version > version)
            .OrderByDescending(e => e.Version)
            .ToList();

        foreach (var entry in toRoll)
        {
            var migration = DiscoverMigrations().FirstOrDefault(m => m.Version == entry.Version);
            if (migration == null) continue;
            await RollbackMigrationAsync(migration, ct);
            await _history.RemoveAsync(entry.Version, ct);
        }
    }

    public async Task<string> PreviewAsync(CancellationToken ct = default)
    {
        await _history.EnsureTableExistsAsync(ct);
        var applied = (await _history.GetAppliedAsync(ct)).ToDictionary(e => e.Version);
        var pending = DiscoverMigrations().Where(m => !applied.ContainsKey(m.Version)).OrderBy(m => m.Version);

        var sb = new StringBuilder();
        foreach (var meta in pending)
        {
            var schema = new SchemaBuilder(_dialect, _registry);
            var migration = (Migration)Activator.CreateInstance(meta.Type)!;
            migration.Up(schema);
            var destructiveTag = meta.IsDestructive ? " [DESTRUCTIVE]" : " [safe]";
            sb.AppendLine($"-- {meta.Version}: {meta.Description}{destructiveTag}");
            sb.AppendLine(schema.ToSql());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public async Task<MigrationStatus> StatusAsync(CancellationToken ct = default)
    {
        await _history.EnsureTableExistsAsync(ct);
        var applied = (await _history.GetAppliedAsync(ct)).ToDictionary(e => e.Version);
        var all = DiscoverMigrations().ToList();

        var appliedInfos = all
            .Where(m => applied.ContainsKey(m.Version))
            .Select(m => new MigrationInfo
            {
                Version = m.Version,
                Description = m.Description,
                IsDestructive = m.IsDestructive,
                DestructiveReason = m.DestructiveReason,
                AppliedAt = applied[m.Version].AppliedAt
            }).ToList();

        var pendingAll = all
            .Where(m => !applied.ContainsKey(m.Version))
            .Select(m => new MigrationInfo
            {
                Version = m.Version,
                Description = m.Description,
                IsDestructive = m.IsDestructive,
                DestructiveReason = m.DestructiveReason
            }).ToList();

        return new MigrationStatus
        {
            Applied = appliedInfos,
            Pending = pendingAll.Where(m => !m.IsDestructive).ToList(),
            DestructivePending = pendingAll.Where(m => m.IsDestructive).ToList()
        };
    }

    /// <summary>
    /// Validates that no previously-applied migration has been modified after being applied.
    /// Throws <see cref="MigrationTamperedWithException"/> if a checksum mismatch is detected.
    /// </summary>
    public async Task ValidateChecksumsAsync(CancellationToken ct = default)
    {
        var applied = (await _history.GetAppliedAsync(ct)).ToDictionary(e => e.Version);
        var discovered = DiscoverMigrations().ToDictionary(m => m.Version);

        foreach (var (version, entry) in applied)
        {
            if (!discovered.TryGetValue(version, out var meta)) continue;

            // Compute current checksum from the migration's Up() output
            var currentChecksum = ComputeMigrationChecksum(meta);
            if (!string.Equals(entry.Checksum, currentChecksum, StringComparison.OrdinalIgnoreCase))
                throw new MigrationTamperedWithException(version);
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private string ComputeMigrationChecksum(MigrationMeta meta)
    {
        // Hash the SQL generated by Up() — if migration logic changes, checksum changes
        try
        {
            var schema = new SchemaBuilder(_dialect, _registry);
            var migration = (Migration)Activator.CreateInstance(meta.Type)!;
            migration.Up(schema);
            return MigrationHistory.ComputeChecksum(schema.ToSql());
        }
        catch
        {
            // If Up() throws during checksum computation, fall back to type identity
            return MigrationHistory.ComputeChecksum(meta.Type.AssemblyQualifiedName ?? meta.Type.Name);
        }
    }

    private async Task ApplyMigrationAsync(MigrationMeta meta, CancellationToken ct)
    {
        var migration = (Migration)Activator.CreateInstance(meta.Type)!;
        var schema = new SchemaBuilder(_dialect, _registry);
        migration.Up(schema);

        var sw = Stopwatch.StartNew();
        using var conn = await _factory.OpenAsync(ct);
        using var txn = conn.BeginTransaction();
        try
        {
            foreach (var sql in schema.Statements)
                await ExecuteSqlAsync(conn, txn, sql, ct);
            txn.Commit();
        }
        catch (Exception ex)
        {
            try { txn.Rollback(); } catch { }
            throw new MigrationExecutionException(meta.Version, ex.Message, ex);
        }

        sw.Stop();
        var checksum = MigrationHistory.ComputeChecksum(schema.ToSql());
        await _history.RecordAsync(new MigrationHistoryEntry
        {
            Version = meta.Version,
            Description = meta.Description,
            AppliedAt = DateTime.UtcNow,
            AppliedBy = Environment.MachineName,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Checksum = checksum
        }, ct);
    }

    private async Task RollbackMigrationAsync(MigrationMeta meta, CancellationToken ct)
    {
        var migration = (Migration)Activator.CreateInstance(meta.Type)!;
        var schema = new SchemaBuilder(_dialect, _registry);

        try { migration.Down(schema); }
        catch (IrreversibleMigrationException iex)
        {
            throw new IrreversibleMigrationException(
                $"Cannot roll back migration {meta.Version} ({meta.Description}): {iex.Message}");
        }

        using var conn = await _factory.OpenAsync(ct);
        using var txn = conn.BeginTransaction();
        try
        {
            foreach (var sql in schema.Statements)
                await ExecuteSqlAsync(conn, txn, sql, ct);
            txn.Commit();
        }
        catch (Exception ex)
        {
            try { txn.Rollback(); } catch { }
            throw new MigrationExecutionException(meta.Version, ex.Message, ex);
        }
    }

    private static async Task ExecuteSqlAsync(IDbConnection conn, IDbTransaction txn, string sql, CancellationToken ct)
    {
        var trimmed = sql.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--")) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = txn;
        if (cmd is DbCommand dbCmd) await dbCmd.ExecuteNonQueryAsync(ct);
        else cmd.ExecuteNonQuery();
    }

    private IEnumerable<MigrationMeta> DiscoverMigrations()
    {
        var metas = new List<MigrationMeta>();
        foreach (var assembly in _assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(Migration).IsAssignableFrom(type)) continue;
                var attr = type.GetCustomAttribute<MigrationAttribute>();
                if (attr == null) continue;
                var destructive = type.GetCustomAttribute<DestructiveAttribute>();
                metas.Add(new MigrationMeta
                {
                    Version = attr.Version,
                    Description = attr.Description,
                    Type = type,
                    IsDestructive = destructive != null,
                    DestructiveReason = destructive?.Reason
                });
            }
        }

        // Guard: two migrations cannot share a version number
        var duplicates = metas.GroupBy(m => m.Version).Where(g => g.Count() > 1).ToList();
        if (duplicates.Any())
            throw new MigrationConflictException(duplicates.First().Key);

        return metas.OrderBy(m => m.Version);
    }

    private sealed class MigrationMeta
    {
        public long Version { get; init; }
        public string Description { get; init; } = string.Empty;
        public Type Type { get; init; } = null!;
        public bool IsDestructive { get; init; }
        public string? DestructiveReason { get; init; }
    }
}
