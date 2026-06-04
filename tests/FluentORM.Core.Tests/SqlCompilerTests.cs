using FluentORM.Core.Compiler;
using FluentORM.Core.Mapping;
using FluentORM.Core.Query;
using FluentORM.Sqlite;
using Xunit;
using FluentAssertions;

namespace FluentORM.Core.Tests;

public class SqlCompilerTests
{
    private readonly EntityMapRegistry _registry;
    private readonly SqlCompiler _compiler;

    public SqlCompilerTests()
    {
        _registry = new EntityMapRegistry();
        _registry.GetDescriptor<Pest>();
        _registry.GetDescriptor<Scouting>();
        _compiler = new SqlCompiler(_registry, new SqliteDialect());
    }

    [Fact]
    public void Compile_BasicSelect_GeneratesCorrectSql()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("SELECT");
        compiled.Sql.Should().Contain("FROM");
        compiled.Sql.Should().Contain("Pests");
    }

    [Fact]
    public void Compile_WhereClause_IncludesCondition()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);
        builder.WhereClauses.Add(new ExpressionWhereClause(
            (System.Linq.Expressions.Expression<System.Func<Pest, bool>>)(p => p.RiskLevel > 3)));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("WHERE");
        compiled.Sql.Should().ContainAny(">", "RiskLevel");
    }

    [Fact]
    public void Compile_TenantWhereClause_InjectsTenantFilter()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);
        builder.WhereClauses.Add(new TenantWhereClause(() => "farm-001", "TenantId", "p"));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("WHERE");
        compiled.Parameters.Should().ContainKey("@tenantId");
        compiled.Parameters["@tenantId"].Should().Be("farm-001");
    }

    [Fact]
    public void Compile_SoftDeleteClause_AddsIsNullFilter()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);
        builder.WhereClauses.Add(new SoftDeleteWhereClause("DeletedAt", "p"));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("IS NULL");
    }

    [Fact]
    public void Compile_OrderBy_GeneratesOrderByClause()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);
        builder.OrderByClauses.Add(new OrderByClause
        {
            Expression = (System.Linq.Expressions.Expression<System.Func<Pest, object>>)(p => p.RiskLevel),
            Descending = true,
            IsThenBy = false
        });

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("ORDER BY");
        compiled.Sql.Should().Contain("DESC");
    }

    [Fact]
    public void Compile_Paging_GeneratesLimitOffset()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);
        builder.Skip = 10;
        builder.Take = 5;

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().ContainAny("LIMIT", "OFFSET", "FETCH");
    }
}
