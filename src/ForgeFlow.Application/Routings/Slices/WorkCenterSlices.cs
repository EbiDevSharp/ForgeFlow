using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Routings;

namespace ForgeFlow.Application.Routings.Slices;

/// <summary>
/// Operational vertical slice for Work Centers catalog, capacity definition, and load inquiries.
/// </summary>
public sealed class WorkCenterSlices(
    IRoutingOperationsStore store,
    RoutingSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<WorkCenterSummary>>> ListWorkCentersAsync(
        OrganizationScope? scope,
        long organizationId,
        WorkCenterListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<WorkCenterSummary>>.Failure(auth.Error);
        if (!RoutingSliceSupport.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<WorkCenterSummary>>.Failure(RoutingErrors.AccessDenied);

        return await RoutingSliceSupport.ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<WorkCenterSummary>>.Success(
                await store.ListWorkCenters(organizationId, RoutingSliceSupport.Bound(query, forcedSiteId), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<WorkCenterSummary>>> ListActiveWorkCentersAsync(
        OrganizationScope? scope,
        long organizationId,
        long? siteId = null,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<WorkCenterSummary>>.Failure(auth.Error);
        if (!RoutingSliceSupport.TryResolveScope(scope, organizationId, siteId, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<WorkCenterSummary>>.Failure(RoutingErrors.AccessDenied);

        return await RoutingSliceSupport.ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<WorkCenterSummary>>.Success(
                await store.ListActiveWorkCenters(organizationId, forcedSiteId, ct)));
    }

    public async Task<OrganizationResult<WorkCenterSummary>> GetWorkCenterAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<WorkCenterSummary>.Failure(auth.Error);
        if (!RoutingSliceSupport.TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);

        return await RoutingSliceSupport.ExecuteStore(async () =>
        {
            var wc = await store.GetWorkCenter(organizationId, id, ct);
            if (wc is null) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
            if (forcedSiteId.HasValue && wc.SiteId != forcedSiteId.Value)
                return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.AccessDenied);
            return OrganizationResult<WorkCenterSummary>.Success(wc);
        });
    }

    public async Task<OrganizationResult<WorkCenterSummary>> CreateWorkCenterAsync(
        OrganizationScope? scope,
        CreateWorkCenterRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<WorkCenterSummary> res;
        if (auth.Error is not null) res = OrganizationResult<WorkCenterSummary>.Failure(auth.Error);
        else if (request.SiteId <= 0) res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.SiteInvalid);
        else if (!RoutingSliceSupport.TryResolveScope(scope, request.OrganizationId, request.SiteId, out _))
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.AccessDenied);
        else
            res = await RoutingSliceSupport.ExecuteStore(() =>
            {
                _ = WorkCenter.Create(
                    request.OrganizationId,
                    request.SiteId,
                    request.Code,
                    request.Name,
                    request.WorkCenterType,
                    request.CapacityUnitsPerHour,
                    request.DailyAvailableHours,
                    request.HourlyCostRate,
                    request.Notes);

                return store.CreateWorkCenter(request, support.Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null,
            "WorkCenter", request.Code ?? string.Empty, "CreateWorkCenter");
        return res;
    }

    public async Task<OrganizationResult<WorkCenterSummary>> UpdateWorkCenterAsync(
        OrganizationScope? scope,
        UpdateWorkCenterRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<WorkCenterSummary> res;
        if (auth.Error is not null) res = OrganizationResult<WorkCenterSummary>.Failure(auth.Error);
        else if (!RoutingSliceSupport.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
        else if (RoutingSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await RoutingSliceSupport.ExecuteStore(async () =>
            {
                if (!Enum.IsDefined(request.WorkCenterType))
                    return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.TypeInvalid);
                _ = RoutingText.RequiredName(request.Name);
                _ = RoutingText.PositiveCapacity(request.CapacityUnitsPerHour);
                _ = RoutingText.DailyHours(request.DailyAvailableHours);
                _ = RoutingText.OptionalHourlyCost(request.HourlyCostRate);
                _ = RoutingText.OptionalNotes(request.Notes);

                var existing = await store.GetWorkCenter(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
                if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                    return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.AccessDenied);

                return await store.UpdateWorkCenter(request.OrganizationId, request.Id, version, request,
                    support.Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "WorkCenter", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateWorkCenter");
        return res;
    }

    public async Task<OrganizationResult<WorkCenterSummary>> SetWorkCenterActiveAsync(
        OrganizationScope? scope,
        SetWorkCenterActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<WorkCenterSummary> res;
        if (auth.Error is not null) res = OrganizationResult<WorkCenterSummary>.Failure(auth.Error);
        else if (!RoutingSliceSupport.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
        else if (RoutingSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await RoutingSliceSupport.ExecuteStore(async () =>
            {
                var existing = await store.GetWorkCenter(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
                if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                    return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.AccessDenied);

                return await store.SetWorkCenterActive(request.OrganizationId, request.Id, version, request.IsActive,
                    support.Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "WorkCenter", request.Id.ToString(CultureInfo.InvariantCulture), request.IsActive ? "ActivateWorkCenter" : "DeactivateWorkCenter");
        return res;
    }

    public async Task<OrganizationResult<IReadOnlyList<WorkCenterLoadSummary>>> GetWorkCenterLoadsAsync(
        OrganizationScope? scope,
        long organizationId,
        long? siteId = null,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<WorkCenterLoadSummary>>.Failure(auth.Error);
        if (!RoutingSliceSupport.TryResolveScope(scope, organizationId, siteId, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<WorkCenterLoadSummary>>.Failure(RoutingErrors.AccessDenied);

        return await RoutingSliceSupport.ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<WorkCenterLoadSummary>>.Success(
                await store.GetWorkCenterLoads(organizationId, forcedSiteId, ct)));
    }
}
