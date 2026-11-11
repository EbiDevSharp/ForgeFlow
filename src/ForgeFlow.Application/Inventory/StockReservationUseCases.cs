using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Inventory;

public sealed class StockReservationUseCases(
    IStockReservationStore store,
    IInventoryOperationsStore inventoryStore,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private async ValueTask<(TrustedActor? Actor, string? Permission, string? Error)> AuthorizeAny(
        OrganizationScope? scope,
        CancellationToken ct,
        params string[] allowedPermissions)
    {
        if (actors is null) return (null, null, InventoryErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, null, InventoryErrors.ActorRequired);
        if (scope is null) return (null, null, InventoryErrors.ScopeRequired);
        if (permissions is null) return (null, null, InventoryErrors.AccessDenied);

        foreach (var perm in allowedPermissions)
        {
            if ((await permissions.EvaluateAsync(actor, scope, perm, ct)).IsAllowed)
                return (actor, perm, null);
        }
        return (null, null, InventoryErrors.AccessDenied);
    }

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await AuthorizeAny(scope, ct, permission);
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

    private static ReservationListQuery Bound(ReservationListQuery query, long? forcedSiteId) => query with
    {
        SiteId = forcedSiteId,
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code == UnitErrors.ConversionQuantityInvalid ? InventoryErrors.QuantityInvalid : InventoryErrors.UnitInvalid, ex.Parameters); }
        catch (InventoryStoreUnavailableException) { return OrganizationResult<T>.Failure(InventoryErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<OrganizationPage<StockReservationSummary>>> ListReservations(
        OrganizationScope? scope,
        long organizationId,
        ReservationListQuery query,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<StockReservationSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<StockReservationSummary>>.Failure(InventoryErrors.AccessDenied);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<StockReservationSummary>>.Success(
                await store.ListReservations(organizationId, Bound(query, forcedSiteId), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ListOrderReservations(
        OrganizationScope? scope,
        long organizationId,
        long productionOrderId,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId) || productionOrderId <= 0)
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);

        return await ExecuteStore(async () =>
        {
            var po = await inventoryStore.GetProductionOrderLookup(organizationId, productionOrderId, ct);
            if (po is null) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);
            if (forcedSiteId.HasValue && po.SiteId != forcedSiteId.Value)
                return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.AccessDenied);
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Success(
                await store.ListOrderReservations(organizationId, productionOrderId, ct));
        });
    }

    public async Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveStock(
        OrganizationScope? scope,
        ReserveStockRequest request,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, InventoryPermissions.InventoryManage, ProductionOrderPermissions.ProductionOrdersManage);
        OrganizationResult<IReadOnlyList<StockReservationSummary>> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(auth.Error);
        }
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.ProductionOrderId <= 0)
        {
            res = OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);
        }
        else
        {
            res = await ExecuteStore(async () =>
            {
                _ = InventoryText.OptionalNote(request.Notes);
                var qty = InventoryText.ValidatePositiveQuantity(request.Quantity);
                if (request.ItemId <= 0) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ItemInvalid);

                var po = await inventoryStore.GetProductionOrderLookup(request.OrganizationId, request.ProductionOrderId, ct);
                if (po is null) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);
                if (forcedSiteId.HasValue && po.SiteId != forcedSiteId.Value)
                    return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.AccessDenied);
                if (po.Status is not (ProductionOrderStatus.Released or ProductionOrderStatus.InProgress))
                    return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ReservationNotAllowedForOrderState);

                var itemContexts = await inventoryStore.LoadItemUnitContexts(request.OrganizationId, [request.ItemId], ct);
                if (!itemContexts.TryGetValue(request.ItemId, out var itemCtx) || !itemCtx.ItemIsActive)
                    return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ItemInvalid);

                var unitId = request.UnitId ?? itemCtx.BaseUnitId;
                if (unitId != itemCtx.BaseUnitId)
                {
                    var activeUnits = await inventoryStore.LoadActiveUnitIds(request.OrganizationId, [unitId], ct);
                    if (!activeUnits.Contains(unitId))
                        return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.UnitInvalid);
                }

                var baseQty = unitId == itemCtx.BaseUnitId
                    ? qty
                    : UnitConversionGraph.ConvertQuantity(qty, unitId, itemCtx.BaseUnitId, itemCtx.Edges).ConvertedQuantity;
                baseQty = InventoryText.ValidatePositiveQuantity(baseQty);

                var normalizedLot = InventoryText.LotCode(request.LotCode);
                if (!string.IsNullOrEmpty(request.LotCode) && string.IsNullOrEmpty(normalizedLot))
                    return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.LotCodeInvalid);

                var normalizedReq = request with { LotCode = string.IsNullOrEmpty(normalizedLot) ? null : normalizedLot };
                return await store.ReserveStock(normalizedReq, baseQty, itemCtx.BaseUnitId, UtcNow,
                    Access(auth.Actor!, scope!, auth.Permission!), ct);
            });
        }

        await OperationAuditHelper.RecordIfRejectedAsync(res, operationRecorder, opContext, UserOperationCatalog.InventoryActionRejected,
            auth.Actor, scope, request.OrganizationId, null, "StockReservation",
            request.ProductionOrderId.ToString(CultureInfo.InvariantCulture), new { Action = "ReserveStock", request.ItemId });
        return res;
    }

    public async Task<OrganizationResult<StockReservationSummary>> ReleaseReservation(
        OrganizationScope? scope,
        ReleaseReservationRequest request,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, InventoryPermissions.InventoryManage, ProductionOrderPermissions.ProductionOrdersManage);
        OrganizationResult<StockReservationSummary> res;
        if (auth.Error is not null) res = OrganizationResult<StockReservationSummary>.Failure(auth.Error);
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.ReservationId <= 0)
            res = OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ReservationNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(async () =>
            {
                var existing = await store.GetReservation(request.OrganizationId, request.ReservationId, ct);
                if (existing is null) return OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ReservationNotFound);
                if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                    return OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.AccessDenied);

                return await store.ReleaseReservation(request.OrganizationId, request.ReservationId, version, UtcNow,
                    Access(auth.Actor!, scope!, auth.Permission!), ct);
            });

        await OperationAuditHelper.RecordIfRejectedAsync(res, operationRecorder, opContext, UserOperationCatalog.InventoryActionRejected,
            auth.Actor, scope, request.OrganizationId, null, "StockReservation",
            request.ReservationId.ToString(CultureInfo.InvariantCulture), new { Action = "ReleaseReservation" });
        return res;
    }
}
