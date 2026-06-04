using System;
using System.Linq.Expressions;
using FluentORM.Core.Compiler;
using FluentORM.Core.Mapping;
using FluentORM.Core.Query;
using FluentORM.Sqlite;
using Xunit;
using FluentAssertions;

namespace FluentORM.Core.Tests;

public class ExpressionCompilerTests
{
    private readonly EntityMapRegistry _registry;
    private readonly SqlCompiler _compiler;

    public ExpressionCompilerTests()
    {
        _registry = new EntityMapRegistry();
        _registry.GetDescriptor<Pest>();
        _compiler = new SqlCompiler(_registry, new SqliteDialect());
    }

    [Fact]
    public void Compile_CapturedVariable_CorrectlyParameterizes()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);

        int minRisk = 3;
        builder.WhereClauses.Add(new ExpressionWhereClause(
            (Expression<Func<Pest, bool>>)(p => p.RiskLevel > minRisk)));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("RiskLevel");
        // ParameterBag lowercases hint names. Captured variable is "minRisk" → "@minrisk"
        compiled.Parameters.Should().ContainKey("@minrisk").WhoseValue.Should().Be(3);
    }

    [Fact]
    public void Compile_CapturedVariable_ChangedValue_ExtractsFreshValue()
    {
        // Verifies plan cache re-extracts values correctly
        var descriptor = _registry.GetDescriptor<Pest>();

        int threshold = 3;
        var builder1 = new QueryBuilder<Pest>(descriptor);
        builder1.WhereClauses.Add(new ExpressionWhereClause(
            (Expression<Func<Pest, bool>>)(p => p.RiskLevel > threshold)));
        var compiled1 = _compiler.Compile(builder1);

        threshold = 7; // change the captured variable
        var builder2 = new QueryBuilder<Pest>(descriptor);
        builder2.WhereClauses.Add(new ExpressionWhereClause(
            (Expression<Func<Pest, bool>>)(p => p.RiskLevel > threshold)));
        var compiled2 = _compiler.Compile(builder2);

        // Both should produce same SQL structure (cache hit, same CAPTURE fingerprint)
        compiled1.Sql.Should().Be(compiled2.Sql);
        // Params reflect the values at their respective compilation/extraction times
        // compiled1 was compiled when threshold=3
        compiled1.Parameters["@threshold"].Should().Be(3);
        // compiled2: plan cache warm path re-extracts from builder2's expression, threshold=7
        compiled2.Parameters["@threshold"].Should().Be(7);
    }

    [Fact]
    public void Compile_StringContains_ProducesLike()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);

        builder.WhereClauses.Add(new ExpressionWhereClause(
            (Expression<Func<Pest, bool>>)(p => p.Name.Contains("moth"))));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("LIKE");
        compiled.Parameters.Values.Should().Contain(v => v != null && v.ToString()!.Contains("moth"));
    }

    [Fact]
    public void Compile_NullCheck_ProducesIsNull()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);

        builder.WhereClauses.Add(new ExpressionWhereClause(
            (Expression<Func<Pest, bool>>)(p => p.DeletedAt == null)));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("IS NULL");
        compiled.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Compile_AndCondition_ProducesAndConnector()
    {
        var descriptor = _registry.GetDescriptor<Pest>();
        var builder = new QueryBuilder<Pest>(descriptor);

        builder.WhereClauses.Add(new ExpressionWhereClause(
            (Expression<Func<Pest, bool>>)(p => p.RiskLevel > 2 && p.RiskLevel < 8)));

        var compiled = _compiler.Compile(builder);

        compiled.Sql.Should().Contain("AND");
    }

    [Fact]
    public void PlanCache_SameStructure_ReturnsCachedSql()
    {
        var descriptor = _registry.GetDescriptor<Pest>();

        // Build two structurally identical queries
        int val1 = 1;
        var b1 = new QueryBuilder<Pest>(descriptor);
        b1.WhereClauses.Add(new ExpressionWhereClause(
            (Expression<Func<Pest, bool>>)(p => p.RiskLevel > val1)));
        var compiled1 = _compiler.Compile(b1);

        int val2 = 99;
        var b2 = new QueryBuilder<Pest>(descriptor);
        b2.WhereClauses.Add(new ExpressionWhereClause(
            (Expression<Func<Pest, bool>>)(p => p.RiskLevel > val2)));
        var compiled2 = _compiler.Compile(b2);

        // Same SQL shape
        compiled1.Sql.Should().Be(compiled2.Sql);
        // Different param values
        // Both have the same structural fingerprint (CAPTURE → same plan key)
        // Values re-extracted from current builder on each call
        compiled1.Parameters.Values.Should().Contain(v => Convert.ToInt32(v) == 1);
        compiled2.Parameters.Values.Should().Contain(v => Convert.ToInt32(v) == 99);
    }
}
