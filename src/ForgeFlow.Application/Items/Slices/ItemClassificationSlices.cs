using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Items;

namespace ForgeFlow.Application.Items.Slices;

/// <summary>Vertical slice handlers for Item classification sets, trees, and item assignments.</summary>
internal sealed class ItemClassificationSlices(IItemOperationsStore store, ItemSliceSupport support)
{
    public async Task<OrganizationResult<ItemDetails>> SaveItemClassificationsAsync(
        OrganizationScope? scope, SaveItemClassificationsRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ItemDetails> res;
        if (auth.Error is not null) res = OrganizationResult<ItemDetails>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.ItemId <= 0)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.SaveItemClassifications(
                request.OrganizationId,
                request.ItemId,
                version,
                request.ClassificationIds,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "ItemClassification", request.ItemId.ToString(CultureInfo.InvariantCulture), "SaveItemClassifications");
    }

    public async Task<OrganizationResult<IReadOnlyList<ClassificationSetSummary>>> ListSetsAsync(
        OrganizationScope? scope, long organizationId, bool includeInactive, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ClassificationSetSummary>>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<ClassificationSetSummary>>.Failure(ItemErrors.NotFound);

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<IReadOnlyList<ClassificationSetSummary>>.Success(
                await store.ListClassificationSets(organizationId, includeInactive, ct)));
    }

    public async Task<OrganizationResult<ClassificationSetSummary>> CreateSetAsync(
        OrganizationScope? scope, CreateClassificationSetRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ClassificationSetSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ClassificationSetSummary>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<ClassificationSetSummary>.Failure(ItemErrors.NotFound);
        else
        {
            try
            {
                _ = ClassificationSet.Create(request.OrganizationId, request.Code, request.Name, request.IsMandatory, request.AllowMultiple);
                res = await ItemSliceSupport.ExecuteStoreAsync(() =>
                    store.CreateClassificationSet(request, support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
            }
            catch (ItemRuleException ex)
            {
                res = OrganizationResult<ClassificationSetSummary>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "ClassificationSet", request.Code, "CreateClassificationSet");
    }

    public async Task<OrganizationResult<OrganizationPage<ClassificationSummary>>> ListAsync(
        OrganizationScope? scope, long organizationId, ClassificationListQuery query, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ClassificationSummary>>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<ClassificationSummary>>.Failure(ItemErrors.NotFound);

        var bounded = query with
        {
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<ClassificationSummary>>.Success(
                await store.ListClassifications(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<ClassificationSummary>>> ListActiveAsync(
        OrganizationScope? scope, long organizationId, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ClassificationSummary>>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<ClassificationSummary>>.Failure(ItemErrors.NotFound);

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<IReadOnlyList<ClassificationSummary>>.Success(
                await store.ListActiveClassifications(organizationId, ct)));
    }

    public async Task<OrganizationResult<ClassificationSummary>> GetAsync(
        OrganizationScope? scope, long organizationId, long id, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<ClassificationSummary>.Failure(auth.Error);
        if (!ItemSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound);

        return await ItemSliceSupport.ExecuteStoreAsync(async () =>
        {
            var value = await store.GetClassification(organizationId, id, ct);
            return value is null
                ? OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound)
                : OrganizationResult<ClassificationSummary>.Success(value);
        });
    }

    public async Task<OrganizationResult<ClassificationSummary>> CreateAsync(
        OrganizationScope? scope, CreateClassificationRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ClassificationSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ClassificationSummary>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<ClassificationSummary>.Failure(ItemErrors.NotFound);
        else
        {
            try
            {
                _ = Classification.Create(request.OrganizationId, request.ClassificationSetId, request.ParentClassificationId, request.Code, request.Name);
                res = await ItemSliceSupport.ExecuteStoreAsync(() =>
                    store.CreateClassification(request, support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
            }
            catch (ItemRuleException ex)
            {
                res = OrganizationResult<ClassificationSummary>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Classification", request.Code, "CreateClassification");
    }

    public async Task<OrganizationResult<ClassificationSummary>> UpdateAsync(
        OrganizationScope? scope, UpdateClassificationRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ClassificationSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ClassificationSummary>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound);
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ConcurrencyConflict);
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.UpdateClassification(
                request.OrganizationId,
                request.Id,
                version,
                request.Name,
                request.ParentClassificationId,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Classification", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateClassification");
    }

    public async Task<OrganizationResult<ClassificationSummary>> SetActiveAsync(
        OrganizationScope? scope, SetClassificationActiveRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, ItemPermissions.ItemsManage, ct);
        OrganizationResult<ClassificationSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ClassificationSummary>.Failure(auth.Error);
        else if (!ItemSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound);
        else if (ItemSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ConcurrencyConflict);
        else
            res = await ItemSliceSupport.ExecuteStoreAsync(() => store.SetClassificationActive(
                request.OrganizationId,
                request.Id,
                version,
                request.IsActive,
                support.Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Classification", request.Id.ToString(CultureInfo.InvariantCulture), "SetClassificationActive");
    }
}
