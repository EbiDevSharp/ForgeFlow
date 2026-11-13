using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Items.Slices;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;

namespace ForgeFlow.Application.Items;

/// <summary>Secured vertical-slice facade for organization-scoped Items, Classifications, Item Unit Conversions, and Attributes.</summary>
public sealed class ItemUseCases(
    IItemOperationsStore store,
    IUnitOperationsStore? unitsStore = null,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    private readonly ItemCatalogSlices catalogSlices = new(store, new ItemSliceSupport(actors, permissions, operationRecorder, opContext));
    private readonly ItemConversionSlices conversionSlices = new(store, unitsStore, new ItemSliceSupport(actors, permissions, operationRecorder, opContext));
    private readonly ItemClassificationSlices classificationSlices = new(store, new ItemSliceSupport(actors, permissions, operationRecorder, opContext));
    private readonly ItemAttributeSlices attributeSlices = new(store, new ItemSliceSupport(actors, permissions, operationRecorder, opContext));

    public Task<OrganizationResult<OrganizationPage<ItemSummary>>> ListItems(
        OrganizationScope? scope, long organizationId, ItemListQuery query, CancellationToken ct = default)
        => catalogSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<IReadOnlyList<ItemSummary>>> ListActiveItems(
        OrganizationScope? scope, long organizationId, CancellationToken ct = default)
        => catalogSlices.ListActiveAsync(scope, organizationId, ct);

    public Task<OrganizationResult<ItemDetails>> GetItem(
        OrganizationScope? scope, long organizationId, long id, CancellationToken ct = default)
        => catalogSlices.GetAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<ItemDetails>> CreateItem(
        OrganizationScope? scope, CreateItemRequest request, CancellationToken ct = default)
        => catalogSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<ItemDetails>> UpdateItem(
        OrganizationScope? scope, UpdateItemRequest request, CancellationToken ct = default)
        => catalogSlices.UpdateAsync(scope, request, ct);

    public Task<OrganizationResult<ItemDetails>> SetItemActive(
        OrganizationScope? scope, SetItemActiveRequest request, CancellationToken ct = default)
        => catalogSlices.SetActiveAsync(scope, request, ct);

    public Task<OrganizationResult<ItemDetails>> SaveItemClassifications(
        OrganizationScope? scope, SaveItemClassificationsRequest request, CancellationToken ct = default)
        => classificationSlices.SaveItemClassificationsAsync(scope, request, ct);

    public Task<OrganizationResult<ItemDetails>> CreateItemConversion(
        OrganizationScope? scope, CreateItemUnitConversionRequest request, CancellationToken ct = default)
        => conversionSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<ItemDetails>> UpdateItemConversionFactor(
        OrganizationScope? scope, UpdateItemUnitConversionFactorRequest request, CancellationToken ct = default)
        => conversionSlices.UpdateFactorAsync(scope, request, ct);

    public Task<OrganizationResult<ItemDetails>> SetItemConversionActive(
        OrganizationScope? scope, SetItemUnitConversionActiveRequest request, CancellationToken ct = default)
        => conversionSlices.SetActiveAsync(scope, request, ct);

    /// <summary>A08 hybrid conversion resolver combining item-specific edges and global organization edges.</summary>
    public Task<OrganizationResult<UnitConversionPreviewResult>> PreviewItemConversion(
        OrganizationScope? scope, ConvertItemQuantityRequest request, CancellationToken ct = default)
        => conversionSlices.PreviewAsync(scope, request, ct);

    public Task<OrganizationResult<IReadOnlyList<ClassificationSetSummary>>> ListClassificationSets(
        OrganizationScope? scope, long organizationId, bool includeInactive = false, CancellationToken ct = default)
        => classificationSlices.ListSetsAsync(scope, organizationId, includeInactive, ct);

    public Task<OrganizationResult<ClassificationSetSummary>> CreateClassificationSet(
        OrganizationScope? scope, CreateClassificationSetRequest request, CancellationToken ct = default)
        => classificationSlices.CreateSetAsync(scope, request, ct);

    public Task<OrganizationResult<OrganizationPage<ClassificationSummary>>> ListClassifications(
        OrganizationScope? scope, long organizationId, ClassificationListQuery query, CancellationToken ct = default)
        => classificationSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<IReadOnlyList<ClassificationSummary>>> ListActiveClassifications(
        OrganizationScope? scope, long organizationId, CancellationToken ct = default)
        => classificationSlices.ListActiveAsync(scope, organizationId, ct);

    public Task<OrganizationResult<ClassificationSummary>> GetClassification(
        OrganizationScope? scope, long organizationId, long id, CancellationToken ct = default)
        => classificationSlices.GetAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<ClassificationSummary>> CreateClassification(
        OrganizationScope? scope, CreateClassificationRequest request, CancellationToken ct = default)
        => classificationSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<ClassificationSummary>> UpdateClassification(
        OrganizationScope? scope, UpdateClassificationRequest request, CancellationToken ct = default)
        => classificationSlices.UpdateAsync(scope, request, ct);

    public Task<OrganizationResult<ClassificationSummary>> SetClassificationActive(
        OrganizationScope? scope, SetClassificationActiveRequest request, CancellationToken ct = default)
        => classificationSlices.SetActiveAsync(scope, request, ct);

    public Task<OrganizationResult<OrganizationPage<AttributeDefinitionSummary>>> ListAttributeDefinitions(
        OrganizationScope? scope, long organizationId, AttributeDefinitionListQuery query, CancellationToken ct = default)
        => attributeSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<IReadOnlyList<AttributeDefinitionSummary>>> ListActiveAttributeDefinitions(
        OrganizationScope? scope, long organizationId, CancellationToken ct = default)
        => attributeSlices.ListActiveAsync(scope, organizationId, ct);

    public Task<OrganizationResult<AttributeDefinitionSummary>> GetAttributeDefinition(
        OrganizationScope? scope, long organizationId, long id, CancellationToken ct = default)
        => attributeSlices.GetAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<AttributeDefinitionSummary>> CreateAttributeDefinition(
        OrganizationScope? scope, CreateAttributeDefinitionRequest request, CancellationToken ct = default)
        => attributeSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<AttributeDefinitionSummary>> UpdateAttributeDefinition(
        OrganizationScope? scope, UpdateAttributeDefinitionRequest request, CancellationToken ct = default)
        => attributeSlices.UpdateAsync(scope, request, ct);

    public Task<OrganizationResult<AttributeDefinitionSummary>> SetAttributeDefinitionActive(
        OrganizationScope? scope, SetAttributeDefinitionActiveRequest request, CancellationToken ct = default)
        => attributeSlices.SetActiveAsync(scope, request, ct);

    public Task<OrganizationResult<ItemDetails>> SaveItemAttributeValues(
        OrganizationScope? scope, SaveItemAttributeValuesRequest request, CancellationToken ct = default)
        => attributeSlices.SaveItemAttributeValuesAsync(scope, request, ct);
}
