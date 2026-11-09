using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Infrastructure.Organizations;

public static class OrganizationPersistenceRegistration
{
    public static IServiceCollection AddOrganizationPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContextFactory<OrganizationDbContext>(options =>
        {
            var connection = configuration.GetConnectionString("ForgeFlow");
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("OrganizationSite.PersistenceUnavailable");
            options.UseSqlServer(connection); // No retry, automatic migration, or sensitive-data logging.
        });
        // EF also registers a scoped context by default. Expose only the factory to prevent circuit capture.
        services.RemoveAll<OrganizationDbContext>();
        // Lazy persistence resolution lets missing trusted adapters fail closed before requesting a connection.
        services.AddScoped<OrganizationUseCases>(provider => new OrganizationUseCases(
            new OrganizationOperationsStore(() => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>()));
        return services;
    }
}
