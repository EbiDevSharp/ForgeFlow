using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;

namespace ForgeFlow.Application.Organizations.Slices;

/// <summary>
/// Operational vertical slice for site listing, retrieval, provisioning, and metadata updates within an organization.
/// </summary>
public sealed class SiteSlices(
    IOrganizationOperationsStore store,
    OrganizationSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<SiteDetails>>> ListSitesAsync(OrganizationScope? scope, long organizationId, OrganizationListQuery query, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, OrganizationPermissions.SitesRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<SiteDetails>>.Failure(auth.Error);
        if (!OrganizationSliceSupport.SiteMatches(scope, organizationId)) return OrganizationResult<OrganizationPage<SiteDetails>>.Failure("Site.NotFound");
        return await OrganizationSliceSupport.Read(async () => OrganizationResult<OrganizationPage<SiteDetails>>.Success(await store.ListSites(organizationId, (scope as OrganizationScope.Site)?.SiteId, OrganizationSliceSupport.Bound(query), ct)));
    }

    public async Task<OrganizationResult<SiteDetails>> GetSiteAsync(OrganizationScope? scope, long organizationId, long id, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, OrganizationPermissions.SitesRead, ct);
        if (auth.Error is not null) return OrganizationResult<SiteDetails>.Failure(auth.Error);
        if (!OrganizationSliceSupport.SiteMatches(scope, organizationId, id)) return OrganizationResult<SiteDetails>.Failure("Site.NotFound");
        return await OrganizationSliceSupport.Read(async () => {
            var value = await store.GetSite(organizationId, id, ct);
            return value is null ? OrganizationResult<SiteDetails>.Failure("Site.NotFound") : OrganizationResult<SiteDetails>.Success(value);
        });
    }

    public async Task<OrganizationResult<SiteDetails>> CreateSiteAsync(OrganizationScope? scope, CreateSite request, CancellationToken ct = default)
    {
        const string permission = OrganizationPermissions.SitesCreate;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<SiteDetails> res;
        if (auth.Error is not null) res = OrganizationResult<SiteDetails>.Failure(auth.Error);
        else if (!OrganizationSliceSupport.OrganizationMatches(scope, request.OrganizationId)) res = OrganizationResult<SiteDetails>.Failure("Site.NotFound");
        else res = await store.CreateSite(request, support.Access(auth.Actor!, scope!, permission), ct);
        return await OperationAuditHelper.RecordIfRejectedAsync(res, support.OperationRecorder, support.OpContext,
            UserOperationCatalog.SiteActionRejected, auth.Actor, scope, request.OrganizationId, null, "Site", request.Code, new { Action = "CreateSite" });
    }

    public Task<OrganizationResult<SiteDetails>> RenameSiteAsync(OrganizationScope? scope, RenameSite request, CancellationToken ct = default)
        => ChangeSite(scope, request.OrganizationId, request.Id, request.ExpectedRowVersion, OrganizationMutation.Rename, request.Name, ct);

    public Task<OrganizationResult<SiteDetails>> SetSiteActiveAsync(OrganizationScope? scope, SetSiteActive request, CancellationToken ct = default)
        => ChangeSite(scope, request.OrganizationId, request.Id, request.ExpectedRowVersion, request.IsActive ? OrganizationMutation.Activate : OrganizationMutation.Deactivate, null, ct);

    private async Task<OrganizationResult<SiteDetails>> ChangeSite(OrganizationScope? scope, long orgId, long id, string? expected, OrganizationMutation mutation, string? name, CancellationToken ct)
    {
        var permission = mutation == OrganizationMutation.Rename ? OrganizationPermissions.SitesUpdate : OrganizationPermissions.SitesSetActive;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<SiteDetails> res;
        if (auth.Error is not null) res = OrganizationResult<SiteDetails>.Failure(auth.Error);
        else if (!OrganizationSliceSupport.SiteMatches(scope, orgId, id)) res = OrganizationResult<SiteDetails>.Failure("Site.NotFound");
        else if (OrganizationSliceSupport.Version(expected) is not byte[] version) res = OrganizationResult<SiteDetails>.Failure(OrganizationAccessErrors.ConcurrencyConflict);
        else res = await store.UpdateSite(orgId, id, version, mutation, name, support.Access(auth.Actor!, scope!, permission), ct);
        return await OperationAuditHelper.RecordIfRejectedAsync(res, support.OperationRecorder, support.OpContext,
            UserOperationCatalog.SiteActionRejected, auth.Actor, scope, orgId, id > 0 ? id : null, "Site",
            id.ToString(CultureInfo.InvariantCulture), new { Action = mutation.ToString() });
    }
}
