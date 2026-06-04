using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using FluentORM.Core.Compiler;
using FluentORM.Core.Mapping;
using FluentORM.Core.Query;
using FluentORM.Sqlite;
using Xunit;
using FluentAssertions;

namespace FluentORM.Core.Tests;

public class WhereClauseTests
{
    private readonly EntityMapRegistry _registry;
    private readonly SqlCompiler _compiler;

    public WhereClauseTests()
    {
        _registry = new EntityMapRegistry();
        _registry.GetDescriptor<Pest>();
        _registry.GetDescriptor<Scouting>();
        _compiler = new SqlCompiler(_registry, new SqliteDialect());
    }

    [Fact]
    public void WhereNotIn_GeneratesNotInSql()
    {
        var desc = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(desc);
        builder.WhereClauses.Add(new WhereNotInClause(
            (Expression<Func<Pest, int>>)(p => p.Id),
            new object?[] { 1, 2, 3 }));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("NOT IN");
        compiled.Parameters.Should().HaveCount(3);
    }

    [Fact]
    public void WhereIn_GeneratesInSql()
    {
        var desc = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(desc);
        builder.WhereClauses.Add(new WhereInClause(
            (Expression<Func<Pest, int>>)(p => p.Id),
            new object?[] { 10, 20 }));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain(" IN (");
        compiled.Sql.Should().NotContain("NOT IN");
    }

    [Fact]
    public void WhereBetween_GeneratesBetweenSql()
    {
        var desc = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(desc);
        builder.WhereClauses.Add(new WhereBetweenClause(
            (Expression<Func<Pest, int>>)(p => p.RiskLevel), 2, 5));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("BETWEEN");
        compiled.Parameters.Values.Should().Contain(2);
        compiled.Parameters.Values.Should().Contain(5);
    }

    [Fact]
    public void WhereNull_GeneratesIsNullSql()
    {
        var desc = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(desc);
        builder.WhereClauses.Add(new WhereNullClause(
            (Expression<Func<Pest, DateTime?>>)(p => p.DeletedAt), isNull: true));

        var compiled = _compiler.Compile(builder);
        compiled.Sql.Should().Contain("IS NULL");
    }

    [Fact]
    public void WhereNotNull_GeneratesIsNotNullSql()
    {
        var desc = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(desc);
        builder.WhereClauses.Add(new WhereNullClause(
            (Expression<Func<Pest, DateTime?>>)(p => p.DeletedAt), isNull: false));

        var compiled = _compiler.Compile(builder);
        compiled.Sql.Should().Contain("IS NOT NULL");
    }

    [Fact]
    public void WhereRaw_InjectsSqlFragment()
    {
        var desc = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(desc);
        builder.WhereClauses.Add(new RawWhereClause("YEAR(p.CreatedAt) = {0}", new object?[] { 2024 }));

        var compiled = _compiler.Compile(builder);
        compiled.Sql.Should().Contain("YEAR(p.CreatedAt)");
        compiled.Parameters.Values.Should().Contain(2024);
    }

    [Fact]
    public void WhereExists_GeneratesExistsSql()
    {
        var pestDesc = _registry.GetDescriptor<Pest>();
        var scoutingDesc = _registry.GetDescriptor<Scouting>();

        var builder = new QueryBuilder<Pest>(pestDesc);
        var subBuilder = new QueryBuilder<Scouting>(scoutingDesc);
        builder.WhereClauses.Add(new WhereExistsClause(subBuilder));

        var compiled = _compiler.Compile(builder);
        compiled.Sql.Should().Contain("EXISTS");
    }
}
