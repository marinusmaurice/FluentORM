using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Compiler;
using FluentORM.Core.Configuration;
using FluentORM.Core.Execution;
using FluentORM.Core.Extensions;
using FluentORM.Core.Interceptors;
using FluentORM.Core.Mapping;
using FluentORM.Core.Mutations;
using FluentORM.Migrations.Engine;
using FluentORM.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace FluentORM.Testing;

public sealed class DbTest : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FluentDb _db;
    private readonly QueryMonitor _monitor;
    private DateTime? _frozenTime;

    private DbTest(SqliteConnection connection, FluentDb db)
    {
        _connection = connection;
        _db = db;
        _monitor = new QueryMonitor();
    }

    public static async Task<DbTest> CreateAsync<TContext>(
        Action<FluentOrmBuilder>? configure = null,
        CancellationToken ct = default)
    {
        var cacheKey = Guid.NewGuid().ToString("N");
        var cs = $"Data Source={cacheKey};Mode=Memory;Cache=Shared";
        var conn = new SqliteConnection(cs);
        await conn.OpenAsync(ct);

        // Enable foreign keys
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var factory = new SharedConnectionFactory(conn);
        var dialect = new SqliteDialect();
        var registry = new EntityMapRegistry();
        var options = new FluentOrmOptions();
        var compiler = new SqlCompiler(registry, dialect);
        var mutationCompiler = new MutationCompiler(dialect);
        var executor = new DbExecutor(factory, null, dialect, options);

        var db = new FluentDb(registry, compiler, executor, mutationCompiler, dialect, options, factory);

        return new DbTest(conn, db);
    }

    public IFluentDb Db => _db;

    public QueryMonitor MonitorQueries() => _monitor;

    public async Task SeedAsync<T>(T entity, CancellationToken ct = default) where T : class
        => await _db.InsertAsync(entity, ct);

    public async Task SeedManyAsync<T>(IEnumerable<T> entities, CancellationToken ct = default) where T : class
        => await _db.BulkInsertAsync(entities, ct);

    public IFluentDb ForTenant(string tenantId) => _db.ForTenant(tenantId);

    public void FreezeTime(DateTime time) => _frozenTime = time;

    public DateTime Now => _frozenTime ?? DateTime.UtcNow;

    public async Task ApplyMigrationsAsync(CancellationToken ct = default)
    {
        var engine = new MigrationEngine(
            new SharedConnectionFactory(_connection),
            new SqliteDialect(),
            _db.Registry);
        await engine.ApplyAsync(allowDestructive: true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync();
        await _connection.DisposeAsync();
    }
}

internal sealed class SharedConnectionFactory : IConnectionFactory
{
    private readonly IDbConnection _connection;
    public SharedConnectionFactory(IDbConnection connection) => _connection = connection;

    public Task<IDbConnection> OpenAsync(CancellationToken ct = default)
        => Task.FromResult<IDbConnection>(new NonDisposableConnection(_connection));
}

/// <summary>Wraps a connection and swallows Dispose() so the shared connection stays open.</summary>
internal sealed class NonDisposableConnection : IDbConnection
{
    private readonly IDbConnection _inner;
    public NonDisposableConnection(IDbConnection inner) => _inner = inner;

    public string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
    public int ConnectionTimeout => _inner.ConnectionTimeout;
    public string Database => _inner.Database;
    public ConnectionState State => _inner.State;

    public IDbTransaction BeginTransaction() => _inner.BeginTransaction();
    public IDbTransaction BeginTransaction(IsolationLevel il) => _inner.BeginTransaction(il);
    public void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
    public void Close() { } // swallow
    public IDbCommand CreateCommand() => _inner.CreateCommand();
    public void Open() => _inner.Open();
    public void Dispose() { } // swallow — keep connection alive
}
