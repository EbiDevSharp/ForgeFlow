using System.Reflection;
using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Units;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Roles;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ProductionOrderTests
{
    private sealed class ActorStub(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(actor);
    }

    private sealed class PermissionStub(Func<OrganizationScope, string, bool> predicate) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor,
            OrganizationScope requestedScope,
            string permission,
            CancellationToken ct)
            => ValueTask.FromResult(new PermissionDecision(predicate(requestedScope, permission)));
    }

    private sealed class BomStoreStub : IBomOperationsStore
    {
        public Dictionary<long, BomResolutionDataset> Datasets { get; } = [];
        public List<UnitConversionEdge> ItemEdges { get; } = [];

        public Task<BomResolutionDataset?> LoadItemBomResolutionDataset(long organizationId, long itemId, CancellationToken ct)
            => Task.FromResult(Datasets.TryGetValue(itemId, out var ds) ? ds : null);

        public Task<IReadOnlyList<UnitConversionEdge>> ListItemUnitEdges(long organizationId, long itemId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<UnitConversionEdge>>(ItemEdges);

        public Task<OrganizationPage<BomSummary>> ListBoms(long organizationId, BomListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<BomDetails?> GetBom(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<BomVersionDetails?> GetBomVersion(long organizationId, long bomVersionId, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomDetails>> CreateBom(CreateBomRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomDetails>> UpdateBom(long organizationId, long id, byte[] version, string? name, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomDetails>> SetBomActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> CreateBomVersion(CreateBomVersionRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> UpdateDraftBomVersion(long organizationId, long bomVersionId, byte[] version, UpdateDraftBomVersionRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> SaveDraftBomLines(long organizationId, long bomVersionId, byte[] version, IReadOnlyList<BomInputLineInput> inputs, IReadOnlyList<BomOutputLineInput> outputs, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> SaveDraftBomApplicability(long organizationId, long bomVersionId, byte[] version, SaveDraftBomApplicabilityRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> TransitionBomVersionStatus(long organizationId, long bomVersionId, byte[] version, BomVersionStatus targetStatus, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<ContextDimensionSummary>> ListContextDimensions(long organizationId, ContextDimensionListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ContextDimensionSummary>> ListActiveContextDimensions(long organizationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ContextDimensionSummary?> GetContextDimension(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ContextDimensionSummary>> CreateContextDimension(CreateContextDimensionRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ContextDimensionSummary>> UpdateContextDimension(long organizationId, long id, byte[] version, string? name, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ContextDimensionSummary>> SetContextDimensionActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SeedContextDimensionsResult>> SeedStandardContextDimensions(long organizationId, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class OrderStoreStub : IProductionOrderOperationsStore
    {
        private long nextId = 100;
        public List<ProductionOrderSiteInfo> Sites { get; } = [];
        public Dictionary<long, ProductionOrderDetails> Orders { get; } = [];
        public Dictionary<long, byte[]> Versions { get; } = [];

        public Task<IReadOnlyList<ProductionOrderSiteInfo>> ListActiveSites(long organizationId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProductionOrderSiteInfo>>(Sites.Where(s => s.OrganizationId == organizationId && s.IsActive && s.OrganizationIsActive).ToArray());

        public Task<ProductionOrderSiteInfo?> GetSiteInfo(long organizationId, long siteId, CancellationToken ct)
            => Task.FromResult(Sites.FirstOrDefault(s => s.OrganizationId == organizationId && s.Id == siteId));

        public Task<OrganizationPage<ProductionOrderSummary>> ListOrders(long organizationId, ProductionOrderListQuery query, CancellationToken ct)
        {
            var q = Orders.Values.Where(o => o.Summary.OrganizationId == organizationId);
            if (query.SiteId.HasValue) q = q.Where(o => o.Summary.SiteId == query.SiteId.Value);
            if (query.Status.HasValue) q = q.Where(o => o.Summary.Status == query.Status.Value);
            var list = q.Select(o => o.Summary).ToArray();
            return Task.FromResult(new OrganizationPage<ProductionOrderSummary>(list, list.Length, query.Page, query.PageSize));
        }

        public Task<ProductionOrderDetails?> GetOrder(long organizationId, long id, CancellationToken ct)
            => Task.FromResult(Orders.TryGetValue(id, out var d) && d.Summary.OrganizationId == organizationId ? d : null);

        public Task<OrganizationResult<ProductionOrderDetails>> CreateDraft(CreateProductionOrderRequest request, ResolvedBomSnapshotWrite snapshot, OrganizationWriteAccess access, CancellationToken ct, ForgeFlow.Application.Routings.ResolvedRoutingSnapshotWrite? routingSnapshot = null)
        {
            var number = ProductionOrderText.OrderNumber(request.OrderNumber);
            if (Orders.Values.Any(o => o.Summary.OrganizationId == request.OrganizationId && o.Summary.OrderNumber == number))
                return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.OrderNumberDuplicate));
            var site = Sites.Single(s => s.Id == request.SiteId);
            var id = nextId++;
            var ver = BitConverter.GetBytes(1L);
            Versions[id] = ver;
            var details = ToDetails(id, request.OrganizationId, site, number, ProductionOrderStatus.Draft, request.ItemId, request.RequestedQuantity, snapshot.EffectiveRequestedUnitId, request.PlannedStartDate, request.PlannedDueDate, snapshot, 0m, 0m, request.Notes, null, null, null, null, null, ver);
            Orders[id] = details;
            return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Success(details));
        }

        public Task<OrganizationResult<ProductionOrderDetails>> UpdateDraft(long organizationId, long id, byte[] version, UpdateDraftProductionOrderRequest request, ResolvedBomSnapshotWrite snapshot, OrganizationWriteAccess access, CancellationToken ct, ForgeFlow.Application.Routings.ResolvedRoutingSnapshotWrite? routingSnapshot = null)
        {
            if (!Orders.TryGetValue(id, out var existing)) return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound));
            if (!Versions[id].SequenceEqual(version)) return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict));
            if (existing.Summary.Status != ProductionOrderStatus.Draft) return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.OrderImmutable));
            var site = Sites.Single(s => s.Id == existing.Summary.SiteId);
            var nextVer = BitConverter.GetBytes(BitConverter.ToInt64(Versions[id]) + 1);
            Versions[id] = nextVer;
            var details = ToDetails(id, organizationId, site, existing.Summary.OrderNumber, ProductionOrderStatus.Draft, existing.Summary.ItemId, request.RequestedQuantity, snapshot.EffectiveRequestedUnitId, request.PlannedStartDate, request.PlannedDueDate, snapshot, 0m, 0m, request.Notes, null, null, null, null, null, nextVer);
            Orders[id] = details;
            return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Success(details));
        }

        public Task<OrganizationResult<ProductionOrderDetails>> RefreshDraftSnapshot(long organizationId, long id, byte[] version, ResolvedBomSnapshotWrite snapshot, OrganizationWriteAccess access, CancellationToken ct, ForgeFlow.Application.Routings.ResolvedRoutingSnapshotWrite? routingSnapshot = null)
        {
            if (!Orders.TryGetValue(id, out var existing)) return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound));
            if (!Versions[id].SequenceEqual(version)) return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict));
            if (existing.Summary.Status != ProductionOrderStatus.Draft) return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.OrderImmutable));
            var site = Sites.Single(s => s.Id == existing.Summary.SiteId);
            var nextVer = BitConverter.GetBytes(BitConverter.ToInt64(Versions[id]) + 1);
            Versions[id] = nextVer;
            var details = ToDetails(id, organizationId, site, existing.Summary.OrderNumber, ProductionOrderStatus.Draft, existing.Summary.ItemId, existing.Summary.RequestedQuantity, existing.Summary.RequestedUnitId, existing.Summary.PlannedStartDate, existing.Summary.PlannedDueDate, snapshot, 0m, 0m, existing.Summary.Notes, null, null, null, null, null, nextVer);
            Orders[id] = details;
            return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Success(details));
        }

        public Task<OrganizationResult<ProductionOrderDetails>> ReleaseOrder(long organizationId, long id, byte[] version, ResolvedBomSnapshotWrite snapshot, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct, ForgeFlow.Application.Routings.ResolvedRoutingSnapshotWrite? routingSnapshot = null)
            => Transition(id, version, ProductionOrderStatus.Draft, ProductionOrderStatus.Released, snapshot, null, null, utcNow);

        public Task<OrganizationResult<ProductionOrderDetails>> StartOrder(long organizationId, long id, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
            => Transition(id, version, ProductionOrderStatus.Released, ProductionOrderStatus.InProgress, null, null, null, utcNow);

        public Task<OrganizationResult<ProductionOrderDetails>> RecordActuals(long organizationId, long id, byte[] version, decimal producedQuantity, decimal scrapQuantity, string? notes, OrganizationWriteAccess access, CancellationToken ct)
            => Transition(id, version, ProductionOrderStatus.InProgress, ProductionOrderStatus.InProgress, null, (producedQuantity, scrapQuantity, notes), null, null);

        public Task<OrganizationResult<ProductionOrderDetails>> CompleteOrder(long organizationId, long id, byte[] version, decimal producedQuantity, decimal scrapQuantity, string? notes, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
            => Transition(id, version, ProductionOrderStatus.InProgress, ProductionOrderStatus.Completed, null, (producedQuantity, scrapQuantity, notes), null, utcNow);

        public Task<OrganizationResult<ProductionOrderDetails>> CancelOrder(long organizationId, long id, byte[] version, string? reason, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
            => Transition(id, version, null, ProductionOrderStatus.Cancelled, null, null, reason, utcNow);

        private Task<OrganizationResult<ProductionOrderDetails>> Transition(long id, byte[] version, ProductionOrderStatus? requiredCurrent, ProductionOrderStatus target, ResolvedBomSnapshotWrite? snapshot, (decimal p, decimal s, string? n)? actuals, string? reason, DateTime? utcNow)
        {
            if (!Orders.TryGetValue(id, out var existing)) return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound));
            if (!Versions[id].SequenceEqual(version)) return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict));
            if (requiredCurrent.HasValue && existing.Summary.Status != requiredCurrent.Value)
                return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.TransitionInvalid));
            if (target == ProductionOrderStatus.Cancelled && existing.Summary.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
                return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.TransitionInvalid));

            var nextVer = BitConverter.GetBytes(BitConverter.ToInt64(Versions[id]) + 1);
            Versions[id] = nextVer;
            var s = existing.Summary;
            if (snapshot is not null)
            {
                var site = Sites.Single(st => st.Id == s.SiteId);
                var rebuilt = ToDetails(id, s.OrganizationId, site, s.OrderNumber, target, s.ItemId, s.RequestedQuantity, snapshot.EffectiveRequestedUnitId, s.PlannedStartDate, s.PlannedDueDate, snapshot, s.ActualProducedQuantity, s.ActualScrapQuantity, s.Notes, s.CancellationReason, utcNow, s.StartedAtUtc, s.CompletedAtUtc, s.CancelledAtUtc, nextVer);
                Orders[id] = rebuilt;
                return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Success(rebuilt));
            }
            var updatedSummary = s with
            {
                Status = target,
                ActualProducedQuantity = actuals?.p ?? s.ActualProducedQuantity,
                ActualScrapQuantity = actuals?.s ?? s.ActualScrapQuantity,
                Notes = actuals is not null ? actuals.Value.n : s.Notes,
                CancellationReason = reason ?? s.CancellationReason,
                StartedAtUtc = target == ProductionOrderStatus.InProgress ? utcNow ?? s.StartedAtUtc : s.StartedAtUtc,
                CompletedAtUtc = target == ProductionOrderStatus.Completed ? utcNow : s.CompletedAtUtc,
                CancelledAtUtc = target == ProductionOrderStatus.Cancelled ? utcNow : s.CancelledAtUtc,
                RowVersion = Convert.ToBase64String(nextVer)
            };
            var updated = existing with { Summary = updatedSummary };
            Orders[id] = updated;
            return Task.FromResult(OrganizationResult<ProductionOrderDetails>.Success(updated));
        }

        private static ProductionOrderDetails ToDetails(
            long id, long orgId, ProductionOrderSiteInfo site, string orderNumber, ProductionOrderStatus status,
            long itemId, decimal qty, long unitId, DateOnly start, DateOnly due, ResolvedBomSnapshotWrite snapshot,
            decimal actualProd, decimal actualScrap, string? notes, string? cancelReason,
            DateTime? relAt, DateTime? startAt, DateTime? compAt, DateTime? cancAt, byte[] rowVer)
        {
            var summary = new ProductionOrderSummary(
                id, orgId, site.Id, site.Code, site.Name, orderNumber, status, itemId, "FG-FRAME", "Steel Frame",
                qty, unitId, "PCS", "pcs", start, due,
                snapshot.BomId, snapshot.BomCode, snapshot.BomVersionId, snapshot.VersionNumber, snapshot.BaseOutputQuantity, snapshot.BaseOutputUnitId, "pcs",
                actualProd, actualScrap, notes, cancelReason, DateTime.UtcNow, relAt, startAt, compAt, cancAt, Convert.ToBase64String(rowVer));
            var inputs = snapshot.ConsumedInputs.Select((i, idx) => new ProductionOrderInputSummary(
                idx + 1, i.Level, idx + 1, i.ParentItemId, i.ParentItemCode, i.ItemId, i.ItemCode, i.ItemName,
                i.NetQuantity, i.ScrapPercentage, i.GrossQuantity, i.UnitId, i.UnitCode, i.UnitSymbol, i.IsOptional, i.IsSubAssembly, i.ResolvedBomVersionId)).ToArray();
            var outputs = snapshot.ProducedOutputs.Select((o, idx) => new ProductionOrderOutputSummary(
                idx + 1, o.Level, idx + 1, o.ItemId, o.ItemCode, o.ItemName, o.OutputType, o.ProducedQuantity, o.UnitId, o.UnitCode, o.UnitSymbol)).ToArray();
            var contexts = snapshot.EffectiveContext.Select(kv => new ProductionOrderContextSummary(kv.Key, kv.Value)).ToArray();
            return new ProductionOrderDetails(summary, inputs, outputs, contexts);
        }
    }

    private static BomVersionCandidateRecord Candidate(long bomId, string code, long verId, int verNo, string? plantFilter, decimal steelPerPcs)
    {
        var conditions = plantFilter is null
            ? Array.Empty<BomApplicabilityConditionSummary>()
            : [new BomApplicabilityConditionSummary(10, "PLANT", "Plant / Site", true, ApplicabilityOperator.Equals, plantFilter)];
        var applicability = plantFilter is null
            ? null
            : new BomApplicabilityDetails($"Plant {plantFilter}", true, conditions);

        return new BomVersionCandidateRecord(
            bomId,
            code,
            "Frame BOM",
            100,
            new BomVersionDetails(
                new BomVersionSummary(verId, 1, bomId, verNo, BomVersionStatus.Active, 1m, 1, "PCS", "pcs", new DateOnly(2026, 1, 1), null, null, applicability is not null, applicability?.IsActive ?? false, conditions.Length, "AA=="),
                [new BomInputSummary(1, 1, 200, "STEEL", "Raw Steel", steelPerPcs, 2, "KG", "kg", 10m, false, null)],
                [new BomOutputSummary(1, 1, 100, "FG-FRAME", "Steel Frame", BomOutputType.MainProduct, 1m, 1, "PCS", "pcs")],
                applicability),
            []);
    }

    [Fact]
    public void P01_domain_production_order_lifecycle_and_immutability_enforced()
    {
        var order = ProductionOrder.CreateDraft(
            1, 10, "po-001", 100, 5m, 1,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5),
            20, "BOM-FRAME", 200, 1, 1m, 1, "Initial");

        Assert.Equal("PO-001", order.OrderNumber);
        Assert.Equal(ProductionOrderStatus.Draft, order.Status);

        order.Release(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));
        Assert.Equal(ProductionOrderStatus.Released, order.Status);

        Assert.Equal(ProductionOrderErrors.OrderImmutable,
            Assert.Throws<ProductionOrderRuleException>(() =>
                order.UpdateDraftHeader(6m, 1, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 6), null)).Code);

        order.Start(new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc));
        Assert.Equal(ProductionOrderStatus.InProgress, order.Status);

        Assert.Equal(ProductionOrderErrors.ActualQuantityInvalid,
            Assert.Throws<ProductionOrderRuleException>(() => order.RecordActuals(-1m, 0m, null)).Code);

        order.Complete(5m, 0.25m, "Done", new DateTime(2026, 3, 2, 17, 0, 0, DateTimeKind.Utc));
        Assert.Equal(ProductionOrderStatus.Completed, order.Status);
        Assert.Equal(5m, order.ActualProducedQuantity);

        Assert.Equal(ProductionOrderErrors.TransitionInvalid,
            Assert.Throws<ProductionOrderRuleException>(() =>
                order.Cancel("Late", new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc))).Code);
    }

    [Fact]
    public async Task P02_P03_P04_site_isolation_plant_context_and_immutable_bom_snapshot()
    {
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var orderStore = new OrderStoreStub();
        orderStore.Sites.Add(new ProductionOrderSiteInfo(10, 1, "S1", "Tehran Plant", true, true));
        orderStore.Sites.Add(new ProductionOrderSiteInfo(20, 1, "S2", "Isfahan Plant", true, true));

        var bomStore = new BomStoreStub();
        bomStore.Datasets[100] = new BomResolutionDataset(
            100, "FG-FRAME", "Steel Frame", 1, "PCS", "pcs", true,
            [
                Candidate(1, "BOM-S1", 101, 1, "S1", 2m),
                Candidate(2, "BOM-S2", 102, 1, "S2", 3m)
            ],
            []);

        var site1Scope = new OrganizationScope.Site(1, 10);
        var useCases = new ProductionOrderUseCases(
            orderStore,
            bomStore,
            new ActorStub(actor),
            new PermissionStub((scope, perm) => scope is OrganizationScope.Site s && s.SiteId == 10 && perm.StartsWith("ProductionOrders.")));

        // P02: Site-1 operator cannot create or read Site-2 order
        var crossSite = await useCases.CreateDraftOrder(site1Scope, new CreateProductionOrderRequest(1, 20, "PO-S2", 100, 10m, 1, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 2)));
        Assert.False(crossSite.Succeeded);
        Assert.Equal(ProductionOrderErrors.AccessDenied, crossSite.Error!.Code);

        // P03: PLANT = "S1" injected automatically from Site.Code
        var created = await useCases.CreateDraftOrder(site1Scope, new CreateProductionOrderRequest(1, 10, "PO-S1-1", 100, 10m, 1, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 2)));
        Assert.True(created.Succeeded);
        Assert.Equal("BOM-S1", created.Value!.Summary.BomCodeSnapshot);
        Assert.Equal(1, created.Value.Summary.BomVersionNumberSnapshot);
        Assert.Equal("S1", created.Value.Contexts.Single(c => c.DimensionCode == "PLANT").DimensionValue);
        Assert.Equal(22m, created.Value.Inputs.Single().GrossQuantity); // 10 * 2 * 1.10

        // P04: BOM v2 replaces v1 -> Draft refreshes to v2; then Release freezes v2 permanently
        bomStore.Datasets[100] = new BomResolutionDataset(
            100, "FG-FRAME", "Steel Frame", 1, "PCS", "pcs", true,
            [
                Candidate(1, "BOM-S1", 103, 2, "S1", 5m),
                Candidate(2, "BOM-S2", 102, 1, "S2", 3m)
            ],
            []);

        var refreshed = await useCases.RefreshDraftSnapshot(site1Scope, new RefreshProductionOrderSnapshotRequest(1, created.Value.Summary.Id, created.Value.Summary.RowVersion));
        Assert.True(refreshed.Succeeded);
        Assert.Equal(2, refreshed.Value!.Summary.BomVersionNumberSnapshot);
        Assert.Equal(55m, refreshed.Value.Inputs.Single().GrossQuantity); // 10 * 5 * 1.10

        var released = await useCases.ReleaseOrder(site1Scope, new ReleaseProductionOrderRequest(1, refreshed.Value.Summary.Id, refreshed.Value.Summary.RowVersion));
        Assert.True(released.Succeeded);
        Assert.Equal(ProductionOrderStatus.Released, released.Value!.Summary.Status);

        // Even after BOM updates to v3, RefreshDraftSnapshot fails with OrderImmutable & stored snapshot remains v2
        bomStore.Datasets[100] = new BomResolutionDataset(
            100, "FG-FRAME", "Steel Frame", 1, "PCS", "pcs", true,
            [Candidate(1, "BOM-S1", 104, 3, "S1", 99m)],
            []);
        var refreshAfterRelease = await useCases.RefreshDraftSnapshot(site1Scope, new RefreshProductionOrderSnapshotRequest(1, released.Value.Summary.Id, released.Value.Summary.RowVersion));
        Assert.False(refreshAfterRelease.Succeeded);
        Assert.Equal(ProductionOrderErrors.OrderImmutable, refreshAfterRelease.Error!.Code);

        var fetched = await useCases.GetOrder(site1Scope, 1, released.Value.Summary.Id);
        Assert.True(fetched.Succeeded);
        Assert.Equal(2, fetched.Value!.Summary.BomVersionNumberSnapshot);
        Assert.Equal(55m, fetched.Value.Inputs.Single().GrossQuantity);
    }

    [Fact]
    public async Task P05_P06_validation_permissions_and_routing_contract_hold()
    {
        Assert.True(PermissionCatalog.TryGet(ProductionOrderPermissions.ProductionOrdersRead, out var readDesc));
        Assert.Equal(PermissionScopeApplicability.OrganizationOrSite, readDesc.Applicability);
        Assert.True(PermissionCatalog.TryGet(ProductionOrderPermissions.ProductionOrdersManage, out var manageDesc));
        Assert.Equal(PermissionScopeApplicability.OrganizationOrSite, manageDesc.Applicability);
        Assert.Contains(PermissionModule.ProductionOrders, RolePermissionLocalization.TenantModules);

        var routes = typeof(ProductionOrders).GetCustomAttributes<RouteAttribute>().Select(a => a.Template).ToArray();
        Assert.Contains("/production-orders", routes);
        Assert.Contains("/organizations/{OrganizationId:long}/production-orders", routes);

        // Empty cancel reason rejected
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var useCases = new ProductionOrderUseCases(new OrderStoreStub(), new BomStoreStub(), new ActorStub(actor), new PermissionStub((_, _) => true));
        var badCancel = await useCases.CancelOrder(new OrganizationScope.Organization(1), new CancelProductionOrderRequest(1, 100, "  ", Convert.ToBase64String(new byte[8])));
        Assert.False(badCancel.Succeeded);
        Assert.Equal(ProductionOrderErrors.CancellationReasonInvalid, badCancel.Error!.Code);
    }
}
