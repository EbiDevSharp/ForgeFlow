using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders.Slices;
using ForgeFlow.Application.Routings;

namespace ForgeFlow.Application.ProductionOrders;

/// <summary>
/// Facade for site-scoped Production Orders and immutable Day 11 BOM snapshots.
/// </summary>
public sealed class ProductionOrderUseCases(
    IProductionOrderOperationsStore store,
    IBomOperationsStore bomStore,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    IRoutingOperationsStore? routingStore = null)
{
    private readonly ProductionOrderSliceSupport support = new(bomStore, actors, permissions, operationRecorder, opContext, routingStore);
    private readonly ProductionOrderLifecycleSlices lifecycleSlices = new(
        store,
        new ProductionOrderSliceSupport(bomStore, actors, permissions, operationRecorder, opContext, routingStore));

    public Task<OrganizationResult<OrganizationPage<ProductionOrderSummary>>> ListOrders(
        OrganizationScope? scope,
        long organizationId,
        ProductionOrderListQuery query,
        CancellationToken ct = default)
        => lifecycleSlices.ListOrdersAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> GetOrder(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => lifecycleSlices.GetOrderAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<IReadOnlyList<ProductionOrderSiteInfo>>> ListAuthorizedSites(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
        => lifecycleSlices.ListAuthorizedSitesAsync(scope, organizationId, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> CreateDraftOrder(
        OrganizationScope? scope,
        CreateProductionOrderRequest request,
        CancellationToken ct = default)
        => lifecycleSlices.CreateDraftOrderAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> UpdateDraftOrder(
        OrganizationScope? scope,
        UpdateDraftProductionOrderRequest request,
        CancellationToken ct = default)
        => lifecycleSlices.UpdateDraftOrderAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> RefreshDraftSnapshot(
        OrganizationScope? scope,
        RefreshProductionOrderSnapshotRequest request,
        CancellationToken ct = default)
        => lifecycleSlices.RefreshDraftSnapshotAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> ReleaseOrder(
        OrganizationScope? scope,
        ReleaseProductionOrderRequest request,
        CancellationToken ct = default)
        => lifecycleSlices.ReleaseOrderAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> StartOrder(
        OrganizationScope? scope,
        StartProductionOrderRequest request,
        CancellationToken ct = default)
        => lifecycleSlices.StartOrderAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> RecordActuals(
        OrganizationScope? scope,
        RecordProductionOrderActualsRequest request,
        CancellationToken ct = default)
        => lifecycleSlices.RecordActualsAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> CompleteOrder(
        OrganizationScope? scope,
        CompleteProductionOrderRequest request,
        CancellationToken ct = default)
        => lifecycleSlices.CompleteOrderAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> CancelOrder(
        OrganizationScope? scope,
        CancelProductionOrderRequest request,
        CancellationToken ct = default)
        => lifecycleSlices.CancelOrderAsync(scope, request, ct);
}
