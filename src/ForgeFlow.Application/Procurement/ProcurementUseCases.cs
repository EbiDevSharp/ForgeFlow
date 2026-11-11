using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Procurement;

/// <summary>
/// Secured use cases for Organization-shared Suppliers, Site-scoped Purchase Orders,
/// and atomic Goods Receipts into Inventory, Lot Tracking, and Quality Control.
/// </summary>
public sealed class ProcurementUseCases(
    IProcurementOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, ProcurementErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, ProcurementErrors.ActorRequired);
        if (scope is null) return (null, ProcurementErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, ProcurementErrors.AccessDenied);
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

    private static bool TryResolveSiteScope(OrganizationScope? scope, long requestedOrgId, long? requestedSiteId, out long? forcedSiteId)
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

    private static bool MatchesOrganizationOnly(OrganizationScope? scope, long organizationId)
        => scope is OrganizationScope.Organization org && org.OrganizationId == organizationId && organizationId > 0;

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
        catch (ProcurementRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code == UnitErrors.ConversionQuantityInvalid ? ProcurementErrors.QuantityInvalid : ProcurementErrors.LineUnitInvalid, ex.Parameters); }
        catch (ProcurementStoreUnavailableException) { return OrganizationResult<T>.Failure(ProcurementErrors.PersistenceUnavailable); }
    }

    private async Task<OrganizationResult<T>> AuditFailureIfRejected<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long? orgId,
        long? siteId,
        string targetType,
        string? targetRef,
        string actionName)
    {
        if (result.Succeeded || result.Error is null)
            return result;

        await OperationAuditHelper.RecordAsync(
            operationRecorder,
            opContext,
            UserOperationCatalog.ProcurementActionRejected,
            OperationAuditHelper.MapFailureOutcome(result.Error.Code),
            actor,
            scope,
            orgId,
            siteId,
            targetType,
            targetRef,
            result.Error.Code,
            new { Action = actionName });
        return result;
    }

    // --- Suppliers (Organization-shared) ---

    public async Task<OrganizationResult<OrganizationPage<SupplierSummary>>> ListSuppliers(
        OrganizationScope? scope,
        long organizationId,
        SupplierListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProcurementPermissions.ProcurementRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<SupplierSummary>>.Failure(auth.Error);
        if (!TryResolveSiteScope(scope, organizationId, null, out _))
            return OrganizationResult<OrganizationPage<SupplierSummary>>.Failure(ProcurementErrors.AccessDenied);

        try
        {
            var bound = new SupplierListQuery(
                query.Search?.Trim(),
                query.IncludeInactive,
                Math.Clamp(query.Page, 1, 1_000_000),
                Math.Clamp(query.PageSize, 1, 100));
            return OrganizationResult<OrganizationPage<SupplierSummary>>.Success(await store.ListSuppliers(organizationId, bound, ct));
        }
        catch (ProcurementStoreUnavailableException)
        {
            return OrganizationResult<OrganizationPage<SupplierSummary>>.Failure(ProcurementErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<SupplierSummary>> GetSupplier(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProcurementPermissions.ProcurementRead, ct);
        if (auth.Error is not null) return OrganizationResult<SupplierSummary>.Failure(auth.Error);
        if (!TryResolveSiteScope(scope, organizationId, null, out _))
            return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.AccessDenied);
        if (id <= 0)
            return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound);

        try
        {
            var found = await store.GetSupplier(organizationId, id, ct);
            return found is null
                ? OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound)
                : OrganizationResult<SupplierSummary>.Success(found);
        }
        catch (ProcurementStoreUnavailableException)
        {
            return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<SupplierSummary>> CreateSupplier(
        OrganizationScope? scope,
        CreateSupplierRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProcurementPermissions.ManageSuppliers, ct);
        OrganizationResult<SupplierSummary> result;
        if (auth.Error is not null) result = OrganizationResult<SupplierSummary>.Failure(auth.Error);
        else if (!MatchesOrganizationOnly(scope, request.OrganizationId))
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.AccessDenied);
        else
            result = await ExecuteStore(() => store.CreateSupplier(request, Access(auth.Actor!, scope!, ProcurementPermissions.ManageSuppliers), ct));

        return await AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, null, "Supplier", request.Code, nameof(CreateSupplier));
    }

    public async Task<OrganizationResult<SupplierSummary>> UpdateSupplier(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        UpdateSupplierRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProcurementPermissions.ManageSuppliers, ct);
        OrganizationResult<SupplierSummary> result;
        if (auth.Error is not null) result = OrganizationResult<SupplierSummary>.Failure(auth.Error);
        else if (!MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.ConcurrencyConflict);
        else
            result = await ExecuteStore(() => store.UpdateSupplier(organizationId, id, ver, request, Access(auth.Actor!, scope!, ProcurementPermissions.ManageSuppliers), ct));

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, null, "Supplier", id.ToString(), nameof(UpdateSupplier));
    }

    public async Task<OrganizationResult<SupplierSummary>> SetSupplierActive(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        bool isActive,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProcurementPermissions.ManageSuppliers, ct);
        OrganizationResult<SupplierSummary> result;
        if (auth.Error is not null) result = OrganizationResult<SupplierSummary>.Failure(auth.Error);
        else if (!MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.ConcurrencyConflict);
        else
            result = await ExecuteStore(() => store.SetSupplierActive(organizationId, id, ver, isActive, Access(auth.Actor!, scope!, ProcurementPermissions.ManageSuppliers), ct));

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, null, "Supplier", id.ToString(), nameof(SetSupplierActive));
    }

    // --- Purchase Orders (Site-scoped) ---

    public async Task<OrganizationResult<OrganizationPage<PurchaseOrderSummary>>> ListPurchaseOrders(
        OrganizationScope? scope,
        long organizationId,
        PurchaseOrderListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProcurementPermissions.ProcurementRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<PurchaseOrderSummary>>.Failure(auth.Error);
        if (!TryResolveSiteScope(scope, organizationId, query.SiteId, out var forcedSiteId))
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
        var auth = await Authorize(scope, ProcurementPermissions.ProcurementRead, ct);
        if (auth.Error is not null) return OrganizationResult<PurchaseOrderDetails>.Failure(auth.Error);
        if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
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
        var auth = await Authorize(scope, ProcurementPermissions.ManageOrders, ct);
        OrganizationResult<PurchaseOrderDetails> result;
        if (auth.Error is not null)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(auth.Error);
        else if (!TryResolveSiteScope(scope, request.OrganizationId, request.SiteId, out _))
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);
        else if (request.SiteId <= 0)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SiteInvalid);
        else
            result = await ExecuteStore(async () =>
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

                return await store.CreateDraft(request, resolved.Value!, Access(auth.Actor!, scope!, ProcurementPermissions.ManageOrders), ct);
            });

        return await AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, request.SiteId, "PurchaseOrder", request.OrderNumber, nameof(CreateDraft));
    }

    public async Task<OrganizationResult<PurchaseOrderDetails>> UpdateDraft(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        UpdatePurchaseOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProcurementPermissions.ManageOrders, ct);
        OrganizationResult<PurchaseOrderDetails> result;
        if (auth.Error is not null)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(auth.Error);
        else if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.ConcurrencyConflict);
        else
            result = await ExecuteStore(async () =>
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

                return await store.UpdateDraft(organizationId, id, ver, request, resolved.Value!, Access(auth.Actor!, scope!, ProcurementPermissions.ManageOrders), ct);
            });

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, null, "PurchaseOrder", id.ToString(), nameof(UpdateDraft));
    }

    public Task<OrganizationResult<PurchaseOrderDetails>> ApproveOrder(
        OrganizationScope? scope, long organizationId, long id, string? expectedVersion, CancellationToken ct = default)
        => ExecuteSiteOrderActionAsync(
            scope, organizationId, id, expectedVersion, nameof(ApproveOrder),
            (ver, access) => store.ApproveOrder(organizationId, id, ver, UtcNow, access, ct),
            ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CloseOrder(
        OrganizationScope? scope, long organizationId, long id, string? expectedVersion, CancellationToken ct = default)
        => ExecuteSiteOrderActionAsync(
            scope, organizationId, id, expectedVersion, nameof(CloseOrder),
            (ver, access) => store.CloseOrder(organizationId, id, ver, UtcNow, access, ct),
            ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CancelOrder(
        OrganizationScope? scope, long organizationId, long id, string? expectedVersion, string? reason, CancellationToken ct = default)
        => ExecuteSiteOrderActionAsync(
            scope, organizationId, id, expectedVersion, nameof(CancelOrder),
            (ver, access) => store.CancelOrder(organizationId, id, ver, reason, UtcNow, access, ct),
            ct);

    public async Task<OrganizationResult<PurchaseOrderReceiptResult>> ReceiveOrder(
        OrganizationScope? scope,
        ReceivePurchaseOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ProcurementPermissions.ReceiveOrders, ct);
        OrganizationResult<PurchaseOrderReceiptResult> result;
        long? targetSiteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(auth.Error);
        else if (!TryResolveSiteScope(scope, request.OrganizationId, null, out var forcedSiteId))
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.AccessDenied);
        else if (request.PurchaseOrderId <= 0)
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.NotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] ver)
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.ConcurrencyConflict);
        else if (request.Lines is null || request.Lines.Count == 0)
            result = OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.QuantityInvalid);
        else
            result = await ExecuteStore(async () =>
            {
                var docNum = ProcurementText.Code(request.ReceiptDocumentNumber, ProcurementErrors.ReceiptDocumentInvalid);
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
                    UtcNow,
                    Access(auth.Actor!, scope!, ProcurementPermissions.ReceiveOrders),
                    ct);
            });

        return await AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, targetSiteId, "PurchaseOrder", request.PurchaseOrderId.ToString(), nameof(ReceiveOrder));
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
        var auth = await Authorize(scope, ProcurementPermissions.ManageOrders, ct);
        OrganizationResult<PurchaseOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(auth.Error);
        else if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.ConcurrencyConflict);
        else
            result = await ExecuteStore(async () =>
            {
                var existing = await store.GetPurchaseOrder(organizationId, id, ct);
                if (existing is null)
                    return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                return await operation(ver, Access(auth.Actor!, scope!, ProcurementPermissions.ManageOrders));
            });

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "PurchaseOrder", id.ToString(), actionName);
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
