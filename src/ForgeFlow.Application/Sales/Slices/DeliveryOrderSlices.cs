using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Sales.Slices;

internal sealed class DeliveryOrderSlices(ISalesOperationsStore store, SalesSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<DeliveryOrderSummary>>> ListAsync(
        OrganizationScope? scope,
        long organizationId,
        DeliveryOrderListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<OrganizationPage<DeliveryOrderSummary>>.Failure(auth.Error);

        if (!support.TryResolveSiteScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<DeliveryOrderSummary>>.Failure(SalesErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
        };

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<DeliveryOrderSummary>>.Success(
                await store.ListDeliveryOrders(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<DeliveryOrderDetails>> GetAsync(
        OrganizationScope? scope,
        long organizationId,
        long deliveryOrderId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<DeliveryOrderDetails>.Failure(auth.Error);

        if (!support.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);

        if (deliveryOrderId <= 0)
            return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNotFound);

        return await support.ExecuteStoreAsync(async () =>
        {
            var details = await store.GetDeliveryOrder(organizationId, deliveryOrderId, ct);
            if (details is null)
                return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNotFound);
            if (forcedSiteId.HasValue && details.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);

            return OrganizationResult<DeliveryOrderDetails>.Success(details);
        });
    }

    public async Task<OrganizationResult<IReadOnlyList<DispatchableLotSummary>>> ListDispatchableLotsAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        long warehouseId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(auth.Error);

        if (!support.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.AccessDenied);

        if (salesOrderId <= 0)
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.NotFound);
        if (warehouseId <= 0)
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.WarehouseInvalid);

        return await support.ExecuteStoreAsync(async () =>
        {
            var order = await store.GetSalesOrder(organizationId, salesOrderId, ct);
            if (order is null)
                return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.NotFound);
            if (forcedSiteId.HasValue && order.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.AccessDenied);

            var effectiveDate = asOfDate ?? DateOnly.FromDateTime(support.UtcNow);
            var lots = await store.ListDispatchableLots(organizationId, salesOrderId, warehouseId, effectiveDate, ct);
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Success(lots);
        });
    }

    public async Task<OrganizationResult<DeliveryOrderDetails>> PostAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        PostDeliveryOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.Dispatch, ct);
        OrganizationResult<DeliveryOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(auth.Error);
        else if (!support.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);
        else if (salesOrderId <= 0)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.NotFound);
        else if (request.WarehouseId <= 0)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.WarehouseInvalid);
        else if (request.Lines is null || request.Lines.Count == 0)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.EmptyDispatch);
        else
            result = await support.ExecuteStoreAsync(async () =>
            {
                var existing = await store.GetSalesOrder(organizationId, salesOrderId, ct);
                if (existing is null)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                var resolvedLines = await ResolveDispatchLinesAsync(organizationId, existing.Lines, request.Lines, ct);
                if (!resolvedLines.Succeeded)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(resolvedLines.Error!.Code, resolvedLines.Error.Parameters);

                return await store.PostDeliveryOrder(
                    organizationId,
                    salesOrderId,
                    request,
                    resolvedLines.Value!,
                    support.UtcNow,
                    support.Access(auth.Actor!, scope!, SalesPermissions.Dispatch),
                    ct);
            });

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "DeliveryOrder", request.DispatchNumber, nameof(PostAsync));
    }

    public async Task<OrganizationResult<DeliveryOrderDetails>> ReverseAsync(
        OrganizationScope? scope,
        long organizationId,
        long deliveryOrderId,
        ReverseDeliveryOrderRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.Dispatch, ct);
        OrganizationResult<DeliveryOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(auth.Error);
        else if (!support.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);
        else if (deliveryOrderId <= 0)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNotFound);
        else if (support.DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await support.ExecuteStoreAsync(async () =>
            {
                var existing = await store.GetDeliveryOrder(organizationId, deliveryOrderId, ct);
                if (existing is null)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                var reason = SalesText.RequiredReason(request?.Reason);
                return await store.ReverseDeliveryOrder(
                    organizationId,
                    deliveryOrderId,
                    reason,
                    version,
                    support.UtcNow,
                    support.Access(auth.Actor!, scope!, SalesPermissions.Dispatch),
                    ct);
            });

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "DeliveryOrder", deliveryOrderId.ToString(), nameof(ReverseAsync));
    }

    private async Task<OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>> ResolveDispatchLinesAsync(
        long organizationId,
        IReadOnlyList<SalesOrderLineDetails> orderLines,
        IReadOnlyList<PostDeliveryOrderLineRequest> inputs,
        CancellationToken ct)
    {
        var orderLineById = orderLines.ToDictionary(l => l.Id);
        var itemIds = orderLines.Select(l => l.ItemId).Distinct().ToArray();
        var itemContexts = await store.LoadItemContexts(organizationId, itemIds, ct);

        var requestedUnitIds = inputs.Where(l => l.UnitId is > 0).Select(l => l.UnitId!.Value).Distinct().ToArray();
        var activeUnitIds = requestedUnitIds.Length > 0
            ? await store.LoadActiveUnitIds(organizationId, requestedUnitIds, ct)
            : new HashSet<long>();

        var resolved = new List<ResolvedDeliveryOrderLine>(inputs.Count);
        foreach (var line in inputs)
        {
            if (!orderLineById.TryGetValue(line.SalesOrderLineId, out var orderLine))
                return OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>.Failure(SalesErrors.LineNotFound);
            if (!itemContexts.TryGetValue(orderLine.ItemId, out var ctx) || !ctx.ItemIsActive)
                return OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>.Failure(SalesErrors.ItemInvalid);

            var lotCode = SalesText.RequiredLotCode(line.LotCode);
            var unitId = line.UnitId is > 0 ? line.UnitId.Value : orderLine.BaseUnitIdSnapshot;
            if (unitId != orderLine.BaseUnitIdSnapshot && !activeUnitIds.Contains(unitId))
                return OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>.Failure(SalesErrors.LineUnitInvalid);

            var qty = SalesText.PositiveQuantity(line.DispatchedQuantity);
            var baseQty = unitId == orderLine.BaseUnitIdSnapshot
                ? qty
                : UnitConversionGraph.ConvertQuantity(qty, unitId, orderLine.BaseUnitIdSnapshot, ctx.Edges).ConvertedQuantity;
            var notes = SalesText.OptionalText(line.Notes, SalesText.MaxLineNotesLength, SalesErrors.NotesInvalid);

            resolved.Add(new ResolvedDeliveryOrderLine(
                orderLine.Id,
                orderLine.ItemId,
                lotCode,
                baseQty,
                orderLine.BaseUnitIdSnapshot,
                notes));
        }

        return OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>.Success(resolved);
    }
}
