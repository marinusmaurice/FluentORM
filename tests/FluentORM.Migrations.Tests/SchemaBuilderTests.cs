using System;
using System.Linq;
using FluentORM.Core.Attributes;
using FluentORM.Core.Mapping;
using FluentORM.Migrations.Schema;
using FluentORM.Sqlite;
using Xunit;
using FluentAssertions;

namespace FluentORM.Migrations.Tests;

[Table("Fields")]
public class FieldEntity
{
    [PrimaryKey(autoIncrement: true)]
    public int Id { get; set; }
    [NotNull]
    public string Name { get; set; } = string.Empty;
    [TenantKey]
    public string TenantId { get; set; } = string.Empty;
    [SoftDelete]
    public DateTime? DeletedAt { get; set; }
    [RowVersion]
    public int Version { get; set; }
}

public sealed class SchemaBuilderTests
{
    private readonly EntityMapRegistry _registry;
    private readonly SqliteDialect _dialect;
    private readonly SchemaBuilder _schema;

    public SchemaBuilderTests()
    {
        _registry = new EntityMapRegistry();
        _registry.GetDescriptor<FieldEntity>();
        _registry.GetDescriptor<Crop>();
        _dialect = new SqliteDialect();
        _schema = new SchemaBuilder(_dialect, _registry);
    }

    [Fact]
    public void CreateTable_GeneratesCorrectSql()
    {
        _schema.CreateTable<FieldEntity>(t =>
        {
            t.PrimaryKey(f => f.Id).AutoIncrement();
            t.Column(f => f.Name).NotNull().MaxLength(200);
            t.Column(f => f.TenantId).NotNull().IsTenantKey();
        });

        var sql = _schema.ToSql();
        sql.Should().Contain("CREATE TABLE");
        sql.Should().Contain("Fields");
        sql.Should().Contain("AUTOINCREMENT");
    }

    [Fact]
    public void AddIndex_GeneratesIndexSql()
    {
        _schema.AddIndex<FieldEntity>(f => f.TenantId);
        var sql = _schema.ToSql();
        sql.Should().Contain("CREATE");
        sql.Should().Contain("INDEX");
        sql.Should().Contain("TenantId");
    }

    [Fact]
    public void AddUniqueIndex_GeneratesUniqueIndexSql()
    {
        _schema.AddUniqueIndex<FieldEntity>(f => f.TenantId);
        var sql = _schema.ToSql();
        sql.Should().Contain("UNIQUE");
        sql.Should().Contain("INDEX");
    }

    [Fact]
    public void DropTable_GeneratesDropSql()
    {
        _schema.DropTable<FieldEntity>();
        var sql = _schema.ToSql();
        sql.Should().Contain("DROP TABLE");
        sql.Should().Contain("Fields");
    }

    [Fact]
    public void RenameColumn_GeneratesAlterSql()
    {
        _schema.RenameColumn<FieldEntity>("old_name", "Name");
        var sql = _schema.ToSql();
        sql.Should().Contain("RENAME COLUMN");
    }

    [Fact]
    public void RawSql_AppendsToStatements()
    {
        _schema.Sql("UPDATE Fields SET Version = 1 WHERE Version IS NULL");
        var sql = _schema.ToSql();
        sql.Should().Contain("UPDATE Fields SET Version = 1");
    }

    [Fact]
    public void MultipleStatements_AllIncluded()
    {
        _schema.CreateTable<Crop>(t =>
        {
            t.PrimaryKey(c => c.Id).AutoIncrement();
            t.Column(c => c.Name).NotNull();
        });
        _schema.AddIndex<Crop>(c => c.Season);
        _schema.Sql("INSERT INTO Crops (Name, Season) SELECT Name, Season FROM OldCrops");

        var statements = _schema.Statements;
        statements.Should().HaveCount(3);
    }

    [Fact]
    public void AddColumn_WithDefaultThenNotNull_Succeeds()
    {
        // Default must come before NotNull
        Action act = () => _schema.AddColumn<Crop>(c => c.Season).Default(0).NotNull();
        act.Should().NotThrow();
    }

    [Fact]
    public void AddColumn_NotNull_WithoutDefault_Throws()
    {
        // AddColumn with NotNull() requires Default() first
        Action act = () => new SchemaBuilder(_dialect, _registry)
            .AddColumn<Crop>(c => c.Season)
            .NotNull(); // no Default() called — should throw

        act.Should().Throw<Core.Exceptions.NotNullWithoutDefaultException>()
            .Which.Column.Should().Be("Season");
    }

    [Fact]
    public void SqlServerDialect_CreateTable_UsesBrackets()
    {
        var sqlServerDialect = new SqlServer.SqlServerDialect();
        var schema = new SchemaBuilder(sqlServerDialect, _registry);
        schema.CreateTable<FieldEntity>(t =>
        {
            t.PrimaryKey(f => f.Id).AutoIncrement();
            t.Column(f => f.Name).NotNull();
        });
        var sql = schema.ToSql();
        sql.Should().Contain("[Fields]");
        sql.Should().Contain("IDENTITY(1,1)");
    }
}
