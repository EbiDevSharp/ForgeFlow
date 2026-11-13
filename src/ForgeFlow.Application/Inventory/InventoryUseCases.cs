using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory.Slices;
using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Application.Inventory;

/// <summary>
/// Facade for Site-scoped Warehouses, append-only Stock Ledger, serialized balances,
/// and Production Order material issues/receipts.
/// </summary>
public sealed class InventoryUseCases(
    IInventoryOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    private readonly InventorySliceSupport support = new(actors, permissions, operationRecorder, opContext);
    private readonly WarehouseSlices warehouseSlices = new(
        store,
        new InventorySliceSupport(actors, permissions, operationRecorder, opContext));
    private readonly StockMovementSlices stockMovementSlices = new(
        store,
        new InventorySliceSupport(actors, permissions, operationRecorder, opContext));

    public Task<OrganizationResult<OrganizationPage<WarehouseSummary>>> ListWarehouses(
        OrganizationScope? scope,
        long organizationId,
        WarehouseListQuery query,
        CancellationToken ct = default)
        => warehouseSlices.ListWarehousesAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<WarehouseSummary>> CreateWarehouse(
        OrganizationScope? scope,
        CreateWarehouseRequest request,
        CancellationToken ct = default)
        => warehouseSlices.CreateWarehouseAsync(scope, request, ct);

    public Task<OrganizationResult<WarehouseSummary>> UpdateWarehouse(
        OrganizationScope? scope,
        UpdateWarehouseRequest request,
        CancellationToken ct = default)
        => warehouseSlices.UpdateWarehouseAsync(scope, request, ct);

    public Task<OrganizationResult<WarehouseSummary>> SetWarehouseActive(
        OrganizationScope? scope,
        SetWarehouseActiveRequest request,
        CancellationToken ct = default)
        => warehouseSlices.SetWarehouseActiveAsync(scope, request, ct);

    public Task<OrganizationResult<OrganizationPage<InventoryBalanceSummary>>> ListBalances(
        OrganizationScope? scope,
        long organizationId,
        InventoryBalanceListQuery query,
        CancellationToken ct = default)
        => stockMovementSlices.ListBalancesAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<OrganizationPage<InventoryTransactionSummary>>> ListTransactions(
        OrganizationScope? scope,
        long organizationId,
        InventoryTransactionListQuery query,
        CancellationToken ct = default)
        => stockMovementSlices.ListTransactionsAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<InventoryTransactionDetails>> GetTransaction(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => stockMovementSlices.GetTransactionAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<InventoryTransactionDetails>> PostTransaction(
        OrganizationScope? scope,
        PostInventoryTransactionRequest request,
        CancellationToken ct = default)
        => stockMovementSlices.PostTransactionAsync(scope, request, ct);

    public Task<OrganizationResult<InventoryTransactionDetails>> ReverseTransaction(
        OrganizationScope? scope,
        ReverseInventoryTransactionRequest request,
        CancellationToken ct = default)
        => stockMovementSlices.ReverseTransactionAsync(scope, request, ct);
}
