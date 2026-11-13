using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Inventory.Slices;

public sealed class StockMovementSlices(
    IInventoryOperationsStore store,
    InventorySliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<InventoryBalanceSummary>>> ListBalancesAsync(
        OrganizationScope? scope,
        long organizationId,
        InventoryBalanceListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, InventoryPermissions.InventoryRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<InventoryBalanceSummary>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<InventoryBalanceSummary>>.Failure(InventoryErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<InventoryBalanceSummary>>.Success(
                await store.ListBalances(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<OrganizationPage<InventoryTransactionSummary>>> ListTransactionsAsync(
        OrganizationScope? scope,
        long organizationId,
        InventoryTransactionListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, InventoryPermissions.InventoryRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<InventoryTransactionSummary>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<InventoryTransactionSummary>>.Failure(InventoryErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<InventoryTransactionSummary>>.Success(
                await store.ListTransactions(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<InventoryTransactionDetails>> GetTransactionAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, InventoryPermissions.InventoryRead, ct);
        if (auth.Error is not null) return OrganizationResult<InventoryTransactionDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);

        return await support.ExecuteStoreAsync(async () =>
        {
            var details = await store.GetTransaction(organizationId, id, ct);
            if (details is null) return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);
            if (forcedSiteId.HasValue && details.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AccessDenied);
            return OrganizationResult<InventoryTransactionDetails>.Success(details);
        });
    }

    public async Task<OrganizationResult<InventoryTransactionDetails>> PostTransactionAsync(
        OrganizationScope? scope,
        PostInventoryTransactionRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, InventoryPermissions.InventoryManage, ct);
        if (auth.Error is not null) return OrganizationResult<InventoryTransactionDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId))
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AccessDenied);
        if (request.WarehouseId <= 0)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.WarehouseNotFound);
        if (request.TransactionType is InventoryTransactionType.Reversal or InventoryTransactionType.TransferIn or InventoryTransactionType.QualityScrap)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);
        if (request.Lines is null || request.Lines.Count == 0)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.LinesRequired);

        var res = await support.ExecuteStoreAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(request.DocumentNumber))
                _ = InventoryText.Code(request.DocumentNumber, InventoryErrors.DocumentNumberInvalid);
            _ = InventoryText.OptionalNote(request.ReferenceNote);
            _ = InventoryText.OptionalIdempotencyKey(request.IdempotencyKey);

            var wh = await store.GetWarehouseLookup(request.OrganizationId, request.WarehouseId, ct);
            if (wh is null) return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.WarehouseNotFound);
            if (forcedSiteId.HasValue && wh.SiteId != forcedSiteId.Value)
                return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AccessDenied);
            if (!wh.OrganizationIsActive) return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.ParentOrganizationInactive);
            if (!wh.SiteIsActive) return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.SiteInactive);
            if (!wh.IsActive) return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.WarehouseInactive);

            InventoryWarehouseLookup? destWh = null;
            if (request.TransactionType == InventoryTransactionType.TransferOut)
            {
                if (!request.DestinationWarehouseId.HasValue || request.DestinationWarehouseId.Value <= 0)
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.DestinationWarehouseInvalid);
                if (request.DestinationWarehouseId.Value == request.WarehouseId)
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransferSameWarehouse);

                destWh = await store.GetWarehouseLookup(request.OrganizationId, request.DestinationWarehouseId.Value, ct);
                if (destWh is null) return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.DestinationWarehouseInvalid);
                if (forcedSiteId.HasValue && destWh.SiteId != forcedSiteId.Value)
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AccessDenied);
                if (!destWh.SiteIsActive || !destWh.IsActive)
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.WarehouseInactive);
            }
            else if (request.DestinationWarehouseId.HasValue)
            {
                return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.DestinationWarehouseInvalid);
            }

            if (request.TransactionType is InventoryTransactionType.ProductionIssue or InventoryTransactionType.ProductionReceipt)
            {
                if (!request.ProductionOrderId.HasValue || request.ProductionOrderId.Value <= 0)
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.ProductionOrderInvalid);
                var po = await store.GetProductionOrderLookup(request.OrganizationId, request.ProductionOrderId.Value, ct);
                if (po is null || po.SiteId != wh.SiteId)
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.ProductionOrderInvalid);
                if (po.Status is not (ProductionOrderStatus.Released or ProductionOrderStatus.InProgress))
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.ProductionOrderStateInvalid);
            }
            else if (request.ProductionOrderId.HasValue)
            {
                return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.ProductionOrderInvalid);
            }

            var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToArray();
            if (itemIds.Any(id => id <= 0))
                return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.ItemInvalid);

            var itemContexts = await store.LoadItemUnitContexts(request.OrganizationId, itemIds, ct);
            var explicitUnitIds = request.Lines.Where(l => l.UnitId.HasValue).Select(l => l.UnitId!.Value).Distinct().ToArray();
            var activeUnitIds = explicitUnitIds.Length == 0
                ? new IReadOnlyHashSetLike([])
                : await store.LoadActiveUnitIds(request.OrganizationId, explicitUnitIds, ct);

            var resolvedLines = new List<ResolvedInventoryLineWrite>(request.Lines.Count * (destWh is null ? 1 : 2));
            foreach (var line in request.Lines)
            {
                if (!itemContexts.TryGetValue(line.ItemId, out var itemCtx) || !itemCtx.ItemIsActive)
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.ItemInvalid);

                var lotCode = InventoryText.LotCode(line.LotCode);
                if (itemCtx.RequiresLotTracking && string.IsNullOrEmpty(lotCode))
                    return OrganizationResult<InventoryTransactionDetails>.Failure(
                        InventoryErrors.LotRequired,
                        new Dictionary<string, object> { ["itemId"] = line.ItemId });

                if (line.ManufactureDate.HasValue && line.ExpirationDate.HasValue && line.ExpirationDate.Value < line.ManufactureDate.Value)
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.LotCodeInvalid);

                var note = InventoryText.OptionalNote(line.Notes, InventoryText.MaxLineNoteLength);
                var enteredQty = request.TransactionType == InventoryTransactionType.Adjustment
                    ? InventoryText.ValidateNonZeroQuantity(line.Quantity)
                    : InventoryText.ValidatePositiveQuantity(line.Quantity);

                var enteredUnitId = line.UnitId ?? itemCtx.BaseUnitId;
                if (enteredUnitId != itemCtx.BaseUnitId && !activeUnitIds.Contains(enteredUnitId))
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.UnitInvalid);

                var absQty = Math.Abs(enteredQty);
                var absBaseQty = enteredUnitId == itemCtx.BaseUnitId
                    ? absQty
                    : UnitConversionGraph.ConvertQuantity(absQty, enteredUnitId, itemCtx.BaseUnitId, itemCtx.Edges).ConvertedQuantity;
                absBaseQty = InventoryText.ValidatePositiveQuantity(absBaseQty);

                var triggersQc = (request.TransactionType == InventoryTransactionType.Receipt
                                  && itemCtx.QualityControlMode is QualityControlMode.IncomingReceipts or QualityControlMode.AllReceipts)
                              || (request.TransactionType == InventoryTransactionType.ProductionReceipt
                                  && itemCtx.QualityControlMode is QualityControlMode.ProductionReceipts or QualityControlMode.AllReceipts);

                if (destWh is not null)
                {
                    resolvedLines.Add(new(wh.SiteId, wh.Id, line.ItemId, lotCode, -enteredQty, enteredUnitId, -absBaseQty, itemCtx.BaseUnitId, note, false, line.ManufactureDate, line.ExpirationDate));
                    resolvedLines.Add(new(destWh.SiteId, destWh.Id, line.ItemId, lotCode, enteredQty, enteredUnitId, absBaseQty, itemCtx.BaseUnitId, note, false, line.ManufactureDate, line.ExpirationDate));
                }
                else
                {
                    var sign = request.TransactionType switch
                    {
                        InventoryTransactionType.Receipt or InventoryTransactionType.ProductionReceipt => 1m,
                        InventoryTransactionType.Issue or InventoryTransactionType.ProductionIssue => -1m,
                        InventoryTransactionType.Adjustment => enteredQty > 0m ? 1m : -1m,
                        _ => throw new InventoryRuleException(InventoryErrors.TransactionNotFound)
                    };
                    var signedEntered = request.TransactionType == InventoryTransactionType.Adjustment ? enteredQty : sign * absQty;
                    resolvedLines.Add(new(wh.SiteId, wh.Id, line.ItemId, lotCode, signedEntered, enteredUnitId, sign * absBaseQty, itemCtx.BaseUnitId, note, triggersQc && sign > 0m, line.ManufactureDate, line.ExpirationDate));
                }
            }

            return await store.PostTransaction(
                request,
                wh.SiteId,
                resolvedLines,
                DateTime.UtcNow,
                support.Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage),
                ct);
        });

        await support.RecordIfConsequentialRejectionAsync(res, auth.Actor!, scope!, request.OrganizationId, forcedSiteId,
            "InventoryTransaction", request.DocumentNumber, new { Action = "PostTransaction", Type = request.TransactionType.ToString() });
        return res;
    }

    public async Task<OrganizationResult<InventoryTransactionDetails>> ReverseTransactionAsync(
        OrganizationScope? scope,
        ReverseInventoryTransactionRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, InventoryPermissions.InventoryManage, ct);
        if (auth.Error is not null) return OrganizationResult<InventoryTransactionDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.TransactionId <= 0)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);

        var res = await support.ExecuteStoreAsync(async () =>
        {
            var reversalDoc = string.IsNullOrWhiteSpace(request.ReversalDocumentNumber)
                ? string.Empty
                : InventoryText.Code(request.ReversalDocumentNumber, InventoryErrors.DocumentNumberInvalid);
            var note = InventoryText.OptionalNote(request.ReferenceNote);
            var idemp = InventoryText.OptionalIdempotencyKey(request.IdempotencyKey);

            var existing = await store.GetTransaction(request.OrganizationId, request.TransactionId, ct);
            if (existing is null) return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AccessDenied);
            if (existing.Summary.TransactionType == InventoryTransactionType.Reversal)
                return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.CannotReverseReversal);
            if (existing.Summary.IsReversed)
                return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AlreadyReversed);

            return await store.ReverseTransaction(
                request.OrganizationId,
                request.TransactionId,
                reversalDoc,
                note,
                idemp,
                DateTime.UtcNow,
                support.Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage),
                ct);
        });

        await support.RecordIfConsequentialRejectionAsync(res, auth.Actor!, scope!, request.OrganizationId, forcedSiteId,
            "InventoryTransaction", request.ReversalDocumentNumber, new { Action = "ReverseTransaction", request.TransactionId });
        return res;
    }
}
