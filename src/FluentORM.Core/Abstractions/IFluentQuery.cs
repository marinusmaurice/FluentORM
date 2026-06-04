using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Query;

namespace FluentORM.Core.Abstractions;

public interface IFluentQuery<T> where T : class
{
    IFluentQuery<T> Where(Expression<Func<T, bool>> predicate);
    IFluentQuery<T> OrWhere(Expression<Func<T, bool>> predicate);
    IFluentQuery<T> WhereIn<TVal>(Expression<Func<T, TVal>> col, IEnumerable<TVal> values);
    IFluentQuery<T> WhereIn<TVal>(Expression<Func<T, TVal>> col, IFluentQuery<T> subQuery);
    IFluentQuery<T> WhereIn<TVal, T2>(Expression<Func<T, TVal>> col, IFluentQuery<T2> subQuery) where T2 : class;
    IFluentQuery<T> WhereNotIn<TVal>(Expression<Func<T, TVal>> col, IEnumerable<TVal> values);
    IFluentQuery<T> WhereBetween<TVal>(Expression<Func<T, TVal>> col, TVal low, TVal high);
    IFluentQuery<T> WhereNull<TVal>(Expression<Func<T, TVal?>> col);
    IFluentQuery<T> WhereNotNull<TVal>(Expression<Func<T, TVal?>> col);
    IFluentQuery<T> WhereRaw(string sql, params object?[] args);
    IFluentQuery<T> WhereExists<T2>(Expression<Func<T, T2, bool>> predicate) where T2 : class;

    IFluentQuery<T> Join<T2>(Expression<Func<T, T2, bool>> on) where T2 : class;
    IFluentQuery<T> Join<T2, T3>(Expression<Func<T, T2, T3, bool>> on) where T2 : class where T3 : class;
    IFluentQuery<T> LeftJoin<T2>(Expression<Func<T, T2, bool>> on) where T2 : class;
    IFluentQuery<T> LeftJoin<T2, T3>(Expression<Func<T, T2, T3, bool>> on) where T2 : class where T3 : class;
    IFluentQuery<T> RightJoin<T2>(Expression<Func<T, T2, bool>> on) where T2 : class;
    IFluentQuery<T> CrossJoin<T2>() where T2 : class;

    IFluentQuery<T> Select<TResult>(Expression<Func<T, TResult>> projection);
    IFluentQuery<T> Select<T2, TResult>(Expression<Func<T, T2, TResult>> projection) where T2 : class;
    IFluentQuery<T> Select<T2, T3, TResult>(Expression<Func<T, T2, T3, TResult>> projection) where T2 : class where T3 : class;
    IFluentQuery<TDto> ProjectTo<TDto>() where TDto : class;
    IFluentQuery<TDto> ProjectTo<TDto>(Action<ProjectionConfig<T, TDto>> configure) where TDto : class;

    IFluentQuery<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector);
    IFluentQuery<T> Having(Expression<Func<T, bool>> predicate);

    IFluentQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);
    IFluentQuery<T> OrderByDesc<TKey>(Expression<Func<T, TKey>> keySelector);
    IFluentQuery<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector);
    IFluentQuery<T> ThenByDesc<TKey>(Expression<Func<T, TKey>> keySelector);

    IFluentQuery<T> Skip(int count);
    IFluentQuery<T> Take(int count);
    IFluentQuery<T> Distinct();

    IFluentQuery<T> IncludeDeleted();
    IFluentQuery<T> OnlyDeleted();

    IFluentQuery<T> CacheFor(TimeSpan ttl);
    IFluentQuery<T> InvalidateOn<T2>() where T2 : class;

    IFluentQuery<T> WithDiagnostics();

    string ToSql();

    Task<List<T>> ToListAsync(CancellationToken ct = default);
    Task<T[]> ToArrayAsync(CancellationToken ct = default);
    Task<T?> FirstOrDefaultAsync(CancellationToken ct = default);
    Task<T> FirstAsync(CancellationToken ct = default);
    Task<T?> SingleOrDefaultAsync(CancellationToken ct = default);
    Task<T> SingleAsync(CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<int> CountDistinctAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default);
    Task<bool> ExistsAsync(CancellationToken ct = default);
    Task<bool> AnyAsync(CancellationToken ct = default);
    Task<double> AverageAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default);
    Task<TVal> SumAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default);
    Task<TVal> MaxAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default);
    Task<TVal> MinAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default);
    Task<PagedResult<T>> ToPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<(List<T> Results, QueryDiagnostics Diagnostics)> ToListWithDiagnosticsAsync(CancellationToken ct = default);

    /// <summary>Returns a subquery expression for use in scalar SELECT projections. Compiles to COUNT(*).</summary>
    IFluentQuery<T> CountSubquery();
}

public sealed class ProjectionConfig<TSource, TTarget>
{
    internal List<(string TargetProp, System.Linq.Expressions.LambdaExpression SourceExpr)> Mappings { get; } = new();

    public ProjectionConfig<TSource, TTarget> For<TVal>(
        Expression<Func<TTarget, TVal>> target,
        Expression<Func<TSource, TVal>> source)
    {
        var prop = ((System.Linq.Expressions.MemberExpression)target.Body).Member.Name;
        Mappings.Add((prop, source));
        return this;
    }
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public sealed class QueryDiagnostics
{
    public required string Sql { get; init; }
    public required string Parameters { get; init; }
    public required double ExecutionMs { get; init; }
    public required int RowsRead { get; init; }
}
