using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.ProductionOrders;

/// <summary>
/// Secured use cases for site-scoped Production Orders and immutable Day 11 BOM snapshots.
/// </summary>
public sealed class ProductionOrderUseCases(
    IProductionOrderOperationsStore store,
    IBomOperationsStore bomStore,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null)
{
    private readonly BomUseCases bomEngine = new(bomStore, actors, permissions);

    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, ProductionOrderErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, ProductionOrderErrors.ActorRequired);
        if (scope is null) return (null, ProductionOrderErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, ProductionOrderErrors.AccessDenied);
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

    private static ProductionOrderListQuery Bound(ProductionOrderListQuery query, long? forcedSiteId) => query with
    {
        SiteId = forcedSiteId,
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (ProductionOrderRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (BomRuleException ex) { return OrganizationResult<T>.Failure(MapBomError(ex.Code), ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(MapUnitError(ex.Code), ex.Parameters); }
        catch (BomStoreUnavailableException) { return OrganizationResult<T>.Failure(ProductionOrderErrors.PersistenceUnavailable); }
        catch (ProductionOrderStoreUnavailableException) { return OrganizationResult<T>.Failure(ProductionOrderErrors.PersistenceUnavailable); }
    }

    private static string MapBomError(string code) => code switch
    {
        BomErrors.NoActiveBomFound => ProductionOrderErrors.NoActiveBomFound,
        BomErrors.AmbiguousResolution => ProductionOrderErrors.AmbiguousResolution,
        BomErrors.PrimaryItemInvalid or BomErrors.InputItemInvalid or BomErrors.OutputItemInvalid => ProductionOrderErrors.ItemInvalid,
        BomErrors.PrimaryItemNotManufacturable => ProductionOrderErrors.ItemNotManufacturable,
        BomErrors.UnitInvalid => ProductionOrderErrors.UnitInvalid,
        BomErrors.QuantityInvalid or BomErrors.BaseOutputQuantityInvalid => ProductionOrderErrors.QuantityInvalid,
        _ => ProductionOrderErrors.NoActiveBomFound
    };

    private static string MapUnitError(string code) => code switch
    {
        UnitErrors.ConversionQuantityInvalid => ProductionOrderErrors.QuantityInvalid,
        _ => ProductionOrderErrors.UnitInvalid
    };

    public async Task<OrganizationResult<OrganizationPage<ProductionOrderSummary>>> ListOrders(
        OrganizationScope? scope,
        long organizationId,
        ProductionOrderListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ProductionOrderSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<ProductionOrderSummary>>.Failure(ProductionOrderErrors.AccessDenied);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<ProductionOrderSummary>>.Success(
                await store.ListOrders(organizationId, Bound(query, forcedSiteId), ct)));
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> GetOrder(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersRead, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);

        return await ExecuteStore(async () =>
        {
            var order = await store.GetOrder(organizationId, id, ct);
            if (order is null)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && order.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);
            return OrganizationResult<ProductionOrderDetails>.Success(order);
        });
    }

    public async Task<OrganizationResult<IReadOnlyList<ProductionOrderSiteInfo>>> ListAuthorizedSites(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ProductionOrderSiteInfo>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<ProductionOrderSiteInfo>>.Failure(ProductionOrderErrors.AccessDenied);

        return await ExecuteStore(async () =>
        {
            var all = await store.ListActiveSites(organizationId, ct);
            if (forcedSiteId.HasValue)
                all = all.Where(s => s.Id == forcedSiteId.Value).ToArray();
            return OrganizationResult<IReadOnlyList<ProductionOrderSiteInfo>>.Success(all);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> CreateDraftOrder(
        OrganizationScope? scope,
        CreateProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (request.SiteId <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.SiteInvalid);
        if (!TryResolveScope(scope, request.OrganizationId, request.SiteId, out _))
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);

        return await ExecuteStore(async () =>
        {
            if (request.PlannedDueDate < request.PlannedStartDate)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.DatesInvalid);

            _ = ProductionOrderText.OrderNumber(request.OrderNumber);
            _ = ProductionOrderText.ValidatePositiveQuantity(request.RequestedQuantity);
            _ = ProductionOrderText.OptionalText(request.Notes, ProductionOrderErrors.NotesInvalid);

            var siteInfo = await store.GetSiteInfo(request.OrganizationId, request.SiteId, ct);
            if (siteInfo is null)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.SiteInvalid);
            if (!siteInfo.OrganizationIsActive)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ParentOrganizationInactive);
            if (!siteInfo.IsActive)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.SiteInactive);

            var snapshotRes = await BuildSnapshotAsync(
                request.OrganizationId,
                siteInfo.Code,
                request.ItemId,
                request.PlannedStartDate,
                request.RequestedQuantity,
                request.RequestedUnitId,
                request.ContextValues,
                ct);
            if (!snapshotRes.Succeeded)
                return OrganizationResult<ProductionOrderDetails>.Failure(snapshotRes.Error!.Code, snapshotRes.Error.Parameters);

            return await store.CreateDraft(request, snapshotRes.Value!, Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> UpdateDraftOrder(
        OrganizationScope? scope,
        UpdateDraftProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await ExecuteStore(async () =>
        {
            if (request.PlannedDueDate < request.PlannedStartDate)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.DatesInvalid);

            _ = ProductionOrderText.ValidatePositiveQuantity(request.RequestedQuantity);
            _ = ProductionOrderText.OptionalText(request.Notes, ProductionOrderErrors.NotesInvalid);

            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);
            if (existing.Summary.Status != ProductionOrderStatus.Draft)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.OrderImmutable);

            var snapshotRes = await BuildSnapshotAsync(
                request.OrganizationId,
                existing.Summary.SiteCode,
                existing.Summary.ItemId,
                request.PlannedStartDate,
                request.RequestedQuantity,
                request.RequestedUnitId,
                request.ContextValues,
                ct);
            if (!snapshotRes.Succeeded)
                return OrganizationResult<ProductionOrderDetails>.Failure(snapshotRes.Error!.Code, snapshotRes.Error.Parameters);

            return await store.UpdateDraft(request.OrganizationId, request.Id, version, request, snapshotRes.Value!,
                Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> RefreshDraftSnapshot(
        OrganizationScope? scope,
        RefreshProductionOrderSnapshotRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await ExecuteStore(async () =>
        {
            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);
            if (existing.Summary.Status != ProductionOrderStatus.Draft)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.OrderImmutable);

            var ctx = existing.Contexts.ToDictionary(c => c.DimensionCode, c => c.DimensionValue, StringComparer.OrdinalIgnoreCase);
            var snapshotRes = await BuildSnapshotAsync(
                request.OrganizationId,
                existing.Summary.SiteCode,
                existing.Summary.ItemId,
                existing.Summary.PlannedStartDate,
                existing.Summary.RequestedQuantity,
                existing.Summary.RequestedUnitId,
                ctx,
                ct);
            if (!snapshotRes.Succeeded)
                return OrganizationResult<ProductionOrderDetails>.Failure(snapshotRes.Error!.Code, snapshotRes.Error.Parameters);

            return await store.RefreshDraftSnapshot(request.OrganizationId, request.Id, version, snapshotRes.Value!,
                Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> ReleaseOrder(
        OrganizationScope? scope,
        ReleaseProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await ExecuteStore(async () =>
        {
            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);
            if (existing.Summary.Status != ProductionOrderStatus.Draft)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.TransitionInvalid);

            // Re-validate and freeze the active BOM snapshot immediately before leaving Draft (Q-P02).
            var ctx = existing.Contexts.ToDictionary(c => c.DimensionCode, c => c.DimensionValue, StringComparer.OrdinalIgnoreCase);
            var snapshotRes = await BuildSnapshotAsync(
                request.OrganizationId,
                existing.Summary.SiteCode,
                existing.Summary.ItemId,
                existing.Summary.PlannedStartDate,
                existing.Summary.RequestedQuantity,
                existing.Summary.RequestedUnitId,
                ctx,
                ct);
            if (!snapshotRes.Succeeded)
                return OrganizationResult<ProductionOrderDetails>.Failure(snapshotRes.Error!.Code, snapshotRes.Error.Parameters);

            return await store.ReleaseOrder(request.OrganizationId, request.Id, version, snapshotRes.Value!, DateTime.UtcNow,
                Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> StartOrder(
        OrganizationScope? scope,
        StartProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await ExecuteStore(async () =>
        {
            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);

            return await store.StartOrder(request.OrganizationId, request.Id, version, DateTime.UtcNow,
                Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> RecordActuals(
        OrganizationScope? scope,
        RecordProductionOrderActualsRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await ExecuteStore(async () =>
        {
            _ = ProductionOrderText.ValidateNonNegativeQuantity(request.ActualProducedQuantity);
            _ = ProductionOrderText.ValidateNonNegativeQuantity(request.ActualScrapQuantity);
            if (request.Notes is not null)
                _ = ProductionOrderText.OptionalText(request.Notes, ProductionOrderErrors.NotesInvalid);

            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);

            return await store.RecordActuals(request.OrganizationId, request.Id, version,
                request.ActualProducedQuantity, request.ActualScrapQuantity, request.Notes,
                Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> CompleteOrder(
        OrganizationScope? scope,
        CompleteProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await ExecuteStore(async () =>
        {
            _ = ProductionOrderText.ValidateNonNegativeQuantity(request.ActualProducedQuantity);
            _ = ProductionOrderText.ValidateNonNegativeQuantity(request.ActualScrapQuantity);
            if (request.Notes is not null)
                _ = ProductionOrderText.OptionalText(request.Notes, ProductionOrderErrors.NotesInvalid);

            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);

            return await store.CompleteOrder(request.OrganizationId, request.Id, version,
                request.ActualProducedQuantity, request.ActualScrapQuantity, request.Notes, DateTime.UtcNow,
                Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> CancelOrder(
        OrganizationScope? scope,
        CancelProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await ExecuteStore(async () =>
        {
            _ = ProductionOrderText.RequiredReason(request.Reason);

            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);

            return await store.CancelOrder(request.OrganizationId, request.Id, version, request.Reason, DateTime.UtcNow,
                Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    private async Task<OrganizationResult<ResolvedBomSnapshotWrite>> BuildSnapshotAsync(
        long organizationId,
        string siteCode,
        long itemId,
        DateOnly plannedStartDate,
        decimal requestedQuantity,
        long? requestedUnitId,
        IReadOnlyDictionary<string, string>? contextValues,
        CancellationToken ct)
    {
        var mergedContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (contextValues is not null)
        {
            foreach (var (k, v) in contextValues)
            {
                if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                    mergedContext[k.Trim().ToUpperInvariant()] = v.Trim();
            }
        }
        if (!mergedContext.ContainsKey("PLANT") && !string.IsNullOrWhiteSpace(siteCode))
            mergedContext["PLANT"] = siteCode.Trim();

        var core = await bomEngine.ResolveAndExplodeCoreAsync(
            organizationId, itemId, plannedStartDate, requestedQuantity, requestedUnitId, mergedContext, ct);
        if (!core.Succeeded)
            return OrganizationResult<ResolvedBomSnapshotWrite>.Failure(MapBomError(core.Error!.Code), core.Error.Parameters);

        var (res, exp) = core.Value!;
        return OrganizationResult<ResolvedBomSnapshotWrite>.Success(new ResolvedBomSnapshotWrite(
            res.BomId,
            res.BomCode,
            res.BomVersionId,
            res.VersionNumber,
            res.BaseOutputQuantity,
            res.BaseOutputUnitId,
            exp.RequestedUnitId,
            exp.ConsumedInputs,
            exp.ProducedOutputs,
            mergedContext));
    }
}
