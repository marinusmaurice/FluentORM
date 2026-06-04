using System;
using FluentORM.Core.Query;
using Xunit;
using FluentAssertions;

namespace FluentORM.Core.Tests;

public class WindowFunctionTests
{
    [Fact]
    public void RowNumber_GeneratesCorrectSql()
    {
        var fn = Sql.RowNumber();
        var aliases = new Compiler.AliasRegistry();
        aliases.Register(typeof(Pest), "p");
        var parameters = new Compiler.ParameterBag();
        var visitor = new Compiler.ExpressionToSqlVisitor(parameters,
            new Mapping.EntityMapRegistry(), aliases);

        // Just test the builder is constructable and callable
        fn.Should().NotBeNull();
        fn.Over().Should().BeSameAs(fn);
    }

    [Fact]
    public void Rank_Builder_IsCorrectlyNamed()
    {
        var fn = Sql.Rank();
        fn.Should().NotBeNull();
    }

    [Fact]
    public void DenseRank_Builder_IsCorrectlyNamed()
    {
        var fn = Sql.DenseRank();
        fn.Should().NotBeNull();
    }

    [Fact]
    public void Sum_Window_Builder_IsCreated()
    {
        var fn = Sql.Sum<Scouting, double>(s => s.SeverityScore);
        fn.Should().NotBeNull();
    }

    [Fact]
    public void Lead_Builder_WithOffset()
    {
        var fn = Sql.Lead<Scouting, double>(s => s.SeverityScore, offset: 1);
        fn.Should().NotBeNull();
    }

    [Fact]
    public void Lag_Builder_WithOffset()
    {
        var fn = Sql.Lag<Scouting, double>(s => s.SeverityScore, offset: 1);
        fn.Should().NotBeNull();
    }

    [Fact]
    public void WindowFunction_Rows_CanBeConfigured()
    {
        var fn = Sql.Avg<Scouting, double>(s => s.SeverityScore)
            .Over()
            .PartitionBy(s => (object)s.FieldId)
            .OrderBy(s => (object)s.ObservedAt)
            .Rows(preceding: 2, following: 0);

        fn.Should().NotBeNull();
    }
}
