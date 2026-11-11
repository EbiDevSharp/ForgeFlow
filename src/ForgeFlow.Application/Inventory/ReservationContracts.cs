using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;

namespace ForgeFlow.Application.Inventory;

public sealed record StockReservationSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    long WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    long ProductionOrderId,
    string OrderNumber,
    long? ProductionOrderInputId,
    long ItemId,
    string ItemCode,
    string ItemName,
    string LotCode,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    decimal ReservedBaseQuantity,
    decimal ConsumedBaseQuantity,
    decimal RemainingReservedBaseQuantity,
    ReservationStatus Status,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime? ReleasedAtUtc,
    string RowVersion);

public sealed record ReservationListQuery(
    long? SiteId = null,
    long? ProductionOrderId = null,
    long? WarehouseId = null,
    long? ItemId = null,
    ReservationStatus? Status = null,
    bool OpenOnly = true,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record ReserveStockRequest(
    long OrganizationId,
    long ProductionOrderId,
    long? ProductionOrderInputId,
    long ItemId,
    decimal Quantity,
    long? UnitId = null,
    long? WarehouseId = null,
    string? LotCode = null,
    string? Notes = null);

public sealed record ReleaseReservationRequest(
    long OrganizationId,
    long ReservationId,
    string? ExpectedRowVersion);

public interface IStockReservationStore
{
    Task<OrganizationPage<StockReservationSummary>> ListReservations(long organizationId, ReservationListQuery query, CancellationToken ct);
    Task<IReadOnlyList<StockReservationSummary>> ListOrderReservations(long organizationId, long productionOrderId, CancellationToken ct);
    Task<StockReservationSummary?> GetReservation(long organizationId, long reservationId, CancellationToken ct);

    Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveStock(
        ReserveStockRequest request,
        decimal requestedBaseQuantity,
        long baseUnitId,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<StockReservationSummary>> ReleaseReservation(
        long organizationId,
        long reservationId,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);
}
