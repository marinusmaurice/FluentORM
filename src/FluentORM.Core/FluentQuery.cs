using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Compiler;
using FluentORM.Core.Execution;
using FluentORM.Core.Interceptors;
using FluentORM.Core.Mapping;
using FluentORM.Core.Materializer;
using FluentORM.Core.Query;

namespace FluentORM.Core;

internal sealed class FluentQuery<T> : IFluentQuery<T> where T : class
{
    private readonly QueryBuilder<T> _builder;
    private readonly SqlCompiler _compiler;
    private readonly DbExecutor _executor;
    private readonly TenantInjector? _tenantInjector;
    private readonly EntityDescriptor<T> _descriptor;
    private readonly RowMapper<T> _mapper;
    private readonly EntityMapRegistry _registry;

    public FluentQuery(
        EntityDescriptor<T> descriptor,
        SqlCompiler compiler,
        DbExecutor executor,
        TenantInjector? tenantInjector,
        EntityMapRegistry registry)
    {
        _descriptor = descriptor;
        _builder = new QueryBuilder<T>(descriptor);
        _compiler = compiler;
        _executor = executor;
        _tenantInjector = tenantInjector;
        _mapper = new RowMapper<T>(descriptor);
        _registry = registry;
    }

    public IFluentQuery<T> Where(Expression<Func<T, bool>> predicate)
    {
        _builder.WhereClauses.Add(new ExpressionWhereClause(predicate));
        return this;
    }

    public IFluentQuery<T> OrWhere(Expression<Func<T, bool>> predicate)
    {
        _builder.WhereClauses.Add(new ExpressionWhereClause(predicate, isOr: true));
        return this;
    }

    public IFluentQuery<T> WhereIn<TVal>(Expression<Func<T, TVal>> col, IEnumerable<TVal> values)
    {
        _builder.WhereClauses.Add(new WhereInClause(col, values.Cast<object?>()));
        return this;
    }

    public IFluentQuery<T> WhereIn<TVal>(Expression<Func<T, TVal>> col, IFluentQuery<T> subQuery)
    {
        if (subQuery is FluentQuery<T> fq)
            _builder.WhereClauses.Add(new WhereInSubqueryClause(col, fq._builder));
        return this;
    }

    public IFluentQuery<T> WhereIn<TVal, T2>(Expression<Func<T, TVal>> col, IFluentQuery<T2> subQuery) where T2 : class
    {
        if (subQuery is FluentQuery<T2> fq)
            _builder.WhereClauses.Add(new WhereInSubqueryClause(col, fq._builder));
        return this;
    }

    public IFluentQuery<T> WhereNotIn<TVal>(Expression<Func<T, TVal>> col, IEnumerable<TVal> values)
    {
        _builder.WhereClauses.Add(new WhereNotInClause(col, values.Cast<object?>()));
        return this;
    }

    public IFluentQuery<T> WhereBetween<TVal>(Expression<Func<T, TVal>> col, TVal low, TVal high)
    {
        _builder.WhereClauses.Add(new WhereBetweenClause(col, low!, high!));
        return this;
    }

    public IFluentQuery<T> WhereNull<TVal>(Expression<Func<T, TVal?>> col)
    {
        _builder.WhereClauses.Add(new WhereNullClause(col, isNull: true));
        return this;
    }

    public IFluentQuery<T> WhereNotNull<TVal>(Expression<Func<T, TVal?>> col)
    {
        _builder.WhereClauses.Add(new WhereNullClause(col, isNull: false));
        return this;
    }

    public IFluentQuery<T> WhereRaw(string sql, params object?[] args)
    {
        _builder.WhereClauses.Add(new RawWhereClause(sql, args));
        return this;
    }

    public IFluentQuery<T> WhereExists<T2>(Expression<Func<T, T2, bool>> predicate) where T2 : class
    {
        var desc2 = _registry.GetDescriptor<T2>();
        var subBuilder = new QueryBuilder<T2>(desc2);
        // Build the EXISTS subquery: SELECT 1 FROM T2 WHERE predicate
        subBuilder.WhereClauses.Add(new ExpressionWhereClause(predicate));
        _builder.WhereClauses.Add(new WhereExistsClause(subBuilder));
        return this;
    }

    public IFluentQuery<T> Join<T2>(Expression<Func<T, T2, bool>> on) where T2 : class
        => AddJoin<T2>(on, JoinType.Inner);

    public IFluentQuery<T> Join<T2, T3>(Expression<Func<T, T2, T3, bool>> on) where T2 : class where T3 : class
        => AddJoin<T3>(on, JoinType.Inner);

    public IFluentQuery<T> LeftJoin<T2>(Expression<Func<T, T2, bool>> on) where T2 : class
        => AddJoin<T2>(on, JoinType.Left);

    public IFluentQuery<T> LeftJoin<T2, T3>(Expression<Func<T, T2, T3, bool>> on) where T2 : class where T3 : class
        => AddJoin<T3>(on, JoinType.Left);

    public IFluentQuery<T> RightJoin<T2>(Expression<Func<T, T2, bool>> on) where T2 : class
        => AddJoin<T2>(on, JoinType.Right);

    public IFluentQuery<T> CrossJoin<T2>() where T2 : class
    {
        var desc2 = GetDescriptor<T2>();
        _builder.Joins.Add(new JoinClause
        {
            JoinedType = typeof(T2),
            JoinedTableName = desc2.TableName,
            JoinedAlias = desc2.Alias,
            JoinType = JoinType.Cross,
            OnExpression = Expression.Lambda(Expression.Constant(true))
        });
        return this;
    }

    private IFluentQuery<T> AddJoin<T2>(LambdaExpression on, JoinType joinType) where T2 : class
    {
        var desc2 = GetDescriptor<T2>();
        _builder.Joins.Add(new JoinClause
        {
            JoinedType = typeof(T2),
            JoinedTableName = desc2.TableName,
            JoinedAlias = desc2.Alias,
            JoinType = joinType,
            OnExpression = on
        });
        return this;
    }

    private EntityDescriptor<T2> GetDescriptor<T2>() where T2 : class =>
        _registry.GetDescriptor<T2>();

    public IFluentQuery<T> Select<TResult>(Expression<Func<T, TResult>> projection)
    {
        _builder.SelectProjection = projection;
        return this;
    }

    public IFluentQuery<T> Select<T2, TResult>(Expression<Func<T, T2, TResult>> projection) where T2 : class
    {
        _builder.SelectProjection = projection;
        return this;
    }

    public IFluentQuery<T> Select<T2, T3, TResult>(Expression<Func<T, T2, T3, TResult>> projection) where T2 : class where T3 : class
    {
        _builder.SelectProjection = projection;
        return this;
    }

    public IFluentQuery<TDto> ProjectTo<TDto>() where TDto : class
    {
        _builder.Projection = new ProjectionDescriptor { TargetType = typeof(TDto) };
        return null!; // Caller should cast
    }

    public IFluentQuery<TDto> ProjectTo<TDto>(Action<ProjectionConfig<T, TDto>> configure) where TDto : class
    {
        var config = new ProjectionConfig<T, TDto>();
        configure(config);
        var projection = new ProjectionDescriptor { TargetType = typeof(TDto) };
        foreach (var (target, source) in config.Mappings)
            projection.Overrides.Add(new ProjectionOverride { TargetProperty = target, SourceExpression = source });
        _builder.Projection = projection;
        return null!;
    }

    public IFluentQuery<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        _builder.GroupByClauses.Add(new GroupByClause { Expression = keySelector });
        return this;
    }

    public IFluentQuery<T> Having(Expression<Func<T, bool>> predicate)
    {
        _builder.HavingClauses.Add(new HavingClause { Expression = predicate });
        return this;
    }

    public IFluentQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        _builder.OrderByClauses.Add(new OrderByClause { Expression = keySelector, Descending = false, IsThenBy = false });
        return this;
    }

    public IFluentQuery<T> OrderByDesc<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        _builder.OrderByClauses.Add(new OrderByClause { Expression = keySelector, Descending = true, IsThenBy = false });
        return this;
    }

    public IFluentQuery<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        _builder.OrderByClauses.Add(new OrderByClause { Expression = keySelector, Descending = false, IsThenBy = true });
        return this;
    }

    public IFluentQuery<T> ThenByDesc<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        _builder.OrderByClauses.Add(new OrderByClause { Expression = keySelector, Descending = true, IsThenBy = true });
        return this;
    }

    public IFluentQuery<T> Skip(int count) { _builder.Skip = count; return this; }
    public IFluentQuery<T> Take(int count) { _builder.Take = count; return this; }
    public IFluentQuery<T> Distinct() { _builder.IsDistinct = true; return this; }

    public IFluentQuery<T> IncludeDeleted() { _builder.IncludeDeleted = true; return this; }
    public IFluentQuery<T> OnlyDeleted() { _builder.OnlyDeletedFlag = true; return this; }

    public IFluentQuery<T> CacheFor(TimeSpan ttl)
    {
        _builder.CacheOptions = new Query.CachingOptions { Ttl = ttl };
        return this;
    }

    public IFluentQuery<T> InvalidateOn<T2>() where T2 : class
    {
        _builder.CacheOptions?.InvalidateOnTypes.Add(typeof(T2));
        return this;
    }

    public IFluentQuery<T> WithDiagnostics() { _builder.WithDiagnosticsFlag = true; return this; }

    public string ToSql()
    {
        InjectFilters();
        return _compiler.Compile(_builder).Sql;
    }

    public async Task<List<T>> ToListAsync(CancellationToken ct = default)
    {
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        var results = await _executor.QueryAsync<T>(compiled, reader => _mapper.MapAll(reader),
            useReplica: true, trackedEntityType: typeof(T), ct: ct);
        return results.ToList();
    }

    public async Task<T[]> ToArrayAsync(CancellationToken ct = default)
        => (await ToListAsync(ct)).ToArray();

    public async Task<T?> FirstOrDefaultAsync(CancellationToken ct = default)
    {
        _builder.Take = 1;
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        return await _executor.QuerySingleAsync<T>(compiled, reader => _mapper.MapSingle(reader),
            useReplica: true, trackedEntityType: typeof(T), ct: ct);
    }

    public async Task<T> FirstAsync(CancellationToken ct = default)
        => await FirstOrDefaultAsync(ct)
           ?? throw new Exceptions.EntityNotFoundException(typeof(T), "first");

    public async Task<T?> SingleOrDefaultAsync(CancellationToken ct = default)
    {
        _builder.Take = 2;
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        var results = await _executor.QueryAsync<T>(compiled, r => _mapper.MapAll(r),
            useReplica: true, trackedEntityType: typeof(T), ct: ct);
        return results.Count > 1
            ? throw new InvalidOperationException("Sequence contains more than one element.")
            : results.FirstOrDefault();
    }

    public async Task<T> SingleAsync(CancellationToken ct = default)
        => await SingleOrDefaultAsync(ct)
           ?? throw new Exceptions.EntityNotFoundException(typeof(T), "single");

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        var countSql = ReplaceSelectWithCount(compiled.Sql, "COUNT(*)");
        var result = await _executor.ExecuteScalarAsync(
            new CompiledQuery { Sql = countSql, Parameters = compiled.Parameters }, ct);
        return Convert.ToInt32(result);
    }

    public async Task<int> CountDistinctAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default)
    {
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        var colSql = ResolveColumnSql(col);
        var aggSql = ReplaceSelectWithCount(compiled.Sql, $"COUNT(DISTINCT {colSql})");
        var result = await _executor.ExecuteScalarAsync(
            new CompiledQuery { Sql = aggSql, Parameters = compiled.Parameters }, ct);
        return Convert.ToInt32(result);
    }

    public async Task<bool> ExistsAsync(CancellationToken ct = default)
        => await CountAsync(ct) > 0;

    public async Task<bool> AnyAsync(CancellationToken ct = default)
        => await ExistsAsync(ct);

    public async Task<double> AverageAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default)
    {
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        var colSql = ResolveColumnSql(col);
        var aggSql = ReplaceSelectWithCount(compiled.Sql, $"AVG({colSql})");
        var result = await _executor.ExecuteScalarAsync(
            new CompiledQuery { Sql = aggSql, Parameters = compiled.Parameters }, ct);
        return Convert.ToDouble(result);
    }

    public async Task<TVal> SumAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default)
    {
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        var colSql = ResolveColumnSql(col);
        var aggSql = ReplaceSelectWithCount(compiled.Sql, $"SUM({colSql})");
        var result = await _executor.ExecuteScalarAsync(
            new CompiledQuery { Sql = aggSql, Parameters = compiled.Parameters }, ct);
        return (TVal)Convert.ChangeType(result!, typeof(TVal));
    }

    public async Task<TVal> MaxAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default)
    {
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        var colSql = ResolveColumnSql(col);
        var aggSql = ReplaceSelectWithCount(compiled.Sql, $"MAX({colSql})");
        var result = await _executor.ExecuteScalarAsync(
            new CompiledQuery { Sql = aggSql, Parameters = compiled.Parameters }, ct);
        return (TVal)Convert.ChangeType(result!, typeof(TVal));
    }

    public async Task<TVal> MinAsync<TVal>(Expression<Func<T, TVal>> col, CancellationToken ct = default)
    {
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        var colSql = ResolveColumnSql(col);
        var aggSql = ReplaceSelectWithCount(compiled.Sql, $"MIN({colSql})");
        var result = await _executor.ExecuteScalarAsync(
            new CompiledQuery { Sql = aggSql, Parameters = compiled.Parameters }, ct);
        return (TVal)Convert.ChangeType(result!, typeof(TVal));
    }

    public async Task<PagedResult<T>> ToPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        _builder.Skip = page * pageSize;
        _builder.Take = pageSize;
        var items = await ToListAsync(ct);
        var total = await CountAsync(ct);
        return new PagedResult<T>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<(List<T> Results, QueryDiagnostics Diagnostics)> ToListWithDiagnosticsAsync(CancellationToken ct = default)
    {
        InjectFilters();
        var compiled = _compiler.Compile(_builder);
        var sw = Stopwatch.StartNew();
        var results = await _executor.QueryAsync<T>(compiled, r => _mapper.MapAll(r),
            useReplica: true, trackedEntityType: typeof(T), ct: ct);
        sw.Stop();
        var diag = new QueryDiagnostics
        {
            Sql = compiled.Sql,
            Parameters = compiled.Parameters.ToString() ?? "",
            ExecutionMs = sw.Elapsed.TotalMilliseconds,
            RowsRead = results.Count
        };
        return (results.ToList(), diag);
    }

    public IFluentQuery<T> CountSubquery()
    {
        // Marks this query as a COUNT(*) subquery — used in scalar projections
        _builder.SelectProjection = Expression.Lambda(Expression.Call(
            typeof(System.Linq.Enumerable), nameof(System.Linq.Enumerable.Count),
            [typeof(T)], Expression.Constant(new T[0])));
        return this;
    }

    private bool _filtersInjected;

    private string ResolveColumnSql<TVal>(Expression<Func<T, TVal>> col)
    {
        if (col.Body is MemberExpression member)
        {
            var propName = member.Member.Name;
            var colMap = _descriptor.Columns.FirstOrDefault(c =>
                string.Equals(c.PropertyName, propName, StringComparison.OrdinalIgnoreCase));
            if (colMap != null)
                return $"{_descriptor.Alias}.{colMap.ColumnName}";
        }
        return "*";
    }

    private void InjectFilters()
    {
        if (_filtersInjected) return;
        _filtersInjected = true;

        _tenantInjector?.InjectQuery(_builder);

        // Inject soft delete filter for entities with [SoftDelete]
        if (!_builder.IncludeDeleted && _descriptor.SoftDeleteColumn != null)
        {
            _builder.WhereClauses.Insert(0, new SoftDeleteWhereClause(
                _descriptor.SoftDeleteColumn.ColumnName,
                _descriptor.Alias,
                _builder.OnlyDeletedFlag));
        }
    }

    private QueryBuilder<T> BuildCountQuery(string aggregate) => _builder;

    private static string ReplaceSelectWithCount(string sql, string aggregate)
    {
        var selectEnd = sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
        if (selectEnd < 0) return sql;
        return $"SELECT {aggregate}\n" + sql[selectEnd..];
    }
}
