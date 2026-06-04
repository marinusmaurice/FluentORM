using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Compiler;
using FluentORM.Core.Configuration;
using FluentORM.Core.Diagnostics;
using FluentORM.Core.Transactions;
using Microsoft.Extensions.Logging;

namespace FluentORM.Core.Execution;

public sealed class DbExecutor
{
    private readonly IConnectionFactory _primary;
    private readonly IConnectionFactory? _replica;
    private readonly ISqlDialect _dialect;
    private readonly FluentOrmOptions _options;
    private readonly RetryPolicy _retry;

    public DbExecutor(
        IConnectionFactory primary,
        IConnectionFactory? replica,
        ISqlDialect dialect,
        FluentOrmOptions options)
    {
        _primary = primary;
        _replica = replica;
        _dialect = dialect;
        _options = options;
        _retry = new RetryPolicy(options);
    }

    /// <summary>Executes a SELECT query and materializes results. Routed to replica if available.</summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        CompiledQuery query,
        Func<IDataReader, IReadOnlyList<T>> materializer,
        bool useReplica = true,
        Type? trackedEntityType = null,
        CancellationToken ct = default)
    {
        // N+1 detection
        if (trackedEntityType != null)
            QueryTracker.Current?.Track(trackedEntityType);

        var conn = await AcquireAsync(useReplica, ct);
        var sw = Stopwatch.StartNew();

        var results = await _retry.ExecuteAsync(async c =>
        {
            using var cmd = CreateCommand(conn, query);
            using var reader = await ExecuteReaderAsync(cmd, c);
            return materializer(reader);
        }, ct);

        sw.Stop();
        _options.OnQueryExecuted?.Invoke(query.Sql, query.Parameters, sw.Elapsed, results.Count);

        if (sw.Elapsed > _options.SlowQueryThreshold)
            _options.Logger?.LogWarning("Slow query ({ms}ms):\n{sql}",
                sw.ElapsedMilliseconds, query.Sql);

        if (FluentTransactionScope.Current == null) conn.Dispose();
        return results;
    }

    /// <summary>Executes a SELECT and returns at most one row. Routed to replica if available.</summary>
    public async Task<T?> QuerySingleAsync<T>(
        CompiledQuery query,
        Func<IDataReader, T?> materializer,
        bool useReplica = true,
        Type? trackedEntityType = null,
        CancellationToken ct = default)
    {
        if (trackedEntityType != null)
            QueryTracker.Current?.Track(trackedEntityType);

        var conn = await AcquireAsync(useReplica, ct);
        var sw = Stopwatch.StartNew();

        var result = await _retry.ExecuteAsync(async c =>
        {
            using var cmd = CreateCommand(conn, query);
            using var reader = await ExecuteReaderAsync(cmd, c);
            return materializer(reader);
        }, ct);

        sw.Stop();
        _options.OnQueryExecuted?.Invoke(query.Sql, query.Parameters, sw.Elapsed, result != null ? 1 : 0);

        if (FluentTransactionScope.Current == null) conn.Dispose();
        return result;
    }

    /// <summary>Executes a scalar (COUNT, SUM, etc.). Always routes to primary.</summary>
    public async Task<object?> ExecuteScalarAsync(CompiledQuery query, CancellationToken ct = default)
    {
        var conn = await AcquireAsync(false, ct);
        var result = await _retry.ExecuteAsync(async c =>
        {
            using var cmd = CreateCommand(conn, query);
            return await ExecuteScalarInternalAsync(cmd, c);
        }, ct);

        if (FluentTransactionScope.Current == null) conn.Dispose();
        return result;
    }

    /// <summary>Executes a non-query (INSERT/UPDATE/DELETE). Always routes to primary.</summary>
    public async Task<int> ExecuteNonQueryAsync(CompiledQuery query, CancellationToken ct = default)
    {
        var conn = await AcquireAsync(false, ct);
        var sw = Stopwatch.StartNew();

        var rows = await _retry.ExecuteAsync(async c =>
        {
            using var cmd = CreateCommand(conn, query);
            return await ExecuteNonQueryInternalAsync(cmd, c);
        }, ct);

        sw.Stop();
        _options.OnMutationExecuted?.Invoke("EXECUTE", "", rows, sw.Elapsed);

        if (FluentTransactionScope.Current == null) conn.Dispose();
        return rows;
    }

    // ── Connection routing ────────────────────────────────────────────────────

    private async Task<IDbConnection> AcquireAsync(bool preferReplica, CancellationToken ct)
    {
        // Active transaction → always use scope connection (primary)
        if (FluentTransactionScope.Current is { } scope)
            return scope.Connection;

        // Selects → replica if configured
        var factory = preferReplica && _replica != null ? _replica : _primary;
        return await factory.OpenAsync(ct);
    }

    // ── Command construction ──────────────────────────────────────────────────

    private IDbCommand CreateCommand(IDbConnection conn, CompiledQuery query)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = query.Sql;
        cmd.CommandTimeout = _options.CommandTimeoutSeconds;

        if (FluentTransactionScope.Current is { } scope)
            cmd.Transaction = scope.Transaction;

        foreach (var (key, value) in query.Parameters)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = key;
            param.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
        return cmd;
    }

    // ── Async-safe ADO.NET wrappers ───────────────────────────────────────────

    private static async Task<IDataReader> ExecuteReaderAsync(IDbCommand cmd, CancellationToken ct)
    {
        if (cmd is DbCommand dbCmd) return await dbCmd.ExecuteReaderAsync(ct);
        return cmd.ExecuteReader();
    }

    private static async Task<object?> ExecuteScalarInternalAsync(IDbCommand cmd, CancellationToken ct)
    {
        if (cmd is DbCommand dbCmd) return await dbCmd.ExecuteScalarAsync(ct);
        return cmd.ExecuteScalar();
    }

    private static async Task<int> ExecuteNonQueryInternalAsync(IDbCommand cmd, CancellationToken ct)
    {
        if (cmd is DbCommand dbCmd) return await dbCmd.ExecuteNonQueryAsync(ct);
        return cmd.ExecuteNonQuery();
    }
}
