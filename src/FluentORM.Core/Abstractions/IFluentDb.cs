using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace FluentORM.Core.Abstractions;

public interface IFluentDb
{
    IFluentQuery<T> Query<T>() where T : class;
    IFluentQuery<T> QueryAllTenants<T>() where T : class;

    Task<T?> FindAsync<T>(object id, CancellationToken ct = default) where T : class;

    Task InsertAsync<T>(T entity, CancellationToken ct = default) where T : class;
    Task InsertAsync<T>(T entity, Expression<Func<T, object>> columns, CancellationToken ct = default) where T : class;
    Task<TKey> InsertAndGetIdAsync<T, TKey>(T entity, CancellationToken ct = default) where T : class;
    Task<T> InsertAndReturnAsync<T>(T entity, CancellationToken ct = default) where T : class;
    Task InsertOrIgnoreAsync<T>(T entity, Expression<Func<T, object>> conflictOn, CancellationToken ct = default) where T : class;

    Task UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class;
    Task UpdateAsync<T>(T entity, Expression<Func<T, object>> columns, CancellationToken ct = default) where T : class;
    Task<int> UpdateWhereAsync<T>(Expression<Func<T, bool>> where, Expression<Func<T, object>> columns, object values, CancellationToken ct = default) where T : class;

    Task UpsertAsync<T>(T entity, Expression<Func<T, object>> conflictOn, Expression<Func<T, object>>? updateOnly = null, CancellationToken ct = default) where T : class;

    Task DeleteAsync<T>(object id, CancellationToken ct = default) where T : class;
    Task<int> DeleteWhereAsync<T>(Expression<Func<T, bool>> where, CancellationToken ct = default) where T : class;
    Task HardDeleteAsync<T>(object id, CancellationToken ct = default) where T : class;
    Task RestoreAsync<T>(object id, CancellationToken ct = default) where T : class;

    Task BulkInsertAsync<T>(IEnumerable<T> items, CancellationToken ct = default) where T : class;
    Task BulkInsertAsync<T>(IEnumerable<T> items, Expression<Func<T, object>> columns, CancellationToken ct = default) where T : class;
    Task BulkUpsertAsync<T>(IEnumerable<T> items, Expression<Func<T, object>> conflictOn, Expression<Func<T, object>>? updateOnly = null, CancellationToken ct = default) where T : class;
    Task<int> BulkUpdateAsync<T>(Expression<Func<T, bool>> where, Expression<Func<T, object>> columns, object values, CancellationToken ct = default) where T : class;
    Task<int> BulkDeleteAsync<T>(Expression<Func<T, bool>> where, CancellationToken ct = default) where T : class;

    Task TransactionAsync(Func<IFluentDb, Task> action, IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default);
    Task<IFluentTransaction> BeginTransactionAsync(IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default);

    IFluentDb WithCTE(string name, Func<IFluentDb, IFluentQuery<object>> builder);
    IFluentDb WithRecursiveCTE<T>(string name, Func<IFluentDb, IFluentQuery<T>> anchor, Func<IFluentDb, IFluentQuery<T>, IFluentQuery<T>> recursive) where T : class;

    Task<IEnumerable<T>> RawAsync<T>(string sql, object? parameters = null, CancellationToken ct = default) where T : class;
    Task<T> ScalarAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);
    Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default);

    Task<IEnumerable<AuditEntry>> AuditHistory<T>(object id, CancellationToken ct = default) where T : class;

    IFluentDb ForTenant(string tenantId);
    Execution.PoolStatistics PoolStats();

    IMigrationRunner Migrations { get; }
    ICacheInvalidator Cache { get; }
}

public interface IFluentTransaction : IFluentDb, IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    Task SavepointAsync(string name, CancellationToken ct = default);
    Task RollbackToAsync(string name, CancellationToken ct = default);
}

public interface IMigrationRunner
{
    Task ApplyAsync(bool allowDestructive = false, CancellationToken ct = default);
    Task ApplyToAsync(long version, bool allowDestructive = false, CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    Task RollbackToAsync(long version, CancellationToken ct = default);
    Task<string> PreviewAsync(CancellationToken ct = default);
    Task<Migrations.MigrationStatus> StatusAsync(CancellationToken ct = default);
}

public interface ICacheInvalidator
{
    Task InvalidateAsync<T>(CancellationToken ct = default) where T : class;
    Task InvalidateAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class;
}
