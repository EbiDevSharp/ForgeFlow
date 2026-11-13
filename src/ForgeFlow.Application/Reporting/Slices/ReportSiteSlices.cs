using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Reporting;

namespace ForgeFlow.Application.Reporting.Slices;

/// <summary>
/// Operational slice for listing authorized reporting sites within an organization scope.
/// </summary>
public sealed class ReportSiteSlices(
    IReportingQueryStore store,
    ReportingSliceSupport support)
{
    public async Task<OrganizationResult<IReadOnlyList<ReportSiteInfo>>> ListAuthorizedSitesAsync(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ReportingPermissions.ReportsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ReportSiteInfo>>.Failure(auth.Error);
        if (!ReportingSliceSupport.TryResolveScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<ReportSiteInfo>>.Failure(ReportingErrors.AccessDenied);

        try
        {
            var sites = await store.ListAuthorizedSites(organizationId, forcedSiteId, ct);
            return OrganizationResult<IReadOnlyList<ReportSiteInfo>>.Success(sites);
        }
        catch (ReportingStoreUnavailableException)
        {
            return OrganizationResult<IReadOnlyList<ReportSiteInfo>>.Failure(ReportingErrors.PersistenceUnavailable);
        }
    }
}
