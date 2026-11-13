using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Sales.Slices;

internal sealed class SalesOrderSlices(ISalesOperationsStore store, SalesSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<SalesOrderSummary>>> ListAsync(
        OrganizationScope? scope,
        long organizationId,
        SalesOrderListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<OrganizationPage<SalesOrderSummary>>.Failure(auth.Error);

        if (!support.TryResolveSiteScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<SalesOrderSummary>>.Failure(SalesErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
        };

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<SalesOrderSummary>>.Success(
                await store.ListSalesOrders(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<SalesOrderDetails>> GetAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<SalesOrderDetails>.Failure(auth.Error);

        if (!support.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);

        if (salesOrderId <= 0)
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);

        return await support.ExecuteStoreAsync(async () =>
        {
            var details = await store.GetSalesOrder(organizationId, salesOrderId, ct);
            if (details is null)
                return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
            if (forcedSiteId.HasValue && details.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);

            return OrganizationResult<SalesOrderDetails>.Success(details);
        });
    }

    public async Task<OrganizationResult<SalesOrderDetails>> CreateAsync(
        OrganizationScope? scope,
        CreateSalesOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.ManageOrders, ct);
        OrganizationResult<SalesOrderDetails> result;

        if (auth.Error is not null)
            result = OrganizationResult<SalesOrderDetails>.Failure(auth.Error);
        else if (!support.TryResolveSiteScope(scope, request.OrganizationId, request.SiteId, out var resolvedSiteId) || resolvedSiteId is not > 0)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);
        else
            result = await support.ExecuteStoreAsync(async () =>
            {
                var linesResult = await ResolveOrderLinesAsync(request.OrganizationId, request.Lines, ct);
                if (!linesResult.Succeeded)
                    return OrganizationResult<SalesOrderDetails>.Failure(linesResult.Error!.Code, linesResult.Error.Parameters);

                return await store.CreateSalesOrder(
                    request with { SiteId = resolvedSiteId.Value },
                    linesResult.Value!,
                    support.UtcNow,
                    support.Access(auth.Actor!, scope!, SalesPermissions.ManageOrders),
                    ct);
            });

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, request.SiteId, "SalesOrder", request.OrderNumber, nameof(CreateAsync));
    }

    public async Task<OrganizationResult<SalesOrderDetails>> UpdateAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        UpdateSalesOrderRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.ManageOrders, ct);
        OrganizationResult<SalesOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<SalesOrderDetails>.Failure(auth.Error);
        else if (!support.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);
        else if (salesOrderId <= 0)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
        else if (support.DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await support.ExecuteStoreAsync(async () =>
            {
                var existing = await store.GetSalesOrder(organizationId, salesOrderId, ct);
                if (existing is null)
                    return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                var linesResult = await ResolveOrderLinesAsync(organizationId, request.Lines, ct);
                if (!linesResult.Succeeded)
                    return OrganizationResult<SalesOrderDetails>.Failure(linesResult.Error!.Code, linesResult.Error.Parameters);

                return await store.UpdateSalesOrder(
                    organizationId,
                    salesOrderId,
                    request,
                    linesResult.Value!,
                    version,
                    support.UtcNow,
                    support.Access(auth.Actor!, scope!, SalesPermissions.ManageOrders),
                    ct);
            });

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "SalesOrder", salesOrderId.ToString(), nameof(UpdateAsync));
    }

    public Task<OrganizationResult<SalesOrderDetails>> ApproveAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        string? expectedVersion,
        CancellationToken ct = default)
        => TransitionOrderAsync(scope, organizationId, salesOrderId, expectedVersion, nameof(ApproveAsync),
            (ver, access) => store.ApproveSalesOrder(organizationId, salesOrderId, ver, support.UtcNow, access, ct), ct);

    public Task<OrganizationResult<SalesOrderDetails>> CloseAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        string? expectedVersion,
        CancellationToken ct = default)
        => TransitionOrderAsync(scope, organizationId, salesOrderId, expectedVersion, nameof(CloseAsync),
            (ver, access) => store.CloseSalesOrder(organizationId, salesOrderId, ver, support.UtcNow, access, ct), ct);

    public Task<OrganizationResult<SalesOrderDetails>> CancelAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        CancelSalesOrderRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
        => TransitionOrderAsync(scope, organizationId, salesOrderId, expectedVersion, nameof(CancelAsync),
            (ver, access) =>
            {
                var reason = SalesText.RequiredReason(request?.Reason);
                return store.CancelSalesOrder(organizationId, salesOrderId, reason, ver, support.UtcNow, access, ct);
            }, ct);

    private async Task<OrganizationResult<SalesOrderDetails>> TransitionOrderAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        string? expectedVersion,
        string actionName,
        Func<byte[], OrganizationWriteAccess, Task<OrganizationResult<SalesOrderDetails>>> operation,
        CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.ManageOrders, ct);
        OrganizationResult<SalesOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<SalesOrderDetails>.Failure(auth.Error);
        else if (!support.TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);
        else if (salesOrderId <= 0)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
        else if (support.DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await support.ExecuteStoreAsync(async () =>
            {
                var existing = await store.GetSalesOrder(organizationId, salesOrderId, ct);
                if (existing is null)
                    return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                return await operation(version, support.Access(auth.Actor!, scope!, SalesPermissions.ManageOrders));
            });

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "SalesOrder", salesOrderId.ToString(), actionName);
    }

    private async Task<OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>> ResolveOrderLinesAsync(
        long organizationId,
        IReadOnlyList<SalesOrderLineRequest>? inputs,
        CancellationToken ct)
    {
        if (inputs is null || inputs.Count == 0)
            return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.EmptyOrder);

        var seenItems = new HashSet<long>();
        foreach (var line in inputs)
        {
            if (line.ItemId <= 0)
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.ItemInvalid);
            if (!seenItems.Add(line.ItemId))
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.DuplicateItem);
        }

        var itemIds = inputs.Select(l => l.ItemId).Distinct().ToArray();
        var itemContexts = await store.LoadItemContexts(organizationId, itemIds, ct);

        var requestedUnitIds = inputs.Where(l => l.OrderedUnitId > 0).Select(l => l.OrderedUnitId).Distinct().ToArray();
        var activeUnitIds = requestedUnitIds.Length > 0
            ? await store.LoadActiveUnitIds(organizationId, requestedUnitIds, ct)
            : new HashSet<long>();

        var resolved = new List<ResolvedSalesOrderLine>(inputs.Count);
        foreach (var line in inputs)
        {
            if (!itemContexts.TryGetValue(line.ItemId, out var ctx) || !ctx.ItemIsActive)
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.ItemInvalid);
            if (!ctx.CanBeSold)
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.ItemNotSellable);

            var unitId = line.OrderedUnitId > 0 ? line.OrderedUnitId : ctx.BaseUnitId;
            if (unitId != ctx.BaseUnitId && !activeUnitIds.Contains(unitId))
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.LineUnitInvalid);

            var qty = SalesText.PositiveQuantity(line.OrderedQuantity);
            var baseQty = unitId == ctx.BaseUnitId
                ? qty
                : UnitConversionGraph.ConvertQuantity(qty, unitId, ctx.BaseUnitId, ctx.Edges).ConvertedQuantity;
            var price = SalesText.OptionalUnitPrice(line.UnitPrice);
            var notes = SalesText.OptionalText(line.Notes, SalesText.MaxLineNotesLength, SalesErrors.NotesInvalid);

            resolved.Add(new ResolvedSalesOrderLine(
                line.ItemId,
                qty,
                unitId,
                ctx.BaseUnitId,
                baseQty,
                price,
                notes));
        }

        return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Success(resolved);
    }
}
