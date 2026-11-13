using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Sales.Slices;

namespace ForgeFlow.Application.Sales;

/// <summary>
/// Facade for Sales domain slices: Organization-shared Customers, Site-scoped Sales Orders,
/// and atomic Delivery Orders (Dispatches).
/// </summary>
public sealed class SalesUseCases(
    ISalesOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private readonly SalesSliceSupport support = new(actors, permissions, operationRecorder, opContext, clock);
    private readonly CustomerSlices customerSlices = new(store, new SalesSliceSupport(actors, permissions, operationRecorder, opContext, clock));
    private readonly SalesOrderSlices salesOrderSlices = new(store, new SalesSliceSupport(actors, permissions, operationRecorder, opContext, clock));
    private readonly DeliveryOrderSlices deliveryOrderSlices = new(store, new SalesSliceSupport(actors, permissions, operationRecorder, opContext, clock));

    // --- Customers ---
    public Task<OrganizationResult<OrganizationPage<CustomerSummary>>> ListCustomers(
        OrganizationScope? scope,
        long organizationId,
        CustomerListQuery query,
        CancellationToken ct = default)
        => customerSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<CustomerSummary>> CreateCustomer(
        OrganizationScope? scope,
        CreateCustomerRequest request,
        CancellationToken ct = default)
        => customerSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<CustomerSummary>> UpdateCustomer(
        OrganizationScope? scope,
        long organizationId,
        long customerId,
        UpdateCustomerRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
        => customerSlices.UpdateAsync(scope, organizationId, customerId, request, expectedVersion, ct);

    public Task<OrganizationResult<CustomerSummary>> SetCustomerActive(
        OrganizationScope? scope,
        long organizationId,
        long customerId,
        bool isActive,
        string? expectedVersion,
        CancellationToken ct = default)
        => customerSlices.SetActiveAsync(scope, organizationId, customerId, isActive, expectedVersion, ct);

    // --- Sales Orders ---
    public Task<OrganizationResult<OrganizationPage<SalesOrderSummary>>> ListSalesOrders(
        OrganizationScope? scope,
        long organizationId,
        SalesOrderListQuery query,
        CancellationToken ct = default)
        => salesOrderSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<SalesOrderDetails>> GetSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        CancellationToken ct = default)
        => salesOrderSlices.GetAsync(scope, organizationId, salesOrderId, ct);

    public Task<OrganizationResult<SalesOrderDetails>> CreateSalesOrder(
        OrganizationScope? scope,
        CreateSalesOrderRequest request,
        CancellationToken ct = default)
        => salesOrderSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<SalesOrderDetails>> UpdateSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        UpdateSalesOrderRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
        => salesOrderSlices.UpdateAsync(scope, organizationId, salesOrderId, request, expectedVersion, ct);

    public Task<OrganizationResult<SalesOrderDetails>> ApproveSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        string? expectedVersion,
        CancellationToken ct = default)
        => salesOrderSlices.ApproveAsync(scope, organizationId, salesOrderId, expectedVersion, ct);

    public Task<OrganizationResult<SalesOrderDetails>> CloseSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        string? expectedVersion,
        CancellationToken ct = default)
        => salesOrderSlices.CloseAsync(scope, organizationId, salesOrderId, expectedVersion, ct);

    public Task<OrganizationResult<SalesOrderDetails>> CancelSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        CancelSalesOrderRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
        => salesOrderSlices.CancelAsync(scope, organizationId, salesOrderId, request, expectedVersion, ct);

    // --- Delivery Orders ---
    public Task<OrganizationResult<OrganizationPage<DeliveryOrderSummary>>> ListDeliveryOrders(
        OrganizationScope? scope,
        long organizationId,
        DeliveryOrderListQuery query,
        CancellationToken ct = default)
        => deliveryOrderSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<DeliveryOrderDetails>> GetDeliveryOrder(
        OrganizationScope? scope,
        long organizationId,
        long deliveryOrderId,
        CancellationToken ct = default)
        => deliveryOrderSlices.GetAsync(scope, organizationId, deliveryOrderId, ct);

    public Task<OrganizationResult<IReadOnlyList<DispatchableLotSummary>>> ListDispatchableLots(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        long warehouseId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default)
        => deliveryOrderSlices.ListDispatchableLotsAsync(scope, organizationId, salesOrderId, warehouseId, asOfDate, ct);

    public Task<OrganizationResult<DeliveryOrderDetails>> PostDeliveryOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        PostDeliveryOrderRequest request,
        CancellationToken ct = default)
        => deliveryOrderSlices.PostAsync(scope, organizationId, salesOrderId, request, ct);

    public Task<OrganizationResult<DeliveryOrderDetails>> ReverseDeliveryOrder(
        OrganizationScope? scope,
        long organizationId,
        long deliveryOrderId,
        ReverseDeliveryOrderRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
        => deliveryOrderSlices.ReverseAsync(scope, organizationId, deliveryOrderId, request, expectedVersion, ct);
}
