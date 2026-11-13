using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Items;

namespace ForgeFlow.Application.Items.Slices;

/// <summary>Vertical slice handlers for dynamic Attribute Definitions and Item attribute values.</summary>
internal sealed class ItemAttributeSlices(IItemOperationsStore store, ItemSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<AttributeDefinitionSummary>>> ListAsync(
        OrganizationScope? scope, long organizationId, AttributeDefinitionListQuery query, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<AttributeDefinitionSummary>>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<AttributeDefinitionSummary>>.Failure(ItemErrors.NotFound);

        var bounded = query with
        {
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<AttributeDefinitionSummary>>.Success(
                await store.ListAttributeDefinitions(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<AttributeDefinitionSummary>>> ListActiveAsync(
        OrganizationScope? scope, long organizationId, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<AttributeDefinitionSummary>>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<AttributeDefinitionSummary>>.Failure(ItemErrors.NotFound);

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<IReadOnlyList<AttributeDefinitionSummary>>.Success(
                await store.ListActiveAttributeDefinitions(organizationId, ct)));
    }

    public async Task<OrganizationResult<AttributeDefinitionSummary>> GetAsync(
        OrganizationScope? scope, long organizationId, long id, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<AttributeDefinitionSummary>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound);

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
        {
            var value = await store.GetAttributeDefinition(organizationId, id, ct);
            return value is null
                ? OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound)
                : OrganizationResult<AttributeDefinitionSummary>.Success(value);
        });
    }

    public async Task<OrganizationResult<AttributeDefinitionSummary>> CreateAsync(
        OrganizationScope? scope, CreateAttributeDefinitionRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<AttributeDefinitionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<AttributeDefinitionSummary>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.NotFound);
        else
        {
            try
            {
                _ = AttributeDefinition.Create(request.OrganizationId, request.Code, request.Name, request.DataType, request.UnitId, request.IsRequired);
                res = await ItemSliceSupport.ExecuteStoreAsync(() =>
                    store.CreateAttributeDefinition(request, support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
            }
            catch (ItemRuleException ex)
            {
                res = OrganizationResult<AttributeDefinitionSummary>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "AttributeDefinition", request.Code, "CreateAttributeDefinition");
    }

    public async Task<OrganizationResult<AttributeDefinitionSummary>> UpdateAsync(
        OrganizationScope? scope, UpdateAttributeDefinitionRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<AttributeDefinitionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<AttributeDefinitionSummary>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound);
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.ConcurrencyConflict);
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.UpdateAttributeDefinition(
                request.OrganizationId,
                request.Id,
                version,
                request.Name,
                request.UnitId,
                request.IsRequired,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "AttributeDefinition", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateAttributeDefinition");
    }

    public async Task<OrganizationResult<AttributeDefinitionSummary>> SetActiveAsync(
        OrganizationScope? scope, SetAttributeDefinitionActiveRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<AttributeDefinitionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<AttributeDefinitionSummary>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound);
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.ConcurrencyConflict);
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.SetAttributeDefinitionActive(
                request.OrganizationId,
                request.Id,
                version,
                request.IsActive,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "AttributeDefinition", request.Id.ToString(CultureInfo.InvariantCulture), "SetAttributeDefinitionActive");
    }

    public async Task<OrganizationResult<ItemDetails>> SaveItemAttributeValuesAsync(
        OrganizationScope? scope, SaveItemAttributeValuesRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ItemDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ItemDetails>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.ItemId <= 0)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.SaveItemAttributeValues(
                request.OrganizationId,
                request.ItemId,
                version,
                request.Values,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "ItemAttributeValue", request.ItemId.ToString(CultureInfo.InvariantCulture), "SaveItemAttributeValues");
    }
}
