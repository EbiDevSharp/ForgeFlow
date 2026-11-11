using System.Reflection;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Roles;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ItemTests
{
    private sealed class ActorStub(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(actor);
    }

    private sealed class PermissionStub(Func<OrganizationScope, string, bool> predicate) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor,
            OrganizationScope requestedScope,
            string permission,
            CancellationToken ct)
            => ValueTask.FromResult(new PermissionDecision(predicate(requestedScope, permission)));
    }

    private sealed class UnitEdgesStub(IReadOnlyList<UnitConversionEdge> edges) : IUnitOperationsStore
    {
        public Task<OrganizationPage<UnitSummary>> ListUnits(long organizationId, UnitListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<UnitSummary>> ListActiveUnits(long organizationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<UnitSummary?> GetUnit(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<UnitSummary>> CreateUnit(CreateUnitRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<UnitSummary>> UpdateUnit(long organizationId, long id, byte[] version, string? name, string? symbol, byte precisionScale, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<UnitSummary>> SetUnitActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<UnitConversionSummary>> ListConversions(long organizationId, UnitConversionListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<UnitConversionSummary?> GetConversion(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<UnitConversionEdge>> ListActiveConversionEdges(long organizationId, CancellationToken ct) => Task.FromResult(edges);
        public Task<OrganizationResult<UnitConversionSummary>> CreateConversion(CreateUnitConversionRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<UnitConversionSummary>> UpdateConversionFactor(long organizationId, long id, byte[] version, decimal factor, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<UnitConversionSummary>> SetConversionActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SeedStandardUnitsResult>> SeedStandardUnits(long organizationId, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<CurrencySummary>> ListCurrencies(long organizationId, CurrencyListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<CurrencySummary>> ListActiveCurrencies(long organizationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<CurrencySummary?> GetCurrency(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<CurrencySummary>> CreateCurrency(CreateCurrencyRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<CurrencySummary>> UpdateCurrency(long organizationId, long id, byte[] version, string? name, string? symbol, byte precisionScale, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<CurrencySummary>> SetCurrencyActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class ItemStoreStub : IItemOperationsStore
    {
        public ItemDetails? CurrentItem { get; set; }
        public List<UnitConversionEdge> ItemEdges { get; } = [];
        public bool CreateCalled { get; private set; }

        public Task<OrganizationPage<ItemSummary>> ListItems(long organizationId, ItemListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<ItemSummary>(CurrentItem is null ? [] : [CurrentItem.Summary], CurrentItem is null ? 0 : 1, query.Page, query.PageSize));
        public Task<IReadOnlyList<ItemSummary>> ListActiveItems(long organizationId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ItemSummary>>(CurrentItem is null ? [] : [CurrentItem.Summary]);
        public Task<ItemDetails?> GetItem(long organizationId, long id, CancellationToken ct)
            => Task.FromResult(CurrentItem is not null && CurrentItem.Summary.OrganizationId == organizationId && CurrentItem.Summary.Id == id ? CurrentItem : null);
        public Task<OrganizationResult<ItemDetails>> CreateItem(CreateItemRequest request, OrganizationWriteAccess access, CancellationToken ct)
        {
            CreateCalled = true;
            throw new NotImplementedException();
        }
        public Task<OrganizationResult<ItemDetails>> UpdateItem(long organizationId, long id, byte[] version, UpdateItemRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ItemDetails>> SetItemActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ItemDetails>> SaveItemClassifications(long organizationId, long itemId, byte[] version, IReadOnlyList<long> classificationIds, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<UnitConversionEdge>> ListItemActiveConversionEdges(long organizationId, long itemId, CancellationToken ct) => Task.FromResult<IReadOnlyList<UnitConversionEdge>>(ItemEdges);
        public Task<OrganizationResult<ItemDetails>> CreateItemConversion(CreateItemUnitConversionRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ItemDetails>> UpdateItemConversionFactor(long organizationId, long itemId, long conversionId, byte[] version, decimal factor, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ItemDetails>> SetItemConversionActive(long organizationId, long itemId, long conversionId, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ClassificationSetSummary>> ListClassificationSets(long organizationId, bool includeInactive, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ClassificationSetSummary>> CreateClassificationSet(CreateClassificationSetRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<ClassificationSummary>> ListClassifications(long organizationId, ClassificationListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ClassificationSummary>> ListActiveClassifications(long organizationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ClassificationSummary?> GetClassification(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ClassificationSummary>> CreateClassification(CreateClassificationRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ClassificationSummary>> UpdateClassification(long organizationId, long id, byte[] version, string? name, long? parentClassificationId, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ClassificationSummary>> SetClassificationActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<AttributeDefinitionSummary>> ListAttributeDefinitions(long organizationId, AttributeDefinitionListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<AttributeDefinitionSummary>> ListActiveAttributeDefinitions(long organizationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<AttributeDefinitionSummary?> GetAttributeDefinition(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<AttributeDefinitionSummary>> CreateAttributeDefinition(CreateAttributeDefinitionRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<AttributeDefinitionSummary>> UpdateAttributeDefinition(long organizationId, long id, byte[] version, string? name, long? unitId, bool isRequired, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<AttributeDefinitionSummary>> SetAttributeDefinitionActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ItemDetails>> SaveItemAttributeValues(long organizationId, long itemId, byte[] version, IReadOnlyList<ItemAttributeValueInput> values, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    [Fact]
    public void Item_requires_valid_code_name_base_unit_and_at_least_one_capability()
    {
        var item = Item.Create(1, "  hips-01 ", "  پلی‌استایرن مقاوم  ", "Graded polymer resin", 2, true, false, false, true);
        Assert.Equal("HIPS-01", item.Code);
        Assert.Equal("پلی‌استایرن مقاوم", item.Name);
        Assert.True(item.IsActive);

        Assert.Equal(ItemErrors.CapabilityRequired,
            Assert.Throws<ItemRuleException>(() => Item.Create(1, "X", "Name", null, 2, false, false, false, false)).Code);
        Assert.Equal(ItemErrors.CodeInvalid,
            Assert.Throws<ItemRuleException>(() => Item.Create(1, "bad code", "Name", null, 2, true, false, false, false)).Code);
        Assert.Equal(ItemErrors.BaseUnitNotFound,
            Assert.Throws<ItemRuleException>(() => Item.Create(1, "X", "Name", null, 0, true, false, false, false)).Code);
    }

    [Fact]
    public void Typed_attributes_validate_required_and_data_types()
    {
        var mfiDef = AttributeDefinition.Create(1, "MFI", "Melt Flow Index", AttributeDataType.Decimal, 5, isRequired: true);
        Assert.Equal(ItemErrors.AttributeRequired,
            Assert.Throws<ItemRuleException>(() => ItemAttributeValue.CreateValidated(mfiDef, 10, null, null, null)).Code);

        var val = ItemAttributeValue.CreateValidated(mfiDef, 10, null, 12.5m, null);
        Assert.Equal(12.5m, val.DecimalValue);

        var boolDef = AttributeDefinition.Create(1, "FOOD_GRADE", "Food Grade", AttributeDataType.Boolean, null, isRequired: true);
        Assert.Equal(ItemErrors.AttributeRequired,
            Assert.Throws<ItemRuleException>(() => ItemAttributeValue.CreateValidated(boolDef, 10, null, null, null)).Code);
        Assert.True(ItemAttributeValue.CreateValidated(boolDef, 10, null, null, true).BooleanValue);
    }

    [Fact]
    public async Task Item_use_cases_combine_item_and_global_conversions_for_A08_and_fail_closed()
    {
        var itemStore = new ItemStoreStub
        {
            CurrentItem = new ItemDetails(
                new ItemSummary(7, 1, "OIL-800ML", "Frying Oil", null, 10, "BTL", "Bottle", "btl", false, true, true, false, true, Convert.ToBase64String(new byte[8])),
                [], [], [])
        };
        // Item-specific: 1 BOX(11) = 12 BTL(10); 1 BTL(10) = 800 ML(12)
        itemStore.ItemEdges.Add(new UnitConversionEdge(11, 10, 12m, IsItemSpecific: true));
        itemStore.ItemEdges.Add(new UnitConversionEdge(10, 12, 800m, IsItemSpecific: true));
        // Global org edge: 1 L(13) = 1000 ML(12)
        var unitStore = new UnitEdgesStub([new UnitConversionEdge(13, 12, 1000m, IsItemSpecific: false)]);

        var actor = new TrustedActor(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var siteRead = new ItemUseCases(itemStore, unitStore, new ActorStub(actor),
            new PermissionStub((scope, perm) => scope is OrganizationScope.Site s && s.OrganizationId == 1 && perm == ItemPermissions.ItemsRead));

        // 2 BOX -> L = 2 * 12 * 800 / 1000 = 19.2 L
        var preview = await siteRead.PreviewItemConversion(new OrganizationScope.Site(1, 5), new ConvertItemQuantityRequest(1, 7, 11, 13, 2m));
        Assert.True(preview.Succeeded);
        Assert.Equal(19.2m, preview.Value!.ConvertedQuantity);
        Assert.Equal(3, preview.Value.Hops);

        // Site scope cannot manage items (Items.Manage is OrganizationOnly)
        var siteManageDenied = await siteRead.CreateItem(new OrganizationScope.Site(1, 5), new CreateItemRequest(1, "A", "B", null, 10, true, false, false, false));
        Assert.False(siteManageDenied.Succeeded);
        Assert.Equal(ItemErrors.AccessDenied, siteManageDenied.Error!.Code);
        Assert.False(itemStore.CreateCalled);
    }

    [Fact]
    public void Items_page_routes_and_built_in_admin_permissions_are_configured()
    {
        var routes = typeof(Items).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).OrderBy(x => x).ToArray();
        Assert.Equal(["/items", "/organizations/{OrganizationId:long}/items"], routes);

        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Organization(1), ItemPermissions.ItemsRead));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Organization(1), ItemPermissions.ItemsManage));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Site(1, 1), ItemPermissions.ItemsRead));
        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Site(1, 1), ItemPermissions.ItemsManage));
        Assert.Equal("ItemErrorCapabilityRequired", RolePermissionLocalization.FailureKey(ItemErrors.CapabilityRequired));
    }
}
