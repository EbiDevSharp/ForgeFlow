using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Inventory;

public static class InventoryPermissions
{
    public const string InventoryRead = "Inventory.Read";
    public const string InventoryManage = "Inventory.Manage";
}

public sealed record WarehouseSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    string Code,
    string Name,
    bool IsDefaultForSite,
    bool IsActive,
    string RowVersion);

public sealed record WarehouseListQuery(
    long? SiteId = null,
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateWarehouseRequest(
    long OrganizationId,
    long SiteId,
    string? Code,
    string? Name,
    bool IsDefaultForSite);

public sealed record UpdateWarehouseRequest(
    long OrganizationId,
    long Id,
    string? Name,
    bool IsDefaultForSite,
    string? ExpectedRowVersion);

public sealed record SetWarehouseActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record InventoryBalanceSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    long WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    long ItemId,
    string ItemCode,
    string ItemName,
    string LotCode,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    decimal OnHandBaseQuantity,
    DateTime LastMovedAtUtc,
    string RowVersion,
    decimal BlockedBaseQuantity = 0m,
    decimal AvailableBaseQuantity = 0m,
    DateOnly? ManufactureDate = null,
    DateOnly? ExpirationDate = null,
    bool IsExpired = false,
    bool IsExpiringSoon = false);

public sealed record InventoryBalanceListQuery(
    long? SiteId = null,
    long? WarehouseId = null,
    long? ItemId = null,
    string? Search = null,
    bool IncludeZeroBalances = false,
    int Page = 1,
    int PageSize = 25);

public sealed record InventoryLedgerEntrySummary(
    long Id,
    int LineNumber,
    long SiteId,
    long WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    long ItemId,
    string ItemCode,
    string ItemName,
    string LotCode,
    decimal EnteredQuantity,
    long EnteredUnitId,
    string EnteredUnitSymbol,
    decimal BaseQuantityDelta,
    long BaseUnitId,
    string BaseUnitSymbol,
    DateTime OccurredAtUtc,
    string? Notes);

public sealed record InventoryTransactionSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    long WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    long? DestinationWarehouseId,
    string? DestinationWarehouseCode,
    string DocumentNumber,
    InventoryTransactionType TransactionType,
    long? ProductionOrderId,
    string? ProductionOrderNumber,
    long? ReversedTransactionId,
    bool IsReversed,
    string? ReferenceNote,
    DateTime PostedAtUtc,
    int LineCount,
    long? QualityInspectionId = null,
    string? QualityInspectionNumber = null,
    long? PurchaseOrderId = null,
    string? PurchaseOrderNumber = null);

public sealed record InventoryTransactionDetails(
    InventoryTransactionSummary Summary,
    IReadOnlyList<InventoryLedgerEntrySummary> Entries);

public sealed record InventoryTransactionListQuery(
    long? SiteId = null,
    long? WarehouseId = null,
    InventoryTransactionType? TransactionType = null,
    long? ProductionOrderId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record InventoryLineInput(
    long ItemId,
    string? LotCode,
    decimal Quantity,
    long? UnitId = null,
    string? Notes = null,
    DateOnly? ManufactureDate = null,
    DateOnly? ExpirationDate = null);

public sealed record PostInventoryTransactionRequest(
    long OrganizationId,
    long WarehouseId,
    long? DestinationWarehouseId,
    string? DocumentNumber,
    InventoryTransactionType TransactionType,
    long? ProductionOrderId,
    string? ReferenceNote,
    string? IdempotencyKey,
    IReadOnlyList<InventoryLineInput> Lines);

public sealed record ReverseInventoryTransactionRequest(
    long OrganizationId,
    long TransactionId,
    string? ReversalDocumentNumber,
    string? ReferenceNote = null,
    string? IdempotencyKey = null);

public sealed record InventoryWarehouseLookup(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    bool IsActive,
    bool SiteIsActive,
    bool OrganizationIsActive);

public sealed record InventoryItemConversionContext(
    long ItemId,
    bool ItemIsActive,
    long BaseUnitId,
    IReadOnlyList<UnitConversionEdge> Edges,
    bool RequiresLotTracking = false,
    QualityControlMode QualityControlMode = QualityControlMode.None);

public sealed record InventoryProductionOrderLookup(
    long Id,
    long OrganizationId,
    long SiteId,
    ProductionOrderStatus Status,
    string OrderNumber);

public sealed record ResolvedInventoryLineWrite(
    long SiteId,
    long WarehouseId,
    long ItemId,
    string LotCode,
    decimal EnteredQuantity,
    long EnteredUnitId,
    decimal BaseQuantityDelta,
    long BaseUnitId,
    string? Notes,
    bool TriggersQualityControl = false,
    DateOnly? ManufactureDate = null,
    DateOnly? ExpirationDate = null);

public sealed class InventoryStoreUnavailableException(Exception inner)
    : Exception(InventoryErrors.PersistenceUnavailable, inner);

public interface IInventoryOperationsStore
{
    Task<OrganizationPage<WarehouseSummary>> ListWarehouses(long organizationId, WarehouseListQuery query, CancellationToken ct);
    Task<WarehouseSummary?> GetWarehouse(long organizationId, long id, CancellationToken ct);
    Task<OrganizationResult<WarehouseSummary>> CreateWarehouse(CreateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<WarehouseSummary>> UpdateWarehouse(long organizationId, long id, byte[] version, UpdateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<WarehouseSummary>> SetWarehouseActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationPage<InventoryBalanceSummary>> ListBalances(long organizationId, InventoryBalanceListQuery query, CancellationToken ct);
    Task<OrganizationPage<InventoryTransactionSummary>> ListTransactions(long organizationId, InventoryTransactionListQuery query, CancellationToken ct);
    Task<InventoryTransactionDetails?> GetTransaction(long organizationId, long id, CancellationToken ct);

    Task<InventoryWarehouseLookup?> GetWarehouseLookup(long organizationId, long warehouseId, CancellationToken ct);
    Task<IReadOnlyDictionary<long, InventoryItemConversionContext>> LoadItemUnitContexts(long organizationId, IReadOnlyCollection<long> itemIds, CancellationToken ct);
    Task<IReadOnlyHashSetLike> LoadActiveUnitIds(long organizationId, IReadOnlyCollection<long> unitIds, CancellationToken ct);
    Task<InventoryProductionOrderLookup?> GetProductionOrderLookup(long organizationId, long productionOrderId, CancellationToken ct);

    Task<OrganizationResult<InventoryTransactionDetails>> PostTransaction(
        PostInventoryTransactionRequest request,
        long siteId,
        IReadOnlyList<ResolvedInventoryLineWrite> resolvedLines,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<InventoryTransactionDetails>> ReverseTransaction(
        long organizationId,
        long transactionId,
        string reversalDocumentNumber,
        string? referenceNote,
        string? idempotencyKey,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);
}

public sealed class IReadOnlyHashSetLike(IEnumerable<long> values)
{
    private readonly HashSet<long> set = [.. values];
    public bool Contains(long id) => set.Contains(id);
}
