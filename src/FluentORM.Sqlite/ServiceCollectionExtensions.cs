using System;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentORM.Sqlite;

public static class ServiceCollectionExtensions
{
    public static FluentOrmBuilder UseSqlite(
        this FluentOrmBuilder builder,
        string connectionString)
    {
        var dialect = new SqliteDialect();
        var factory = new SqliteConnectionFactory(connectionString);
        builder.WithPrimaryFactory(factory);
        SqliteRegistration.Dialect = dialect;
        return builder;
    }

    public static FluentOrmBuilder UseSqliteInMemory(
        this FluentOrmBuilder builder,
        string? cacheKey = null)
    {
        var cs = cacheKey != null
            ? $"Data Source={cacheKey};Mode=Memory;Cache=Shared"
            : "Data Source=:memory:";
        return builder.UseSqlite(cs);
    }
}

internal static class SqliteRegistration
{
    internal static ISqlDialect? Dialect { get; set; }
}
