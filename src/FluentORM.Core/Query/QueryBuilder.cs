using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using FluentORM.Core.Mapping;

namespace FluentORM.Core.Query;

public sealed class QueryBuilder<T> : IQueryDescriptor where T : class
{
    public EntityDescriptor<T> EntityDescriptor { get; }
    public List<JoinClause> Joins { get; } = new();
    public List<WhereClause> WhereClauses { get; } = new();
    public bool AllTenants { get; set; }
    public bool IncludeDeleted { get; set; }
    public bool OnlyDeletedFlag { get; set; }
    public ProjectionDescriptor? Projection { get; set; }
    public bool IsDistinct { get; set; }
    public List<GroupByClause> GroupByClauses { get; } = new();
    public List<HavingClause> HavingClauses { get; } = new();
    public List<OrderByClause> OrderByClauses { get; } = new();
    public int? Skip { get; set; }
    public int? Take { get; set; }
    public List<CteDescriptor> Ctes { get; } = new();
    public bool WithDiagnosticsFlag { get; set; }
    public CachingOptions? CacheOptions { get; set; }
    public LambdaExpression? SelectProjection { get; set; }

    public QueryBuilder(EntityDescriptor<T> descriptor) => EntityDescriptor = descriptor;
}

public sealed class JoinClause
{
    public required string JoinedTableName { get; init; }
    public required string JoinedAlias { get; init; }
    public required LambdaExpression OnExpression { get; init; }
    public required JoinType JoinType { get; init; }
    public required Type JoinedType { get; init; }
}

public enum JoinType { Inner, Left, Right, Cross }

public sealed class GroupByClause
{
    public required LambdaExpression Expression { get; init; }
}

public sealed class HavingClause
{
    public required LambdaExpression Expression { get; init; }
}

public sealed class OrderByClause
{
    public required LambdaExpression Expression { get; init; }
    public required bool Descending { get; init; }
    public required bool IsThenBy { get; init; }
}

public sealed class ProjectionDescriptor
{
    public Type? TargetType { get; init; }
    public LambdaExpression? Expression { get; init; }
    public List<ProjectionOverride> Overrides { get; } = new();
}

public sealed class ProjectionOverride
{
    public required string TargetProperty { get; init; }
    public required LambdaExpression SourceExpression { get; init; }
}

public sealed class CteDescriptor
{
    public required string Name { get; init; }
    public required IQueryDescriptor Query { get; init; }
    public bool IsRecursive { get; init; }
    public IQueryDescriptor? RecursiveQuery { get; init; }
}

public sealed class CachingOptions
{
    public TimeSpan Ttl { get; set; }
    public List<Type> InvalidateOnTypes { get; } = new();
}
