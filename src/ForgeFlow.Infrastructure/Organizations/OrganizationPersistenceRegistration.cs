using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Audit.Handlers;
using ForgeFlow.Application.Boms;
using ForgeFlow.Application.BusinessPartners;
using ForgeFlow.Application.Common;
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
using ForgeFlow.Application.Sales;
using ForgeFlow.Application.Security;
using ForgeFlow.Application.Units;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Events;
using ForgeFlow.Infrastructure.Boms;
using ForgeFlow.Infrastructure.BusinessPartners;
using ForgeFlow.Infrastructure.Common;
using ForgeFlow.Infrastructure.Dashboard;
using ForgeFlow.Infrastructure.Inventory;
using ForgeFlow.Infrastructure.Items;
using ForgeFlow.Infrastructure.Procurement;
using ForgeFlow.Infrastructure.ProductionOrders;
using ForgeFlow.Infrastructure.Quality;
using ForgeFlow.Infrastructure.Reporting;
using ForgeFlow.Infrastructure.Routings;
using ForgeFlow.Infrastructure.Sales;
using ForgeFlow.Infrastructure.Security;
using ForgeFlow.Infrastructure.Units;

namespace ForgeFlow.Infrastructure.Organizations;

public static class OrganizationPersistenceRegistration
{
    public static IServiceCollection AddOrganizationPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.TryAddSingleton<IOrganizationGraphCache, MemoryOrganizationGraphCache>();
        services.TryAddSingleton<IOrganizationPermissionCache, MemoryOrganizationPermissionCache>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOrganizationTimeZoneProvider, DefaultOrganizationTimeZoneProvider>();
        services.TryAddSingleton<IOrganizationClock, OrganizationClock>();

        // Multi-Tenancy Ambient Accessor (FFA-010)
        services.TryAddSingleton<AmbientOrganizationAccessor>();
        services.TryAddSingleton<ICurrentOrganizationAccessor>(sp => sp.GetRequiredService<AmbientOrganizationAccessor>());

        // Domain Event Dispatching & Audit Trail Sink Pipeline
        services.TryAddScoped<IAuditTrailSinkAccessor, AmbientAuditTrailSinkAccessor>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        services.AddScoped<ProductionOrderAuditHandler>();
        services.AddScoped<IDomainEventHandler<ProductionOrderReleasedEvent>>(sp => sp.GetRequiredService<ProductionOrderAuditHandler>());
        services.AddScoped<IDomainEventHandler<ProductionOrderCompletedEvent>>(sp => sp.GetRequiredService<ProductionOrderAuditHandler>());
        services.AddScoped<IDomainEventHandler<ProductionOrderStartedEvent>>(sp => sp.GetRequiredService<ProductionOrderAuditHandler>());
        services.AddScoped<IDomainEventHandler<ProductionOrderCancelledEvent>>(sp => sp.GetRequiredService<ProductionOrderAuditHandler>());

        services.AddScoped<SalesOrderAuditHandler>();
        services.AddScoped<IDomainEventHandler<SalesOrderApprovedEvent>>(sp => sp.GetRequiredService<SalesOrderAuditHandler>());
        services.AddScoped<IDomainEventHandler<SalesOrderDispatchedEvent>>(sp => sp.GetRequiredService<SalesOrderAuditHandler>());

        services.AddScoped<InventoryAuditHandler>();
        services.AddScoped<IDomainEventHandler<StockAdjustedEvent>>(sp => sp.GetRequiredService<InventoryAuditHandler>());
        services.AddScoped<IDomainEventHandler<StockReservedEvent>>(sp => sp.GetRequiredService<InventoryAuditHandler>());

        services.AddScoped<QualityAuditHandler>();
        services.AddScoped<IDomainEventHandler<QualityInspectionRecordedEvent>>(sp => sp.GetRequiredService<QualityAuditHandler>());
        services.AddScoped<IDomainEventHandler<LotCoaIssuedEvent>>(sp => sp.GetRequiredService<QualityAuditHandler>());

        services.AddScoped<BomAuditHandler>();
        services.AddScoped<IDomainEventHandler<BomApprovedEvent>>(sp => sp.GetRequiredService<BomAuditHandler>());

        services.AddScoped<SecurityAuditHandler>();
        services.AddScoped<IDomainEventHandler<RolePermissionsUpdatedEvent>>(sp => sp.GetRequiredService<SecurityAuditHandler>());
        services.AddScoped<IDomainEventHandler<UserAccessModifiedEvent>>(sp => sp.GetRequiredService<SecurityAuditHandler>());

        services.AddScoped<ForgeFlow.Infrastructure.Persistence.Interceptors.TenantEnforcementSaveChangesInterceptor>();
        services.AddScoped<ForgeFlow.Infrastructure.Persistence.Interceptors.AuditingSaveChangesInterceptor>(sp =>
            new ForgeFlow.Infrastructure.Persistence.Interceptors.AuditingSaveChangesInterceptor(
                sp.GetService<ICurrentActorAccessor>(),
                sp.GetService<TimeProvider>()));
        services.AddScoped<ForgeFlow.Infrastructure.Persistence.Interceptors.DomainEventsSaveChangesInterceptor>(sp =>
            new ForgeFlow.Infrastructure.Persistence.Interceptors.DomainEventsSaveChangesInterceptor(
                sp.GetService<IDomainEventDispatcher>(),
                sp.GetService<IAuditTrailSinkAccessor>(),
                sp.GetService<ICurrentActorAccessor>()));

        services.AddDbContextFactory<OrganizationDbContext>((provider, options) =>
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
            new UnitOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IOrganizationGraphCache>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<ItemUseCases>(provider => new ItemUseCases(
            new ItemOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IOrganizationGraphCache>()),
            new UnitOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IOrganizationGraphCache>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<BomUseCases>(provider => new BomUseCases(
            new BomOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IOrganizationGraphCache>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<ProductionOrderUseCases>(provider => new ProductionOrderUseCases(
            new SqlProductionOrderOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>(),
                provider.GetService<IUserNotificationPublisher>()),
            new BomOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IOrganizationGraphCache>()),
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
                provider.GetService<IOperationContextAccessor>(),
                provider.GetService<IOrganizationClock>()),
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
        services.AddScoped<QualitySpecUseCases>(provider => new QualitySpecUseCases(
            new SqlQualitySpecStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>(),
                provider.GetRequiredService<IServiceScopeFactory>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()));
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
        services.AddScoped<IStockReservationStore>(provider => new SqlStockReservationStore(
            () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
            provider.GetService<IErrorLogRecorder>(),
            provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<ForgeFlow.Application.Inventory.Slices.StockReservationSlices>(provider => new ForgeFlow.Application.Inventory.Slices.StockReservationSlices(
            provider.GetRequiredService<IStockReservationStore>(),
            new SqlInventoryOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>()),
            new ForgeFlow.Application.Inventory.Slices.InventorySliceSupport(
                provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
                provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>()),
            provider.GetService<TimeProvider>()));
        services.AddScoped<StockReservationUseCases>(provider => new StockReservationUseCases(
            provider.GetRequiredService<IStockReservationStore>(),
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
            provider.GetService<TimeProvider>(),
            provider.GetService<IOrganizationClock>()));
        services.AddScoped<SalesUseCases>(provider => new SalesUseCases(
            new SqlSalesOperationsStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddScoped<DocumentSequenceUseCases>(provider => new DocumentSequenceUseCases(
            new SqlDocumentSequenceStore(
                () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
                provider.GetService<IErrorLogRecorder>(),
                provider.GetService<IOperationContextAccessor>(),
                provider.GetService<IOrganizationTimeZoneProvider>()),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddScoped<IBusinessPartnerStore>(provider => new SqlBusinessPartnerStore(
            () => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>(),
            provider.GetService<IErrorLogRecorder>(),
            provider.GetService<IOperationContextAccessor>()));
        services.AddScoped<ForgeFlow.Application.BusinessPartners.Slices.BusinessPartnerSliceSupport>(provider => new ForgeFlow.Application.BusinessPartners.Slices.BusinessPartnerSliceSupport(
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>(),
            provider.GetService<TimeProvider>()));
        services.AddScoped<ForgeFlow.Application.BusinessPartners.Slices.BusinessPartnerCatalogSlices>(provider => new ForgeFlow.Application.BusinessPartners.Slices.BusinessPartnerCatalogSlices(
            provider.GetRequiredService<IBusinessPartnerStore>(),
            provider.GetRequiredService<ForgeFlow.Application.BusinessPartners.Slices.BusinessPartnerSliceSupport>()));
        services.AddScoped<BusinessPartnerUseCases>(provider => new BusinessPartnerUseCases(
            provider.GetRequiredService<IBusinessPartnerStore>(),
            provider.GetService<ITrustedActorAccessor>(), provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<IUserOperationRecorder>(), provider.GetService<IOperationContextAccessor>(),
            provider.GetService<TimeProvider>()));
        return services;
    }
}
