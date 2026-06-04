using System;
using FluentORM.Core.Abstractions;
using FluentORM.Core.Compiler;
using FluentORM.Core.Configuration;
using FluentORM.Core.Execution;
using FluentORM.Core.Interceptors;
using FluentORM.Core.Mapping;
using FluentORM.Core.Mutations;
using Microsoft.Extensions.DependencyInjection;

namespace FluentORM.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFluentOrm(
        this IServiceCollection services,
        Action<FluentOrmBuilder> configure)
    {
        var builder = new FluentOrmBuilder();
        configure(builder);

        if (builder.PrimaryFactory == null)
            throw new InvalidOperationException(
                "No connection factory registered. Call UseSqlServer() or UseSqlite().");

        services.AddSingleton(builder.Options);
        services.AddSingleton(builder.PrimaryFactory);
        services.AddSingleton<EntityMapRegistry>();
        services.AddSingleton<SqlCompiler>();
        services.AddSingleton<MutationCompiler>();
        services.AddSingleton<TenantInjector>();
        services.AddSingleton<AuditInterceptor>();
        services.AddSingleton<DbExecutor>(sp =>
        {
            var primary = sp.GetRequiredService<IConnectionFactory>();
            var replica = builder.ReplicaFactory;
            var dialect = sp.GetRequiredService<ISqlDialect>();
            var options = sp.GetRequiredService<FluentOrmOptions>();
            return new DbExecutor(primary, replica, dialect, options);
        });
        services.AddScoped<IFluentDb, FluentDb>();

        return services;
    }
}
