using System;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentORM.SqlServer;

public static class ServiceCollectionExtensions
{
    public static FluentOrmBuilder UseSqlServer(
        this FluentOrmBuilder builder,
        string connectionString,
        Action<SqlServerBuilder>? configure = null)
    {
        var dialect = new SqlServerDialect();
        var factory = new SqlServerConnectionFactory(connectionString);

        builder.WithPrimaryFactory(factory);
        builder.WithDialect(dialect);

        var sqlBuilder = new SqlServerBuilder(builder.Options);
        configure?.Invoke(sqlBuilder);

        return builder;
    }

    public static FluentOrmBuilder UseReadReplica(this FluentOrmBuilder builder, string replicaConnectionString)
    {
        builder.UseReadReplica(new SqlServerConnectionFactory(replicaConnectionString));
        return builder;
    }
}

public sealed class SqlServerBuilder
{
    private readonly FluentOrmOptions _options;
    internal SqlServerBuilder(FluentOrmOptions options) => _options = options;

    public SqlServerBuilder CommandTimeout(int seconds)
    {
        _options.CommandTimeoutSeconds = seconds;
        return this;
    }

    public SqlServerBuilder EnableRetry(int attempts = 3, BackoffStrategy backoff = BackoffStrategy.Exponential)
    {
        _options.RetryAttempts = attempts;
        _options.RetryBackoff = backoff;
        return this;
    }
}

