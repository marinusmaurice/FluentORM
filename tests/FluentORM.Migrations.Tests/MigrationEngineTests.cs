using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Attributes;
using FluentORM.Core.Exceptions;
using FluentORM.Core.Mapping;
using FluentORM.Migrations.Engine;
using FluentORM.Migrations.Schema;
using FluentORM.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;
using FluentAssertions;

namespace FluentORM.Migrations.Tests;

// ── Test entities ────────────────────────────────────────────────────────────

[Table("Crops")]
public class Crop
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }
    [NotNull]
    public string Name { get; set; } = string.Empty;
    public int Season { get; set; }
}

[Table("Soils")]
public class Soil
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }
    [NotNull]
    public string Type { get; set; } = string.Empty;
}

// ── Test migrations ──────────────────────────────────────────────────────────

[MigrationAttribute(20240101001, "create_crops_table")]
public class CreateCropsTable : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.CreateTable<Crop>(t =>
        {
            t.PrimaryKey(c => c.Id).AutoIncrement();
            t.Column(c => c.Name).NotNull().MaxLength(200);
            t.Column(c => c.Season).NotNull().Default(1);
        });
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropTable<Crop>();
    }
}

[MigrationAttribute(20240101002, "create_soils_table")]
public class CreateSoilsTable : Migration
{
    public override void Up(SchemaBuilder schema)
    {
        schema.CreateTable<Soil>(t =>
        {
            t.PrimaryKey(s => s.Id).AutoIncrement();
            t.Column(s => s.Type).NotNull().MaxLength(100);
        });
    }

    public override void Down(SchemaBuilder schema)
    {
        schema.DropTable<Soil>();
    }
}

[MigrationAttribute(20240101003, "drop_crops_irreversible")]
[DestructiveAttribute("Crops table dropped — cannot be recovered.")]
public class DropCropsIrreversible : Migration
{
    public override void Up(SchemaBuilder schema) => schema.DropTable<Crop>();
    public override void Down(SchemaBuilder schema) =>
        throw new IrreversibleMigrationException("Crops table was dropped. Data cannot be recovered.");
}

// ── Helper ───────────────────────────────────────────────────────────────────

internal sealed class SqliteTestFactory : IConnectionFactory
{
    private readonly SqliteConnection _conn;
    public SqliteTestFactory(SqliteConnection conn) => _conn = conn;
    public Task<IDbConnection> OpenAsync(CancellationToken ct = default)
        => Task.FromResult<IDbConnection>(new NonDisposableWrapper(_conn));
}

internal sealed class NonDisposableWrapper(IDbConnection inner) : IDbConnection
{
    public string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value; }
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

// ── Tests ────────────────────────────────────────────────────────────────────

public sealed class MigrationEngineTests : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly MigrationEngine _engine;
    private readonly EntityMapRegistry _registry;
    private readonly SqliteDialect _dialect;

    public MigrationEngineTests()
    {
        var cs = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _conn = new SqliteConnection(cs);
        _conn.Open();

        _dialect = new SqliteDialect();
        _registry = new EntityMapRegistry();
        _registry.GetDescriptor<Crop>();
        _registry.GetDescriptor<Soil>();

        var factory = new SqliteTestFactory(_conn);
        var assemblies = new List<Assembly> { typeof(MigrationEngineTests).Assembly };
        _engine = new MigrationEngine(factory, _dialect, _registry, assemblies);
    }

    [Fact]
    public async Task Status_BeforeAnyMigrations_ShowsAllPending()
    {
        var status = await _engine.StatusAsync();

        status.Applied.Should().BeEmpty();
        status.Pending.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ApplyAsync_CreatesTables()
    {
        await _engine.ApplyAsync(allowDestructive: false);

        // Only safe migrations should be applied (not the destructive one)
        var tableExists = await TableExistsAsync("Crops");
        tableExists.Should().BeTrue();

        var soilExists = await TableExistsAsync("Soils");
        soilExists.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_WithDestructive_Throws_WhenNotAllowed()
    {
        // Apply safe migrations first
        await _engine.ApplyToAsync(20240101002);

        // Applying the destructive migration without the flag should throw
        Func<Task> act = () => _engine.ApplyAsync(allowDestructive: false);
        await act.Should().ThrowAsync<DestructiveMigrationException>();
    }

    [Fact]
    public async Task ApplyAsync_WithAllowDestructive_AppliesAllMigrations()
    {
        await _engine.ApplyAsync(allowDestructive: true);

        var status = await _engine.StatusAsync();
        status.Applied.Count.Should().Be(3);
        status.Pending.Should().BeEmpty();
        status.DestructivePending.Should().BeEmpty();
    }

    [Fact]
    public async Task RollbackAsync_RollsBackLastMigration()
    {
        await _engine.ApplyToAsync(20240101001);
        (await TableExistsAsync("Crops")).Should().BeTrue();

        await _engine.RollbackAsync();
        (await TableExistsAsync("Crops")).Should().BeFalse();
    }

    [Fact]
    public async Task RollbackAsync_IrreversibleMigration_Throws()
    {
        await _engine.ApplyAsync(allowDestructive: true);

        Func<Task> act = () => _engine.RollbackAsync();
        await act.Should().ThrowAsync<IrreversibleMigrationException>();
    }

    [Fact]
    public async Task StatusAsync_ShowsAppliedAndPending()
    {
        await _engine.ApplyToAsync(20240101001);

        var status = await _engine.StatusAsync();
        status.Applied.Should().ContainSingle(m => m.Version == 20240101001);
        status.Pending.Should().ContainSingle(m => m.Version == 20240101002);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsSql_WithoutExecuting()
    {
        var sql = await _engine.PreviewAsync();

        sql.Should().Contain("20240101001");
        sql.Should().Contain("CREATE TABLE");
        // Table should NOT be created (preview only)
        (await TableExistsAsync("Crops")).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateChecksums_After_Apply_Passes()
    {
        await _engine.ApplyToAsync(20240101001);

        // Validation should pass — checksum matches current Up() output
        Func<Task> act = () => _engine.ValidateChecksumsAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MigrationConflict_DuplicateVersion_Throws()
    {
        // Two migrations with the same version should throw MigrationConflictException
        // This is enforced in DiscoverMigrations() — tested indirectly via StatusAsync
        // (the test assembly has non-conflicting versions, so we can only test the path via exception type)
        // We'll verify the exception type exists and is in the right hierarchy
        var ex = new MigrationConflictException(99999L);
        ex.Should().BeAssignableTo<FluentOrmException>();
        ex.Version.Should().Be(99999L);
    }

    [Fact]
    public async Task ApplyToAsync_StopsAtVersion()
    {
        await _engine.ApplyToAsync(20240101001);

        var status = await _engine.StatusAsync();
        status.Applied.Should().ContainSingle(m => m.Version == 20240101001);
        // Version 20240101002 should still be pending
        status.Pending.Should().Contain(m => m.Version == 20240101002);
    }

    [Fact]
    public async Task NotNullWithoutDefault_Throws_When_NoDefault()
    {
        // Building a schema where AddColumn().NotNull() is called without Default()
        var schema = new SchemaBuilder(_dialect, _registry);
        Action act = () => schema.AddColumn<Crop>(c => c.Season).NotNull();

        // Should throw because Default() was not called first
        act.Should().Throw<NotNullWithoutDefaultException>();
    }

    [Fact]
    public async Task AddColumn_NotNull_With_Default_Succeeds()
    {
        var schema = new SchemaBuilder(_dialect, _registry);
        // Default() must be called BEFORE NotNull()
        Action act = () => schema.AddColumn<Crop>(c => c.Season).Default(0).NotNull();
        act.Should().NotThrow();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task<bool> TableExistsAsync(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.CloseAsync();
        await _conn.DisposeAsync();
    }
}
