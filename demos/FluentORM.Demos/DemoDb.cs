using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Cache;
using FluentORM.Core.Compiler;
using FluentORM.Core.Configuration;
using FluentORM.Core.Execution;
using FluentORM.Core.Mapping;
using FluentORM.Core.Mutations;
using FluentORM.Sqlite;
using Microsoft.Data.Sqlite;

namespace FluentORM.Demos;

/// <summary>
/// Shared in-memory SQLite database for all demos.
/// Creates the schema once and provides IFluentDb instances per tenant.
/// </summary>
public sealed class DemoDb : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    public readonly FluentDb Db;

    private DemoDb(SqliteConnection conn, FluentDb db)
    {
        _conn = conn;
        Db = db;
    }

    public static async Task<DemoDb> CreateAsync(string? dbName = null)
    {
        var cs = dbName != null
            ? $"Data Source={dbName};Mode=Memory;Cache=Shared"
            : "Data Source=:memory:";

        var conn = new SqliteConnection(cs);
        await conn.OpenAsync();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync();
        }

        var factory = new PersistentConnectionFactory(conn);
        var dialect = new SqliteDialect();
        var registry = new EntityMapRegistry();

        // Register all entities
        registry.GetDescriptor<Farm>();
        registry.GetDescriptor<Field>();
        registry.GetDescriptor<Pest>();
        registry.GetDescriptor<Inspection>();
        registry.GetDescriptor<SprayEvent>();
        registry.GetDescriptor<Product>();
        registry.GetDescriptor<Core.Abstractions.AuditEntry>();

        var options = new FluentOrmOptions
        {
            SlowQueryThreshold = TimeSpan.FromMilliseconds(200)
        };

        var compiler = new SqlCompiler(registry, dialect);
        var mutCompiler = new MutationCompiler(dialect);
        var executor = new DbExecutor(factory, null, dialect, options);
        var db = new FluentDb(registry, compiler, executor, mutCompiler, dialect, options, factory);

        await CreateSchemaAsync(conn);

        return new DemoDb(conn, db);
    }

    public IFluentDb ForTenant(string tenantId) => Db.ForTenant(tenantId);

    private static async Task CreateSchemaAsync(SqliteConnection conn)
    {
        var ddl = @"
CREATE TABLE IF NOT EXISTS Farms (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT    NOT NULL,
    Location    TEXT    NOT NULL DEFAULT '',
    Active      INTEGER NOT NULL DEFAULT 1,
    TenantId    TEXT    NOT NULL,
    DeletedAt   TEXT    NULL,
    HectareSize INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Fields (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Name         TEXT    NOT NULL,
    FarmId       INTEGER NOT NULL,
    AreaHectares INTEGER NOT NULL DEFAULT 0,
    CropType     TEXT    NOT NULL DEFAULT '',
    TenantId     TEXT    NOT NULL,
    DeletedAt    TEXT    NULL,
    FOREIGN KEY (FarmId) REFERENCES Farms(Id)
);

CREATE TABLE IF NOT EXISTS Pests (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name      TEXT    NOT NULL,
    RiskLevel INTEGER NOT NULL DEFAULT 1,
    Category  TEXT    NULL,
    TenantId  TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS Inspections (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    FieldId       INTEGER NOT NULL,
    PestId        INTEGER NOT NULL,
    SeverityScore REAL    NOT NULL DEFAULT 0,
    Notes         TEXT    NOT NULL DEFAULT '',
    InspectedAt   TEXT    NOT NULL,
    ResolvedAt    TEXT    NULL,
    TenantId      TEXT    NOT NULL,
    FOREIGN KEY (FieldId) REFERENCES Fields(Id),
    FOREIGN KEY (PestId)  REFERENCES Pests(Id)
);

CREATE TABLE IF NOT EXISTS SprayEvents (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    FieldId   INTEGER NOT NULL,
    Chemical  TEXT    NOT NULL,
    CostZAR   REAL    NOT NULL DEFAULT 0,
    AppliedAt TEXT    NOT NULL,
    TenantId  TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS Products (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    ExternalId TEXT    NOT NULL UNIQUE,
    Name       TEXT    NOT NULL,
    Price      REAL    NOT NULL DEFAULT 0,
    Stock      INTEGER NOT NULL DEFAULT 0,
    Version    INTEGER NOT NULL DEFAULT 1,
    TenantId   TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS __AuditEntries (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId   TEXT    NOT NULL DEFAULT '',
    UserId     TEXT    NOT NULL DEFAULT '',
    Operation  TEXT    NOT NULL,
    TableName  TEXT    NOT NULL,
    PrimaryKey TEXT    NOT NULL,
    OldValues  TEXT    NULL,
    NewValues  TEXT    NULL,
    Timestamp  TEXT    NOT NULL,
    IpAddress  TEXT    NULL
);
";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.CloseAsync();
        await _conn.DisposeAsync();
    }
}

internal sealed class PersistentConnectionFactory : IConnectionFactory
{
    private readonly IDbConnection _conn;
    public PersistentConnectionFactory(IDbConnection conn) => _conn = conn;
    public Task<IDbConnection> OpenAsync(CancellationToken ct = default)
        => Task.FromResult<IDbConnection>(new NoDisposeWrapper(_conn));
}

internal sealed class NoDisposeWrapper(IDbConnection inner) : IDbConnection
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
