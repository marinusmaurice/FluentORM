using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Compiler;
using FluentORM.Core.Configuration;
using FluentORM.Core.Execution;
using FluentORM.Core.Interceptors;
using FluentORM.Core.Mapping;
using FluentORM.Core.Materializer;
using FluentORM.Core.Migrations;
using FluentORM.Core.Mutations;
using FluentORM.Core.Transactions;

namespace FluentORM.Core;

public sealed class FluentDb : IFluentDb
{
    internal readonly EntityMapRegistry Registry;
    private readonly SqlCompiler _compiler;
    private readonly DbExecutor _executor;
    private readonly MutationCompiler _mutationCompiler;
    private readonly TenantInjector? _tenantInjector;
    private readonly AuditInterceptor? _auditInterceptor;
    private readonly ISqlDialect _dialect;
    private readonly FluentOrmOptions _options;
    private readonly IConnectionFactory _connectionFactory;
    private readonly string? _forcedTenantId;

    public IMigrationRunner Migrations { get; }
    public ICacheInvalidator Cache { get; }

    public FluentDb(
        EntityMapRegistry registry,
        SqlCompiler compiler,
        DbExecutor executor,
        MutationCompiler mutationCompiler,
        ISqlDialect dialect,
        FluentOrmOptions options,
        IConnectionFactory connectionFactory,
        TenantInjector? tenantInjector = null,
        AuditInterceptor? auditInterceptor = null,
        IMigrationRunner? migrations = null,
        ICacheInvalidator? cache = null,
        string? forcedTenantId = null)
    {
        Registry = registry;
        _compiler = compiler;
        _executor = executor;
        _mutationCompiler = mutationCompiler;
        _dialect = dialect;
        _options = options;
        _connectionFactory = connectionFactory;
        _tenantInjector = tenantInjector;
        _auditInterceptor = auditInterceptor;
        Migrations = migrations ?? new NullMigrationRunner();
        Cache = cache ?? new NullCacheInvalidator();
        _forcedTenantId = forcedTenantId;

        // Activate N+1 detection if configured
        if (options.NPlusOneDetection.HasValue)
            Diagnostics.QueryTracker.SetCurrent(
                new Diagnostics.QueryTracker(options.NPlusOneDetection.Value, options.Logger));
    }

    public IFluentQuery<T> Query<T>() where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        return new FluentQuery<T>(descriptor, _compiler, _executor, _tenantInjector, Registry);
    }

    public IFluentQuery<T> QueryAllTenants<T>() where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        var query = new FluentQuery<T>(descriptor, _compiler, _executor, null, Registry);
        return query;
    }

    public async Task<T?> FindAsync<T>(object id, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        var query = Query<T>().Where(BuildPkPredicate<T>(descriptor, id));
        var result = await query.FirstOrDefaultAsync(ct);
        if (result == null && _options.ThrowOnEntityNotFound)
            throw new Exceptions.EntityNotFoundException(typeof(T), id);
        return result;
    }

    public async Task InsertAsync<T>(T entity, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        _tenantInjector?.ValidateMutation(entity, descriptor);
        var compiled = _mutationCompiler.CompileInsert(entity, descriptor);
        await _executor.ExecuteNonQueryAsync(compiled, ct);
        await WriteAuditAsync(_auditInterceptor?.CapturePostMutation(entity, descriptor, MutationKind.Insert, null), ct);
    }

    public async Task InsertAsync<T>(T entity, Expression<Func<T, object>> columns, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        _tenantInjector?.ValidateMutation(entity, descriptor);
        var cols = ExtractColumns(descriptor, columns);
        var compiled = _mutationCompiler.CompileInsert(entity, descriptor, cols);
        await _executor.ExecuteNonQueryAsync(compiled, ct);
        await WriteAuditAsync(_auditInterceptor?.CapturePostMutation(entity, descriptor, MutationKind.Insert, null), ct);
    }

    public async Task<TKey> InsertAndGetIdAsync<T, TKey>(T entity, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        _tenantInjector?.ValidateMutation(entity, descriptor);
        var compiled = _mutationCompiler.CompileInsertAndReturn(entity, descriptor);
        var result = await _executor.ExecuteScalarAsync(compiled, ct);
        return (TKey)Convert.ChangeType(result!, typeof(TKey));
    }

    public async Task<T> InsertAndReturnAsync<T>(T entity, CancellationToken ct = default) where T : class
    {
        var id = await InsertAndGetIdAsync<T, object>(entity, ct);
        return await FindAsync<T>(id, ct)
               ?? throw new InvalidOperationException("Insert succeeded but entity not found after insert.");
    }

    public async Task InsertOrIgnoreAsync<T>(T entity, Expression<Func<T, object>> conflictOn, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        var conflictCols = ExtractColumnNames(descriptor, conflictOn);
        var compiled = _mutationCompiler.CompileInsertOrIgnore(entity, descriptor, conflictCols);
        await _executor.ExecuteNonQueryAsync(compiled, ct);
    }

    public async Task UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        _tenantInjector?.ValidateMutation(entity, descriptor);
        var pre = _auditInterceptor?.CapturePreMutation(entity, descriptor, MutationKind.Update);
        var compiled = _mutationCompiler.CompileUpdate(entity, descriptor);
        var rows = await _executor.ExecuteNonQueryAsync(compiled, ct);
        if (rows == 0 && descriptor.RowVersionColumn != null)
        {
            _options.OnConcurrencyConflict?.Invoke(descriptor.TableName, descriptor.PrimaryKey!.GetValue(entity)!);
            throw new Exceptions.ConcurrencyException(descriptor.TableName, descriptor.PrimaryKey!.GetValue(entity)!);
        }
        await WriteAuditAsync(_auditInterceptor?.CapturePostMutation(entity, descriptor, MutationKind.Update, pre), ct);
    }

    public async Task UpdateAsync<T>(T entity, Expression<Func<T, object>> columns, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        _tenantInjector?.ValidateMutation(entity, descriptor);
        var pre = _auditInterceptor?.CapturePreMutation(entity, descriptor, MutationKind.Update);
        var cols = ExtractColumns(descriptor, columns);
        var compiled = _mutationCompiler.CompileUpdate(entity, descriptor, cols);
        var rows = await _executor.ExecuteNonQueryAsync(compiled, ct);
        if (rows == 0 && descriptor.RowVersionColumn != null)
            throw new Exceptions.ConcurrencyException(descriptor.TableName, descriptor.PrimaryKey!.GetValue(entity)!);
        await WriteAuditAsync(_auditInterceptor?.CapturePostMutation(entity, descriptor, MutationKind.Update, pre), ct);
    }

    public async Task<int> UpdateWhereAsync<T>(Expression<Func<T, bool>> where,
        Expression<Func<T, object>> columns, object values, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        var setCols = ExtractColumns(descriptor, columns);
        var parameters = new ParameterBag();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"UPDATE {_dialect.QualifyTable(null, descriptor.TableName)}");
        sb.AppendLine("SET");

        var valueProps = values.GetType().GetProperties();
        var setItems = new List<string>();
        foreach (var col in setCols)
        {
            var vProp = valueProps.FirstOrDefault(p => string.Equals(p.Name, col.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (vProp != null)
                setItems.Add($"    {col.ColumnName} = {parameters.Add(vProp.GetValue(values), col.PropertyName.ToLower())}");
        }
        sb.AppendLine(string.Join(",\n", setItems));

        // WHERE without aliases — SQLite UPDATE doesn't support qualified column names
        var visitor = new Compiler.ExpressionToSqlVisitor(parameters, Registry, new Compiler.AliasRegistry(), unaliased: true);
        sb.AppendLine("WHERE");
        sb.Append($"    {visitor.Compile(where)}");

        if (_tenantInjector != null && descriptor.TenantKeyColumn != null)
        {
            var tenantId = GetCurrentTenantId();
            if (tenantId != null)
                sb.Append($"\n    AND {descriptor.TenantKeyColumn.ColumnName} = {parameters.Add(tenantId, "tenantId")}");
        }

        var compiled = new Compiler.CompiledQuery { Sql = sb.ToString().TrimEnd(), Parameters = parameters.Parameters };
        return await _executor.ExecuteNonQueryAsync(compiled, ct);
    }

    public async Task UpsertAsync<T>(T entity, Expression<Func<T, object>> conflictOn,
        Expression<Func<T, object>>? updateOnly = null, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        _tenantInjector?.ValidateMutation(entity, descriptor);
        var conflictCols = ExtractColumnNames(descriptor, conflictOn);
        var updateCols = updateOnly != null ? ExtractColumns(descriptor, updateOnly) : null;
        var compiled = _mutationCompiler.CompileUpsert(entity, descriptor, conflictCols, updateCols);
        await _executor.ExecuteNonQueryAsync(compiled, ct);
    }

    public async Task DeleteAsync<T>(object id, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        var tenantId = GetCurrentTenantId();
        var softDelete = descriptor.SoftDeleteColumn != null;
        var compiled = _mutationCompiler.CompileDelete(id, descriptor, tenantId, softDelete);
        await _executor.ExecuteNonQueryAsync(compiled, ct);
    }

    public async Task<int> DeleteWhereAsync<T>(Expression<Func<T, bool>> where, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        var parameters = new ParameterBag();
        var aliases = new Compiler.AliasRegistry();
        aliases.Register(typeof(T), descriptor.Alias);
        var visitor = new Compiler.ExpressionToSqlVisitor(parameters, Registry, aliases, unaliased: true);
        var sb = new System.Text.StringBuilder();

        if (descriptor.SoftDeleteColumn != null)
        {
            sb.AppendLine($"UPDATE {_dialect.QualifyTable(null, descriptor.TableName)}");
            sb.AppendLine($"SET {descriptor.SoftDeleteColumn.ColumnName} = {_dialect.NowExpression}");
        }
        else
        {
            sb.AppendLine($"DELETE FROM {_dialect.QualifyTable(null, descriptor.TableName)}");
        }

        sb.AppendLine("WHERE");
        sb.Append($"    {visitor.Compile(where)}");

        var compiled = new Compiler.CompiledQuery { Sql = sb.ToString().TrimEnd(), Parameters = parameters.Parameters };
        return await _executor.ExecuteNonQueryAsync(compiled, ct);
    }

    public async Task HardDeleteAsync<T>(object id, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        var tenantId = GetCurrentTenantId();
        var compiled = _mutationCompiler.CompileDelete(id, descriptor, tenantId, softDelete: false);
        await _executor.ExecuteNonQueryAsync(compiled, ct);
    }

    public async Task RestoreAsync<T>(object id, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        var tenantId = GetCurrentTenantId();
        var compiled = _mutationCompiler.CompileRestore(id, descriptor, tenantId);
        await _executor.ExecuteNonQueryAsync(compiled, ct);
    }

    public async Task BulkInsertAsync<T>(IEnumerable<T> items, CancellationToken ct = default) where T : class
    {
        foreach (var item in items)
            await InsertAsync(item, ct);
    }

    public async Task BulkInsertAsync<T>(IEnumerable<T> items, Expression<Func<T, object>> columns, CancellationToken ct = default) where T : class
    {
        foreach (var item in items)
            await InsertAsync(item, columns, ct);
    }

    public async Task BulkUpsertAsync<T>(IEnumerable<T> items, Expression<Func<T, object>> conflictOn,
        Expression<Func<T, object>>? updateOnly = null, CancellationToken ct = default) where T : class
    {
        foreach (var item in items)
            await UpsertAsync(item, conflictOn, updateOnly, ct);
    }

    public async Task<int> BulkUpdateAsync<T>(Expression<Func<T, bool>> where,
        Expression<Func<T, object>> columns, object values, CancellationToken ct = default) where T : class
        => await UpdateWhereAsync(where, columns, values, ct);

    public async Task<int> BulkDeleteAsync<T>(Expression<Func<T, bool>> where, CancellationToken ct = default) where T : class
        => await DeleteWhereAsync(where, ct);

    public async Task TransactionAsync(Func<IFluentDb, Task> action,
        IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        var conn = await _connectionFactory.OpenAsync(ct);
        await using var scope = FluentTransactionScope.Begin(conn, isolation);
        try
        {
            await action(this);
            scope.Commit();
        }
        catch
        {
            scope.Rollback();
            throw;
        }
    }

    public async Task<IFluentTransaction> BeginTransactionAsync(
        IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        var conn = await _connectionFactory.OpenAsync(ct);
        var scope = FluentTransactionScope.Begin(conn, isolation);
        return new FluentTransaction(this, scope, _dialect);
    }

    public IFluentDb WithCTE(string name, Func<IFluentDb, IFluentQuery<object>> builder) => this;

    public IFluentDb WithRecursiveCTE<T>(string name,
        Func<IFluentDb, IFluentQuery<T>> anchor,
        Func<IFluentDb, IFluentQuery<T>, IFluentQuery<T>> recursive) where T : class => this;

    public async Task<IEnumerable<T>> RawAsync<T>(string sql, object? parameters = null, CancellationToken ct = default) where T : class
    {
        var paramDict = ObjectToParams(parameters);
        var compiled = new Compiler.CompiledQuery { Sql = sql, Parameters = paramDict };
        var descriptor = Registry.GetDescriptor<T>();
        var mapper = new RowMapper<T>(descriptor);
        return await _executor.QueryAsync<T>(compiled, r => mapper.MapAll(r), useReplica: false, ct: ct);
    }

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        var paramDict = ObjectToParams(parameters);
        var compiled = new Compiler.CompiledQuery { Sql = sql, Parameters = paramDict };
        var result = await _executor.ExecuteScalarAsync(compiled, ct);
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default)
    {
        var paramDict = ObjectToParams(parameters);
        var compiled = new Compiler.CompiledQuery { Sql = sql, Parameters = paramDict };
        return await _executor.ExecuteNonQueryAsync(compiled, ct);
    }

    public async Task<IEnumerable<AuditEntry>> AuditHistory<T>(object id, CancellationToken ct = default) where T : class
    {
        var descriptor = Registry.GetDescriptor<T>();
        return await Query<AuditEntry>()
            .Where(a => a.TableName == descriptor.TableName && a.PrimaryKey == id.ToString())
            .OrderBy(a => a.Timestamp)
            .ToListAsync(ct);
    }

    public IFluentDb ForTenant(string tenantId)
    {
        var provider = new StaticTenantProvider(tenantId);
        var newInjector = new TenantInjector(provider);
        return new FluentDb(Registry, _compiler, _executor, _mutationCompiler, _dialect,
            _options, _connectionFactory, newInjector, _auditInterceptor, Migrations, Cache, tenantId);
    }

    public PoolStatistics PoolStats() => new() { Active = 0, Idle = 0, WaitCount = 0, TotalCreated = 0 };

    private string? GetCurrentTenantId() => _forcedTenantId;

    private async Task WriteAuditAsync(AuditEntry? entry, CancellationToken ct)
    {
        if (entry == null) return;
        try
        {
            var auditDesc = Registry.GetDescriptor<AuditEntry>();
            var compiled = _mutationCompiler.CompileInsert(entry, auditDesc);
            await _executor.ExecuteNonQueryAsync(compiled, ct);
        }
        catch
        {
            // Audit write failure must not break the main operation
        }
    }

    private Expression<Func<T, bool>> BuildPkPredicate<T>(EntityDescriptor<T> descriptor, object id) where T : class
    {
        var param = Expression.Parameter(typeof(T), descriptor.Alias);
        var prop = Expression.Property(param, descriptor.PrimaryKey!.PropertyName);
        var value = Expression.Constant(Convert.ChangeType(id, descriptor.PrimaryKey!.ClrType));
        var eq = Expression.Equal(prop, value);
        return Expression.Lambda<Func<T, bool>>(eq, param);
    }

    private static IReadOnlyList<ColumnMap> ExtractColumns<T>(EntityDescriptor<T> descriptor,
        Expression<Func<T, object>> expr) where T : class
    {
        var names = ExtractColumnNames(descriptor, expr);
        return names.Select(n => descriptor.TryResolve(n) ?? descriptor.ResolveByColumn(n)!)
            .Where(c => c != null).ToList();
    }

    private static IReadOnlyList<string> ExtractColumnNames<T>(EntityDescriptor<T> descriptor,
        Expression<Func<T, object>> expr) where T : class
    {
        var body = expr.Body;
        if (body is UnaryExpression unary) body = unary.Operand;
        if (body is NewExpression newExpr)
            return newExpr.Members?.Select(m => m.Name).ToList() ?? [];
        if (body is MemberExpression member)
            return [member.Member.Name];
        return [];
    }

    private static IReadOnlyDictionary<string, object?> ObjectToParams(object? parameters)
    {
        if (parameters == null) return new Dictionary<string, object?>();
        return parameters.GetType().GetProperties()
            .ToDictionary(
                p => "@" + p.Name,
                p => p.GetValue(parameters));
    }
}

internal sealed class StaticTenantProvider : ITenantContextProvider
{
    private readonly string _tenantId;
    public StaticTenantProvider(string tenantId) => _tenantId = tenantId;
    public string? GetCurrentTenantId() => _tenantId;
}

internal sealed class NullMigrationRunner : IMigrationRunner
{
    public Task ApplyAsync(bool allowDestructive = false, CancellationToken ct = default) => Task.CompletedTask;
    public Task ApplyToAsync(long version, bool allowDestructive = false, CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackToAsync(long version, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> PreviewAsync(CancellationToken ct = default) => Task.FromResult(string.Empty);
    public Task<MigrationStatus> StatusAsync(CancellationToken ct = default) => Task.FromResult(new MigrationStatus());
}

internal sealed class NullCacheInvalidator : ICacheInvalidator
{
    public Task InvalidateAsync<T>(CancellationToken ct = default) where T : class => Task.CompletedTask;
    public Task InvalidateAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class => Task.CompletedTask;
}

internal sealed class FluentTransaction : IFluentTransaction
{
    private readonly FluentDb _db;
    private readonly FluentTransactionScope _scope;
    private readonly ISqlDialect _dialect;

    public FluentTransaction(FluentDb db, FluentTransactionScope scope, ISqlDialect dialect)
    {
        _db = db;
        _scope = scope;
        _dialect = dialect;
    }

    public IMigrationRunner Migrations => _db.Migrations;
    public ICacheInvalidator Cache => _db.Cache;

    public Task CommitAsync(CancellationToken ct = default) { _scope.Commit(); return Task.CompletedTask; }
    public Task RollbackAsync(CancellationToken ct = default) { _scope.Rollback(); return Task.CompletedTask; }
    public Task SavepointAsync(string name, CancellationToken ct = default) => _scope.SavepointAsync(name, _dialect, ct);
    public Task RollbackToAsync(string name, CancellationToken ct = default) => _scope.RollbackToAsync(name, _dialect, ct);

    public ValueTask DisposeAsync() => _scope.DisposeAsync();

    // Delegate all IFluentDb calls to the underlying db
    public IFluentQuery<T> Query<T>() where T : class => _db.Query<T>();
    public IFluentQuery<T> QueryAllTenants<T>() where T : class => _db.QueryAllTenants<T>();
    public Task<T?> FindAsync<T>(object id, CancellationToken ct = default) where T : class => _db.FindAsync<T>(id, ct);
    public Task InsertAsync<T>(T entity, CancellationToken ct = default) where T : class => _db.InsertAsync(entity, ct);
    public Task InsertAsync<T>(T entity, Expression<Func<T, object>> columns, CancellationToken ct = default) where T : class => _db.InsertAsync(entity, columns, ct);
    public Task<TKey> InsertAndGetIdAsync<T, TKey>(T entity, CancellationToken ct = default) where T : class => _db.InsertAndGetIdAsync<T, TKey>(entity, ct);
    public Task<T> InsertAndReturnAsync<T>(T entity, CancellationToken ct = default) where T : class => _db.InsertAndReturnAsync(entity, ct);
    public Task InsertOrIgnoreAsync<T>(T entity, Expression<Func<T, object>> conflictOn, CancellationToken ct = default) where T : class => _db.InsertOrIgnoreAsync(entity, conflictOn, ct);
    public Task UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class => _db.UpdateAsync(entity, ct);
    public Task UpdateAsync<T>(T entity, Expression<Func<T, object>> columns, CancellationToken ct = default) where T : class => _db.UpdateAsync(entity, columns, ct);
    public Task<int> UpdateWhereAsync<T>(Expression<Func<T, bool>> where, Expression<Func<T, object>> columns, object values, CancellationToken ct = default) where T : class => _db.UpdateWhereAsync(where, columns, values, ct);
    public Task UpsertAsync<T>(T entity, Expression<Func<T, object>> conflictOn, Expression<Func<T, object>>? updateOnly = null, CancellationToken ct = default) where T : class => _db.UpsertAsync(entity, conflictOn, updateOnly, ct);
    public Task DeleteAsync<T>(object id, CancellationToken ct = default) where T : class => _db.DeleteAsync<T>(id, ct);
    public Task<int> DeleteWhereAsync<T>(Expression<Func<T, bool>> where, CancellationToken ct = default) where T : class => _db.DeleteWhereAsync(where, ct);
    public Task HardDeleteAsync<T>(object id, CancellationToken ct = default) where T : class => _db.HardDeleteAsync<T>(id, ct);
    public Task RestoreAsync<T>(object id, CancellationToken ct = default) where T : class => _db.RestoreAsync<T>(id, ct);
    public Task BulkInsertAsync<T>(IEnumerable<T> items, CancellationToken ct = default) where T : class => _db.BulkInsertAsync(items, ct);
    public Task BulkInsertAsync<T>(IEnumerable<T> items, Expression<Func<T, object>> columns, CancellationToken ct = default) where T : class => _db.BulkInsertAsync(items, columns, ct);
    public Task BulkUpsertAsync<T>(IEnumerable<T> items, Expression<Func<T, object>> conflictOn, Expression<Func<T, object>>? updateOnly = null, CancellationToken ct = default) where T : class => _db.BulkUpsertAsync(items, conflictOn, updateOnly, ct);
    public Task<int> BulkUpdateAsync<T>(Expression<Func<T, bool>> where, Expression<Func<T, object>> columns, object values, CancellationToken ct = default) where T : class => _db.BulkUpdateAsync(where, columns, values, ct);
    public Task<int> BulkDeleteAsync<T>(Expression<Func<T, bool>> where, CancellationToken ct = default) where T : class => _db.BulkDeleteAsync(where, ct);
    public Task TransactionAsync(Func<IFluentDb, Task> action, IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default) => _db.TransactionAsync(action, isolation, ct);
    public Task<IFluentTransaction> BeginTransactionAsync(IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default) => _db.BeginTransactionAsync(isolation, ct);
    public IFluentDb WithCTE(string name, Func<IFluentDb, IFluentQuery<object>> builder) => _db.WithCTE(name, builder);
    public IFluentDb WithRecursiveCTE<T>(string name, Func<IFluentDb, IFluentQuery<T>> anchor, Func<IFluentDb, IFluentQuery<T>, IFluentQuery<T>> recursive) where T : class => _db.WithRecursiveCTE(name, anchor, recursive);
    public Task<IEnumerable<T>> RawAsync<T>(string sql, object? parameters = null, CancellationToken ct = default) where T : class => _db.RawAsync<T>(sql, parameters, ct);
    public Task<T> ScalarAsync<T>(string sql, object? parameters = null, CancellationToken ct = default) => _db.ScalarAsync<T>(sql, parameters, ct);
    public Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default) => _db.ExecuteAsync(sql, parameters, ct);
    public Task<IEnumerable<AuditEntry>> AuditHistory<T>(object id, CancellationToken ct = default) where T : class => _db.AuditHistory<T>(id, ct);
    public IFluentDb ForTenant(string tenantId) => _db.ForTenant(tenantId);
    public PoolStatistics PoolStats() => _db.PoolStats();
}
