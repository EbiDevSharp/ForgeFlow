using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ReservationTests
{
    private sealed class ActorStub(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult(actor);
    }

    private sealed class PermissionStub(Func<OrganizationScope, string, bool> predicate) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(predicate(scope, permission)));
    }

    private sealed class ReservationStoreStub : IStockReservationStore
    {
        public List<StockReservationSummary> Saved { get; } = [];
        public decimal? LastRequestedBaseQuantity { get; private set; }
        public long? LastBaseUnitId { get; private set; }

        public Task<OrganizationPage<StockReservationSummary>> ListReservations(long organizationId, ReservationListQuery query, CancellationToken ct) =>
            Task.FromResult(new OrganizationPage<StockReservationSummary>(Saved, Saved.Count, query.Page, query.PageSize));

        public Task<IReadOnlyList<StockReservationSummary>> ListOrderReservations(long organizationId, long productionOrderId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StockReservationSummary>>(Saved.Where(x => x.ProductionOrderId == productionOrderId).ToArray());

        public Task<StockReservationSummary?> GetReservation(long organizationId, long reservationId, CancellationToken ct) =>
            Task.FromResult(Saved.FirstOrDefault(x => x.Id == reservationId));

        public Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveStock(
            ReserveStockRequest request,
            decimal requestedBaseQuantity,
            long baseUnitId,
            DateTime utcNow,
            OrganizationWriteAccess access,
            CancellationToken ct)
        {
            LastRequestedBaseQuantity = requestedBaseQuantity;
            LastBaseUnitId = baseUnitId;
            var item = new StockReservationSummary(
                10, request.OrganizationId, 1, "SITE-1", "Main Site",
                request.WarehouseId ?? 2, "WH-1", "Main WH",
                request.ProductionOrderId, "MO-100", request.ProductionOrderInputId,
                request.ItemId, "RAW-1", "Steel Bar", request.LotCode ?? string.Empty,
                baseUnitId, "KG", "kg", requestedBaseQuantity, 0m, requestedBaseQuantity,
                ReservationStatus.Active, request.Notes, utcNow, null, "AQ==");
            Saved.Add(item);
            return Task.FromResult(OrganizationResult<IReadOnlyList<StockReservationSummary>>.Success([item]));
        }

        public Task<OrganizationResult<StockReservationSummary>> ReleaseReservation(
            long organizationId, long reservationId, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
        {
            var existing = Saved.FirstOrDefault(x => x.Id == reservationId);
            if (existing is null) return Task.FromResult(OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ReservationNotFound));
            var updated = existing with { RemainingReservedBaseQuantity = 0m, Status = ReservationStatus.Released, ReleasedAtUtc = utcNow };
            return Task.FromResult(OrganizationResult<StockReservationSummary>.Success(updated));
        }
    }

    private sealed class InventoryLookupStub : IInventoryOperationsStore
    {
        public Dictionary<long, InventoryItemConversionContext> ItemContexts { get; } = [];

        public Task<OrganizationPage<WarehouseSummary>> ListWarehouses(long organizationId, WarehouseListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<WarehouseSummary?> GetWarehouse(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> CreateWarehouse(CreateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> UpdateWarehouse(long organizationId, long id, byte[] version, UpdateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> SetWarehouseActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<InventoryBalanceSummary>> ListBalances(long organizationId, InventoryBalanceListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<InventoryTransactionSummary>> ListTransactions(long organizationId, InventoryTransactionListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<InventoryTransactionDetails?> GetTransaction(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<InventoryWarehouseLookup?> GetWarehouseLookup(long organizationId, long warehouseId, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyDictionary<long, InventoryItemConversionContext>> LoadItemUnitContexts(long organizationId, IReadOnlyCollection<long> itemIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<long, InventoryItemConversionContext>>(itemIds.Where(ItemContexts.ContainsKey).ToDictionary(x => x, x => ItemContexts[x]));

        public Task<IReadOnlyHashSetLike> LoadActiveUnitIds(long organizationId, IReadOnlyCollection<long> unitIds, CancellationToken ct) =>
            Task.FromResult(new IReadOnlyHashSetLike(unitIds));

        public Task<InventoryProductionOrderLookup?> GetProductionOrderLookup(long organizationId, long productionOrderId, CancellationToken ct) =>
            Task.FromResult<InventoryProductionOrderLookup?>(new InventoryProductionOrderLookup(
                productionOrderId, organizationId, 1, ProductionOrderStatus.Released, "MO-100"));
        public Task<OrganizationResult<InventoryTransactionDetails>> PostTransaction(PostInventoryTransactionRequest request, long siteId, IReadOnlyList<ResolvedInventoryLineWrite> resolvedLines, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<InventoryTransactionDetails>> ReverseTransaction(long organizationId, long transactionId, string reversalDocumentNumber, string? referenceNote, string? idempotencyKey, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class ReportingStub(ShortageDataset dataset) : IReportingQueryStore
    {
        public Task<IReadOnlyList<ReportSiteInfo>> ListAuthorizedSites(long organizationId, long? forcedSiteId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ReportSiteInfo>>([new ReportSiteInfo(1, "S1", "Main Plant")]);

        public Task<ShortageDataset> GetShortageDataset(long organizationId, long? siteId, DateOnly? dueFrom, DateOnly? dueTo, CancellationToken ct) =>
            Task.FromResult(dataset);

        public Task<VarianceDataset> GetVarianceDataset(long organizationId, long? siteId, long? productionOrderId, ProductionOrderStatus? status, DateOnly? dueFrom, DateOnly? dueTo, string? search, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    [Fact]
    public void Domain_balance_and_reservation_enforce_available_stock_and_status_lifecycle()
    {
        var now = DateTime.UtcNow;
        var balance = InventoryBalance.CreateZero(1, 1, 2, 100, "", 1, now);
        balance.ApplyDelta(100m, now);
        balance.BlockQuantity(20m, now);
        Assert.Equal(80m, balance.AvailableBaseQuantity);

        balance.ReserveQuantity(35m, now);
        Assert.Equal(35m, balance.ReservedBaseQuantity);
        Assert.Equal(45m, balance.AvailableBaseQuantity);

        var exOver = Assert.Throws<InventoryRuleException>(() => balance.ReserveQuantity(46m, now));
        Assert.Equal(InventoryErrors.ReservationExceedsAvailable, exOver.Code);

        var res = StockReservation.Create(1, 1, 2, 50, null, 100, "L1", 1, 35m, "alloc");
        Assert.Equal(ReservationStatus.Active, res.Status);
        var consumed = res.Consume(20m);
        Assert.Equal(20m, consumed);
        Assert.Equal(15m, res.RemainingReservedBaseQuantity);
        Assert.Equal(ReservationStatus.PartiallyConsumed, res.Status);

        var released = res.Release(now);
        Assert.Equal(15m, released);
        Assert.Equal(0m, res.RemainingReservedBaseQuantity);
        Assert.Equal(ReservationStatus.Released, res.Status);

        balance.ReleaseReservedQuantity(res.ConsumedBaseQuantity + released, now);
        Assert.Equal(0m, balance.ReservedBaseQuantity);
    }

    [Fact]
    public async Task R01_ReserveStock_converts_units_to_base_and_enforces_permissions()
    {
        var store = new ReservationStoreStub();
        var inv = new InventoryLookupStub();
        inv.ItemContexts[100] = new InventoryItemConversionContext(
            100, true, 1,
            [new UnitConversionEdge(2, 1, 25m)]);
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var denied = new StockReservationUseCases(store, inv, new ActorStub(actor), new PermissionStub((_, _) => false));

        var deniedRes = await denied.ReserveStock(new OrganizationScope.Organization(1),
            new ReserveStockRequest(1, 50, null, 100, 2m, 2, null, null, null));
        Assert.False(deniedRes.Succeeded);
        Assert.Equal(InventoryErrors.AccessDenied, deniedRes.Error!.Code);

        var allowed = new StockReservationUseCases(store, inv, new ActorStub(actor),
            new PermissionStub((_, p) => p == ProductionOrderPermissions.ProductionOrdersManage));
        var okRes = await allowed.ReserveStock(new OrganizationScope.Organization(1),
            new ReserveStockRequest(1, 50, 7, 100, 2m, 2, 3, "LOT-A", "note"));
        Assert.True(okRes.Succeeded);
        Assert.Equal(50m, store.LastRequestedBaseQuantity);
        Assert.Equal(1L, store.LastBaseUnitId);
    }

    [Fact]
    public async Task R01_MaterialShortageReport_deducts_reserved_quantity_and_reports_total_reserved()
    {
        var dataset = new ShortageDataset(
            Orders: [new ReportOpenOrderRow(10, "MO-10", 1, "S1", new DateOnly(2026, 11, 20), ProductionOrderStatus.Released)],
            Inputs: [new ReportOpenOrderInputRow(10, 100, 80m, 1)],
            Balances: [new ReportStockBalanceRow(1, 100, OnHandBaseQuantity: 100m, BlockedBaseQuantity: 10m, ReservedBaseQuantity: 45m)],
            Items: [new ReportItemRef(100, "RAW-1", "Steel Bar", 1, "KG", "kg")],
            Conversions: []);
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var svc = new ReportingUseCases(new ReportingStub(dataset), new ActorStub(actor), new PermissionStub((_, p) => p == ReportingPermissions.ReportsRead));

        var res = await svc.GetMaterialShortageReport(new OrganizationScope.Organization(1), 1, new MaterialShortageQuery(OnlyShortages: false));
        Assert.True(res.Succeeded);
        var report = res.Value!;
        Assert.Equal(45m, report.TotalReservedBaseQuantity);
        var item = Assert.Single(report.Items);
        Assert.Equal(100m, item.OnHandBaseQuantity);
        Assert.Equal(10m, item.BlockedBaseQuantity);
        Assert.Equal(45m, item.ReservedBaseQuantity);
        Assert.Equal(45m, item.AvailableBaseQuantity);
        Assert.Equal(35m, item.ShortageBaseQuantity);
    }
}
