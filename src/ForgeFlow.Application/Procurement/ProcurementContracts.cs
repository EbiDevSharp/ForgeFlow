using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Procurement;

public static class ProcurementPermissions
{
    public const string ProcurementRead = "Procurement.Read";
    public const string ManageSuppliers = "Procurement.ManageSuppliers";
    public const string ManageOrders = "Procurement.ManageOrders";
    public const string ReceiveOrders = "Procurement.ReceiveOrders";
}

public sealed record SupplierSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes,
    bool IsActive,
    string RowVersion);

public sealed record SupplierListQuery(
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateSupplierRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    string? ContactName = null,
    string? Phone = null,
    string? Email = null,
    string? Address = null,
    string? Notes = null);

public sealed record UpdateSupplierRequest(
    string? Name,
    string? ContactName = null,
    string? Phone = null,
    string? Email = null,
    string? Address = null,
    string? Notes = null);

public sealed record PurchaseOrderLineDetails(
    long Id,
    int LineNumber,
    long ItemId,
    string ItemCode,
    string ItemName,
    bool RequiresLotTracking,
    QualityControlMode QualityControlMode,
    decimal OrderedQuantity,
    long OrderedUnitId,
    string OrderedUnitCode,
    string OrderedUnitSymbol,
    long BaseUnitIdSnapshot,
    string BaseUnitCodeSnapshot,
    string BaseUnitSymbolSnapshot,
    decimal OrderedBaseQuantity,
    decimal ReceivedBaseQuantity,
    decimal RemainingBaseQuantity,
    decimal? UnitPrice,
    string? Notes);

public sealed record PurchaseOrderSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    long SupplierId,
    string SupplierCode,
    string SupplierName,
    string OrderNumber,
    PurchaseOrderStatus Status,
    DateOnly OrderDate,
    DateOnly? ExpectedDeliveryDate,
    long? TargetWarehouseId,
    string? TargetWarehouseCode,
    string? TargetWarehouseName,
    string CurrencyCode,
    decimal TotalOrderedBaseQuantity,
    decimal TotalReceivedBaseQuantity,
    decimal? TotalEstimatedAmount,
    int LineCount,
    string? Notes,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? CancelledAtUtc,
    string RowVersion);

public sealed record PurchaseOrderReceiptSummary(
    long TransactionId,
    string DocumentNumber,
    long WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    bool IsReversed,
    DateTime PostedAtUtc,
    string? ReferenceNote);

public sealed record PurchaseOrderDetails(
    PurchaseOrderSummary Summary,
    IReadOnlyList<PurchaseOrderLineDetails> Lines,
    IReadOnlyList<PurchaseOrderReceiptSummary> Receipts);

public sealed record PurchaseOrderListQuery(
    long? SiteId = null,
    long? SupplierId = null,
    PurchaseOrderStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record PurchaseOrderLineInput(
    long ItemId,
    decimal OrderedQuantity,
    long? OrderedUnitId = null,
    decimal? UnitPrice = null,
    string? Notes = null);

public sealed record CreatePurchaseOrderRequest(
    long OrganizationId,
    long SiteId,
    long SupplierId,
    string? OrderNumber,
    DateOnly OrderDate,
    DateOnly? ExpectedDeliveryDate,
    long? TargetWarehouseId,
    string? CurrencyCode,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineInput> Lines);

public sealed record UpdatePurchaseOrderRequest(
    long SupplierId,
    DateOnly OrderDate,
    DateOnly? ExpectedDeliveryDate,
    long? TargetWarehouseId,
    string? CurrencyCode,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineInput> Lines);

public sealed record ReceivePurchaseOrderLineInput(
    long PurchaseOrderLineId,
    decimal Quantity,
    long? UnitId = null,
    string? LotCode = null,
    DateOnly? ManufactureDate = null,
    DateOnly? ExpirationDate = null,
    string? Notes = null);

public sealed record ReceivePurchaseOrderRequest(
    long OrganizationId,
    long PurchaseOrderId,
    long WarehouseId,
    string? ReceiptDocumentNumber,
    string? ReferenceNote,
    string? IdempotencyKey,
    string? ExpectedRowVersion,
    IReadOnlyList<ReceivePurchaseOrderLineInput> Lines);

public sealed record PurchaseOrderReceiptResult(
    PurchaseOrderDetails Order,
    long InventoryTransactionId,
    string DocumentNumber);

public sealed record ProcurementItemLookup(
    long ItemId,
    bool ItemIsActive,
    bool CanBePurchased,
    long BaseUnitId,
    bool RequiresLotTracking,
    QualityControlMode QualityControlMode,
    IReadOnlyList<UnitConversionEdge> Edges);

public sealed record ResolvedPurchaseOrderLineWrite(
    long ItemId,
    decimal OrderedQuantity,
    long OrderedUnitId,
    long BaseUnitIdSnapshot,
    decimal OrderedBaseQuantity,
    decimal? UnitPrice,
    string? Notes);

public sealed record ResolvedPurchaseOrderReceiptLine(
    long PurchaseOrderLineId,
    long ItemId,
    string LotCode,
    decimal EnteredQuantity,
    long EnteredUnitId,
    decimal BaseQuantityDelta,
    long BaseUnitId,
    bool TriggersQualityControl,
    DateOnly? ManufactureDate,
    DateOnly? ExpirationDate,
    string? Notes);

public sealed record PurchaseOrderSiteLookup(
    long SiteId,
    long OrganizationId,
    bool SiteIsActive,
    bool OrganizationIsActive);

public sealed record PurchaseOrderWarehouseLookup(
    long WarehouseId,
    long OrganizationId,
    long SiteId,
    bool WarehouseIsActive,
    bool SiteIsActive,
    bool OrganizationIsActive);

public sealed class ProcurementStoreUnavailableException(Exception inner)
    : Exception(ProcurementErrors.PersistenceUnavailable, inner);

public interface IProcurementOperationsStore
{
    Task<OrganizationPage<SupplierSummary>> ListSuppliers(long organizationId, SupplierListQuery query, CancellationToken ct);
    Task<SupplierSummary?> GetSupplier(long organizationId, long id, CancellationToken ct);
    Task<OrganizationResult<SupplierSummary>> CreateSupplier(CreateSupplierRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SupplierSummary>> UpdateSupplier(long organizationId, long id, byte[] version, UpdateSupplierRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SupplierSummary>> SetSupplierActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationPage<PurchaseOrderSummary>> ListPurchaseOrders(long organizationId, long? forcedSiteId, PurchaseOrderListQuery query, CancellationToken ct);
    Task<PurchaseOrderDetails?> GetPurchaseOrder(long organizationId, long id, CancellationToken ct);

    Task<PurchaseOrderSiteLookup?> GetSiteLookup(long organizationId, long siteId, CancellationToken ct);
    Task<PurchaseOrderWarehouseLookup?> GetWarehouseLookup(long organizationId, long warehouseId, CancellationToken ct);
    Task<IReadOnlyDictionary<long, ProcurementItemLookup>> LoadItemContexts(long organizationId, IReadOnlyCollection<long> itemIds, CancellationToken ct);
    Task<IReadOnlySet<long>> LoadActiveUnitIds(long organizationId, IReadOnlyCollection<long> unitIds, CancellationToken ct);

    Task<OrganizationResult<PurchaseOrderDetails>> CreateDraft(
        CreatePurchaseOrderRequest request,
        IReadOnlyList<ResolvedPurchaseOrderLineWrite> resolvedLines,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<PurchaseOrderDetails>> UpdateDraft(
        long organizationId,
        long id,
        byte[] version,
        UpdatePurchaseOrderRequest request,
        IReadOnlyList<ResolvedPurchaseOrderLineWrite> resolvedLines,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<PurchaseOrderDetails>> ApproveOrder(long organizationId, long id, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<PurchaseOrderDetails>> CloseOrder(long organizationId, long id, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<PurchaseOrderDetails>> CancelOrder(long organizationId, long id, byte[] version, string? reason, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationResult<PurchaseOrderReceiptResult>> ReceiveOrder(
        ReceivePurchaseOrderRequest request,
        long siteId,
        byte[] version,
        string documentNumber,
        IReadOnlyList<ResolvedPurchaseOrderReceiptLine> resolvedLines,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);
}
