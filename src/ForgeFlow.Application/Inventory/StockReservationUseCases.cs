using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory.Slices;
using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Application.Inventory;

/// <summary>
/// Secured vertical-slice facade for Stock Reservations.
/// </summary>
public sealed class StockReservationUseCases(
    IStockReservationStore store,
    IInventoryOperationsStore inventoryStore,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private readonly StockReservationSlices _slices = new(
        store,
        inventoryStore,
        new InventorySliceSupport(actors, permissions, operationRecorder, opContext),
        clock);

    public Task<OrganizationResult<OrganizationPage<StockReservationSummary>>> ListReservations(
        OrganizationScope? scope,
        long organizationId,
        ReservationListQuery query,
        CancellationToken ct = default)
        => _slices.ListReservationsAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ListOrderReservations(
        OrganizationScope? scope,
        long organizationId,
        long productionOrderId,
        CancellationToken ct = default)
        => _slices.ListOrderReservationsAsync(scope, organizationId, productionOrderId, ct);

    public Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ListSalesOrderReservations(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        CancellationToken ct = default)
        => _slices.ListSalesOrderReservationsAsync(scope, organizationId, salesOrderId, ct);

    public Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveStock(
        OrganizationScope? scope,
        ReserveStockRequest request,
        CancellationToken ct = default)
        => _slices.ReserveStockAsync(scope, request, ct);

    public Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveSalesOrderStock(
        OrganizationScope? scope,
        ReserveSalesOrderStockRequest request,
        CancellationToken ct = default)
        => _slices.ReserveSalesOrderStockAsync(scope, request, ct);

    public Task<OrganizationResult<StockReservationSummary>> ReleaseReservation(
        OrganizationScope? scope,
        ReleaseReservationRequest request,
        CancellationToken ct = default)
        => _slices.ReleaseReservationAsync(scope, request, ct);
}
