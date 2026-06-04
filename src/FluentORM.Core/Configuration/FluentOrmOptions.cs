using System;
using System.Collections.Generic;
using FluentORM.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FluentORM.Core.Configuration;

public sealed class FluentOrmOptions
{
    public int PoolMin { get; set; } = 2;
    public int PoolMax { get; set; } = 100;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan SlowQueryThreshold { get; set; } = TimeSpan.FromMilliseconds(500);

    public int RetryAttempts { get; set; } = 0;
    public BackoffStrategy RetryBackoff { get; set; } = BackoffStrategy.Exponential;

    public bool ThrowOnEntityNotFound { get; set; } = false;
    public WhenDetected? NPlusOneDetection { get; set; }

    public DriftMode DriftValidationMode { get; set; } = DriftMode.Disabled;

    public MultiTenancyOptions? MultiTenancy { get; set; }
    public AuditOptions? Audit { get; set; }
    public SoftDeleteOptions? SoftDelete { get; set; }
    public CachingOptions? Caching { get; set; }

    public Action<string, IReadOnlyDictionary<string, object?>, TimeSpan, int>? OnQueryExecuted { get; set; }
    public Action<string, string, int, TimeSpan>? OnMutationExecuted { get; set; }
    public Action<string, object>? OnConcurrencyConflict { get; set; }
    public Action? OnConnectionPoolExhausted { get; set; }

    public ILogger? Logger { get; set; }

    public Dictionary<Type, TypeMapping> TypeMappings { get; } = new();
}

public sealed class MultiTenancyOptions
{
    public string Column { get; set; } = "TenantId";
}

public sealed class AuditOptions
{
    public bool AuditAll { get; set; }
    public HashSet<Type> ExcludedTypes { get; } = new();
}

public sealed class SoftDeleteOptions
{
    public string Column { get; set; } = "DeletedAt";
    public bool EnableGlobally { get; set; }
}

public sealed class CachingOptions
{
    public bool UseMemory { get; set; }
    public string? RedisConnectionString { get; set; }
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(2);
}

public sealed class TypeMapping
{
    public required Func<object, object> ToDb { get; init; }
    public required Func<object, object> FromDb { get; init; }
}

public enum BackoffStrategy { Linear, Exponential }
public enum DriftMode { Disabled, Warn, Throw }
