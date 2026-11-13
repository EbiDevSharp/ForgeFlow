using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement.Slices;
using ForgeFlow.Domain.Procurement;

namespace ForgeFlow.Application.Procurement;

/// <summary>
/// Secured use cases for Organization-shared Suppliers, Site-scoped Purchase Orders,
/// and atomic Goods Receipts into Inventory, Lot Tracking, and Quality Control.
/// Modularized via vertical slices in <see cref="ForgeFlow.Application.Procurement.Slices"/>.
/// </summary>
public sealed class ProcurementUseCases
{
    private readonly SupplierSlices _suppliers;
    private readonly PurchaseOrderSlices _orders;

    public ProcurementUseCases(
        IProcurementOperationsStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null,
        TimeProvider? clock = null)
    {
        var support = new ProcurementSliceSupport(actors, permissions, operationRecorder, opContext, clock);
        _suppliers = new SupplierSlices(store, support);
        _orders = new PurchaseOrderSlices(store, support);
    }

    // --- Suppliers (Organization-shared) ---

    public Task<OrganizationResult<OrganizationPage<SupplierSummary>>> ListSuppliers(
        OrganizationScope? scope,
        long organizationId,
        SupplierListQuery query,
        CancellationToken ct = default)
        => _suppliers.ListSuppliers(scope, organizationId, query, ct);

    public Task<OrganizationResult<SupplierSummary>> GetSupplier(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => _suppliers.GetSupplier(scope, organizationId, id, ct);

    public Task<OrganizationResult<SupplierSummary>> CreateSupplier(
        OrganizationScope? scope,
        CreateSupplierRequest request,
        CancellationToken ct = default)
        => _suppliers.CreateSupplier(scope, request, ct);

    public Task<OrganizationResult<SupplierSummary>> UpdateSupplier(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        UpdateSupplierRequest request,
        CancellationToken ct = default)
        => _suppliers.UpdateSupplier(scope, organizationId, id, expectedVersion, request, ct);

    public Task<OrganizationResult<SupplierSummary>> SetSupplierActive(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        bool isActive,
        CancellationToken ct = default)
        => _suppliers.SetSupplierActive(scope, organizationId, id, expectedVersion, isActive, ct);

    // --- Purchase Orders (Site-scoped) ---

    public Task<OrganizationResult<OrganizationPage<PurchaseOrderSummary>>> ListPurchaseOrders(
        OrganizationScope? scope,
        long organizationId,
        PurchaseOrderListQuery query,
        CancellationToken ct = default)
        => _orders.ListPurchaseOrders(scope, organizationId, query, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> GetPurchaseOrder(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => _orders.GetPurchaseOrder(scope, organizationId, id, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CreateDraft(
        OrganizationScope? scope,
        CreatePurchaseOrderRequest request,
        CancellationToken ct = default)
        => _orders.CreateDraft(scope, request, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> UpdateDraft(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        UpdatePurchaseOrderRequest request,
        CancellationToken ct = default)
        => _orders.UpdateDraft(scope, organizationId, id, expectedVersion, request, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> ApproveOrder(
        OrganizationScope? scope, long organizationId, long id, string? expectedVersion, CancellationToken ct = default)
        => _orders.ApproveOrder(scope, organizationId, id, expectedVersion, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CloseOrder(
        OrganizationScope? scope, long organizationId, long id, string? expectedVersion, CancellationToken ct = default)
        => _orders.CloseOrder(scope, organizationId, id, expectedVersion, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CancelOrder(
        OrganizationScope? scope, long organizationId, long id, string? expectedVersion, string? reason, CancellationToken ct = default)
        => _orders.CancelOrder(scope, organizationId, id, expectedVersion, reason, ct);

    public Task<OrganizationResult<PurchaseOrderReceiptResult>> ReceiveOrder(
        OrganizationScope? scope,
        ReceivePurchaseOrderRequest request,
        CancellationToken ct = default)
        => _orders.ReceiveOrder(scope, request, ct);
}
