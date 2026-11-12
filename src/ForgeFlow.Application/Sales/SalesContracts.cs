using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Sales;

public static class SalesPermissions
{
    public const string SalesRead = "Sales.Read";
    public const string ManageCustomers = "Sales.ManageCustomers";
    public const string ManageOrders = "Sales.ManageOrders";
    public const string Dispatch = "Sales.Dispatch";
}

public sealed record CustomerSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? ShippingAddress,
    string? TaxCode,
    string? Notes,
    bool IsActive,
    string RowVersion);

public sealed record CustomerListQuery(
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateCustomerRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    string? ContactName = null,
    string? Phone = null,
    string? Email = null,
    string? ShippingAddress = null,
    string? TaxCode = null,
    string? Notes = null);

public sealed record UpdateCustomerRequest(
    string? Name,
    string? ContactName = null,
    string? Phone = null,
    string? Email = null,
    string? ShippingAddress = null,
    string? TaxCode = null,
    string? Notes = null);

public sealed record SalesOrderLineDetails(
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
    decimal DispatchedBaseQuantity,
    decimal RemainingBaseQuantity,
    decimal? UnitPrice,
    string? Notes);

public sealed record SalesOrderSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    long CustomerId,
    string CustomerCode,
    string CustomerName,
    string OrderNumber,
    SalesOrderStatus Status,
    DateOnly OrderDate,
    DateOnly? RequestedDeliveryDate,
    long? SourceWarehouseId,
    string? SourceWarehouseCode,
    string? SourceWarehouseName,
    string CurrencyCode,
    string? ShippingAddressSnapshot,
    decimal TotalOrderedBaseQuantity,
    decimal TotalDispatchedBaseQuantity,
    decimal? TotalEstimatedAmount,
    int LineCount,
    string? Notes,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? CancelledAtUtc,
    string RowVersion);

public sealed record DeliveryOrderSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    long SalesOrderId,
    string SalesOrderNumber,
    long CustomerId,
    string CustomerCode,
    string CustomerName,
    long WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    string DispatchNumber,
    DeliveryOrderStatus Status,
    DateOnly DispatchDate,
    string? CarrierName,
    string? TrackingNumber,
    decimal TotalDispatchedBaseQuantity,
    int LineCount,
    string? Notes,
    DateTime PostedAtUtc,
    DateTime? ReversedAtUtc,
    string? ReversalReason,
    string RowVersion);

public sealed record DeliveryOrderLineDetails(
    long Id,
    int LineNumber,
    long SalesOrderLineId,
    long ItemId,
    string ItemCode,
    string ItemName,
    string LotCode,
    decimal DispatchedBaseQuantity,
    long BaseUnitIdSnapshot,
    string BaseUnitCodeSnapshot,
    string BaseUnitSymbolSnapshot,
    long? QualityInspectionId,
    string? CertificateNumberSnapshot,
    string? Notes);

public sealed record DeliveryOrderDetails(
    DeliveryOrderSummary Summary,
    IReadOnlyList<DeliveryOrderLineDetails> Lines);

public sealed record SalesOrderDetails(
    SalesOrderSummary Summary,
    IReadOnlyList<SalesOrderLineDetails> Lines,
    IReadOnlyList<DeliveryOrderSummary> Dispatches);

public sealed record SalesOrderListQuery(
    long? SiteId = null,
    long? CustomerId = null,
    SalesOrderStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record DeliveryOrderListQuery(
    long? SiteId = null,
    long? SalesOrderId = null,
    long? WarehouseId = null,
    DeliveryOrderStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record SalesOrderLineRequest(
    long ItemId,
    decimal OrderedQuantity,
    long OrderedUnitId,
    decimal? UnitPrice = null,
    string? Notes = null);

public sealed record CreateSalesOrderRequest(
    long OrganizationId,
    long SiteId,
    long CustomerId,
    string? OrderNumber,
    DateOnly OrderDate,
    DateOnly? RequestedDeliveryDate = null,
    long? SourceWarehouseId = null,
    string? CurrencyCode = "IRR",
    string? ShippingAddressSnapshot = null,
    string? Notes = null,
    IReadOnlyList<SalesOrderLineRequest>? Lines = null);

public sealed record UpdateSalesOrderRequest(
    long CustomerId,
    DateOnly OrderDate,
    DateOnly? RequestedDeliveryDate = null,
    long? SourceWarehouseId = null,
    string? CurrencyCode = "IRR",
    string? ShippingAddressSnapshot = null,
    string? Notes = null,
    IReadOnlyList<SalesOrderLineRequest>? Lines = null);

public sealed record CancelSalesOrderRequest(
    string? Reason);

public sealed record PostDeliveryOrderLineRequest(
    long SalesOrderLineId,
    string? LotCode,
    decimal DispatchedQuantity,
    long? UnitId = null,
    string? Notes = null);

public sealed record PostDeliveryOrderRequest(
    long WarehouseId,
    string? DispatchNumber,
    DateOnly DispatchDate,
    string? CarrierName = null,
    string? TrackingNumber = null,
    string? Notes = null,
    IReadOnlyList<PostDeliveryOrderLineRequest>? Lines = null);

public sealed record ReverseDeliveryOrderRequest(
    string? Reason);

public sealed record DispatchableLotSummary(
    long WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    long ItemId,
    string LotCode,
    decimal OnHandBaseQuantity,
    decimal BlockedBaseQuantity,
    decimal ReservedBaseQuantity,
    decimal OrderReservedBaseQuantity,
    decimal AvailableForOrderBaseQuantity,
    DateOnly? ExpirationDate,
    bool IsExpired,
    long? LatestQualityInspectionId,
    string? LatestCertificateNumber);

public sealed record ResolvedSalesOrderLine(
    long ItemId,
    decimal OrderedQuantity,
    long OrderedUnitId,
    long BaseUnitIdSnapshot,
    decimal OrderedBaseQuantity,
    decimal? UnitPrice,
    string? Notes);

public sealed record ResolvedDeliveryOrderLine(
    long SalesOrderLineId,
    long ItemId,
    string LotCode,
    decimal DispatchedBaseQuantity,
    long BaseUnitIdSnapshot,
    string? Notes);

public sealed record SalesItemContext(
    long ItemId,
    bool ItemIsActive,
    bool CanBeSold,
    long BaseUnitId,
    IReadOnlyList<UnitConversionEdge> Edges);

public interface ISalesOperationsStore
{
    Task<IReadOnlyDictionary<long, SalesItemContext>> LoadItemContexts(long organizationId, IReadOnlyList<long> itemIds, CancellationToken ct);
    Task<IReadOnlySet<long>> LoadActiveUnitIds(long organizationId, IReadOnlyList<long> unitIds, CancellationToken ct);

    Task<OrganizationPage<CustomerSummary>> ListCustomers(long organizationId, CustomerListQuery query, CancellationToken ct);
    Task<OrganizationResult<CustomerSummary>> CreateCustomer(CreateCustomerRequest request, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<CustomerSummary>> UpdateCustomer(long organizationId, long customerId, UpdateCustomerRequest request, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<CustomerSummary>> SetCustomerActive(long organizationId, long customerId, bool isActive, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationPage<SalesOrderSummary>> ListSalesOrders(long organizationId, SalesOrderListQuery query, CancellationToken ct);
    Task<SalesOrderDetails?> GetSalesOrder(long organizationId, long salesOrderId, CancellationToken ct);
    Task<OrganizationResult<SalesOrderDetails>> CreateSalesOrder(CreateSalesOrderRequest request, IReadOnlyList<ResolvedSalesOrderLine> resolvedLines, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SalesOrderDetails>> UpdateSalesOrder(long organizationId, long salesOrderId, UpdateSalesOrderRequest request, IReadOnlyList<ResolvedSalesOrderLine> resolvedLines, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SalesOrderDetails>> ApproveSalesOrder(long organizationId, long salesOrderId, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SalesOrderDetails>> CloseSalesOrder(long organizationId, long salesOrderId, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SalesOrderDetails>> CancelSalesOrder(long organizationId, long salesOrderId, string reason, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationPage<DeliveryOrderSummary>> ListDeliveryOrders(long organizationId, DeliveryOrderListQuery query, CancellationToken ct);
    Task<DeliveryOrderDetails?> GetDeliveryOrder(long organizationId, long deliveryOrderId, CancellationToken ct);
    Task<IReadOnlyList<DispatchableLotSummary>> ListDispatchableLots(long organizationId, long salesOrderId, long warehouseId, DateOnly asOfDate, CancellationToken ct);
    Task<OrganizationResult<DeliveryOrderDetails>> PostDeliveryOrder(long organizationId, long salesOrderId, PostDeliveryOrderRequest request, IReadOnlyList<ResolvedDeliveryOrderLine> resolvedLines, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<DeliveryOrderDetails>> ReverseDeliveryOrder(long organizationId, long deliveryOrderId, string reason, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
}

public sealed class SalesStoreUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
