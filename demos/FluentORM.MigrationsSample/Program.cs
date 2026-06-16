using System;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Exceptions;
using FluentORM.Core.Mapping;
using FluentORM.Core.Migrations;
using FluentORM.Migrations.Engine;
using FluentORM.Sqlite;
using Microsoft.Data.Sqlite;

namespace FluentORM.MigrationsSample;

internal static class Program
{
    private static async Task Main()
    {
        // Fresh in-memory SQLite DB each run, so the demo always starts from "no migrations applied".
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var factory = new PersistentConnectionFactory(conn);
        var dialect = new SqliteDialect();
        var registry = new EntityMapRegistry();
        registry.GetDescriptor<Farm>();
        registry.GetDescriptor<Field>();

        var engine = new MigrationEngine(factory, dialect, registry,
            assemblies: [Assembly.GetExecutingAssembly()]);

        Section("1. Status before anything is applied");
        await PrintStatus(engine);

        Section("2. Preview — SQL that WOULD run, without executing it");
        Console.WriteLine(await engine.PreviewAsync());

        Section("3. Apply safe migrations (allowDestructive: false)");
        Console.WriteLine("Engine stops automatically once it reaches a [DESTRUCTIVE] migration.");
        await engine.ApplyAsync(allowDestructive: false);
        await PrintStatus(engine);

        Section("4. Try to apply again — only the destructive migration is left");
        try
        {
            await engine.ApplyAsync(allowDestructive: false);
        }
        catch (DestructiveMigrationException ex)
        {
            Console.WriteLine($"  Blocked as expected: {ex.Message}");
        }

        Section("5. Roll back the unique-index migration");
        Console.WriteLine("RollbackToAsync(20240601005) rolls back migration 006 only — it's the most");
        Console.WriteLine("recently applied one, so it can be undone without touching anything older.");
        Console.WriteLine("(We don't roll back further: SQLite's DropColumn/DropForeignKey are no-ops");
        Console.WriteLine(" without a full table rebuild, so undoing column-adding migrations on SQLite");
        Console.WriteLine(" wouldn't actually reverse them. DROP INDEX has no such limitation.)");
        await engine.RollbackToAsync(20240601005);
        await PrintStatus(engine);

        Section("6. Re-apply the migration we just rolled back");
        await engine.ApplyAsync(allowDestructive: false);
        await PrintStatus(engine);

        Section("7. Apply the destructive migration explicitly");
        await engine.ApplyAsync(allowDestructive: true);
        await PrintStatus(engine);

        Section("8. Try to roll back the destructive migration");
        try
        {
            await engine.RollbackAsync();
        }
        catch (IrreversibleMigrationException ex)
        {
            Console.WriteLine($"  Rollback refused: {ex.Message}");
        }

        Section("9. Validate checksums — detects migrations edited after being applied");
        await engine.ValidateChecksumsAsync();
        Console.WriteLine("  All applied migrations match their recorded checksum.");

        Section("10. Inspect the resulting schema");
        await PrintSchema(conn, "Farms");
        await PrintSchema(conn, "Fields");

        await conn.CloseAsync();
        await conn.DisposeAsync();
    }

    private static async Task PrintStatus(MigrationEngine engine)
    {
        var status = await engine.StatusAsync();

        Console.WriteLine($"  Applied ({status.Applied.Count}):");
        foreach (var m in status.Applied)
            Console.WriteLine($"    [{m.Version}] {m.Description}  (applied {m.AppliedAt:u})");

        Console.WriteLine($"  Pending ({status.Pending.Count}):");
        foreach (var m in status.Pending)
            Console.WriteLine($"    [{m.Version}] {m.Description}");

        Console.WriteLine($"  Destructive pending ({status.DestructivePending.Count}):");
        foreach (var m in status.DestructivePending)
            Console.WriteLine($"    [{m.Version}] {m.Description}  -- {m.DestructiveReason}");
    }

    private static async Task PrintSchema(SqliteConnection conn, string table)
    {
        Console.WriteLine($"  {table}:");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            var type = reader.GetString(2);
            var notNull = reader.GetInt32(3) == 1;
            Console.WriteLine($"    {name,-15} {type,-10} {(notNull ? "NOT NULL" : "")}");
        }
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════");
        Console.WriteLine($" {title}");
        Console.WriteLine("═══════════════════════════════════════");
    }
}

// SQLite connection wrapper that survives multiple "OpenAsync" calls from
// the engine without actually closing the in-memory database between them.
internal sealed class PersistentConnectionFactory : IConnectionFactory
{
    private readonly IDbConnection _conn;
    public PersistentConnectionFactory(IDbConnection conn) => _conn = conn;
    public Task<IDbConnection> OpenAsync(CancellationToken ct = default)
        => Task.FromResult<IDbConnection>(new NoDisposeWrapper(_conn));
}

internal sealed class NoDisposeWrapper(IDbConnection inner) : IDbConnection
{
    public string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value!; }
    public int ConnectionTimeout => inner.ConnectionTimeout;
    public string Database => inner.Database;
    public ConnectionState State => inner.State;
    public IDbTransaction BeginTransaction() => inner.BeginTransaction();
    public IDbTransaction BeginTransaction(IsolationLevel il) => inner.BeginTransaction(il);
    public void ChangeDatabase(string db) => inner.ChangeDatabase(db);
    public void Close() { }
    public IDbCommand CreateCommand() => inner.CreateCommand();
    public void Open() => inner.Open();
    public void Dispose() { }
}
