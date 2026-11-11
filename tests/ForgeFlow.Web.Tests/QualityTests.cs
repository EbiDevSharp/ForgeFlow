using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Units;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class QualityTests
{
    private static readonly TrustedActor Actor1 = new(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
    private static readonly OrganizationScope.Organization OrgScope = new(10);
    private static readonly OrganizationScope.Site Site1Scope = new(10, 1);

    [Fact]
    public void Q01_item_policies_and_inventory_balance_blocking_enforce_available_stock_invariants()
    {
        var item = Item.Create(10, "OIL-RAW", "Crude Oil", null, 1, true, false, false, true,
            requiresLotTracking: true, qualityControlMode: QualityControlMode.IncomingReceipts);
        Assert.True(item.RequiresLotTracking);
        Assert.Equal(QualityControlMode.IncomingReceipts, item.QualityControlMode);
        Assert.Throws<ItemRuleException>(() => Item.Create(10, "BAD", "Bad", null, 1, true, false, false, true, false, (QualityControlMode)99));

        var now = DateTime.UtcNow;
        var bal = InventoryBalance.CreateZero(10, 1, 5, 100, "LOT-A1", 1, now);
        bal.ApplyDelta(50m, now);
        Assert.Equal(50m, bal.AvailableBaseQuantity);

        bal.BlockQuantity(30m, now);
        Assert.Equal(30m, bal.BlockedBaseQuantity);
        Assert.Equal(20m, bal.AvailableBaseQuantity);

        // Consuming 25 when only 20 is available (30 blocked) must fail
        Assert.Throws<InventoryRuleException>(() => bal.ApplyDelta(-25m, now));
        // Consuming 20 succeeds
        bal.ApplyDelta(-20m, now);
        Assert.Equal(30m, bal.OnHandBaseQuantity);
        Assert.Equal(0m, bal.AvailableBaseQuantity);

        // Partial release 10 + scrap 20
        bal.ReleaseBlockedQuantity(10m, now);
        Assert.Equal(10m, bal.AvailableBaseQuantity);
        bal.ScrapBlockedQuantity(20m, now);
        Assert.Equal(10m, bal.OnHandBaseQuantity);
        Assert.Equal(0m, bal.BlockedBaseQuantity);
        Assert.Equal(10m, bal.AvailableBaseQuantity);
    }

    [Fact]
    public void Q02_quality_inspection_partial_disposition_and_item_lot_date_invariants_hold()
    {
        var now = DateTime.UtcNow;
        var qi = QualityInspection.Create(10, 1, 5, 100, "LOT-1", "QC-001",
            QualityInspectionSourceType.Receipt, 11, null, 1, 100m, "Initial receipt");

        qi.ReplaceLines([("FFA %", "<= 0.1", "0.08", true, null)]);
        Assert.Equal(QualityInspectionStatus.InReview, qi.Status);

        // Cannot cancel once partial disposition starts
        qi.RecordDisposition(60m, 10m, "HIGH_ACID", "Partial release", 1, now);
        Assert.Equal(QualityInspectionStatus.InReview, qi.Status);
        Assert.Equal(30m, qi.RemainingBlockedBaseQuantity);
        Assert.Throws<QualityRuleException>(() => qi.Cancel("Try cancel", 1, now));
        Assert.Throws<QualityRuleException>(() => qi.RecordDisposition(35m, 0m, null, null, 1, now));

        qi.RecordDisposition(30m, 0m, null, "Final release", 1, now);
        Assert.Equal(QualityInspectionStatus.Completed, qi.Status);
        Assert.Equal(0m, qi.RemainingBlockedBaseQuantity);
        Assert.Throws<QualityRuleException>(() => qi.RecordDisposition(1m, 0m, null, null, 1, now));

        Assert.Throws<QualityRuleException>(() =>
            ItemLot.Create(10, 100, "LOT-1", new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 9)));
        var lot = ItemLot.Create(10, 100, "LOT-1", new DateOnly(2026, 11, 1), new DateOnly(2027, 11, 1));
        Assert.False(lot.IsExpiredOn(new DateOnly(2027, 11, 1)));
        Assert.True(lot.IsExpiredOn(new DateOnly(2027, 11, 2)));
    }

    [Fact]
    public async Task Q03_inventory_use_cases_require_lot_when_item_requires_lot_tracking_and_flag_qc_receipts()
    {
        var invStore = new CapturingInventoryStore
        {
            ItemContexts = new Dictionary<long, InventoryItemConversionContext>
            {
                [100] = new(100, true, 1, [], RequiresLotTracking: true, QualityControlMode: QualityControlMode.IncomingReceipts)
            }
        };
        var useCases = new InventoryUseCases(invStore, new FixedActor(Actor1), new AllowAllPermissions());

        // Missing LotCode fails cleanly
        var missingLot = await useCases.PostTransaction(Site1Scope, new PostInventoryTransactionRequest(
            10, 5, null, "RCV-1", InventoryTransactionType.Receipt, null, null, null,
            [new InventoryLineInput(100, "", 10m, 1)]));
        Assert.False(missingLot.Succeeded);
        Assert.Equal(InventoryErrors.LotRequired, missingLot.Error!.Code);

        // With LotCode, Receipt triggers QC flag
        var ok = await useCases.PostTransaction(Site1Scope, new PostInventoryTransactionRequest(
            10, 5, null, "RCV-2", InventoryTransactionType.Receipt, null, null, null,
            [new InventoryLineInput(100, "LOT-99", 10m, 1)]));
        Assert.True(ok.Succeeded);
        var writtenLine = Assert.Single(invStore.LastPostedLines!);
        Assert.True(writtenLine.TriggersQualityControl);
        Assert.Equal("LOT-99", writtenLine.LotCode);
    }

    [Fact]
    public async Task Q04_quality_use_cases_enforce_permissions_site_isolation_and_rejection_audit()
    {
        var recorder = new CapturingOperationRecorder();
        var qualityStore = new StubQualityStore();
        var readOnlyUseCases = new QualityUseCases(
            qualityStore,
            new FixedActor(Actor1),
            new FixedPermissions(QualityPermissions.QualityRead),
            recorder);

        // Warehouse/Read-only user without Quality.Manage cannot create hold or record disposition
        var deniedHold = await readOnlyUseCases.CreateManualHold(Site1Scope,
            new CreateManualQualityHoldRequest(10, 5, 100, "LOT-1", "QCH-1", 5m));
        Assert.False(deniedHold.Succeeded);
        Assert.Equal(QualityErrors.AccessDenied, deniedHold.Error!.Code);
        Assert.Contains(recorder.Entries, e => e.OperationCode == UserOperationCatalog.QualityActionRejected && e.Outcome == UserOperationOutcome.AccessDenied);

        // Site 1 cannot view Site 2 inspection
        qualityStore.StoredInspectionSiteId = 2;
        var crossSite = await readOnlyUseCases.GetInspection(Site1Scope, 10, 55);
        Assert.False(crossSite.Succeeded);
        Assert.Equal(QualityErrors.AccessDenied, crossSite.Error!.Code);

        // Built-in permissions include Quality.Read & Quality.Manage at both Org and Site scopes
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(QualityPermissions.QualityRead, 1));
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(QualityPermissions.QualityManage, 1));
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(QualityPermissions.QualityRead, 2));
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(QualityPermissions.QualityManage, 2));
    }

    [Fact]
    public void Q05_material_shortage_uses_available_stock_and_reports_blocked_quantity()
    {
        var data = new ShortageDataset(
            Orders: [new ReportOpenOrderRow(1, "PO-1", 1, "S1", new DateOnly(2026, 11, 20), ProductionOrderStatus.Released)],
            Inputs: [new ReportOpenOrderInputRow(1, 100, 100m, 1)],
            Balances: [new ReportStockBalanceRow(1, 100, OnHandBaseQuantity: 90m, BlockedBaseQuantity: 30m)],
            Items: [new ReportItemRef(100, "OIL", "Refined Oil", 1, "KG", "kg")],
            Conversions: []);

        var report = ReportingUseCases.ComputeMaterialShortage(data, new MaterialShortageQuery());
        var row = Assert.Single(report.Items);
        Assert.Equal(90m, row.OnHandBaseQuantity);
        Assert.Equal(30m, row.BlockedBaseQuantity);
        Assert.Equal(60m, row.AvailableBaseQuantity);
        Assert.Equal(40m, row.ShortageBaseQuantity); // 100 - 60 = 40
        Assert.Equal(60m, row.CoveragePercent);
    }

    [Fact]
    public void Q06_multi_level_genealogy_traces_forward_and_backward_excluding_reversals()
    {
        var t0 = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
        var dataset = new LotGenealogyDataset(
            Orders:
            [
                new LotGenealogyOrderRow(1, "PO-WIP", 1, "S1", ProductionOrderStatus.Completed),
                new LotGenealogyOrderRow(2, "PO-FG", 1, "S1", ProductionOrderStatus.Completed)
            ],
            Movements:
            [
                // Raw oil LOT-RAW consumed in PO-WIP -> WIP LOT-WIP
                new LotGenealogyMovementRow(10, InventoryTransactionType.ProductionIssue, null, null, 1, "PO-WIP", 1, "S1", 5, "WH1", "ISS-1", 100, "RAW", "Crude", "LOT-RAW", -50m, "kg", t0, null),
                // Reversed wrong issue should be excluded
                new LotGenealogyMovementRow(11, InventoryTransactionType.ProductionIssue, null, null, 1, "PO-WIP", 1, "S1", 5, "WH1", "ISS-BAD", 101, "WRONG", "Wrong", "LOT-BAD", -10m, "kg", t0, null),
                new LotGenealogyMovementRow(12, InventoryTransactionType.Reversal, 11, null, 1, "PO-WIP", 1, "S1", 5, "WH1", "REV-BAD", 101, "WRONG", "Wrong", "LOT-BAD", 10m, "kg", t0, null),
                new LotGenealogyMovementRow(13, InventoryTransactionType.ProductionReceipt, null, null, 1, "PO-WIP", 1, "S1", 5, "WH1", "REC-1", 200, "WIP", "Refined", "LOT-WIP", 48m, "kg", t0, null),
                // WIP LOT-WIP consumed in PO-FG -> FG LOT-FG
                new LotGenealogyMovementRow(14, InventoryTransactionType.ProductionIssue, null, null, 2, "PO-FG", 1, "S1", 5, "WH1", "ISS-2", 200, "WIP", "Refined", "LOT-WIP", -48m, "kg", t0, null),
                new LotGenealogyMovementRow(15, InventoryTransactionType.ProductionReceipt, null, null, 2, "PO-FG", 1, "S1", 5, "WH1", "REC-2", 300, "FG", "Bottle 800g", "LOT-FG", 60m, "pcs", t0, null)
            ],
            Lots: [new ItemLotSummary(1, 10, 300, "FG", "Bottle 800g", "LOT-FG", new DateOnly(2026, 11, 10), new DateOnly(2027, 11, 10), false, false, null, "AQAAAAAAAAA=")],
            Balances: [],
            Inspections: []);

        // Trace backward from FG LOT-FG: reaches LOT-WIP (Level 1) and LOT-RAW (Level 2), excludes LOT-BAD!
        var backward = QualityUseCases.ComputeGenealogy(dataset, 300, "LOT-FG", LotTraceDirection.Backward);
        Assert.NotNull(backward);
        Assert.Equal(2, backward!.BackwardEdges.Count);
        Assert.Contains(backward.BackwardEdges, e => e.Level == 1 && e.SourceLotCode == "LOT-WIP" && e.TargetLotCode == "LOT-FG");
        Assert.Contains(backward.BackwardEdges, e => e.Level == 2 && e.SourceLotCode == "LOT-RAW" && e.TargetLotCode == "LOT-WIP");
        Assert.DoesNotContain(backward.BackwardEdges, e => e.SourceLotCode == "LOT-BAD");

        // Trace forward from RAW LOT-RAW: reaches LOT-WIP (Level 1) and LOT-FG (Level 2)
        var forward = QualityUseCases.ComputeGenealogy(dataset, 100, "LOT-RAW", LotTraceDirection.Forward);
        Assert.NotNull(forward);
        Assert.Equal(2, forward!.ForwardEdges.Count);
        Assert.Contains(forward.ForwardEdges, e => e.Level == 1 && e.TargetLotCode == "LOT-WIP");
        Assert.Contains(forward.ForwardEdges, e => e.Level == 2 && e.TargetLotCode == "LOT-FG");
    }

    private sealed class FixedActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class AllowAllPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(true));
    }

    private sealed class FixedPermissions(params string[] allowed) : IOrganizationPermissionEvaluator
    {
        private readonly HashSet<string> set = [.. allowed];
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(set.Contains(permission)));
    }

    private sealed class CapturingOperationRecorder : IUserOperationRecorder
    {
        public List<UserOperationRecordRequest> Entries { get; } = [];
        public Task RecordAsync(UserOperationRecordRequest request, CancellationToken ct = default)
        {
            Entries.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingInventoryStore : IInventoryOperationsStore
    {
        public Dictionary<long, InventoryItemConversionContext> ItemContexts { get; init; } = [];
        public IReadOnlyList<ResolvedInventoryLineWrite>? LastPostedLines { get; private set; }

        public Task<InventoryWarehouseLookup?> GetWarehouseLookup(long organizationId, long warehouseId, CancellationToken ct) =>
            Task.FromResult<InventoryWarehouseLookup?>(new(warehouseId, organizationId, 1, "S1", true, true, true));

        public Task<IReadOnlyDictionary<long, InventoryItemConversionContext>> LoadItemUnitContexts(long organizationId, IReadOnlyCollection<long> itemIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<long, InventoryItemConversionContext>>(ItemContexts);

        public Task<IReadOnlyHashSetLike> LoadActiveUnitIds(long organizationId, IReadOnlyCollection<long> unitIds, CancellationToken ct) =>
            Task.FromResult(new IReadOnlyHashSetLike(unitIds));

        public Task<OrganizationResult<InventoryTransactionDetails>> PostTransaction(
            PostInventoryTransactionRequest request, long siteId, IReadOnlyList<ResolvedInventoryLineWrite> resolvedLines,
            DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
        {
            LastPostedLines = resolvedLines;
            var summary = new InventoryTransactionSummary(1, request.OrganizationId, siteId, "S1", "Site 1",
                request.WarehouseId, "WH1", "Warehouse 1", null, null, request.DocumentNumber ?? "DOC",
                request.TransactionType, request.ProductionOrderId, null, null, false, request.ReferenceNote, utcNow, resolvedLines.Count);
            return Task.FromResult(OrganizationResult<InventoryTransactionDetails>.Success(new InventoryTransactionDetails(summary, [])));
        }

        public Task<OrganizationPage<WarehouseSummary>> ListWarehouses(long organizationId, WarehouseListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<WarehouseSummary?> GetWarehouse(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> CreateWarehouse(CreateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> UpdateWarehouse(long organizationId, long id, byte[] version, UpdateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> SetWarehouseActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<InventoryBalanceSummary>> ListBalances(long organizationId, InventoryBalanceListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<InventoryTransactionSummary>> ListTransactions(long organizationId, InventoryTransactionListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<InventoryTransactionDetails?> GetTransaction(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<InventoryProductionOrderLookup?> GetProductionOrderLookup(long organizationId, long productionOrderId, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<InventoryTransactionDetails>> ReverseTransaction(long organizationId, long transactionId, string reversalDocumentNumber, string? referenceNote, string? idempotencyKey, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubQualityStore : IQualityOperationsStore
    {
        public long StoredInspectionSiteId { get; set; } = 1;

        public Task<QualityInspectionDetails?> GetInspection(long organizationId, long id, CancellationToken ct)
        {
            var s = new QualityInspectionSummary(id, organizationId, StoredInspectionSiteId, "S", "Site", 5, "WH", "WH",
                100, "IT", "Item", "LOT-1", "QC-1", QualityInspectionSourceType.ManualHold, null, null, null, null,
                1, "KG", "kg", 10m, 0m, 0m, 10m, QualityInspectionStatus.Pending, null, null, null, null, null, "AQAAAAAAAAA=");
            return Task.FromResult<QualityInspectionDetails?>(new QualityInspectionDetails(s, []));
        }

        public Task<OrganizationPage<QualityInspectionSummary>> ListInspections(long organizationId, QualityInspectionListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<QualityInspectionBalanceTarget?> GetHoldTargetLookup(long organizationId, long warehouseId, long itemId, string lotCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<UnitConversionEdge>> LoadItemUnitEdges(long organizationId, long itemId, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<QualityInspectionDetails>> CreateManualHold(CreateManualQualityHoldRequest request, long siteId, string inspectionNumber, string normalizedLotCode, long baseUnitId, decimal holdBaseQuantity, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<QualityInspectionDetails>> UpdateMeasurements(long organizationId, long inspectionId, byte[] version, string? defectReasonCode, string? notes, IReadOnlyList<QualityInspectionLineInput> lines, OrganizationWriteAccess access, CancellationToken ct, string? certificateNumber = null) => throw new NotImplementedException();
        public Task<OrganizationResult<QualityInspectionDetails>> RecordDisposition(long organizationId, long inspectionId, byte[] version, decimal acceptBaseQuantity, decimal rejectBaseQuantity, string? defectReasonCode, string? notes, string? scrapDocumentNumber, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<QualityInspectionDetails>> CancelHold(long organizationId, long inspectionId, byte[] version, string? notes, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<ItemLotSummary>> ListItemLots(long organizationId, ItemLotListQuery query, DateOnly today, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ItemLotSummary>> UpsertItemLotMetadata(UpsertItemLotMetadataRequest request, DateOnly today, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<LotGenealogyDataset> GetGenealogyDataset(long organizationId, long? forcedSiteId, DateOnly today, CancellationToken ct) => throw new NotImplementedException();
    }
}
