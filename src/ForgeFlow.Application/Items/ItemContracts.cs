using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Items;

public static class ItemPermissions
{
    public const string ItemsRead = "Items.Read";
    public const string ItemsManage = "Items.Manage";
}

public sealed record ItemSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    string? Description,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitName,
    string BaseUnitSymbol,
    bool CanBePurchased,
    bool CanBeManufactured,
    bool CanBeSold,
    bool CanBeConsumed,
    bool IsActive,
    string RowVersion,
    bool RequiresLotTracking = false,
    QualityControlMode QualityControlMode = QualityControlMode.None);

public sealed record ItemClassificationSummary(
    long ClassificationId,
    long ClassificationSetId,
    string ClassificationSetCode,
    string ClassificationSetName,
    string Code,
    string Name);

public sealed record ItemUnitConversionSummary(
    long Id,
    long OrganizationId,
    long ItemId,
    long FromUnitId,
    string FromUnitCode,
    string FromUnitName,
    string FromUnitSymbol,
    long ToUnitId,
    string ToUnitCode,
    string ToUnitName,
    string ToUnitSymbol,
    decimal Factor,
    bool IsActive,
    string RowVersion);

public sealed record ItemAttributeValueSummary(
    long AttributeDefinitionId,
    string Code,
    string Name,
    AttributeDataType DataType,
    long? UnitId,
    string? UnitSymbol,
    bool IsRequired,
    string? TextValue,
    decimal? DecimalValue,
    bool? BooleanValue);

public sealed record ItemDetails(
    ItemSummary Summary,
    IReadOnlyList<ItemClassificationSummary> Classifications,
    IReadOnlyList<ItemUnitConversionSummary> Conversions,
    IReadOnlyList<ItemAttributeValueSummary> Attributes);

public sealed record ItemListQuery(
    string? Search = null,
    long? ClassificationId = null,
    bool? CanBePurchased = null,
    bool? CanBeManufactured = null,
    bool? CanBeSold = null,
    bool? CanBeConsumed = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateItemRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    string? Description,
    long BaseUnitId,
    bool CanBePurchased,
    bool CanBeManufactured,
    bool CanBeSold,
    bool CanBeConsumed,
    bool RequiresLotTracking = false,
    QualityControlMode QualityControlMode = QualityControlMode.None);

public sealed record UpdateItemRequest(
    long OrganizationId,
    long Id,
    string? Name,
    string? Description,
    long BaseUnitId,
    bool CanBePurchased,
    bool CanBeManufactured,
    bool CanBeSold,
    bool CanBeConsumed,
    string? ExpectedRowVersion,
    bool RequiresLotTracking = false,
    QualityControlMode QualityControlMode = QualityControlMode.None);

public sealed record SetItemActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record SaveItemClassificationsRequest(
    long OrganizationId,
    long ItemId,
    IReadOnlyList<long> ClassificationIds,
    string? ExpectedRowVersion);

public sealed record CreateItemUnitConversionRequest(
    long OrganizationId,
    long ItemId,
    long FromUnitId,
    long ToUnitId,
    decimal Factor);

public sealed record UpdateItemUnitConversionFactorRequest(
    long OrganizationId,
    long ItemId,
    long ConversionId,
    decimal Factor,
    string? ExpectedRowVersion);

public sealed record SetItemUnitConversionActiveRequest(
    long OrganizationId,
    long ItemId,
    long ConversionId,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record ConvertItemQuantityRequest(
    long OrganizationId,
    long ItemId,
    long FromUnitId,
    long ToUnitId,
    decimal Quantity);

public sealed record ClassificationSetSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    bool IsMandatory,
    bool AllowMultiple,
    bool IsActive,
    string RowVersion);

public sealed record ClassificationSummary(
    long Id,
    long OrganizationId,
    long ClassificationSetId,
    string ClassificationSetCode,
    string ClassificationSetName,
    long? ParentClassificationId,
    string? ParentClassificationCode,
    string? ParentClassificationName,
    string Code,
    string Name,
    bool IsActive,
    string RowVersion);

public sealed record ClassificationListQuery(
    long? ClassificationSetId = null,
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateClassificationSetRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    bool IsMandatory,
    bool AllowMultiple);

public sealed record CreateClassificationRequest(
    long OrganizationId,
    long ClassificationSetId,
    long? ParentClassificationId,
    string? Code,
    string? Name);

public sealed record UpdateClassificationRequest(
    long OrganizationId,
    long Id,
    string? Name,
    long? ParentClassificationId,
    string? ExpectedRowVersion);

public sealed record SetClassificationActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record AttributeDefinitionSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    AttributeDataType DataType,
    long? UnitId,
    string? UnitCode,
    string? UnitSymbol,
    bool IsRequired,
    bool IsActive,
    string RowVersion);

public sealed record AttributeDefinitionListQuery(
    string? Search = null,
    AttributeDataType? DataType = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateAttributeDefinitionRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    AttributeDataType DataType,
    long? UnitId,
    bool IsRequired);

public sealed record UpdateAttributeDefinitionRequest(
    long OrganizationId,
    long Id,
    string? Name,
    long? UnitId,
    bool IsRequired,
    string? ExpectedRowVersion);

public sealed record SetAttributeDefinitionActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record ItemAttributeValueInput(
    long AttributeDefinitionId,
    string? TextValue,
    decimal? DecimalValue,
    bool? BooleanValue);

public sealed record SaveItemAttributeValuesRequest(
    long OrganizationId,
    long ItemId,
    IReadOnlyList<ItemAttributeValueInput> Values,
    string? ExpectedRowVersion);

public sealed class ItemStoreUnavailableException(Exception inner)
    : Exception(ItemErrors.PersistenceUnavailable, inner);

public interface IItemOperationsStore
{
    Task<OrganizationPage<ItemSummary>> ListItems(long organizationId, ItemListQuery query, CancellationToken ct);
    Task<IReadOnlyList<ItemSummary>> ListActiveItems(long organizationId, CancellationToken ct);
    Task<ItemDetails?> GetItem(long organizationId, long id, CancellationToken ct);
    Task<OrganizationResult<ItemDetails>> CreateItem(CreateItemRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ItemDetails>> UpdateItem(long organizationId, long id, byte[] version, UpdateItemRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ItemDetails>> SetItemActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ItemDetails>> SaveItemClassifications(long organizationId, long itemId, byte[] version, IReadOnlyList<long> classificationIds, OrganizationWriteAccess access, CancellationToken ct);

    Task<IReadOnlyList<UnitConversionEdge>> ListItemActiveConversionEdges(long organizationId, long itemId, CancellationToken ct);
    Task<OrganizationResult<ItemDetails>> CreateItemConversion(CreateItemUnitConversionRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ItemDetails>> UpdateItemConversionFactor(long organizationId, long itemId, long conversionId, byte[] version, decimal factor, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ItemDetails>> SetItemConversionActive(long organizationId, long itemId, long conversionId, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);

    Task<IReadOnlyList<ClassificationSetSummary>> ListClassificationSets(long organizationId, bool includeInactive, CancellationToken ct);
    Task<OrganizationResult<ClassificationSetSummary>> CreateClassificationSet(CreateClassificationSetRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationPage<ClassificationSummary>> ListClassifications(long organizationId, ClassificationListQuery query, CancellationToken ct);
    Task<IReadOnlyList<ClassificationSummary>> ListActiveClassifications(long organizationId, CancellationToken ct);
    Task<ClassificationSummary?> GetClassification(long organizationId, long id, CancellationToken ct);
    Task<OrganizationResult<ClassificationSummary>> CreateClassification(CreateClassificationRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ClassificationSummary>> UpdateClassification(long organizationId, long id, byte[] version, string? name, long? parentClassificationId, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ClassificationSummary>> SetClassificationActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationPage<AttributeDefinitionSummary>> ListAttributeDefinitions(long organizationId, AttributeDefinitionListQuery query, CancellationToken ct);
    Task<IReadOnlyList<AttributeDefinitionSummary>> ListActiveAttributeDefinitions(long organizationId, CancellationToken ct);
    Task<AttributeDefinitionSummary?> GetAttributeDefinition(long organizationId, long id, CancellationToken ct);
    Task<OrganizationResult<AttributeDefinitionSummary>> CreateAttributeDefinition(CreateAttributeDefinitionRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<AttributeDefinitionSummary>> UpdateAttributeDefinition(long organizationId, long id, byte[] version, string? name, long? unitId, bool isRequired, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<AttributeDefinitionSummary>> SetAttributeDefinitionActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ItemDetails>> SaveItemAttributeValues(long organizationId, long itemId, byte[] version, IReadOnlyList<ItemAttributeValueInput> values, OrganizationWriteAccess access, CancellationToken ct);
}
