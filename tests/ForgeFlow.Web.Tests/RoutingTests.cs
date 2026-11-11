using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Routings;
using ForgeFlow.Domain.Routings;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class RoutingTests
{
    private static TrustedActor Actor(long id = 11) => new(id, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    private sealed class FixedActor(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult(actor);
    }

    private sealed class PermissionSetEvaluator(params string[] allowed) : IOrganizationPermissionEvaluator
    {
        private readonly HashSet<string> perms = new(allowed, StringComparer.Ordinal);
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(perms.Contains(permission)));
    }

    private sealed class RoutingStoreStub : IRoutingOperationsStore
    {
        public Dictionary<long, WorkCenterSummary> WorkCenters { get; } = [];
        public Dictionary<long, ProductionOrderOperationSummary> Operations { get; } = [];

        public Task<OrganizationPage<WorkCenterSummary>> ListWorkCenters(long organizationId, WorkCenterListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<WorkCenterSummary>(
                WorkCenters.Values.Where(w => w.OrganizationId == organizationId && (!query.SiteId.HasValue || w.SiteId == query.SiteId.Value)).ToArray(),
                WorkCenters.Count, query.Page, query.PageSize));

        public Task<IReadOnlyList<WorkCenterSummary>> ListActiveWorkCenters(long organizationId, long? siteId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WorkCenterSummary>>(WorkCenters.Values.Where(w => w.IsActive && (!siteId.HasValue || w.SiteId == siteId.Value)).ToArray());

        public Task<WorkCenterSummary?> GetWorkCenter(long organizationId, long id, CancellationToken ct)
            => Task.FromResult(WorkCenters.TryGetValue(id, out var wc) && wc.OrganizationId == organizationId ? wc : null);

        public Task<OrganizationResult<WorkCenterSummary>> CreateWorkCenter(CreateWorkCenterRequest request, OrganizationWriteAccess access, CancellationToken ct)
        {
            var ver = Convert.ToBase64String(BitConverter.GetBytes(1L));
            var summary = new WorkCenterSummary(1, request.OrganizationId, request.SiteId, "S1", "Site 1",
                RoutingText.Code(request.Code, RoutingErrors.WorkCenterCodeInvalid), RoutingText.RequiredName(request.Name),
                request.WorkCenterType, request.CapacityUnitsPerHour, request.DailyAvailableHours, request.HourlyCostRate, request.Notes, true, ver);
            WorkCenters[1] = summary;
            return Task.FromResult(OrganizationResult<WorkCenterSummary>.Success(summary));
        }

        public Task<OrganizationResult<WorkCenterSummary>> UpdateWorkCenter(long organizationId, long id, byte[] version, UpdateWorkCenterRequest request, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<OrganizationResult<WorkCenterSummary>> SetWorkCenterActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<OrganizationPage<RoutingSummary>> ListRoutings(long organizationId, RoutingListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<RoutingSummary>([], 0, query.Page, query.PageSize));
        public Task<RoutingDetails?> GetRouting(long organizationId, long id, CancellationToken ct) => Task.FromResult<RoutingDetails?>(null);
        public Task<IReadOnlyList<RoutingSummary>> ListActiveRoutingsForItem(long organizationId, long siteId, long itemId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<RoutingSummary>>([]);
        public Task<RoutingDetails?> ResolveRoutingForProductionOrder(long organizationId, long siteId, long itemId, long? requestedRoutingId, CancellationToken ct)
            => Task.FromResult<RoutingDetails?>(null);
        public Task<OrganizationResult<RoutingDetails>> CreateDraftRouting(CreateRoutingRequest request, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<OrganizationResult<RoutingDetails>> UpdateDraftRouting(long organizationId, long id, byte[] version, UpdateDraftRoutingRequest request, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<OrganizationResult<RoutingDetails>> SaveRoutingOperations(long organizationId, long routingId, byte[] version, IReadOnlyList<RoutingOperationInput> operations, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<OrganizationResult<RoutingDetails>> ActivateRouting(long organizationId, long id, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<OrganizationResult<RoutingDetails>> MarkRoutingObsolete(long organizationId, long id, byte[] version, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<ProductionOrderOperationSummary?> GetProductionOrderOperation(long organizationId, long operationId, CancellationToken ct)
            => Task.FromResult(Operations.TryGetValue(operationId, out var op) && op.OrganizationId == organizationId ? op : null);
        public Task<IReadOnlyList<ProductionOrderOperationSummary>> ListOperationsForOrder(long organizationId, long productionOrderId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProductionOrderOperationSummary>>([]);
        public Task<OrganizationResult<ProductionOrderOperationSummary>> StartOperation(long organizationId, long operationId, byte[] version, string? notes, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
        {
            var current = Operations[operationId];
            var next = current with { Status = ProductionOrderOperationStatus.InProgress, StartedAtUtc = utcNow };
            Operations[operationId] = next;
            return Task.FromResult(OrganizationResult<ProductionOrderOperationSummary>.Success(next));
        }
        public Task<OrganizationResult<ProductionOrderOperationSummary>> PauseOperation(long organizationId, long operationId, byte[] version, decimal actualSetupMinutes, decimal actualRunMinutes, decimal completedQuantity, decimal scrapQuantity, string? notes, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<OrganizationResult<ProductionOrderOperationSummary>> CompleteOperation(long organizationId, long operationId, byte[] version, decimal actualSetupMinutes, decimal actualRunMinutes, decimal completedQuantity, decimal scrapQuantity, string? notes, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<OrganizationResult<ProductionOrderOperationSummary>> SkipOperation(long organizationId, long operationId, byte[] version, string? notes, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<WorkCenterLoadSummary>> GetWorkCenterLoads(long organizationId, long? siteId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WorkCenterLoadSummary>>([]);
    }

    [Fact]
    public void W01_WorkCenter_and_Routing_domain_invariants_reject_invalid_values()
    {
        var wc = WorkCenter.Create(1, 10, "wc-cnc-01", "CNC Mill 1", WorkCenterType.Machine, 25m, 8m, 120m, "Main bay");
        Assert.Equal("WC-CNC-01", wc.Code);
        Assert.True(wc.IsActive);

        Assert.Equal(RoutingErrors.CapacityInvalid,
            Assert.Throws<RoutingRuleException>(() => WorkCenter.Create(1, 10, "WC-2", "Bad", WorkCenterType.Machine, 0m, 8m, null, null)).Code);
        Assert.Equal(RoutingErrors.DailyHoursInvalid,
            Assert.Throws<RoutingRuleException>(() => WorkCenter.Create(1, 10, "WC-2", "Bad", WorkCenterType.Machine, 5m, 25m, null, null)).Code);

        var routing = Routing.CreateDraft(1, 10, "rt-main", "Main Routing", 100, null, 1, true, null);
        Assert.Equal(RoutingStatus.Draft, routing.Status);
        Assert.Equal(RoutingErrors.EmptyOperations,
            Assert.Throws<RoutingRuleException>(() => routing.Activate(DateTime.UtcNow)).Code);
        Assert.Equal(RoutingErrors.DuplicateSequence,
            Assert.Throws<RoutingRuleException>(() => routing.ReplaceOperations(
                [(10, "OP-10", "Cut", 1, 5m, 1m, 0m, 0m, false, null),
                 (10, "OP-20", "Drill", 1, 5m, 1m, 0m, 0m, false, null)])).Code);
        Assert.Equal(RoutingErrors.RunTimeInvalid,
            Assert.Throws<RoutingRuleException>(() => routing.ReplaceOperations(
                [(10, "OP-10", "Cut", 1, 5m, 0m, 0m, 0m, false, null)])).Code);

        routing.ReplaceOperations([(10, "OP-10", "Cut", 1, 5m, 2m, 1m, 1m, true, null)]);
        routing.Activate(DateTime.UtcNow);
        Assert.Equal(RoutingStatus.Active, routing.Status);
        Assert.Equal(RoutingErrors.RoutingNotDraft,
            Assert.Throws<RoutingRuleException>(() => routing.UpdateDraft("Edited", null, true, null)).Code);
    }

    [Fact]
    public void W01_ProductionOrderOperation_lifecycle_enforces_transitions()
    {
        var op = ProductionOrderOperation.Create(1, 10, 50, 10, "OP-10", "Cut", 5, 15m, 40m, 5m, 0m, true, null);
        Assert.Equal(ProductionOrderOperationStatus.Pending, op.Status);
        Assert.Equal(60m, op.PlannedTotalMinutes);

        Assert.True(op.MarkReady());
        Assert.Equal(ProductionOrderOperationStatus.Ready, op.Status);

        var t0 = DateTime.UtcNow;
        op.Start(t0, "Starting run");
        Assert.Equal(ProductionOrderOperationStatus.InProgress, op.Status);

        op.Pause(14m, 20m, 5m, 0m, "Shift break");
        Assert.Equal(ProductionOrderOperationStatus.Paused, op.Status);

        op.Start(t0.AddMinutes(30));
        op.Complete(14m, 38m, 10m, 1m, "Done", t0.AddMinutes(50));
        Assert.Equal(ProductionOrderOperationStatus.Completed, op.Status);

        Assert.Equal(RoutingErrors.OperationTransitionInvalid,
            Assert.Throws<RoutingRuleException>(() => op.Skip("Too late", t0.AddMinutes(55))).Code);
    }

    [Fact]
    public async Task W02_UseCases_enforce_permissions_and_site_isolation()
    {
        var store = new RoutingStoreStub();
        store.Operations[7] = new ProductionOrderOperationSummary(
            7, 1, 20, 99, "PO-99", 10, "OP-10", "Weld", 5, "WC-5", "Welding",
            10m, 20m, 0m, 0m, 30m, 0m, 0m, 0m, 0m, false, null, null,
            ProductionOrderOperationStatus.Ready, null, null, Convert.ToBase64String(BitConverter.GetBytes(1L)));

        var readOnlyUc = new RoutingUseCases(store, new FixedActor(Actor()), new PermissionSetEvaluator(RoutingPermissions.RoutingsRead));
        var deniedCreate = await readOnlyUc.CreateWorkCenter(new OrganizationScope.Site(1, 10),
            new CreateWorkCenterRequest(1, 10, "WC-1", "Line 1", WorkCenterType.Machine, 10m, 8m, null, null));
        Assert.False(deniedCreate.Succeeded);
        Assert.Equal(RoutingErrors.AccessDenied, deniedCreate.Error!.Code);

        var site10Executor = new RoutingUseCases(store, new FixedActor(Actor()),
            new PermissionSetEvaluator(RoutingPermissions.RoutingsRead, RoutingPermissions.RoutingsManage, RoutingPermissions.OperationsExecute));

        // Cross-site operation start (site 10 actor targeting site 20 op) must be rejected
        var crossSiteStart = await site10Executor.StartOperation(new OrganizationScope.Site(1, 10),
            new StartProductionOrderOperationRequest(1, 7, null, Convert.ToBase64String(BitConverter.GetBytes(1L))));
        Assert.False(crossSiteStart.Succeeded);
        Assert.Equal(RoutingErrors.AccessDenied, crossSiteStart.Error!.Code);

        // Authorized site 20 operation start succeeds
        var site20Start = await site10Executor.StartOperation(new OrganizationScope.Site(1, 20),
            new StartProductionOrderOperationRequest(1, 7, "Go", Convert.ToBase64String(BitConverter.GetBytes(1L))));
        Assert.True(site20Start.Succeeded);
        Assert.Equal(ProductionOrderOperationStatus.InProgress, site20Start.Value!.Status);
    }
}
