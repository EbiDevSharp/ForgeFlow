using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Inventory;

/// <summary>
/// Secured use cases for Site-scoped Warehouses, append-only Stock Ledger, serialized balances,
/// and Production Order material issues/receipts.
/// </summary>
public sealed class InventoryUseCases(
    IInventoryOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null)
{
    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, InventoryErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, InventoryErrors.ActorRequired);
        if (scope is null) return (null, InventoryErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, InventoryErrors.AccessDenied);
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

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(MapUnitError(ex.Code), ex.Parameters); }
        catch (InventoryStoreUnavailableException) { return OrganizationResult<T>.Failure(InventoryErrors.PersistenceUnavailable); }
    }

    private static string MapUnitError(string code) => code switch
    {
        UnitErrors.ConversionQuantityInvalid => InventoryErrors.QuantityInvalid,
        _ => InventoryErrors.UnitInvalid
    };

    public async Task<OrganizationResult<OrganizationPage<WarehouseSummary>>> ListWarehouses(
        OrganizationScope? scope,
        long organizationId,
        WarehouseListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, InventoryPermissions.InventoryRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<WarehouseSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<WarehouseSummary>>.Failure(InventoryErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<WarehouseSummary>>.Success(
                await store.ListWarehouses(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<WarehouseSummary>> CreateWarehouse(
        OrganizationScope? scope,
        CreateWarehouseRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, InventoryPermissions.InventoryManage, ct);
        if (auth.Error is not null) return OrganizationResult<WarehouseSummary>.Failure(auth.Error);
        if (request.SiteId <= 0) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.SiteInvalid);
        if (!TryResolveScope(scope, request.OrganizationId, request.SiteId, out _))
            return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.AccessDenied);

        return await ExecuteStore(() =>
        {
            _ = InventoryText.Code(request.Code);
            _ = InventoryText.Name(request.Name);
            return store.CreateWarehouse(request, Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage), ct);
        });
    }

    public async Task<OrganizationResult<WarehouseSummary>> UpdateWarehouse(
        OrganizationScope? scope,
        UpdateWarehouseRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, InventoryPermissions.InventoryManage, ct);
        if (auth.Error is not null) return OrganizationResult<WarehouseSummary>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);

        var version = DecodeVersion(request.ExpectedRowVersion);
        if (version is null) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.ConcurrencyConflict);

        return await ExecuteStore(async () =>
        {
            _ = InventoryText.Name(request.Name);
            var existing = await store.GetWarehouse(request.OrganizationId, request.Id, ct);
            if (existing is null) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);
            if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.AccessDenied);

            return await store.UpdateWarehouse(request.OrganizationId, request.Id, version, request, Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage), ct);
        });
    }

    public async Task<OrganizationResult<WarehouseSummary>> SetWarehouseActive(
        OrganizationScope? scope,
        SetWarehouseActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, InventoryPermissions.InventoryManage, ct);
        if (auth.Error is not null) return OrganizationResult<WarehouseSummary>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);

        var version = DecodeVersion(request.ExpectedRowVersion);
        if (version is null) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.ConcurrencyConflict);

        return await ExecuteStore(async () =>
        {
            var existing = await store.GetWarehouse(request.OrganizationId, request.Id, ct);
            if (existing is null) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);
            if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.AccessDenied);

            return await store.SetWarehouseActive(request.OrganizationId, request.Id, version, request.IsActive, Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage), ct);
        });
    }

    public async Task<OrganizationResult<OrganizationPage<InventoryBalanceSummary>>> ListBalances(
        OrganizationScope? scope,
        long organizationId,
        InventoryBalanceListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, InventoryPermissions.InventoryRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<InventoryBalanceSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<InventoryBalanceSummary>>.Failure(InventoryErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<InventoryBalanceSummary>>.Success(
                await store.ListBalances(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<OrganizationPage<InventoryTransactionSummary>>> ListTransactions(
        OrganizationScope? scope,
        long organizationId,
        InventoryTransactionListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, InventoryPermissions.InventoryRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<InventoryTransactionSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<InventoryTransactionSummary>>.Failure(InventoryErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<InventoryTransactionSummary>>.Success(
                await store.ListTransactions(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<InventoryTransactionDetails>> GetTransaction(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, InventoryPermissions.InventoryRead, ct);
        if (auth.Error is not null) return OrganizationResult<InventoryTransactionDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);

        return await ExecuteStore(async () =>
        {
            var details = await store.GetTransaction(organizationId, id, ct);
            if (details is null) return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);
            if (forcedSiteId.HasValue && details.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AccessDenied);
            return OrganizationResult<InventoryTransactionDetails>.Success(details);
        });
    }

    public async Task<OrganizationResult<InventoryTransactionDetails>> PostTransaction(
        OrganizationScope? scope,
        PostInventoryTransactionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, InventoryPermissions.InventoryManage, ct);
        if (auth.Error is not null) return OrganizationResult<InventoryTransactionDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId))
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AccessDenied);
        if (request.WarehouseId <= 0)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.WarehouseNotFound);
        if (request.TransactionType is InventoryTransactionType.Reversal or InventoryTransactionType.TransferIn)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);
        if (request.Lines is null || request.Lines.Count == 0)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.LinesRequired);

        return await ExecuteStore(async () =>
        {
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

                if (destWh is not null)
                {
                    resolvedLines.Add(new(wh.SiteId, wh.Id, line.ItemId, lotCode, -enteredQty, enteredUnitId, -absBaseQty, itemCtx.BaseUnitId, note));
                    resolvedLines.Add(new(destWh.SiteId, destWh.Id, line.ItemId, lotCode, enteredQty, enteredUnitId, absBaseQty, itemCtx.BaseUnitId, note));
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
                    resolvedLines.Add(new(wh.SiteId, wh.Id, line.ItemId, lotCode, signedEntered, enteredUnitId, sign * absBaseQty, itemCtx.BaseUnitId, note));
                }
            }

            return await store.PostTransaction(
                request,
                wh.SiteId,
                resolvedLines,
                DateTime.UtcNow,
                Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage),
                ct);
        });
    }

    public async Task<OrganizationResult<InventoryTransactionDetails>> ReverseTransaction(
        OrganizationScope? scope,
        ReverseInventoryTransactionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, InventoryPermissions.InventoryManage, ct);
        if (auth.Error is not null) return OrganizationResult<InventoryTransactionDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.TransactionId <= 0)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);

        return await ExecuteStore(async () =>
        {
            var reversalDoc = InventoryText.Code(request.ReversalDocumentNumber, InventoryErrors.DocumentNumberInvalid);
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
                Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage),
                ct);
        });
    }
}
