using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ForgeFlow.Web.Diagnostics;

public sealed class OrganizationDbContextHealthCheck(IServiceProvider serviceProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext checkContext, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var factory = scope.ServiceProvider.GetService<IDbContextFactory<OrganizationDbContext>>();
            if (factory is not null)
            {
                await using var db = await factory.CreateDbContextAsync(cancellationToken);
                var canConnect = await db.Database.CanConnectAsync(cancellationToken);
                return canConnect
                    ? HealthCheckResult.Healthy("OrganizationDbContext connection is healthy.")
                    : HealthCheckResult.Unhealthy("OrganizationDbContext could not connect to database.");
            }

            var directDb = scope.ServiceProvider.GetService<OrganizationDbContext>();
            if (directDb is not null)
            {
                var canConnect = await directDb.Database.CanConnectAsync(cancellationToken);
                return canConnect
                    ? HealthCheckResult.Healthy("OrganizationDbContext connection is healthy.")
                    : HealthCheckResult.Unhealthy("OrganizationDbContext could not connect to database.");
            }

            return HealthCheckResult.Unhealthy("OrganizationDbContext is not registered in service provider.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"OrganizationDbContext check failed: {ex.Message}", ex);
        }
    }
}

public sealed class AuthDbContextHealthCheck(IServiceProvider serviceProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext checkContext, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetService<AuthDbContext>();
            if (db is null)
            {
                var factory = scope.ServiceProvider.GetService<IDbContextFactory<AuthDbContext>>();
                if (factory is not null)
                {
                    await using var factoryDb = await factory.CreateDbContextAsync(cancellationToken);
                    var ok = await factoryDb.Database.CanConnectAsync(cancellationToken);
                    return ok
                        ? HealthCheckResult.Healthy("AuthDbContext connection is healthy.")
                        : HealthCheckResult.Unhealthy("AuthDbContext could not connect to database.");
                }

                return HealthCheckResult.Unhealthy("AuthDbContext is not registered in service provider.");
            }

            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("AuthDbContext connection is healthy.")
                : HealthCheckResult.Unhealthy("AuthDbContext could not connect to database.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"AuthDbContext check failed: {ex.Message}", ex);
        }
    }
}
