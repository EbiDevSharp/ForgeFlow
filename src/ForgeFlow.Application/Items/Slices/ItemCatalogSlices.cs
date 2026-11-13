using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Items;

namespace ForgeFlow.Application.Items.Slices;

/// <summary>Vertical slice handlers for core Item catalog queries and commands.</summary>
internal sealed class ItemCatalogSlices(IItemOperationsStore store, ItemSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<ItemSummary>>> ListAsync(
        OrganizationScope? scope, long organizationId, ItemListQuery query, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ItemSummary>>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<ItemSummary>>.Failure(ItemErrors.NotFound);

        var bounded = query with
        {
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<ItemSummary>>.Success(
                await store.ListItems(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<ItemSummary>>> ListActiveAsync(
        OrganizationScope? scope, long organizationId, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ItemSummary>>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<ItemSummary>>.Failure(ItemErrors.NotFound);

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<IReadOnlyList<ItemSummary>>.Success(
                await store.ListActiveItems(organizationId, ct)));
    }

    public async Task<OrganizationResult<ItemDetails>> GetAsync(
        OrganizationScope? scope, long organizationId, long id, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
        {
            var item = await store.GetItem(organizationId, id, ct);
            return item is null
                ? OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound)
                : OrganizationResult<ItemDetails>.Success(item);
        });
    }

    public async Task<OrganizationResult<ItemDetails>> CreateAsync(
        OrganizationScope? scope, CreateItemRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ItemDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ItemDetails>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        else
        {
            try
            {
                _ = Item.Create(
                    request.OrganizationId,
                    request.Code,
                    request.Name,
                    request.Description,
                    request.BaseUnitId,
                    request.CanBePurchased,
                    request.CanBeManufactured,
                    request.CanBeSold,
                    request.CanBeConsumed,
                    request.RequiresLotTracking,
                    request.QualityControlMode);
                res = await ItemSliceSupport.ExecuteStoreAsync(() =>
                    store.CreateItem(request, support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
            }
            catch (ItemRuleException ex)
            {
                res = OrganizationResult<ItemDetails>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Item", request.Code, "CreateItem");
    }

    public async Task<OrganizationResult<ItemDetails>> UpdateAsync(
        OrganizationScope? scope, UpdateItemRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ItemDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ItemDetails>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);
        else if (!Enum.IsDefined(request.QualityControlMode))
            res = OrganizationResult<ItemDetails>.Failure(
                ItemErrors.QualityControlModeInvalid,
                new Dictionary<string, object> { ["field"] = "QualityControlMode" });
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.UpdateItem(
                request.OrganizationId,
                request.Id,
                version,
                request,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Item", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateItem");
    }

    public async Task<OrganizationResult<ItemDetails>> SetActiveAsync(
        OrganizationScope? scope, SetItemActiveRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ItemDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ItemDetails>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.SetItemActive(
                request.OrganizationId,
                request.Id,
                version,
                request.IsActive,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Item", request.Id.ToString(CultureInfo.InvariantCulture), "SetItemActive");
    }
}
