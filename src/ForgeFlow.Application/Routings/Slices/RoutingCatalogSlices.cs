using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Routings;

namespace ForgeFlow.Application.Routings.Slices;

/// <summary>
/// Operational vertical slice for Routing templates, version lifecycle, and sequence operations configuration.
/// </summary>
public sealed class RoutingCatalogSlices(
    IRoutingOperationsStore store,
    RoutingSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<RoutingSummary>>> ListRoutingsAsync(
        OrganizationScope? scope,
        long organizationId,
        RoutingListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<RoutingSummary>>.Failure(auth.Error);
        if (!RoutingSliceSupport.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<RoutingSummary>>.Failure(RoutingErrors.AccessDenied);

        return await RoutingSliceSupport.ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<RoutingSummary>>.Success(
                await store.ListRoutings(organizationId, RoutingSliceSupport.Bound(query, forcedSiteId), ct)));
    }

    public async Task<OrganizationResult<RoutingDetails>> GetRoutingAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<RoutingDetails>.Failure(auth.Error);
        if (!RoutingSliceSupport.TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);

        return await RoutingSliceSupport.ExecuteStore(async () =>
        {
            var routing = await store.GetRouting(organizationId, id, ct);
            if (routing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
            if (forcedSiteId.HasValue && routing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);
            return OrganizationResult<RoutingDetails>.Success(routing);
        });
    }

    public async Task<OrganizationResult<IReadOnlyList<RoutingSummary>>> ListActiveRoutingsForItemAsync(
        OrganizationScope? scope,
        long organizationId,
        long siteId,
        long itemId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<RoutingSummary>>.Failure(auth.Error);
        if (siteId <= 0) return OrganizationResult<IReadOnlyList<RoutingSummary>>.Failure(RoutingErrors.SiteInvalid);
        if (!RoutingSliceSupport.TryResolveScope(scope, organizationId, siteId, out _))
            return OrganizationResult<IReadOnlyList<RoutingSummary>>.Failure(RoutingErrors.AccessDenied);
        if (itemId <= 0) return OrganizationResult<IReadOnlyList<RoutingSummary>>.Success([]);

        return await RoutingSliceSupport.ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<RoutingSummary>>.Success(
                await store.ListActiveRoutingsForItem(organizationId, siteId, itemId, ct)));
    }

    public async Task<OrganizationResult<RoutingDetails>> CreateDraftRoutingAsync(
        OrganizationScope? scope,
        CreateRoutingRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (request.SiteId <= 0) res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.SiteInvalid);
        else if (!RoutingSliceSupport.TryResolveScope(scope, request.OrganizationId, request.SiteId, out _))
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);
        else
            res = await RoutingSliceSupport.ExecuteStore(() =>
            {
                _ = Routing.CreateDraft(
                    request.OrganizationId,
                    request.SiteId,
                    request.Code,
                    request.Name,
                    request.ItemId,
                    request.BomVersionId,
                    request.VersionNumber,
                    request.IsDefault,
                    request.Notes);

                return store.CreateDraftRouting(request, support.Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null,
            "Routing", request.Code ?? string.Empty, "CreateDraftRouting");
        return res;
    }

    public async Task<OrganizationResult<RoutingDetails>> UpdateDraftRoutingAsync(
        OrganizationScope? scope,
        UpdateDraftRoutingRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (!RoutingSliceSupport.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        else if (RoutingSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await RoutingSliceSupport.ExecuteStore(async () =>
            {
                _ = RoutingText.RequiredName(request.Name);
                _ = RoutingText.OptionalNotes(request.Notes);

                var existing = await store.GetRouting(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);

                return await store.UpdateDraftRouting(request.OrganizationId, request.Id, version, request,
                    support.Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "Routing", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateDraftRouting");
        return res;
    }

    public async Task<OrganizationResult<RoutingDetails>> SaveRoutingOperationsAsync(
        OrganizationScope? scope,
        SaveRoutingOperationsRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (!RoutingSliceSupport.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.RoutingId <= 0)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        else if (RoutingSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await RoutingSliceSupport.ExecuteStore(async () =>
            {
                if (request.Operations is null || request.Operations.Count == 0)
                    return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.EmptyOperations);

                var seenSeq = new HashSet<int>();
                foreach (var op in request.Operations)
                {
                    if (op.Sequence <= 0) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.SequenceInvalid);
                    if (!seenSeq.Add(op.Sequence)) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.DuplicateSequence);
                    if (op.WorkCenterId <= 0) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.WorkCenterNotFound);
                    _ = RoutingText.Code(op.OperationCode, RoutingErrors.OperationCodeInvalid);
                    _ = RoutingText.RequiredName(op.OperationName);
                    _ = RoutingText.NonNegativeMinutes(op.SetupMinutes);
                    _ = RoutingText.PositiveRunMinutesPerUnit(op.RunMinutesPerUnit);
                    _ = RoutingText.NonNegativeMinutes(op.QueueMinutes);
                    _ = RoutingText.NonNegativeMinutes(op.WaitMinutes);
                    _ = RoutingText.OptionalNotes(op.Instructions);
                }

                var existing = await store.GetRouting(request.OrganizationId, request.RoutingId, ct);
                if (existing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);

                return await store.SaveRoutingOperations(request.OrganizationId, request.RoutingId, version, request.Operations,
                    support.Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "Routing", request.RoutingId.ToString(CultureInfo.InvariantCulture), "SaveRoutingOperations");
        return res;
    }

    public async Task<OrganizationResult<RoutingDetails>> ActivateRoutingAsync(
        OrganizationScope? scope,
        ActivateRoutingRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (!RoutingSliceSupport.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        else if (RoutingSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await RoutingSliceSupport.ExecuteStore(async () =>
            {
                var existing = await store.GetRouting(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);

                return await store.ActivateRouting(request.OrganizationId, request.Id, version, DateTime.UtcNow,
                    support.Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "Routing", request.Id.ToString(CultureInfo.InvariantCulture), "ActivateRouting");
        return res;
    }

    public async Task<OrganizationResult<RoutingDetails>> MarkRoutingObsoleteAsync(
        OrganizationScope? scope,
        MarkRoutingObsoleteRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (!RoutingSliceSupport.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        else if (RoutingSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await RoutingSliceSupport.ExecuteStore(async () =>
            {
                var existing = await store.GetRouting(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);

                return await store.MarkRoutingObsolete(request.OrganizationId, request.Id, version,
                    support.Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "Routing", request.Id.ToString(CultureInfo.InvariantCulture), "MarkRoutingObsolete");
        return res;
    }
}
