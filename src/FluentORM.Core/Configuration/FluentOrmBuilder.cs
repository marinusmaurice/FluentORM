using System;
using System.Collections.Generic;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FluentORM.Core.Configuration;

public sealed class FluentOrmBuilder
{
    internal readonly FluentOrmOptions Options = new();
    internal IConnectionFactory? PrimaryFactory { get; private set; }
    internal IConnectionFactory? ReplicaFactory { get; private set; }
    internal ISqlDialect? Dialect { get; private set; }

    public FluentOrmBuilder WithPrimaryFactory(IConnectionFactory factory)
    {
        PrimaryFactory = factory;
        return this;
    }

    public FluentOrmBuilder WithDialect(ISqlDialect dialect)
    {
        Dialect = dialect;
        return this;
    }

    public FluentOrmBuilder UseReadReplica(IConnectionFactory factory)
    {
        ReplicaFactory = factory;
        return this;
    }

    public FluentOrmBuilder Pool(int min = 2, int max = 100)
    {
        Options.PoolMin = min;
        Options.PoolMax = max;
        return this;
    }

    public FluentOrmBuilder Pool(Action<PoolBuilder> configure)
    {
        var builder = new PoolBuilder(Options);
        configure(builder);
        return this;
    }

    public FluentOrmBuilder Retry(Action<RetryBuilder> configure)
    {
        var builder = new RetryBuilder(Options);
        configure(builder);
        return this;
    }

    public FluentOrmBuilder SlowQueryThreshold(TimeSpan threshold)
    {
        Options.SlowQueryThreshold = threshold;
        return this;
    }

    public FluentOrmBuilder LogTo(ILogger logger)
    {
        Options.Logger = logger;
        return this;
    }

    public FluentOrmBuilder AuditAll<T>()
    {
        Options.Audit ??= new AuditOptions();
        Options.Audit.AuditAll = true;
        return this;
    }

    public FluentOrmBuilder NoAudit<T>()
    {
        Options.Audit ??= new AuditOptions();
        Options.Audit.ExcludedTypes.Add(typeof(T));
        return this;
    }

    public FluentOrmBuilder SoftDeleteAll(string col = "DeletedAt")
    {
        Options.SoftDelete = new SoftDeleteOptions { Column = col, EnableGlobally = true };
        return this;
    }

    public FluentOrmBuilder MultiTenant(Action<MultiTenancyBuilder> configure)
    {
        Options.MultiTenancy = new MultiTenancyOptions();
        configure(new MultiTenancyBuilder(Options.MultiTenancy));
        return this;
    }

    public FluentOrmBuilder MapType<T>(Func<T, object> toDb, Func<object, T> fromDb)
    {
        Options.TypeMappings[typeof(T)] = new TypeMapping
        {
            ToDb = v => toDb((T)v),
            FromDb = v => fromDb(v)!
        };
        return this;
    }

    public FluentOrmBuilder ValidateSchemaOnStartup(DriftMode mode)
    {
        Options.DriftValidationMode = mode;
        return this;
    }

    public FluentOrmBuilder DetectNPlusOne(WhenDetected mode)
    {
        Options.NPlusOneDetection = mode;
        return this;
    }

    public FluentOrmBuilder UseMemoryCache()
    {
        Options.Caching ??= new CachingOptions();
        Options.Caching.UseMemory = true;
        return this;
    }

    public FluentOrmBuilder UseDistributedCache(string redisConnectionString)
    {
        Options.Caching ??= new CachingOptions();
        Options.Caching.RedisConnectionString = redisConnectionString;
        return this;
    }

    public FluentOrmBuilder DefaultCacheTtl(TimeSpan ttl)
    {
        Options.Caching ??= new CachingOptions();
        Options.Caching.DefaultTtl = ttl;
        return this;
    }

    public FluentOrmBuilder OnQueryExecuted(Action<string, IReadOnlyDictionary<string, object?>, TimeSpan, int> callback)
    {
        Options.OnQueryExecuted = callback;
        return this;
    }

    public FluentOrmBuilder OnMutationExecuted(Action<string, string, int, TimeSpan> callback)
    {
        Options.OnMutationExecuted = callback;
        return this;
    }

    public FluentOrmBuilder OnConcurrencyConflict(Action<string, object> callback)
    {
        Options.OnConcurrencyConflict = callback;
        return this;
    }

    public FluentOrmBuilder OnConnectionPoolExhausted(Action callback)
    {
        Options.OnConnectionPoolExhausted = callback;
        return this;
    }
}

public sealed class PoolBuilder
{
    private readonly FluentOrmOptions _options;
    internal PoolBuilder(FluentOrmOptions options) => _options = options;
    public PoolBuilder Min(int min) { _options.PoolMin = min; return this; }
    public PoolBuilder Max(int max) { _options.PoolMax = max; return this; }
    public PoolBuilder ConnectionTimeout(TimeSpan t) { _options.ConnectionTimeout = t; return this; }
    public PoolBuilder CommandTimeout(TimeSpan t) { _options.CommandTimeoutSeconds = (int)t.TotalSeconds; return this; }
    public PoolBuilder IdleTimeout(TimeSpan t) { _options.IdleTimeout = t; return this; }
}

public sealed class RetryBuilder
{
    private readonly FluentOrmOptions _options;
    internal RetryBuilder(FluentOrmOptions options) => _options = options;
    public RetryBuilder Attempts(int n) { _options.RetryAttempts = n; return this; }
    public RetryBuilder Backoff(BackoffStrategy s) { _options.RetryBackoff = s; return this; }
    public RetryBuilder RetryOn<TEx>(Func<TEx, bool>? predicate = null) where TEx : Exception => this;
}

public sealed class MultiTenancyBuilder
{
    private readonly MultiTenancyOptions _options;
    internal MultiTenancyBuilder(MultiTenancyOptions options) => _options = options;
    public MultiTenancyBuilder Column(string col) { _options.Column = col; return this; }
    public MultiTenancyBuilder ResolveFrom<TProvider>(Func<TProvider, string?> resolver) => this;
}
