using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;

namespace ForgeFlow.Application.Inventory.Slices;

public sealed class WarehouseSlices(
    IInventoryOperationsStore store,
    InventorySliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<WarehouseSummary>>> ListWarehousesAsync(
        OrganizationScope? scope,
        long organizationId,
        WarehouseListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, InventoryPermissions.InventoryRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<WarehouseSummary>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<WarehouseSummary>>.Failure(InventoryErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<WarehouseSummary>>.Success(
                await store.ListWarehouses(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<WarehouseSummary>> CreateWarehouseAsync(
        OrganizationScope? scope,
        CreateWarehouseRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, InventoryPermissions.InventoryManage, ct);
        OrganizationResult<WarehouseSummary> res;
        if (auth.Error is not null) res = OrganizationResult<WarehouseSummary>.Failure(auth.Error);
        else if (request.SiteId <= 0) res = OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.SiteInvalid);
        else if (!support.TryResolveScope(scope, request.OrganizationId, request.SiteId, out _))
            res = OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.AccessDenied);
        else
            res = await support.ExecuteStoreAsync(() =>
            {
                _ = InventoryText.Code(request.Code);
                _ = InventoryText.Name(request.Name);
                return store.CreateWarehouse(request, support.Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage), ct);
            });
        await support.RecordIfConsequentialRejectionAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null, "Warehouse", request.Code, new { Action = "CreateWarehouse" });
        return res;
    }

    public async Task<OrganizationResult<WarehouseSummary>> UpdateWarehouseAsync(
        OrganizationScope? scope,
        UpdateWarehouseRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, InventoryPermissions.InventoryManage, ct);
        OrganizationResult<WarehouseSummary> res;
        if (auth.Error is not null) res = OrganizationResult<WarehouseSummary>.Failure(auth.Error);
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(async () =>
            {
                _ = InventoryText.Name(request.Name);
                var existing = await store.GetWarehouse(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);
                if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                    return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.AccessDenied);

                return await store.UpdateWarehouse(request.OrganizationId, request.Id, version, request, support.Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage), ct);
            });
        await support.RecordIfConsequentialRejectionAsync(res, auth.Actor, scope, request.OrganizationId, null, "Warehouse", request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), new { Action = "UpdateWarehouse" });
        return res;
    }

    public async Task<OrganizationResult<WarehouseSummary>> SetWarehouseActiveAsync(
        OrganizationScope? scope,
        SetWarehouseActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, InventoryPermissions.InventoryManage, ct);
        OrganizationResult<WarehouseSummary> res;
        if (auth.Error is not null) res = OrganizationResult<WarehouseSummary>.Failure(auth.Error);
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.Id <= 0)
            res = OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(async () =>
            {
                var existing = await store.GetWarehouse(request.OrganizationId, request.Id, ct);
                if (existing is null) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);
                if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                    return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.AccessDenied);

                return await store.SetWarehouseActive(request.OrganizationId, request.Id, version, request.IsActive, support.Access(auth.Actor!, scope!, InventoryPermissions.InventoryManage), ct);
            });
        await support.RecordIfConsequentialRejectionAsync(res, auth.Actor, scope, request.OrganizationId, null, "Warehouse", request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), new { Action = "SetWarehouseActive" });
        return res;
    }
}
