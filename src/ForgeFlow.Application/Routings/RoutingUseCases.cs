using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Routings.Slices;
using ForgeFlow.Domain.Routings;

namespace ForgeFlow.Application.Routings;

/// <summary>
/// Secured facade for site-scoped Work Centers, Routings, and shop-floor ProductionOrderOperations.
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Routings.Slices"/>.
/// </summary>
public sealed class RoutingUseCases
{
    private readonly WorkCenterSlices workCenterSlices;
    private readonly RoutingCatalogSlices routingSlices;
    private readonly RoutingOperationSlices operationSlices;

    public RoutingUseCases(
        IRoutingOperationsStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null)
    {
        var support = new RoutingSliceSupport(actors, permissions, operationRecorder, opContext);
        workCenterSlices = new WorkCenterSlices(store, support);
        routingSlices = new RoutingCatalogSlices(store, support);
        operationSlices = new RoutingOperationSlices(store, support);
    }

    public Task<OrganizationResult<OrganizationPage<WorkCenterSummary>>> ListWorkCenters(
        OrganizationScope? scope,
        long organizationId,
        WorkCenterListQuery query,
        CancellationToken ct = default)
        => workCenterSlices.ListWorkCentersAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<IReadOnlyList<WorkCenterSummary>>> ListActiveWorkCenters(
        OrganizationScope? scope,
        long organizationId,
        long? siteId = null,
        CancellationToken ct = default)
        => workCenterSlices.ListActiveWorkCentersAsync(scope, organizationId, siteId, ct);

    public Task<OrganizationResult<WorkCenterSummary>> GetWorkCenter(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => workCenterSlices.GetWorkCenterAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<WorkCenterSummary>> CreateWorkCenter(
        OrganizationScope? scope,
        CreateWorkCenterRequest request,
        CancellationToken ct = default)
        => workCenterSlices.CreateWorkCenterAsync(scope, request, ct);

    public Task<OrganizationResult<WorkCenterSummary>> UpdateWorkCenter(
        OrganizationScope? scope,
        UpdateWorkCenterRequest request,
        CancellationToken ct = default)
        => workCenterSlices.UpdateWorkCenterAsync(scope, request, ct);

    public Task<OrganizationResult<WorkCenterSummary>> SetWorkCenterActive(
        OrganizationScope? scope,
        SetWorkCenterActiveRequest request,
        CancellationToken ct = default)
        => workCenterSlices.SetWorkCenterActiveAsync(scope, request, ct);

    public Task<OrganizationResult<IReadOnlyList<WorkCenterLoadSummary>>> GetWorkCenterLoads(
        OrganizationScope? scope,
        long organizationId,
        long? siteId = null,
        CancellationToken ct = default)
        => workCenterSlices.GetWorkCenterLoadsAsync(scope, organizationId, siteId, ct);

    public Task<OrganizationResult<OrganizationPage<RoutingSummary>>> ListRoutings(
        OrganizationScope? scope,
        long organizationId,
        RoutingListQuery query,
        CancellationToken ct = default)
        => routingSlices.ListRoutingsAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<RoutingDetails>> GetRouting(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => routingSlices.GetRoutingAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<IReadOnlyList<RoutingSummary>>> ListActiveRoutingsForItem(
        OrganizationScope? scope,
        long organizationId,
        long siteId,
        long itemId,
        CancellationToken ct = default)
        => routingSlices.ListActiveRoutingsForItemAsync(scope, organizationId, siteId, itemId, ct);

    public Task<OrganizationResult<RoutingDetails>> CreateDraftRouting(
        OrganizationScope? scope,
        CreateRoutingRequest request,
        CancellationToken ct = default)
        => routingSlices.CreateDraftRoutingAsync(scope, request, ct);

    public Task<OrganizationResult<RoutingDetails>> UpdateDraftRouting(
        OrganizationScope? scope,
        UpdateDraftRoutingRequest request,
        CancellationToken ct = default)
        => routingSlices.UpdateDraftRoutingAsync(scope, request, ct);

    public Task<OrganizationResult<RoutingDetails>> SaveRoutingOperations(
        OrganizationScope? scope,
        SaveRoutingOperationsRequest request,
        CancellationToken ct = default)
        => routingSlices.SaveRoutingOperationsAsync(scope, request, ct);

    public Task<OrganizationResult<RoutingDetails>> ActivateRouting(
        OrganizationScope? scope,
        ActivateRoutingRequest request,
        CancellationToken ct = default)
        => routingSlices.ActivateRoutingAsync(scope, request, ct);

    public Task<OrganizationResult<RoutingDetails>> MarkRoutingObsolete(
        OrganizationScope? scope,
        MarkRoutingObsoleteRequest request,
        CancellationToken ct = default)
        => routingSlices.MarkRoutingObsoleteAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> StartOperation(
        OrganizationScope? scope,
        StartProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => operationSlices.StartOperationAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> PauseOperation(
        OrganizationScope? scope,
        PauseProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => operationSlices.PauseOperationAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> CompleteOperation(
        OrganizationScope? scope,
        CompleteProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => operationSlices.CompleteOperationAsync(scope, request, ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> SkipOperation(
        OrganizationScope? scope,
        SkipProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => operationSlices.SkipOperationAsync(scope, request, ct);
}
