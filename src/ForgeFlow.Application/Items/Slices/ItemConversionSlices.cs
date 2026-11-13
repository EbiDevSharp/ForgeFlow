using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Items.Slices;

/// <summary>Vertical slice handlers for Item-specific unit conversions and hybrid conversion preview.</summary>
internal sealed class ItemConversionSlices(
    IItemOperationsStore store,
    IUnitOperationsStore? unitsStore,
    ItemSliceSupport support)
{
    public async Task<OrganizationResult<ItemDetails>> CreateAsync(
        OrganizationScope? scope, CreateItemUnitConversionRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ItemDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ItemDetails>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.ItemId <= 0)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        else
        {
            try
            {
                _ = ItemUnitConversion.Create(request.OrganizationId, request.ItemId, request.FromUnitId, request.ToUnitId, request.Factor);
                res = await ItemSliceSupport.ExecuteStoreAsync(() =>
                    store.CreateItemConversion(request, support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
            }
            catch (ItemRuleException ex) { res = OrganizationResult<ItemDetails>.Failure(ex.Code, ex.Parameters); }
            catch (UnitRuleException ex) { res = OrganizationResult<ItemDetails>.Failure(ex.Code, ex.Parameters); }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "ItemUnitConversion", request.ItemId.ToString(CultureInfo.InvariantCulture), "CreateItemConversion");
    }

    public async Task<OrganizationResult<ItemDetails>> UpdateFactorAsync(
        OrganizationScope? scope, UpdateItemUnitConversionFactorRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ItemDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ItemDetails>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _)
            || request.ItemId <= 0
            || request.ConversionId <= 0)
        {
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemUnitConversionNotFound);
        }
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.UpdateItemConversionFactor(
                request.OrganizationId,
                request.ItemId,
                request.ConversionId,
                version,
                request.Factor,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "ItemUnitConversion", request.ConversionId.ToString(CultureInfo.InvariantCulture), "UpdateItemConversionFactor");
    }

    public async Task<OrganizationResult<ItemDetails>> SetActiveAsync(
        OrganizationScope? scope, SetItemUnitConversionActiveRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ItemDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ItemDetails>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _)
            || request.ItemId <= 0
            || request.ConversionId <= 0)
        {
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemUnitConversionNotFound);
        }
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.SetItemConversionActive(
                request.OrganizationId,
                request.ItemId,
                request.ConversionId,
                version,
                request.IsActive,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "ItemUnitConversion", request.ConversionId.ToString(CultureInfo.InvariantCulture), "SetItemConversionActive");
    }

    public async Task<OrganizationResult<UnitConversionPreviewResult>> PreviewAsync(
        OrganizationScope? scope, ConvertItemQuantityRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<UnitConversionPreviewResult>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: true, out _) || request.ItemId <= 0)
            return OrganizationResult<UnitConversionPreviewResult>.Failure(ItemErrors.NotFound);

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
        {
            if (request.Quantity <= 0m)
                return OrganizationResult<UnitConversionPreviewResult>.Failure(UnitErrors.ConversionQuantityInvalid);

            var item = await store.GetItem(request.OrganizationId, request.ItemId, ct);
            if (item is null)
                return OrganizationResult<UnitConversionPreviewResult>.Failure(ItemErrors.NotFound);
            if (!item.Summary.IsActive)
                return OrganizationResult<UnitConversionPreviewResult>.Failure(ItemErrors.ItemInactive);

            var itemEdges = await store.ListItemActiveConversionEdges(request.OrganizationId, request.ItemId, ct);
            var globalEdges = unitsStore is not null
                ? await unitsStore.ListActiveConversionEdges(request.OrganizationId, ct)
                : [];

            var resolution = UnitConversionGraph.ConvertQuantity(
                request.Quantity,
                request.FromUnitId,
                request.ToUnitId,
                itemEdges.Concat(globalEdges));

            return OrganizationResult<UnitConversionPreviewResult>.Success(new UnitConversionPreviewResult(
                request.FromUnitId,
                request.ToUnitId,
                request.Quantity,
                resolution.Factor,
                resolution.ConvertedQuantity,
                resolution.Hops));
        });
    }
}
