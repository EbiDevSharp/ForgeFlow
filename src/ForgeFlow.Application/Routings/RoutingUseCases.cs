using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Routings;

namespace ForgeFlow.Application.Routings;

/// <summary>
/// Secured use cases for site-scoped Work Centers, Routings, and shop-floor ProductionOrderOperations.
/// </summary>
public sealed class RoutingUseCases(
    IRoutingOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, RoutingErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, RoutingErrors.ActorRequired);
        if (scope is null) return (null, RoutingErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, RoutingErrors.AccessDenied);
        return (actor, null);
    }

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    private static bool TryResolveScope(OrganizationScope? scope, long requestedOrgId, long? requestedSiteId, out long? forcedSiteId)
    {
        forcedSiteId = null;
        if (requestedOrgId <= 0) return false;
        switch (scope)
        {
            case OrganizationScope.Organization org when org.OrganizationId == requestedOrgId:
                forcedSiteId = requestedSiteId;
                return true;
            case OrganizationScope.Site site when site.OrganizationId == requestedOrgId:
                if (requestedSiteId.HasValue && requestedSiteId.Value != site.SiteId)
                    return false;
                forcedSiteId = site.SiteId;
                return true;
            default:
                return false;
        }
    }

    private static byte[]? DecodeVersion(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            var bytes = Convert.FromBase64String(raw);
            return bytes.Length == 8 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static WorkCenterListQuery Bound(WorkCenterListQuery query, long? forcedSiteId) => query with
    {
        SiteId = forcedSiteId,
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static RoutingListQuery Bound(RoutingListQuery query, long? forcedSiteId) => query with
    {
        SiteId = forcedSiteId,
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (RoutingRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (RoutingStoreUnavailableException) { return OrganizationResult<T>.Failure(RoutingErrors.PersistenceUnavailable); }
    }

    private Task RecordRejectedAsync<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long organizationId,
        long? siteId,
        string entityType,
        string entityRef,
        string action)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result,
            operationRecorder,
            opContext,
            UserOperationCatalog.RoutingActionRejected,
            actor,
            scope,
            organizationId,
            siteId,
            entityType,
            entityRef,
            new { Action = action });

    public async Task<OrganizationResult<OrganizationPage<WorkCenterSummary>>> ListWorkCenters(
        OrganizationScope? scope,
        long organizationId,
        WorkCenterListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<WorkCenterSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<WorkCenterSummary>>.Failure(RoutingErrors.AccessDenied);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<WorkCenterSummary>>.Success(
                await store.ListWorkCenters(organizationId, Bound(query, forcedSiteId), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<WorkCenterSummary>>> ListActiveWorkCenters(
        OrganizationScope? scope,
        long organizationId,
        long? siteId = null,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<WorkCenterSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, siteId, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<WorkCenterSummary>>.Failure(RoutingErrors.AccessDenied);

        return await ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<WorkCenterSummary>>.Success(
                await store.ListActiveWorkCenters(organizationId, forcedSiteId, ct)));
    }

    public async Task<OrganizationResult<WorkCenterSummary>> GetWorkCenter(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<WorkCenterSummary>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);

        return await ExecuteStore(async () =>
        {
            var wc = await store.GetWorkCenter(organizationId, id, ct);
            if (wc is null) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
            if (forcedSiteId.HasValue && wc.SiteId != forcedSiteId.Value)
                return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.AccessDenied);
            return OrganizationResult<WorkCenterSummary>.Success(wc);
        });
    }

    public async Task<OrganizationResult<WorkCenterSummary>> CreateWorkCenter(
        OrganizationScope? scope,
        CreateWorkCenterRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<WorkCenterSummary> res;
        if (auth.Error is not null) res = OrganizationResult<WorkCenterSummary>.Failure(auth.Error);
        else if (request.SiteId <= 0) res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.SiteInvalid);
        else if (!TryResolveScope(scope, request.OrganizationId, request.SiteId, out _))
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.AccessDenied);
        else
            res = await ExecuteStore(() =>
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

                return store.CreateWorkCenter(request, Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null,
            "WorkCenter", request.Code ?? string.Empty, "CreateWorkCenter");
        return res;
    }

    public async Task<OrganizationResult<WorkCenterSummary>> UpdateWorkCenter(
        OrganizationScope? scope,
        UpdateWorkCenterRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<WorkCenterSummary> res;
        if (auth.Error is not null) res = OrganizationResult<WorkCenterSummary>.Failure(auth.Error);
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(async () =>
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
                    Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "WorkCenter", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateWorkCenter");
        return res;
    }

    public async Task<OrganizationResult<WorkCenterSummary>> SetWorkCenterActive(
        OrganizationScope? scope,
        SetWorkCenterActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<WorkCenterSummary> res;
        if (auth.Error is not null) res = OrganizationResult<WorkCenterSummary>.Failure(auth.Error);
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(async () =>
            {
                var existing = await store.GetWorkCenter(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
                if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                    return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.AccessDenied);

                return await store.SetWorkCenterActive(request.OrganizationId, request.Id, version, request.IsActive,
                    Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "WorkCenter", request.Id.ToString(CultureInfo.InvariantCulture), request.IsActive ? "ActivateWorkCenter" : "DeactivateWorkCenter");
        return res;
    }

    public async Task<OrganizationResult<OrganizationPage<RoutingSummary>>> ListRoutings(
        OrganizationScope? scope,
        long organizationId,
        RoutingListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<RoutingSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<RoutingSummary>>.Failure(RoutingErrors.AccessDenied);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<RoutingSummary>>.Success(
                await store.ListRoutings(organizationId, Bound(query, forcedSiteId), ct)));
    }

    public async Task<OrganizationResult<RoutingDetails>> GetRouting(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<RoutingDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);

        return await ExecuteStore(async () =>
        {
            var routing = await store.GetRouting(organizationId, id, ct);
            if (routing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
            if (forcedSiteId.HasValue && routing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);
            return OrganizationResult<RoutingDetails>.Success(routing);
        });
    }

    public async Task<OrganizationResult<IReadOnlyList<RoutingSummary>>> ListActiveRoutingsForItem(
        OrganizationScope? scope,
        long organizationId,
        long siteId,
        long itemId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<RoutingSummary>>.Failure(auth.Error);
        if (siteId <= 0) return OrganizationResult<IReadOnlyList<RoutingSummary>>.Failure(RoutingErrors.SiteInvalid);
        if (!TryResolveScope(scope, organizationId, siteId, out _))
            return OrganizationResult<IReadOnlyList<RoutingSummary>>.Failure(RoutingErrors.AccessDenied);
        if (itemId <= 0) return OrganizationResult<IReadOnlyList<RoutingSummary>>.Success([]);

        return await ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<RoutingSummary>>.Success(
                await store.ListActiveRoutingsForItem(organizationId, siteId, itemId, ct)));
    }

    public async Task<OrganizationResult<RoutingDetails>> CreateDraftRouting(
        OrganizationScope? scope,
        CreateRoutingRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (request.SiteId <= 0) res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.SiteInvalid);
        else if (!TryResolveScope(scope, request.OrganizationId, request.SiteId, out _))
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);
        else
            res = await ExecuteStore(() =>
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

                return store.CreateDraftRouting(request, Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null,
            "Routing", request.Code ?? string.Empty, "CreateDraftRouting");
        return res;
    }

    public async Task<OrganizationResult<RoutingDetails>> UpdateDraftRouting(
        OrganizationScope? scope,
        UpdateDraftRoutingRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(async () =>
            {
                _ = RoutingText.RequiredName(request.Name);
                _ = RoutingText.OptionalNotes(request.Notes);

                var existing = await store.GetRouting(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);

                return await store.UpdateDraftRouting(request.OrganizationId, request.Id, version, request,
                    Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "Routing", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateDraftRouting");
        return res;
    }

    public async Task<OrganizationResult<RoutingDetails>> SaveRoutingOperations(
        OrganizationScope? scope,
        SaveRoutingOperationsRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.RoutingId <= 0)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(async () =>
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
                    Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "Routing", request.RoutingId.ToString(CultureInfo.InvariantCulture), "SaveRoutingOperations");
        return res;
    }

    public async Task<OrganizationResult<RoutingDetails>> ActivateRouting(
        OrganizationScope? scope,
        ActivateRoutingRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(async () =>
            {
                var existing = await store.GetRouting(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);

                return await store.ActivateRouting(request.OrganizationId, request.Id, version, DateTime.UtcNow,
                    Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "Routing", request.Id.ToString(CultureInfo.InvariantCulture), "ActivateRouting");
        return res;
    }

    public async Task<OrganizationResult<RoutingDetails>> MarkRoutingObsolete(
        OrganizationScope? scope,
        MarkRoutingObsoleteRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsManage, ct);
        OrganizationResult<RoutingDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoutingDetails>.Failure(auth.Error);
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(async () =>
            {
                var existing = await store.GetRouting(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.AccessDenied);

                return await store.MarkRoutingObsolete(request.OrganizationId, request.Id, version,
                    Access(auth.Actor!, scope!, RoutingPermissions.RoutingsManage), ct);
            });

        await RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null,
            "Routing", request.Id.ToString(CultureInfo.InvariantCulture), "MarkRoutingObsolete");
        return res;
    }

    public async Task<OrganizationResult<ProductionOrderOperationSummary>> StartOperation(
        OrganizationScope? scope,
        StartProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => await ExecuteOperationCommandAsync(
            scope,
            request.OrganizationId,
            request.OperationId,
            request.ExpectedRowVersion,
            "StartOperation",
            (version, access) =>
            {
                _ = RoutingText.OptionalNotes(request.Notes);
                return store.StartOperation(request.OrganizationId, request.OperationId, version, request.Notes, DateTime.UtcNow, access, ct);
            },
            ct);

    public async Task<OrganizationResult<ProductionOrderOperationSummary>> PauseOperation(
        OrganizationScope? scope,
        PauseProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => await ExecuteOperationCommandAsync(
            scope,
            request.OrganizationId,
            request.OperationId,
            request.ExpectedRowVersion,
            "PauseOperation",
            (version, access) =>
            {
                _ = RoutingText.NonNegativeMinutes(request.ActualSetupMinutes);
                _ = RoutingText.NonNegativeMinutes(request.ActualRunMinutes);
                _ = RoutingText.NonNegativeQuantity(request.CompletedQuantity);
                _ = RoutingText.NonNegativeQuantity(request.ScrapQuantity);
                _ = RoutingText.OptionalNotes(request.Notes);
                return store.PauseOperation(request.OrganizationId, request.OperationId, version,
                    request.ActualSetupMinutes, request.ActualRunMinutes, request.CompletedQuantity, request.ScrapQuantity,
                    request.Notes, access, ct);
            },
            ct);

    public async Task<OrganizationResult<ProductionOrderOperationSummary>> CompleteOperation(
        OrganizationScope? scope,
        CompleteProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => await ExecuteOperationCommandAsync(
            scope,
            request.OrganizationId,
            request.OperationId,
            request.ExpectedRowVersion,
            "CompleteOperation",
            (version, access) =>
            {
                _ = RoutingText.NonNegativeMinutes(request.ActualSetupMinutes);
                _ = RoutingText.NonNegativeMinutes(request.ActualRunMinutes);
                _ = RoutingText.NonNegativeQuantity(request.CompletedQuantity);
                _ = RoutingText.NonNegativeQuantity(request.ScrapQuantity);
                _ = RoutingText.OptionalNotes(request.Notes);
                return store.CompleteOperation(request.OrganizationId, request.OperationId, version,
                    request.ActualSetupMinutes, request.ActualRunMinutes, request.CompletedQuantity, request.ScrapQuantity,
                    request.Notes, DateTime.UtcNow, access, ct);
            },
            ct);

    public async Task<OrganizationResult<ProductionOrderOperationSummary>> SkipOperation(
        OrganizationScope? scope,
        SkipProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => await ExecuteOperationCommandAsync(
            scope,
            request.OrganizationId,
            request.OperationId,
            request.ExpectedRowVersion,
            "SkipOperation",
            (version, access) =>
            {
                _ = RoutingText.OptionalNotes(request.Notes);
                return store.SkipOperation(request.OrganizationId, request.OperationId, version, request.Notes, DateTime.UtcNow, access, ct);
            },
            ct);

    public async Task<OrganizationResult<IReadOnlyList<WorkCenterLoadSummary>>> GetWorkCenterLoads(
        OrganizationScope? scope,
        long organizationId,
        long? siteId = null,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RoutingPermissions.RoutingsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<WorkCenterLoadSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, siteId, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<WorkCenterLoadSummary>>.Failure(RoutingErrors.AccessDenied);

        return await ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<WorkCenterLoadSummary>>.Success(
                await store.GetWorkCenterLoads(organizationId, forcedSiteId, ct)));
    }

    private async Task<OrganizationResult<ProductionOrderOperationSummary>> ExecuteOperationCommandAsync(
        OrganizationScope? scope,
        long organizationId,
        long operationId,
        string? expectedRowVersion,
        string actionName,
        Func<byte[], OrganizationWriteAccess, Task<OrganizationResult<ProductionOrderOperationSummary>>> callback,
        CancellationToken ct)
    {
        var auth = await Authorize(scope, RoutingPermissions.OperationsExecute, ct);
        OrganizationResult<ProductionOrderOperationSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ProductionOrderOperationSummary>.Failure(auth.Error);
        else if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId) || operationId <= 0)
            res = OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.OperationNotFound);
        else if (DecodeVersion(expectedRowVersion) is not byte[] version)
            res = OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(async () =>
            {
                var existing = await store.GetProductionOrderOperation(organizationId, operationId, ct);
                if (existing is null) return OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.OperationNotFound);
                if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                    return OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.AccessDenied);

                return await callback(version, Access(auth.Actor!, scope!, RoutingPermissions.OperationsExecute));
            });

        await RecordRejectedAsync(res, auth.Actor, scope, organizationId, null,
            "ProductionOrderOperation", operationId.ToString(CultureInfo.InvariantCulture), actionName);
        return res;
    }
}
