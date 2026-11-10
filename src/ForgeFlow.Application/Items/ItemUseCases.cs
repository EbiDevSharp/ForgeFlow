using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Items;

/// <summary>Secured use cases for organization-scoped Items, Classifications, Item Unit Conversions, and Attributes.</summary>
public sealed class ItemUseCases(
    IItemOperationsStore store,
    IUnitOperationsStore? unitsStore = null,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null)
{
    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, ItemErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, ItemErrors.ActorRequired);
        if (scope is null) return (null, ItemErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, ItemErrors.AccessDenied);
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

    private static bool TryScopeOrganizationId(OrganizationScope? scope, long requestedOrgId, bool allowSite, out long orgId)
    {
        orgId = scope switch
        {
            OrganizationScope.Organization org => org.OrganizationId,
            OrganizationScope.Site site when allowSite => site.OrganizationId,
            _ => 0
        };
        return orgId > 0 && orgId == requestedOrgId;
    }

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

    private static ItemListQuery Bound(ItemListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static ClassificationListQuery Bound(ClassificationListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static AttributeDefinitionListQuery Bound(AttributeDefinitionListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (ItemRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (ItemStoreUnavailableException) { return OrganizationResult<T>.Failure(ItemErrors.PersistenceUnavailable); }
        catch (UnitStoreUnavailableException) { return OrganizationResult<T>.Failure(ItemErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<OrganizationPage<ItemSummary>>> ListItems(
        OrganizationScope? scope,
        long organizationId,
        ItemListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ItemSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<ItemSummary>>.Failure(ItemErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<ItemSummary>>.Success(
                await store.ListItems(organizationId, Bound(query), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<ItemSummary>>> ListActiveItems(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ItemSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<ItemSummary>>.Failure(ItemErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<ItemSummary>>.Success(
                await store.ListActiveItems(organizationId, ct)));
    }

    public async Task<OrganizationResult<ItemDetails>> GetItem(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);

        return await ExecuteStore(async () =>
        {
            var item = await store.GetItem(organizationId, id, ct);
            return item is null
                ? OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound)
                : OrganizationResult<ItemDetails>.Success(item);
        });
    }

    public async Task<OrganizationResult<ItemDetails>> CreateItem(
        OrganizationScope? scope,
        CreateItemRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);

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
                request.CanBeConsumed);
        }
        catch (ItemRuleException ex)
        {
            return OrganizationResult<ItemDetails>.Failure(ex.Code, ex.Parameters);
        }

        return await ExecuteStore(() => store.CreateItem(request, Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
    }

    public async Task<OrganizationResult<ItemDetails>> UpdateItem(
        OrganizationScope? scope,
        UpdateItemRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.UpdateItem(
            request.OrganizationId,
            request.Id,
            version,
            request,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }

    public async Task<OrganizationResult<ItemDetails>> SetItemActive(
        OrganizationScope? scope,
        SetItemActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.SetItemActive(
            request.OrganizationId,
            request.Id,
            version,
            request.IsActive,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }

    public async Task<OrganizationResult<ItemDetails>> SaveItemClassifications(
        OrganizationScope? scope,
        SaveItemClassificationsRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.ItemId <= 0)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.SaveItemClassifications(
            request.OrganizationId,
            request.ItemId,
            version,
            request.ClassificationIds,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }

    public async Task<OrganizationResult<ItemDetails>> CreateItemConversion(
        OrganizationScope? scope,
        CreateItemUnitConversionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.ItemId <= 0)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);

        try
        {
            _ = ItemUnitConversion.Create(request.OrganizationId, request.ItemId, request.FromUnitId, request.ToUnitId, request.Factor);
        }
        catch (ItemRuleException ex) { return OrganizationResult<ItemDetails>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<ItemDetails>.Failure(ex.Code, ex.Parameters); }

        return await ExecuteStore(() => store.CreateItemConversion(request, Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
    }

    public async Task<OrganizationResult<ItemDetails>> UpdateItemConversionFactor(
        OrganizationScope? scope,
        UpdateItemUnitConversionFactorRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _)
            || request.ItemId <= 0
            || request.ConversionId <= 0)
        {
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemUnitConversionNotFound);
        }
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.UpdateItemConversionFactor(
            request.OrganizationId,
            request.ItemId,
            request.ConversionId,
            version,
            request.Factor,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }

    public async Task<OrganizationResult<ItemDetails>> SetItemConversionActive(
        OrganizationScope? scope,
        SetItemUnitConversionActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _)
            || request.ItemId <= 0
            || request.ConversionId <= 0)
        {
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemUnitConversionNotFound);
        }
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.SetItemConversionActive(
            request.OrganizationId,
            request.ItemId,
            request.ConversionId,
            version,
            request.IsActive,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }

    /// <summary>A08 hybrid conversion resolver combining item-specific edges and global organization edges.</summary>
    public async Task<OrganizationResult<UnitConversionPreviewResult>> PreviewItemConversion(
        OrganizationScope? scope,
        ConvertItemQuantityRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<UnitConversionPreviewResult>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: true, out _) || request.ItemId <= 0)
            return OrganizationResult<UnitConversionPreviewResult>.Failure(ItemErrors.NotFound);

        return await ExecuteStore(async () =>
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

    public async Task<OrganizationResult<IReadOnlyList<ClassificationSetSummary>>> ListClassificationSets(
        OrganizationScope? scope,
        long organizationId,
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ClassificationSetSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<ClassificationSetSummary>>.Failure(ItemErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<ClassificationSetSummary>>.Success(
                await store.ListClassificationSets(organizationId, includeInactive, ct)));
    }

    public async Task<OrganizationResult<ClassificationSetSummary>> CreateClassificationSet(
        OrganizationScope? scope,
        CreateClassificationSetRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ClassificationSetSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            return OrganizationResult<ClassificationSetSummary>.Failure(ItemErrors.NotFound);

        try
        {
            _ = ClassificationSet.Create(request.OrganizationId, request.Code, request.Name, request.IsMandatory, request.AllowMultiple);
        }
        catch (ItemRuleException ex)
        {
            return OrganizationResult<ClassificationSetSummary>.Failure(ex.Code, ex.Parameters);
        }

        return await ExecuteStore(() => store.CreateClassificationSet(request, Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
    }

    public async Task<OrganizationResult<OrganizationPage<ClassificationSummary>>> ListClassifications(
        OrganizationScope? scope,
        long organizationId,
        ClassificationListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ClassificationSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<ClassificationSummary>>.Failure(ItemErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<ClassificationSummary>>.Success(
                await store.ListClassifications(organizationId, Bound(query), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<ClassificationSummary>>> ListActiveClassifications(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ClassificationSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<ClassificationSummary>>.Failure(ItemErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<ClassificationSummary>>.Success(
                await store.ListActiveClassifications(organizationId, ct)));
    }

    public async Task<OrganizationResult<ClassificationSummary>> GetClassification(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<ClassificationSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound);

        return await ExecuteStore(async () =>
        {
            var value = await store.GetClassification(organizationId, id, ct);
            return value is null
                ? OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound)
                : OrganizationResult<ClassificationSummary>.Success(value);
        });
    }

    public async Task<OrganizationResult<ClassificationSummary>> CreateClassification(
        OrganizationScope? scope,
        CreateClassificationRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ClassificationSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.NotFound);

        try
        {
            _ = Classification.Create(request.OrganizationId, request.ClassificationSetId, request.ParentClassificationId, request.Code, request.Name);
        }
        catch (ItemRuleException ex)
        {
            return OrganizationResult<ClassificationSummary>.Failure(ex.Code, ex.Parameters);
        }

        return await ExecuteStore(() => store.CreateClassification(request, Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
    }

    public async Task<OrganizationResult<ClassificationSummary>> UpdateClassification(
        OrganizationScope? scope,
        UpdateClassificationRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ClassificationSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.UpdateClassification(
            request.OrganizationId,
            request.Id,
            version,
            request.Name,
            request.ParentClassificationId,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }

    public async Task<OrganizationResult<ClassificationSummary>> SetClassificationActive(
        OrganizationScope? scope,
        SetClassificationActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ClassificationSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.SetClassificationActive(
            request.OrganizationId,
            request.Id,
            version,
            request.IsActive,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }

    public async Task<OrganizationResult<OrganizationPage<AttributeDefinitionSummary>>> ListAttributeDefinitions(
        OrganizationScope? scope,
        long organizationId,
        AttributeDefinitionListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<AttributeDefinitionSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<AttributeDefinitionSummary>>.Failure(ItemErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<AttributeDefinitionSummary>>.Success(
                await store.ListAttributeDefinitions(organizationId, Bound(query), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<AttributeDefinitionSummary>>> ListActiveAttributeDefinitions(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<AttributeDefinitionSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<AttributeDefinitionSummary>>.Failure(ItemErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<AttributeDefinitionSummary>>.Success(
                await store.ListActiveAttributeDefinitions(organizationId, ct)));
    }

    public async Task<OrganizationResult<AttributeDefinitionSummary>> GetAttributeDefinition(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsRead, ct);
        if (auth.Error is not null) return OrganizationResult<AttributeDefinitionSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound);

        return await ExecuteStore(async () =>
        {
            var value = await store.GetAttributeDefinition(organizationId, id, ct);
            return value is null
                ? OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound)
                : OrganizationResult<AttributeDefinitionSummary>.Success(value);
        });
    }

    public async Task<OrganizationResult<AttributeDefinitionSummary>> CreateAttributeDefinition(
        OrganizationScope? scope,
        CreateAttributeDefinitionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<AttributeDefinitionSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.NotFound);

        try
        {
            _ = AttributeDefinition.Create(request.OrganizationId, request.Code, request.Name, request.DataType, request.UnitId, request.IsRequired);
        }
        catch (ItemRuleException ex)
        {
            return OrganizationResult<AttributeDefinitionSummary>.Failure(ex.Code, ex.Parameters);
        }

        return await ExecuteStore(() => store.CreateAttributeDefinition(request, Access(auth.Actor!, scope!, ItemPermissions.ItemsManage), ct));
    }

    public async Task<OrganizationResult<AttributeDefinitionSummary>> UpdateAttributeDefinition(
        OrganizationScope? scope,
        UpdateAttributeDefinitionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<AttributeDefinitionSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.UpdateAttributeDefinition(
            request.OrganizationId,
            request.Id,
            version,
            request.Name,
            request.UnitId,
            request.IsRequired,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }

    public async Task<OrganizationResult<AttributeDefinitionSummary>> SetAttributeDefinitionActive(
        OrganizationScope? scope,
        SetAttributeDefinitionActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<AttributeDefinitionSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.SetAttributeDefinitionActive(
            request.OrganizationId,
            request.Id,
            version,
            request.IsActive,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }

    public async Task<OrganizationResult<ItemDetails>> SaveItemAttributeValues(
        OrganizationScope? scope,
        SaveItemAttributeValuesRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ItemPermissions.ItemsManage, ct);
        if (auth.Error is not null) return OrganizationResult<ItemDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.ItemId <= 0)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.SaveItemAttributeValues(
            request.OrganizationId,
            request.ItemId,
            version,
            request.Values,
            Access(auth.Actor!, scope!, ItemPermissions.ItemsManage),
            ct));
    }
}
