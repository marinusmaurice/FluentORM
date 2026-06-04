using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;

namespace FluentORM.Core.Transactions;

public sealed class FluentTransactionScope : IAsyncDisposable
{
    private static readonly AsyncLocal<FluentTransactionScope?> _current = new();
    public static FluentTransactionScope? Current => _current.Value;

    private readonly FluentTransactionScope? _parent;
    public IDbConnection Connection { get; }
    public IDbTransaction Transaction { get; }
    private bool _committed;
    private bool _rolledBack;

    private FluentTransactionScope(IDbConnection connection, IDbTransaction transaction, FluentTransactionScope? parent)
    {
        Connection = connection;
        Transaction = transaction;
        _parent = parent;
    }

    public static FluentTransactionScope Begin(IDbConnection connection, IsolationLevel isolation)
    {
        var txn = connection.BeginTransaction(isolation);
        var scope = new FluentTransactionScope(connection, txn, _current.Value);
        _current.Value = scope;
        return scope;
    }

    public void Commit()
    {
        if (_committed || _rolledBack) return;
        Transaction.Commit();
        _committed = true;
    }

    public void Rollback()
    {
        if (_committed || _rolledBack) return;
        try { Transaction.Rollback(); } catch { }
        _rolledBack = true;
    }

    public async Task SavepointAsync(string name, ISqlDialect dialect, CancellationToken ct = default)
    {
        using var cmd = Connection.CreateCommand();
        cmd.Transaction = Transaction;
        cmd.CommandText = dialect.Provider switch
        {
            DbProvider.SqlServer => $"SAVE TRANSACTION [{name}]",
            _ => $"SAVEPOINT {name}"
        };
        if (cmd is System.Data.Common.DbCommand dbCmd)
            await dbCmd.ExecuteNonQueryAsync(ct);
        else
            cmd.ExecuteNonQuery();
    }

    public async Task RollbackToAsync(string name, ISqlDialect dialect, CancellationToken ct = default)
    {
        using var cmd = Connection.CreateCommand();
        cmd.Transaction = Transaction;
        cmd.CommandText = dialect.Provider switch
        {
            DbProvider.SqlServer => $"ROLLBACK TRANSACTION [{name}]",
            _ => $"ROLLBACK TO SAVEPOINT {name}"
        };
        if (cmd is System.Data.Common.DbCommand dbCmd)
            await dbCmd.ExecuteNonQueryAsync(ct);
        else
            cmd.ExecuteNonQuery();
    }

    public ValueTask DisposeAsync()
    {
        _current.Value = _parent;
        if (!_committed && !_rolledBack)
            Rollback();
        Transaction.Dispose();
        return ValueTask.CompletedTask;
    }
}
