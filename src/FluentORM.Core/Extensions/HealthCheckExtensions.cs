using System;
using System.Threading;
using System.Threading.Tasks;
using FluentORM.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluentORM.Core.Extensions;

public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds a FluentORM database health check that pings the database connection.
    /// Usage: services.AddHealthChecks().AddFluentOrm("db-check");
    /// </summary>
    public static IHealthChecksBuilder AddFluentOrm(
        this IHealthChecksBuilder builder,
        string name = "database",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        string[]? tags = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new FluentOrmHealthCheck(sp.GetRequiredService<IConnectionFactory>()),
            failureStatus,
            tags));
    }
}

internal sealed class FluentOrmHealthCheck : IHealthCheck
{
    private readonly IConnectionFactory _factory;

    public FluentOrmHealthCheck(IConnectionFactory factory) => _factory = factory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = (System.Data.Common.DbConnection)
                await _factory.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("Database connection is healthy.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connection failed.", ex);
        }
    }
}
