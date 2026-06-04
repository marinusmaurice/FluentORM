using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace FluentORM.Core.Query;

/// <summary>
/// Entry point for window function expressions in SELECT projections.
/// Usage: Sql.RowNumber().Over().PartitionBy(s => s.FieldId).OrderByDesc(s => s.Score)
/// </summary>
public static class Sql
{
    public static WindowFunctionBuilder RowNumber() => new("ROW_NUMBER");
    public static WindowFunctionBuilder Rank() => new("RANK");
    public static WindowFunctionBuilder DenseRank() => new("DENSE_RANK");

    public static WindowFunctionBuilder<TSource, TVal> Sum<TSource, TVal>(
        Expression<Func<TSource, TVal>> col) => new("SUM", col);

    public static WindowFunctionBuilder<TSource, TVal> Avg<TSource, TVal>(
        Expression<Func<TSource, TVal>> col) => new("AVG", col);

    public static WindowFunctionBuilder<TSource, TVal> Min<TSource, TVal>(
        Expression<Func<TSource, TVal>> col) => new("MIN", col);

    public static WindowFunctionBuilder<TSource, TVal> Max<TSource, TVal>(
        Expression<Func<TSource, TVal>> col) => new("MAX", col);

    public static WindowFunctionBuilder<TSource, TVal> Lead<TSource, TVal>(
        Expression<Func<TSource, TVal>> col, int offset = 1) => new("LEAD", col, offset: offset);

    public static WindowFunctionBuilder<TSource, TVal> Lag<TSource, TVal>(
        Expression<Func<TSource, TVal>> col, int offset = 1) => new("LAG", col, offset: offset);

    public static WindowFunctionBuilder<TSource, TVal> FirstValue<TSource, TVal>(
        Expression<Func<TSource, TVal>> col) => new("FIRST_VALUE", col);

    public static WindowFunctionBuilder<TSource, TVal> LastValue<TSource, TVal>(
        Expression<Func<TSource, TVal>> col) => new("LAST_VALUE", col);

    public static WindowFunctionBuilder<TSource, TVal> NthValue<TSource, TVal>(
        Expression<Func<TSource, TVal>> col, int n) => new("NTH_VALUE", col, n: n);
}

/// <summary>Window function builder for functions that take no column argument (ROW_NUMBER, RANK, etc.).</summary>
public sealed class WindowFunctionBuilder
{
    private readonly string _fn;
    private readonly List<Expression> _partitionBys = new();
    private readonly List<(Expression Expr, bool Desc)> _orderBys = new();
    private int? _precedingRows;
    private int? _followingRows;

    internal WindowFunctionBuilder(string fn) => _fn = fn;

    public WindowFunctionBuilder Over() => this;

    public WindowFunctionBuilder PartitionBy<T>(Expression<Func<T, object>> col)
    {
        _partitionBys.Add(col);
        return this;
    }

    public WindowFunctionBuilder OrderBy<T>(Expression<Func<T, object>> col)
    {
        _orderBys.Add((col, false));
        return this;
    }

    public WindowFunctionBuilder OrderByDesc<T>(Expression<Func<T, object>> col)
    {
        _orderBys.Add((col, true));
        return this;
    }

    public WindowFunctionBuilder Rows(int preceding, int following)
    {
        _precedingRows = preceding;
        _followingRows = following;
        return this;
    }

    /// <summary>Renders this window function to SQL given an alias registry and visitor.</summary>
    internal string ToSql(Compiler.AliasRegistry aliases, Compiler.ExpressionToSqlVisitor visitor)
    {
        var sb = new StringBuilder();
        sb.Append($"{_fn}() OVER (");
        AppendOverClause(sb, aliases, visitor);
        sb.Append(')');
        return sb.ToString();
    }

    internal void AppendOverClause(StringBuilder sb, Compiler.AliasRegistry aliases,
        Compiler.ExpressionToSqlVisitor visitor)
    {
        bool hasContent = false;
        if (_partitionBys.Any())
        {
            sb.Append("PARTITION BY ");
            sb.Append(string.Join(", ", _partitionBys.Select(e =>
                visitor.Compile(Expression.Lambda(e, GetParams(e))))));
            hasContent = true;
        }
        if (_orderBys.Any())
        {
            if (hasContent) sb.Append(' ');
            sb.Append("ORDER BY ");
            sb.Append(string.Join(", ", _orderBys.Select(o =>
            {
                var colSql = visitor.Compile(Expression.Lambda(o.Expr, GetParams(o.Expr)));
                return o.Desc ? $"{colSql} DESC" : colSql;
            })));
            hasContent = true;
        }
        if (_precedingRows.HasValue && _followingRows.HasValue)
        {
            if (hasContent) sb.Append(' ');
            var preceding = _precedingRows.Value == 0 ? "CURRENT ROW" : $"{_precedingRows.Value} PRECEDING";
            var following = _followingRows.Value == 0 ? "CURRENT ROW" : $"{_followingRows.Value} FOLLOWING";
            sb.Append($"ROWS BETWEEN {preceding} AND {following}");
        }
    }

    private static System.Linq.Expressions.ParameterExpression[] GetParams(Expression expr)
    {
        if (expr is LambdaExpression l) return l.Parameters.ToArray();
        return Array.Empty<System.Linq.Expressions.ParameterExpression>();
    }
}

/// <summary>Window function builder for aggregate window functions (SUM, AVG, etc.).</summary>
public sealed class WindowFunctionBuilder<TSource, TVal>
{
    private readonly string _fn;
    private readonly Expression<Func<TSource, TVal>> _col;
    private readonly int? _offset;
    private readonly int? _n;
    private readonly List<LambdaExpression> _partitionBys = new();
    private readonly List<(LambdaExpression Expr, bool Desc)> _orderBys = new();
    private int? _precedingRows;
    private int? _followingRows;

    internal WindowFunctionBuilder(string fn, Expression<Func<TSource, TVal>> col,
        int? offset = null, int? n = null)
    {
        _fn = fn;
        _col = col;
        _offset = offset;
        _n = n;
    }

    public WindowFunctionBuilder<TSource, TVal> Over() => this;

    public WindowFunctionBuilder<TSource, TVal> PartitionBy(Expression<Func<TSource, object>> col)
    {
        _partitionBys.Add(col);
        return this;
    }

    public WindowFunctionBuilder<TSource, TVal> OrderBy(Expression<Func<TSource, object>> col)
    {
        _orderBys.Add((col, false));
        return this;
    }

    public WindowFunctionBuilder<TSource, TVal> OrderByDesc(Expression<Func<TSource, object>> col)
    {
        _orderBys.Add((col, true));
        return this;
    }

    public WindowFunctionBuilder<TSource, TVal> Rows(int preceding, int following)
    {
        _precedingRows = preceding;
        _followingRows = following;
        return this;
    }

    internal string ToSql(Compiler.AliasRegistry aliases, Compiler.ExpressionToSqlVisitor visitor)
    {
        var colSql = visitor.Compile(_col);
        var args = _offset.HasValue ? $"{colSql}, {_offset}"
                 : _n.HasValue ? $"{colSql}, {_n}"
                 : colSql;

        var sb = new StringBuilder();
        sb.Append($"{_fn}({args}) OVER (");

        if (_partitionBys.Any())
        {
            sb.Append("PARTITION BY ");
            sb.Append(string.Join(", ", _partitionBys.Select(e => visitor.Compile(e))));
        }
        if (_orderBys.Any())
        {
            if (_partitionBys.Any()) sb.Append(' ');
            sb.Append("ORDER BY ");
            sb.Append(string.Join(", ", _orderBys.Select(o =>
            {
                var c = visitor.Compile(o.Expr);
                return o.Desc ? $"{c} DESC" : c;
            })));
        }
        if (_precedingRows.HasValue && _followingRows.HasValue)
        {
            if (_partitionBys.Any() || _orderBys.Any()) sb.Append(' ');
            var preceding = _precedingRows.Value == 0 ? "CURRENT ROW" : $"{_precedingRows.Value} PRECEDING";
            var following = _followingRows.Value == 0 ? "CURRENT ROW" : $"{_followingRows.Value} FOLLOWING";
            sb.Append($"ROWS BETWEEN {preceding} AND {following}");
        }

        sb.Append(')');
        return sb.ToString();
    }
}
