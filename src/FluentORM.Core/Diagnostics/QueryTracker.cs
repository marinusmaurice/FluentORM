using System;
using System.Collections.Generic;
using System.Threading;
using FluentORM.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace FluentORM.Core.Diagnostics;

public enum WhenDetected { Throw, Warn, Log }

public sealed class QueryTracker
{
    private static readonly AsyncLocal<QueryTracker?> _current = new();
    public static QueryTracker? Current => _current.Value;
    public static void SetCurrent(QueryTracker? tracker) => _current.Value = tracker;

    private readonly Dictionary<Type, int> _queryCounts = new();
    private readonly WhenDetected _mode;
    private readonly ILogger? _logger;
    private const int NPlus1Threshold = 5;

    public QueryTracker(WhenDetected mode, ILogger? logger = null)
    {
        _mode = mode;
        _logger = logger;
    }

    public void Track(Type entityType)
    {
        if (!_queryCounts.TryGetValue(entityType, out var count))
        {
            _queryCounts[entityType] = 1;
            return;
        }

        _queryCounts[entityType] = ++count;
        if (count > NPlus1Threshold)
            Handle(entityType, count);
    }

    private void Handle(Type t, int count)
    {
        switch (_mode)
        {
            case WhenDetected.Throw:
                throw new NPlusOneException(t, count);
            case WhenDetected.Warn:
                _logger?.LogWarning("N+1 detected on {Type}: queried {Count} times", t.Name, count);
                break;
            case WhenDetected.Log:
                _logger?.LogDebug("N+1 on {Type}: {Count}", t.Name, count);
                break;
        }
    }
}
