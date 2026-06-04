using System;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace FluentORM.Core.Execution;

internal sealed class RetryPolicy
{
    private readonly int _attempts;
    private readonly BackoffStrategy _backoff;
    private readonly ILogger? _logger;

    public RetryPolicy(FluentOrmOptions options)
    {
        _attempts = options.RetryAttempts;
        _backoff = options.RetryBackoff;
        _logger = options.Logger;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        if (_attempts <= 0) return await action(ct);

        Exception? last = null;
        for (int attempt = 0; attempt <= _attempts; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < _attempts)
            {
                last = ex;
                var delay = ComputeDelay(attempt);
                _logger?.LogWarning("Transient DB error on attempt {attempt}/{max} — retrying in {ms}ms: {msg}",
                    attempt + 1, _attempts + 1, delay.TotalMilliseconds, ex.Message);
                await Task.Delay(delay, ct);
            }
        }
        throw last!;
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        await ExecuteAsync(async c => { await action(c); return 0; }, ct);
    }

    private TimeSpan ComputeDelay(int attempt) => _backoff switch
    {
        BackoffStrategy.Exponential => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
        BackoffStrategy.Linear      => TimeSpan.FromMilliseconds(200 * (attempt + 1)),
        _ => TimeSpan.FromMilliseconds(100)
    };

    private static bool IsTransient(Exception ex)
    {
        // Microsoft.Data.SqlClient transient error codes
        if (ex is System.Data.Common.DbException dbEx)
        {
            var msg = dbEx.Message;
            if (msg.Contains("deadlock", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("connection", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("SQLITE_BUSY", StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (ex is TimeoutException) return true;
        if (ex is OperationCanceledException) return false; // don't retry cancellations
        return false;
    }
}
