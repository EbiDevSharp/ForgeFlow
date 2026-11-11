using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Dashboard;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Routings;
using ForgeFlow.Application.Units;
using ForgeFlow.Application.Users;

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
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<UnitUseCases>(provider => new UnitUseCases(
            new UnitOperationsStore(() => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<ItemUseCases>(provider => new ItemUseCases(
            new ItemOperationsStore(() => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>()),
            new UnitOperationsStore(() => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<BomUseCases>(provider => new BomUseCases(
            new BomOperationsStore(() => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<ProductionOrderUseCases>(provider => new ProductionOrderUseCases(
            new SqlProductionOrderOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>(),
                provider.GetService<IUserNotificationPublisher>()),
            new BomOperationsStore(() => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>(),
            new SqlRoutingOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>())));
        services.AddScoped<InventoryUseCases>(provider => new InventoryUseCases(
            new SqlInventoryOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<ReportingUseCases>(provider => new ReportingUseCases(
            new SqlReportingQueryStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<QualityUseCases>(provider => new QualityUseCases(
            new SqlQualityOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>(),
                provider.GetRequiredService<IServiceScopeFactory>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddScoped<ProcurementUseCases>(provider => new ProcurementUseCases(
            new SqlProcurementOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddScoped<RoutingUseCases>(provider => new RoutingUseCases(
            new SqlRoutingOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<StockReservationUseCases>(provider => new StockReservationUseCases(
            new SqlStockReservationStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>()),
            new SqlInventoryOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddScoped<DashboardUseCases>(provider => new DashboardUseCases(
            new SqlDashboardQueryStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<TimeProvider>()));
        return services;
    }
}
