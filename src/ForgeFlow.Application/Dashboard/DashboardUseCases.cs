using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Routings;
using ForgeFlow.Domain.Organizations;

namespace ForgeFlow.Application.Dashboard;

public sealed class DashboardUseCases(
    IDashboardQueryStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    TimeProvider? clock = null)
{
    private DateOnly Today => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    public async Task<OrganizationResult<DashboardSnapshot>> GetSnapshot(
        OrganizationScope? scope,
        long organizationId,
        long? siteFilter = null,
        CancellationToken ct = default)
    {
        if (actors is null) return OrganizationResult<DashboardSnapshot>.Failure(OrganizationAccessErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return OrganizationResult<DashboardSnapshot>.Failure(OrganizationAccessErrors.ActorRequired);
        if (scope is null) return OrganizationResult<DashboardSnapshot>.Failure(OrganizationAccessErrors.ScopeRequired);
        if (permissions is null) return OrganizationResult<DashboardSnapshot>.Failure(OrganizationAccessErrors.AccessDenied);

        long? resolvedSiteId = null;
        if (organizationId <= 0) return OrganizationResult<DashboardSnapshot>.Failure(OrganizationAccessErrors.AccessDenied);
        switch (scope)
        {
            case OrganizationScope.Organization org when org.OrganizationId == organizationId:
                resolvedSiteId = siteFilter is > 0 ? siteFilter : null;
                break;
            case OrganizationScope.Site site when site.OrganizationId == organizationId:
                if (siteFilter.HasValue && siteFilter.Value > 0 && siteFilter.Value != site.SiteId)
                    return OrganizationResult<DashboardSnapshot>.Failure(OrganizationAccessErrors.AccessDenied);
                resolvedSiteId = site.SiteId;
                break;
            default:
                return OrganizationResult<DashboardSnapshot>.Failure(OrganizationAccessErrors.AccessDenied);
        }

        async Task<bool> Can(string perm) => (await permissions.EvaluateAsync(actor, scope, perm, ct)).IsAllowed;

        var capabilities = new DashboardCapabilities(
            Production: await Can(ProductionOrderPermissions.ProductionOrdersRead),
            Inventory: await Can(InventoryPermissions.InventoryRead),
            Reports: await Can(ReportingPermissions.ReportsRead),
            Quality: await Can(QualityPermissions.QualityRead),
            Procurement: await Can(ProcurementPermissions.ProcurementRead),
            Routings: await Can(RoutingPermissions.RoutingsRead));

        try
        {
            var snap = await store.GetSnapshot(organizationId, resolvedSiteId, Today, capabilities, ct);
            return OrganizationResult<DashboardSnapshot>.Success(snap);
        }
        catch (InventoryStoreUnavailableException)
        {
            return OrganizationResult<DashboardSnapshot>.Failure(OrganizationAccessErrors.PersistenceUnavailable);
        }
        catch (ReportingStoreUnavailableException)
        {
            return OrganizationResult<DashboardSnapshot>.Failure(OrganizationAccessErrors.PersistenceUnavailable);
        }
    }
}
