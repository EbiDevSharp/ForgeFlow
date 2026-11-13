using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Procurement.Slices;

internal sealed class PurchaseOrderSlices(IProcurementOperationsStore store, ProcurementSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<PurchaseOrderSummary>>> ListPurchaseOrders(
        OrganizationScope? scope,
        long organizationId,
        PurchaseOrderListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ProcurementRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<PurchaseOrderSummary>>.Failure(auth.Error);
        if (!ProcurementSliceSupport.TryResolveSiteScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<PurchaseOrderSummary>>.Failure(ProcurementErrors.AccessDenied);

        try
        {
            var bound = new PurchaseOrderListQuery(
                forcedSiteId,
                query.SupplierId,
                query.Status,
                query.Search?.Trim(),
                Math.Clamp(query.Page, 1, 1_000_000),
                Math.Clamp(query.PageSize, 1, 100));
            return OrganizationResult<OrganizationPage<PurchaseOrderSummary>>.Success(
                await store.ListPurchaseOrders(organizationId, forcedSiteId, bound, ct));
        }
        catch (ProcurementStoreUnavailableException)
        {
            return OrganizationResult<OrganizationPage<PurchaseOrderSummary>>.Failure(ProcurementErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<PurchaseOrderDetails>> GetPurchaseOrder(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ProcurementRead, ct);
        if (auth.Error is not null) return OrganizationResult<PurchaseOrderDetails>.Failure(auth.Error);
        if (!ProcurementSliceSupport.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);
        if (id <= 0)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);

        try
        {
            var found = await store.GetPurchaseOrder(organizationId, id, ct);
            if (found is null)
                return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
            if (forcedSiteId.HasValue && found.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);
            return OrganizationResult<PurchaseOrderDetails>.Success(found);
        }
        catch (ProcurementStoreUnavailableException)
        {
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<PurchaseOrderDetails>> CreateDraft(
        OrganizationScope? scope,
        CreatePurchaseOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ManageOrders, ct);
        OrganizationResult<PurchaseOrderDetails> result;
        if (auth.Error is not null)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(auth.Error);
        else if (!ProcurementSliceSupport.TryResolveSiteScope(scope, request.OrganizationId, request.SiteId, out _))
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);
        else if (request.SiteId <= 0)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SiteInvalid);
        else
            result = await ProcurementSliceSupport.ExecuteStore(async () =>
            {
                var site = await store.GetSiteLookup(request.OrganizationId, request.SiteId, ct);
                if (site is null || !site.OrganizationIsActive || !site.SiteIsActive)
                    return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SiteInvalid);

                if (request.TargetWarehouseId is > 0)
                {
                    var wh = await store.GetWarehouseLookup(request.OrganizationId, request.TargetWarehouseId.Value, ct);
                    if (wh is null || !wh.WarehouseIsActive || wh.SiteId != request.SiteId)
                        return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.WarehouseInvalid);
                }

                var resolved = await ResolveOrderLinesAsync(request.OrganizationId, request.Lines, ct);
                if (!resolved.Succeeded)
                    return OrganizationResult<PurchaseOrderDetails>.Failure(resolved.Error!.Code, resolved.Error.Parameters);

                return await store.CreateDraft(request, resolved.Value!, support.Access(auth.Actor!, scope!, ProcurementPermissions.ManageOrders), ct);
            });

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, request.SiteId, "PurchaseOrder", request.OrderNumber, nameof(CreateDraft));
    }

    public async Task<OrganizationResult<PurchaseOrderDetails>> UpdateDraft(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        UpdatePurchaseOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ManageOrders, ct);
        OrganizationResult<PurchaseOrderDetails> result;
        if (auth.Error is not null)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(auth.Error);
        else if (!ProcurementSliceSupport.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
        else if (ProcurementSliceSupport.DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.ConcurrencyConflict);
        else
            result = await ProcurementSliceSupport.ExecuteStore(async () =>
            {
                var existing = await store.GetPurchaseOrder(organizationId, id, ct);
                if (existing is null)
                    return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);

                if (request.TargetWarehouseId is > 0)
                {
                    var wh = await store.GetWarehouseLookup(organizationId, request.TargetWarehouseId.Value, ct);
                    if (wh is null || !wh.WarehouseIsActive || wh.SiteId != existing.Summary.SiteId)
                        return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.WarehouseInvalid);
                }

                var resolved = await ResolveOrderLinesAsync(organizationId, request.Lines, ct);
                if (!resolved.Succeeded)
                    return OrganizationResult<PurchaseOrderDetails>.Failure(resolved.Error!.Code, resolved.Error.Parameters);

                return await store.UpdateDraft(organizationId, id, ver, request, resolved.Value!, support.Access(auth.Actor!, scope!, ProcurementPermissions.ManageOrders), ct);
            });

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, null, "PurchaseOrder", id.ToString(), nameof(UpdateDraft));
    }

    public Task<OrganizationResult<PurchaseOrderDetails>> ApproveOrder(
        OrganizationScope? scope, long organizationId, long id, string? expectedVersion, CancellationToken ct = default)
        => ExecuteSiteOrderActionAsync(
            scope, organizationId, id, expectedVersion, nameof(ApproveOrder),
            (ver, access) => store.ApproveOrder(organizationId, id, ver, support.UtcNow, access, ct),
            ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CloseOrder(
        OrganizationScope? scope, long organizationId, long id, string? expectedVersion, CancellationToken ct = default)
        => ExecuteSiteOrderActionAsync(
            scope, organizationId, id, expectedVersion, nameof(CloseOrder),
            (ver, access) => store.CloseOrder(organizationId, id, ver, support.UtcNow, access, ct),
            ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CancelOrder(
        OrganizationScope? scope, long organizationId, long id, string? expectedVersion, string? reason, CancellationToken ct = default)
        => ExecuteSiteOrderActionAsync(
            scope, organizationId, id, expectedVersion, nameof(CancelOrder),
            (ver, access) => store.CancelOrder(organizationId, id, ver, reason, support.UtcNow, access, ct),
            ct);

    public async Task<OrganizationResult<PurchaseOrderReceiptResult>> ReceiveOrder(
        OrganizationScope? scope,
        ReceivePurchaseOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ReceiveOrders, ct);
        OrganizationResult<PurchaseOrderReceiptResult> result;
        long? targetSiteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(auth.Error);
        else if (!ProcurementSliceSupport.TryResolveSiteScope(scope, request.OrganizationId, null, out var forcedSiteId))
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.AccessDenied);
        else if (request.PurchaseOrderId <= 0)
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.NotFound);
        else if (ProcurementSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] ver)
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.ConcurrencyConflict);
        else if (request.Lines is null || request.Lines.Count == 0)
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.QuantityInvalid);
        else
            result = await ProcurementSliceSupport.ExecuteStore(async () =>
            {
                var docNum = string.IsNullOrWhiteSpace(request.ReceiptDocumentNumber)
                    ? string.Empty
                    : ProcurementText.Code(request.ReceiptDocumentNumber, ProcurementErrors.ReceiptDocumentInvalid);
                var existing = await store.GetPurchaseOrder(request.OrganizationId, request.PurchaseOrderId, ct);
                if (existing is null)
                    return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.AccessDenied);

                targetSiteId = existing.Summary.SiteId;
                var wh = await store.GetWarehouseLookup(request.OrganizationId, request.WarehouseId, ct);
                if (wh is null || !wh.WarehouseIsActive || !wh.SiteIsActive || !wh.OrganizationIsActive || wh.SiteId != existing.Summary.SiteId)
                    return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.WarehouseInvalid);

                var linesById = existing.Lines.ToDictionary(l => l.Id);
                foreach (var inputLine in request.Lines)
                {
                    if (!linesById.ContainsKey(inputLine.PurchaseOrderLineId))
                        return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.LineNotFound);
                }

                var itemIds = request.Lines.Select(l => linesById[l.PurchaseOrderLineId].ItemId).Distinct().ToArray();
                var items = await store.LoadItemContexts(request.OrganizationId, itemIds, ct);
                var unitIds = request.Lines
                    .Select(l => l.UnitId ?? linesById[l.PurchaseOrderLineId].OrderedUnitId)
                    .Distinct()
                    .ToArray();
                var activeUnits = await store.LoadActiveUnitIds(request.OrganizationId, unitIds, ct);

                var resolved = new List<ResolvedPurchaseOrderReceiptLine>(request.Lines.Count);
                foreach (var lineInput in request.Lines)
                {
                    var poLine = linesById[lineInput.PurchaseOrderLineId];
                    if (!items.TryGetValue(poLine.ItemId, out var itemCtx) || !itemCtx.ItemIsActive)
                        return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.ItemInvalid);
                    if (!itemCtx.CanBePurchased)
                        return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.ItemNotPurchasable);

                    var enteredUnitId = lineInput.UnitId ?? poLine.OrderedUnitId;
                    if (!activeUnits.Contains(enteredUnitId))
                        return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.LineUnitInvalid);

                    var qty = ProcurementText.PositiveQuantity(lineInput.Quantity);
                    var baseDelta = enteredUnitId == poLine.BaseUnitIdSnapshot
                        ? qty
                        : UnitConversionGraph.ConvertQuantity(qty, enteredUnitId, poLine.BaseUnitIdSnapshot, itemCtx.Edges).ConvertedQuantity;

                    var lotCode = InventoryText.LotCode(lineInput.LotCode);
                    if (itemCtx.RequiresLotTracking && string.IsNullOrEmpty(lotCode))
                        return OrganizationResult<PurchaseOrderReceiptResult>.Failure(InventoryErrors.LotRequired);
                    if (lineInput.ManufactureDate.HasValue && lineInput.ExpirationDate.HasValue
                        && lineInput.ExpirationDate.Value < lineInput.ManufactureDate.Value)
                        return OrganizationResult<PurchaseOrderReceiptResult>.Failure(QualityErrors.LotDatesInvalid);

                    var triggersQc = itemCtx.QualityControlMode is QualityControlMode.IncomingReceipts or QualityControlMode.AllReceipts;
                    resolved.Add(new ResolvedPurchaseOrderReceiptLine(
                        poLine.Id,
                        poLine.ItemId,
                        lotCode,
                        qty,
                        enteredUnitId,
                        baseDelta,
                        poLine.BaseUnitIdSnapshot,
                        triggersQc,
                        lineInput.ManufactureDate,
                        lineInput.ExpirationDate,
                        InventoryText.OptionalNote(lineInput.Notes)));
                }

                return await store.ReceiveOrder(
                    request,
                    existing.Summary.SiteId,
                    ver,
                    docNum,
                    resolved,
                    support.UtcNow,
                    support.Access(auth.Actor!, scope!, ProcurementPermissions.ReceiveOrders),
                    ct);
            });

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, targetSiteId, "PurchaseOrder", request.PurchaseOrderId.ToString(), nameof(ReceiveOrder));
    }

    private async Task<OrganizationResult<PurchaseOrderDetails>> ExecuteSiteOrderActionAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        string actionName,
        Func<byte[], OrganizationWriteAccess, Task<OrganizationResult<PurchaseOrderDetails>>> operation,
        CancellationToken ct)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ManageOrders, ct);
        OrganizationResult<PurchaseOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(auth.Error);
        else if (!ProcurementSliceSupport.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
        else if (ProcurementSliceSupport.DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.ConcurrencyConflict);
        else
            result = await ProcurementSliceSupport.ExecuteStore(async () =>
            {
                var existing = await store.GetPurchaseOrder(organizationId, id, ct);
                if (existing is null)
                    return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                return await operation(ver, support.Access(auth.Actor!, scope!, ProcurementPermissions.ManageOrders));
            });

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "PurchaseOrder", id.ToString(), actionName);
    }

    private async Task<OrganizationResult<IReadOnlyList<ResolvedPurchaseOrderLineWrite>>> ResolveOrderLinesAsync(
        long organizationId,
        IReadOnlyList<PurchaseOrderLineInput>? inputs,
        CancellationToken ct)
    {
        if (inputs is null || inputs.Count == 0)
            return OrganizationResult<IReadOnlyList<ResolvedPurchaseOrderLineWrite>>.Failure(ProcurementErrors.EmptyOrder);

        var seenItems = new HashSet<long>();
        foreach (var line in inputs)
        {
            if (line.ItemId <= 0)
                return OrganizationResult<IReadOnlyList<ResolvedPurchaseOrderLineWrite>>.Failure(ProcurementErrors.ItemInvalid);
            if (!seenItems.Add(line.ItemId))
                return OrganizationResult<IReadOnlyList<ResolvedPurchaseOrderLineWrite>>.Failure(ProcurementErrors.DuplicateItem);
        }

        var itemIds = inputs.Select(l => l.ItemId).Distinct().ToArray();
        var itemContexts = await store.LoadItemContexts(organizationId, itemIds, ct);

        var requestedUnitIds = inputs.Where(l => l.OrderedUnitId is > 0).Select(l => l.OrderedUnitId!.Value).Distinct().ToArray();
        var activeUnitIds = requestedUnitIds.Length > 0
            ? await store.LoadActiveUnitIds(organizationId, requestedUnitIds, ct)
            : new HashSet<long>();

        var resolved = new List<ResolvedPurchaseOrderLineWrite>(inputs.Count);
        foreach (var line in inputs)
        {
            if (!itemContexts.TryGetValue(line.ItemId, out var ctx) || !ctx.ItemIsActive)
                return OrganizationResult<IReadOnlyList<ResolvedPurchaseOrderLineWrite>>.Failure(ProcurementErrors.ItemInvalid);
            if (!ctx.CanBePurchased)
                return OrganizationResult<IReadOnlyList<ResolvedPurchaseOrderLineWrite>>.Failure(ProcurementErrors.ItemNotPurchasable);

            var unitId = line.OrderedUnitId is > 0 ? line.OrderedUnitId.Value : ctx.BaseUnitId;
            if (unitId != ctx.BaseUnitId && !activeUnitIds.Contains(unitId))
                return OrganizationResult<IReadOnlyList<ResolvedPurchaseOrderLineWrite>>.Failure(ProcurementErrors.LineUnitInvalid);

            var qty = ProcurementText.PositiveQuantity(line.OrderedQuantity);
            var baseQty = unitId == ctx.BaseUnitId
                ? qty
                : UnitConversionGraph.ConvertQuantity(qty, unitId, ctx.BaseUnitId, ctx.Edges).ConvertedQuantity;
            var price = ProcurementText.OptionalUnitPrice(line.UnitPrice);
            var notes = ProcurementText.OptionalText(line.Notes, ProcurementText.MaxLineNotesLength, ProcurementErrors.NotesInvalid);

            resolved.Add(new ResolvedPurchaseOrderLineWrite(
                line.ItemId,
                qty,
                unitId,
                ctx.BaseUnitId,
                baseQty,
                price,
                notes));
        }

        return OrganizationResult<IReadOnlyList<ResolvedPurchaseOrderLineWrite>>.Success(resolved);
    }
}
