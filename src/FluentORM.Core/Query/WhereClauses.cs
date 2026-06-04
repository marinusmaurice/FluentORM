using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace FluentORM.Core.Query;

public abstract class WhereClause
{
    public bool IsOr { get; init; }
}

public sealed class ExpressionWhereClause : WhereClause
{
    public LambdaExpression Expression { get; }
    public ExpressionWhereClause(LambdaExpression expr, bool isOr = false)
    {
        Expression = expr;
        IsOr = isOr;
    }
}

public sealed class WhereInClause : WhereClause
{
    public LambdaExpression Column { get; }
    public IEnumerable<object?> Values { get; }
    public WhereInClause(LambdaExpression col, IEnumerable<object?> values, bool isOr = false)
    {
        Column = col;
        Values = values;
        IsOr = isOr;
    }
}

public sealed class WhereNotInClause : WhereClause
{
    public LambdaExpression Column { get; }
    public IEnumerable<object?> Values { get; }
    public WhereNotInClause(LambdaExpression col, IEnumerable<object?> values, bool isOr = false)
    {
        Column = col;
        Values = values;
        IsOr = isOr;
    }
}

public sealed class WhereInSubqueryClause : WhereClause
{
    public LambdaExpression Column { get; }
    public IQueryDescriptor Subquery { get; }
    public bool IsNotIn { get; }
    public WhereInSubqueryClause(LambdaExpression col, IQueryDescriptor subquery, bool notIn = false, bool isOr = false)
    {
        Column = col;
        Subquery = subquery;
        IsNotIn = notIn;
        IsOr = isOr;
    }
}

public sealed class WhereBetweenClause : WhereClause
{
    public LambdaExpression Column { get; }
    public object Low { get; }
    public object High { get; }
    public WhereBetweenClause(LambdaExpression col, object low, object high, bool isOr = false)
    {
        Column = col;
        Low = low;
        High = high;
        IsOr = isOr;
    }
}

public sealed class WhereNullClause : WhereClause
{
    public LambdaExpression Column { get; }
    public bool IsNull { get; }
    public WhereNullClause(LambdaExpression col, bool isNull = true, bool isOr = false)
    {
        Column = col;
        IsNull = isNull;
        IsOr = isOr;
    }
}

public sealed class RawWhereClause : WhereClause
{
    public string Sql { get; }
    public object?[] Args { get; }
    public RawWhereClause(string sql, object?[] args, bool isOr = false)
    {
        Sql = sql;
        Args = args;
        IsOr = isOr;
    }
}

public sealed class WhereExistsClause : WhereClause
{
    public IQueryDescriptor Subquery { get; }
    public WhereExistsClause(IQueryDescriptor subquery, bool isOr = false)
    {
        Subquery = subquery;
        IsOr = isOr;
    }
}

public sealed class TenantWhereClause : WhereClause
{
    /// <summary>Dynamic source — re-evaluated on every warm-path plan execution.</summary>
    public Func<string?> TenantIdSource { get; }
    public string ColumnName { get; }
    public string Alias { get; }

    public TenantWhereClause(Func<string?> tenantIdSource, string columnName, string alias)
    {
        TenantIdSource = tenantIdSource;
        ColumnName = columnName;
        Alias = alias;
    }
}

public sealed class SoftDeleteWhereClause : WhereClause
{
    public string ColumnName { get; }
    public string Alias { get; }
    public bool OnlyDeleted { get; }
    public SoftDeleteWhereClause(string columnName, string alias, bool onlyDeleted = false)
    {
        ColumnName = columnName;
        Alias = alias;
        OnlyDeleted = onlyDeleted;
    }
}

public interface IQueryDescriptor { }
