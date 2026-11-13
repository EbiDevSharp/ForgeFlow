using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.ProductionOrders;

namespace ForgeFlow.Application.ProductionOrders.Slices;

public sealed class ProductionOrderLifecycleSlices(
    IProductionOrderOperationsStore store,
    ProductionOrderSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<ProductionOrderSummary>>> ListOrdersAsync(
        OrganizationScope? scope,
        long organizationId,
        ProductionOrderListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ProductionOrderSummary>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<ProductionOrderSummary>>.Failure(ProductionOrderErrors.AccessDenied);

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<ProductionOrderSummary>>.Success(
                await store.ListOrders(organizationId, support.Bound(query, forcedSiteId), ct)));
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> GetOrderAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersRead, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);

        return await support.ExecuteStoreAsync(async () =>
        {
            var order = await store.GetOrder(organizationId, id, ct);
            if (order is null)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && order.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);
            return OrganizationResult<ProductionOrderDetails>.Success(order);
        });
    }

    public async Task<OrganizationResult<IReadOnlyList<ProductionOrderSiteInfo>>> ListAuthorizedSitesAsync(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ProductionOrderSiteInfo>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<ProductionOrderSiteInfo>>.Failure(ProductionOrderErrors.AccessDenied);

        return await support.ExecuteStoreAsync(async () =>
        {
            var all = await store.ListActiveSites(organizationId, ct);
            if (forcedSiteId.HasValue)
                all = all.Where(s => s.Id == forcedSiteId.Value).ToArray();
            return OrganizationResult<IReadOnlyList<ProductionOrderSiteInfo>>.Success(all);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> CreateDraftOrderAsync(
        OrganizationScope? scope,
        CreateProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        OrganizationResult<ProductionOrderDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        else if (request.SiteId <= 0)
            res = OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.SiteInvalid);
        else if (!support.TryResolveScope(scope, request.OrganizationId, request.SiteId, out _))
            res = OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);
        else
            res = await support.ExecuteStoreAsync(async () =>
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

                var snapshotRes = await support.BuildSnapshotAsync(
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

                var routingRes = await support.BuildRoutingSnapshotAsync(
                    request.OrganizationId,
                    request.SiteId,
                    request.ItemId,
                    request.RequestedQuantity,
                    request.RoutingId,
                    ct);
                if (!routingRes.Succeeded)
                    return OrganizationResult<ProductionOrderDetails>.Failure(routingRes.Error!.Code, routingRes.Error.Parameters);

                return await store.CreateDraft(request, snapshotRes.Value!, support.Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct, routingRes.Value);
            });
        await support.RecordIfRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null, request.OrderNumber ?? string.Empty, "CreateDraft");
        return res;
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> UpdateDraftOrderAsync(
        OrganizationScope? scope,
        UpdateDraftProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        OrganizationResult<ProductionOrderDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(async () =>
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

                var snapshotRes = await support.BuildSnapshotAsync(
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

                var routingRes = await support.BuildRoutingSnapshotAsync(
                    request.OrganizationId,
                    existing.Summary.SiteId,
                    existing.Summary.ItemId,
                    request.RequestedQuantity,
                    request.RoutingId,
                    ct);
                if (!routingRes.Succeeded)
                    return OrganizationResult<ProductionOrderDetails>.Failure(routingRes.Error!.Code, routingRes.Error.Parameters);

                return await store.UpdateDraft(request.OrganizationId, request.Id, version, request, snapshotRes.Value!,
                    support.Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct, routingRes.Value);
            });
        await support.RecordIfRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, request.Id, "UpdateDraft");
        return res;
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> RefreshDraftSnapshotAsync(
        OrganizationScope? scope,
        RefreshProductionOrderSnapshotRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        OrganizationResult<ProductionOrderDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(async () =>
            {
                var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
                if (existing is null)
                    return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);
                if (existing.Summary.Status != ProductionOrderStatus.Draft)
                    return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.OrderImmutable);

                var ctx = existing.Contexts.ToDictionary(c => c.DimensionCode, c => c.DimensionValue, StringComparer.OrdinalIgnoreCase);
                var snapshotRes = await support.BuildSnapshotAsync(
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

                var routingRes = await support.BuildRoutingSnapshotAsync(
                    request.OrganizationId,
                    existing.Summary.SiteId,
                    existing.Summary.ItemId,
                    existing.Summary.RequestedQuantity,
                    existing.Summary.RoutingId,
                    ct);
                if (!routingRes.Succeeded)
                    return OrganizationResult<ProductionOrderDetails>.Failure(routingRes.Error!.Code, routingRes.Error.Parameters);

                return await store.RefreshDraftSnapshot(request.OrganizationId, request.Id, version, snapshotRes.Value!,
                    support.Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct, routingRes.Value);
            });
        await support.RecordIfRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, request.Id, "RefreshSnapshot");
        return res;
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> ReleaseOrderAsync(
        OrganizationScope? scope,
        ReleaseProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        var res = await support.ExecuteStoreAsync(async () =>
        {
            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);
            if (existing.Summary.Status != ProductionOrderStatus.Draft)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.TransitionInvalid);

            var ctx = existing.Contexts.ToDictionary(c => c.DimensionCode, c => c.DimensionValue, StringComparer.OrdinalIgnoreCase);
            var snapshotRes = await support.BuildSnapshotAsync(
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

            var routingRes = await support.BuildRoutingSnapshotAsync(
                request.OrganizationId,
                existing.Summary.SiteId,
                existing.Summary.ItemId,
                existing.Summary.RequestedQuantity,
                existing.Summary.RoutingId,
                ct);
            if (!routingRes.Succeeded)
                return OrganizationResult<ProductionOrderDetails>.Failure(routingRes.Error!.Code, routingRes.Error.Parameters);

            return await store.ReleaseOrder(request.OrganizationId, request.Id, version, snapshotRes.Value!, DateTime.UtcNow,
                support.Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct, routingRes.Value);
        });

        await support.RecordIfRejectedAsync(res, auth.Actor!, scope!, request.OrganizationId, forcedSiteId, request.Id, "ReleaseOrder");
        return res;
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> StartOrderAsync(
        OrganizationScope? scope,
        StartProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await support.ExecuteStoreAsync(async () =>
        {
            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);

            return await store.StartOrder(request.OrganizationId, request.Id, version, DateTime.UtcNow,
                support.Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> RecordActualsAsync(
        OrganizationScope? scope,
        RecordProductionOrderActualsRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await support.ExecuteStoreAsync(async () =>
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
                support.Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> CompleteOrderAsync(
        OrganizationScope? scope,
        CompleteProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await support.ExecuteStoreAsync(async () =>
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
                support.Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }

    public async Task<OrganizationResult<ProductionOrderDetails>> CancelOrderAsync(
        OrganizationScope? scope,
        CancelProductionOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ProductionOrderPermissions.ProductionOrdersManage, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionOrderDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        return await support.ExecuteStoreAsync(async () =>
        {
            _ = ProductionOrderText.RequiredReason(request.Reason);

            var existing = await store.GetOrder(request.OrganizationId, request.Id, ct);
            if (existing is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.AccessDenied);

            return await store.CancelOrder(request.OrganizationId, request.Id, version, request.Reason, DateTime.UtcNow,
                support.Access(auth.Actor!, scope!, ProductionOrderPermissions.ProductionOrdersManage), ct);
        });
    }
}
