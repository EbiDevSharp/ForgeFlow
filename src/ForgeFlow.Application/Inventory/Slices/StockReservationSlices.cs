using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Inventory.Slices;

public sealed class StockReservationSlices(
    IStockReservationStore store,
    IInventoryOperationsStore inventoryStore,
    InventorySliceSupport support,
    TimeProvider? clock = null)
{
    private DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static ReservationListQuery Bound(ReservationListQuery query, long? forcedSiteId) => query with
    {
        SiteId = forcedSiteId,
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    public async Task<OrganizationResult<OrganizationPage<StockReservationSummary>>> ListReservationsAsync(
        OrganizationScope? scope,
        long organizationId,
        ReservationListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead, "Sales.Read");
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<StockReservationSummary>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<StockReservationSummary>>.Failure(InventoryErrors.AccessDenied);

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<StockReservationSummary>>.Success(
                await store.ListReservations(organizationId, Bound(query, forcedSiteId), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ListOrderReservationsAsync(
        OrganizationScope? scope,
        long organizationId,
        long productionOrderId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead, "Sales.Read");
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, null, out var forcedSiteId) || productionOrderId <= 0)
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);

        return await support.ExecuteStoreAsync(async () =>
        {
            var po = await inventoryStore.GetProductionOrderLookup(organizationId, productionOrderId, ct);
            if (po is null) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);
            if (forcedSiteId.HasValue && po.SiteId != forcedSiteId.Value)
                return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.AccessDenied);
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Success(
                await store.ListOrderReservations(organizationId, productionOrderId, ct));
        });
    }

    public async Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ListSalesOrderReservationsAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, InventoryPermissions.InventoryRead, "Sales.Read");
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, null, out _) || salesOrderId <= 0)
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.AccessDenied);

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<IReadOnlyList<StockReservationSummary>>.Success(
                await store.ListSalesOrderReservations(organizationId, salesOrderId, ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveStockAsync(
        OrganizationScope? scope,
        ReserveStockRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, InventoryPermissions.InventoryManage, ProductionOrderPermissions.ProductionOrdersManage, "Sales.ManageOrders");
        OrganizationResult<IReadOnlyList<StockReservationSummary>> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(auth.Error);
        }
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.ProductionOrderId <= 0)
        {
            res = OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);
        }
        else
        {
            res = await support.ExecuteStoreAsync(async () =>
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
                    support.Access(auth.Actor!, scope!, auth.Permission!), ct);
            });
        }

        await support.RecordIfConsequentialRejectionAsync(
            res,
            auth.Actor,
            scope,
            request.OrganizationId,
            null,
            "StockReservation",
            request.ProductionOrderId.ToString(CultureInfo.InvariantCulture),
            new { Action = "ReserveStock", request.ItemId });
        return res;
    }

    public async Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveSalesOrderStockAsync(
        OrganizationScope? scope,
        ReserveSalesOrderStockRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, InventoryPermissions.InventoryManage, "Sales.ManageOrders");
        OrganizationResult<IReadOnlyList<StockReservationSummary>> res;
        if (auth.Error is not null) res = OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(auth.Error);
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out _))
            res = OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.AccessDenied);
        else if (request.SalesOrderId <= 0)
            res = OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);
        else if (request.ItemId <= 0)
            res = OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ItemInvalid);
        else
        {
            res = await support.ExecuteStoreAsync(async () =>
            {
                var qty = InventoryText.ValidatePositiveQuantity(request.Quantity);
                var contexts = await inventoryStore.LoadItemUnitContexts(request.OrganizationId, [request.ItemId], ct);
                if (!contexts.TryGetValue(request.ItemId, out var itemCtx) || !itemCtx.ItemIsActive)
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
                return await store.ReserveSalesOrderStock(normalizedReq, baseQty, itemCtx.BaseUnitId, UtcNow,
                    support.Access(auth.Actor!, scope!, auth.Permission!), ct);
            });
        }

        await OperationAuditHelper.RecordIfRejectedAsync(
            res,
            null, // operationRecorder
            null, // opContext
            UserOperationCatalog.SalesActionRejected,
            auth.Actor,
            scope,
            request.OrganizationId,
            null,
            "StockReservation",
            request.SalesOrderId.ToString(CultureInfo.InvariantCulture),
            new { Action = "ReserveSalesOrderStock", request.ItemId });
        return res;
    }

    public async Task<OrganizationResult<StockReservationSummary>> ReleaseReservationAsync(
        OrganizationScope? scope,
        ReleaseReservationRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, InventoryPermissions.InventoryManage, ProductionOrderPermissions.ProductionOrdersManage, "Sales.ManageOrders");
        OrganizationResult<StockReservationSummary> res;
        if (auth.Error is not null) res = OrganizationResult<StockReservationSummary>.Failure(auth.Error);
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.ReservationId <= 0)
            res = OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ReservationNotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(async () =>
            {
                var existing = await store.GetReservation(request.OrganizationId, request.ReservationId, ct);
                if (existing is null) return OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ReservationNotFound);
                if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                    return OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.AccessDenied);

                return await store.ReleaseReservation(request.OrganizationId, request.ReservationId, version, UtcNow,
                    support.Access(auth.Actor!, scope!, auth.Permission!), ct);
            });

        await support.RecordIfConsequentialRejectionAsync(
            res,
            auth.Actor,
            scope,
            request.OrganizationId,
            null,
            "StockReservation",
            request.ReservationId.ToString(CultureInfo.InvariantCulture),
            new { Action = "ReleaseReservation" });
        return res;
    }
}
