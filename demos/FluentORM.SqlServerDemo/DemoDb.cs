using FluentORM.Core;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Compiler;
using FluentORM.Core.Configuration;
using FluentORM.Core.Execution;
using FluentORM.Core.Mapping;
using FluentORM.Core.Mutations;
using FluentORM.SqlServer;
using Microsoft.Data.SqlClient;

namespace FluentORM.SqlServerDemo;

public sealed class DemoDb : IAsyncDisposable
{
    public readonly FluentDb Db;

    private DemoDb(FluentDb db)
    {
        Db = db;
    }

    public static async Task<DemoDb> CreateAsync(string connectionString)
    {
        var factory = new SqlServerConnectionFactory(connectionString);
        var dialect = new SqlServerDialect();
        var registry = new EntityMapRegistry();

        registry.GetDescriptor<Farm>();
        registry.GetDescriptor<Field>();
        registry.GetDescriptor<Pest>();
        registry.GetDescriptor<Inspection>();
        registry.GetDescriptor<SprayEvent>();
        registry.GetDescriptor<Product>();
        registry.GetDescriptor<Core.Abstractions.AuditEntry>();

        var options = new FluentOrmOptions
        {
            SlowQueryThreshold = TimeSpan.FromMilliseconds(500)
        };

        var compiler = new SqlCompiler(registry, dialect);
        var mutCompiler = new MutationCompiler(dialect);
        var executor = new DbExecutor(factory, null, dialect, options);
        var db = new FluentDb(registry, compiler, executor, mutCompiler, dialect, options, factory);

        await CreateSchemaAsync(connectionString);

        return new DemoDb(db);
    }

    public IFluentDb ForTenant(string tenantId) => Db.ForTenant(tenantId);

    private static async Task CreateSchemaAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        // Drop tables in FK order so the demo is repeatable
        var drop = @"
IF OBJECT_ID('Inspections',   'U') IS NOT NULL DROP TABLE Inspections;
IF OBJECT_ID('SprayEvents',   'U') IS NOT NULL DROP TABLE SprayEvents;
IF OBJECT_ID('Fields',       'U') IS NOT NULL DROP TABLE Fields;
IF OBJECT_ID('Pests',        'U') IS NOT NULL DROP TABLE Pests;
IF OBJECT_ID('Products',     'U') IS NOT NULL DROP TABLE Products;
IF OBJECT_ID('Farms',        'U') IS NOT NULL DROP TABLE Farms;
IF OBJECT_ID('__AuditEntries','U') IS NOT NULL DROP TABLE __AuditEntries;
";
        await using (var cmd = new SqlCommand(drop, conn))
            await cmd.ExecuteNonQueryAsync();

        var ddl = @"
CREATE TABLE Farms (
    Id          INT           IDENTITY(1,1) PRIMARY KEY,
    Name        NVARCHAR(500) NOT NULL,
    Location    NVARCHAR(500) NOT NULL DEFAULT '',
    Active      BIT           NOT NULL DEFAULT 1,
    TenantId    NVARCHAR(100) NOT NULL,
    DeletedAt   DATETIME2     NULL,
    HectareSize INT           NOT NULL DEFAULT 0
);

CREATE TABLE Fields (
    Id           INT           IDENTITY(1,1) PRIMARY KEY,
    Name         NVARCHAR(500) NOT NULL,
    FarmId       INT           NOT NULL REFERENCES Farms(Id),
    AreaHectares INT           NOT NULL DEFAULT 0,
    CropType     NVARCHAR(200) NOT NULL DEFAULT '',
    TenantId     NVARCHAR(100) NOT NULL,
    DeletedAt    DATETIME2     NULL
);

CREATE TABLE Pests (
    Id        INT           IDENTITY(1,1) PRIMARY KEY,
    Name      NVARCHAR(500) NOT NULL,
    RiskLevel INT           NOT NULL DEFAULT 1,
    Category  NVARCHAR(200) NULL,
    TenantId  NVARCHAR(100) NOT NULL
);

CREATE TABLE Inspections (
    Id            INT           IDENTITY(1,1) PRIMARY KEY,
    FieldId       INT           NOT NULL REFERENCES Fields(Id),
    PestId        INT           NOT NULL REFERENCES Pests(Id),
    SeverityScore FLOAT         NOT NULL DEFAULT 0,
    Notes         NVARCHAR(MAX) NOT NULL DEFAULT '',
    InspectedAt   DATETIME2     NOT NULL,
    ResolvedAt    DATETIME2     NULL,
    TenantId      NVARCHAR(100) NOT NULL
);

CREATE TABLE SprayEvents (
    Id        INT           IDENTITY(1,1) PRIMARY KEY,
    FieldId   INT           NOT NULL REFERENCES Fields(Id),
    Chemical  NVARCHAR(200) NOT NULL,
    CostZAR   DECIMAL(18,4) NOT NULL DEFAULT 0,
    AppliedAt DATETIME2     NOT NULL,
    TenantId  NVARCHAR(100) NOT NULL
);

CREATE TABLE Products (
    Id         INT           IDENTITY(1,1) PRIMARY KEY,
    ExternalId NVARCHAR(200) NOT NULL,
    Name       NVARCHAR(500) NOT NULL,
    Price      DECIMAL(18,4) NOT NULL DEFAULT 0,
    Stock      INT           NOT NULL DEFAULT 0,
    Version    INT           NOT NULL DEFAULT 1,
    TenantId   NVARCHAR(100) NOT NULL,
    CONSTRAINT UQ_Products_ExternalId UNIQUE (ExternalId)
);

CREATE TABLE __AuditEntries (
    Id         INT           IDENTITY(1,1) PRIMARY KEY,
    TenantId   NVARCHAR(100) NOT NULL DEFAULT '',
    UserId     NVARCHAR(200) NOT NULL DEFAULT '',
    Operation  NVARCHAR(50)  NOT NULL,
    TableName  NVARCHAR(200) NOT NULL,
    PrimaryKey NVARCHAR(200) NOT NULL,
    OldValues  NVARCHAR(MAX) NULL,
    NewValues  NVARCHAR(MAX) NULL,
    Timestamp  DATETIME2     NOT NULL,
    IpAddress  NVARCHAR(100) NULL
);
";
        await using (var cmd = new SqlCommand(ddl, conn))
            await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}
