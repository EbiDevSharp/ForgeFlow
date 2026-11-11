using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Dashboard;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Routings;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ReservationSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    private sealed class FixedActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class AllowPermissions(Func<string, bool> predicate) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default) =>
            ValueTask.FromResult(new PermissionDecision(predicate(permission)));
    }

    [OrganizationSqlServerFact]
    public async Task S01_Reservation_lifecycle_protects_stock_consumes_on_issue_and_releases_on_cancel_and_complete()
    {
        await using var database = await DisposableReservationDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var moStore = new SqlProductionOrderOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var resStore = new SqlStockReservationStore(database.Factory);
        var rptStore = new SqlReportingQueryStore(database.Factory);

        long orgId, siteId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-RES", "Reservation Org");
            var site = org.CreateSite("SITE-1", "Main Plant");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(Attribution());
            orgId = org.Id;
            siteId = site.Id;
        }

        var access = new OrganizationWriteAccess(Attribution(), _ => ValueTask.FromResult(true));
        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var units = await unitStore.ListActiveUnits(orgId, default);
        var pcs = units.Single(u => u.Code == "PCS");
        var kg = units.Single(u => u.Code == "KG");

        var rawSteel = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "RAW-RES", "Raw Steel", null, kg.Id, true, false, false, true, false, QualityControlMode.None), access, default)).Value!.Summary;
        var fgBracket = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "FG-RES", "Bracket FG", null, pcs.Id, false, true, true, false, false, QualityControlMode.None), access, default)).Value!.Summary;

        var bom = (await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-RES", "Bracket BOM", fgBracket.Id), access, default)).Value!;
        var bv1 = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 1, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, "v1"), access, default)).Value!;
        bv1 = (await bomStore.SaveDraftBomLines(orgId, bv1.Summary.Id, Convert.FromBase64String(bv1.Summary.RowVersion),
            [new BomInputLineInput(1, rawSteel.Id, 5m, kg.Id, 0m, false, null)],
            [new BomOutputLineInput(1, fgBracket.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        bv1 = (await bomStore.TransitionBomVersionStatus(orgId, bv1.Summary.Id, Convert.FromBase64String(bv1.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        bv1 = (await bomStore.TransitionBomVersionStatus(orgId, bv1.Summary.Id, Convert.FromBase64String(bv1.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;

        var wh = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgId, siteId, "WH-MAIN", "Main Warehouse", true), access, default)).Value!;

        var actor = Attribution();
        var allPerms = new AllowPermissions(_ => true);
        var invUc = new InventoryUseCases(invStore, new FixedActor(actor), allPerms);
        var moUc = new ProductionOrderUseCases(moStore, bomStore, new FixedActor(actor), allPerms);
        var resUc = new StockReservationUseCases(resStore, invStore, new FixedActor(actor), allPerms);
        var rptUc = new ReportingUseCases(rptStore, new FixedActor(actor), allPerms);

        var scope = new OrganizationScope.Organization(orgId);

        // Receive 40 kg in LOT-OLD (expires earlier) and 60 kg in LOT-NEW
        var rec1 = await invUc.PostTransaction(scope, new PostInventoryTransactionRequest(
            orgId, wh.Id, null, "REC-1", InventoryTransactionType.Receipt, null, "old lot", null,
            [new InventoryLineInput(rawSteel.Id, "LOT-OLD", 40m, kg.Id, null, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1))]));
        Assert.True(rec1.Succeeded);

        var rec2 = await invUc.PostTransaction(scope, new PostInventoryTransactionRequest(
            orgId, wh.Id, null, "REC-2", InventoryTransactionType.Receipt, null, "new lot", null,
            [new InventoryLineInput(rawSteel.Id, "LOT-NEW", 60m, kg.Id, null, new DateOnly(2026, 6, 1), new DateOnly(2027, 12, 1))]));
        Assert.True(rec2.Succeeded);

        // Create and Release MO-1 (needs 50 kg) and MO-2 (needs 60 kg)
        var mo1 = (await moUc.CreateDraftOrder(scope, new CreateProductionOrderRequest(
            orgId, siteId, "MO-1", fgBracket.Id, 10m, pcs.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 15), null, null))).Value!;
        mo1 = (await moUc.ReleaseOrder(scope, new ReleaseProductionOrderRequest(orgId, mo1.Summary.Id, mo1.Summary.RowVersion))).Value!;

        var mo2 = (await moUc.CreateDraftOrder(scope, new CreateProductionOrderRequest(
            orgId, siteId, "MO-2", fgBracket.Id, 12m, pcs.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 16), null, null))).Value!;
        mo2 = (await moUc.ReleaseOrder(scope, new ReleaseProductionOrderRequest(orgId, mo2.Summary.Id, mo2.Summary.RowVersion))).Value!;

        // FIFO Reserve 50 kg for MO-1 -> 40 kg from LOT-OLD + 10 kg from LOT-NEW
        var reserve1 = await resUc.ReserveStock(scope, new ReserveStockRequest(orgId, mo1.Summary.Id, mo1.Inputs[0].Id, rawSteel.Id, 50m, kg.Id, null, null, "FIFO alloc"));
        Assert.True(reserve1.Succeeded);
        Assert.Equal(2, reserve1.Value!.Count);
        Assert.Equal("LOT-OLD", reserve1.Value[0].LotCode);
        Assert.Equal(40m, reserve1.Value[0].ReservedBaseQuantity);
        Assert.Equal("LOT-NEW", reserve1.Value[1].LotCode);
        Assert.Equal(10m, reserve1.Value[1].ReservedBaseQuantity);

        // Unreserved available on LOT-NEW is now 50 kg -> ProductionIssue of 55 kg for MO-2 MUST FAIL with InsufficientStock
        var badIssue = await invUc.PostTransaction(scope, new PostInventoryTransactionRequest(
            orgId, wh.Id, null, "ISS-BAD", InventoryTransactionType.ProductionIssue, mo2.Summary.Id, null, null,
            [new InventoryLineInput(rawSteel.Id, "LOT-NEW", 55m, kg.Id, null, null)]));
        Assert.False(badIssue.Succeeded);
        Assert.Equal(InventoryErrors.InsufficientStock, badIssue.Error!.Code);

        // MaterialShortageReport should see TotalReservedBaseQuantity = 50 kg and unreserved AvailableBaseQuantity = 50 kg
        var shortage = await rptUc.GetMaterialShortageReport(scope, orgId, new MaterialShortageQuery(OnlyShortages: false));
        Assert.True(shortage.Succeeded);
        Assert.Equal(50m, shortage.Value!.TotalReservedBaseQuantity);
        var rawShortage = Assert.Single(shortage.Value.Items);
        Assert.Equal(50m, rawShortage.ReservedBaseQuantity);
        Assert.Equal(50m, rawShortage.AvailableBaseQuantity);
        Assert.Equal(60m, rawShortage.ShortageBaseQuantity); // 110 required - 50 available

        // Post ProductionIssue of 30 kg on LOT-OLD for MO-1 -> consumes 30 kg from LOT-OLD reservation
        var goodIssue = await invUc.PostTransaction(scope, new PostInventoryTransactionRequest(
            orgId, wh.Id, null, "ISS-MO1", InventoryTransactionType.ProductionIssue, mo1.Summary.Id, "issue steel", null,
            [new InventoryLineInput(rawSteel.Id, "LOT-OLD", 30m, kg.Id, null, null)]));
        Assert.True(goodIssue.Succeeded);

        var mo1ResAfterIssue = await resUc.ListOrderReservations(scope, orgId, mo1.Summary.Id);
        Assert.True(mo1ResAfterIssue.Succeeded);
        var oldLotRes = mo1ResAfterIssue.Value!.Single(r => r.LotCode == "LOT-OLD");
        Assert.Equal(30m, oldLotRes.ConsumedBaseQuantity);
        Assert.Equal(10m, oldLotRes.RemainingReservedBaseQuantity);
        Assert.Equal(ReservationStatus.PartiallyConsumed, oldLotRes.Status);

        // Explicit reserve 20 kg on LOT-NEW for MO-2, then cancel MO-2 -> auto-releases MO-2's reservation
        var reserve2 = await resUc.ReserveStock(scope, new ReserveStockRequest(orgId, mo2.Summary.Id, mo2.Inputs[0].Id, rawSteel.Id, 20m, kg.Id, wh.Id, "LOT-NEW", null));
        Assert.True(reserve2.Succeeded);

        var cancelled = await moUc.CancelOrder(scope, new CancelProductionOrderRequest(orgId, mo2.Summary.Id, "no longer needed", mo2.Summary.RowVersion));
        Assert.True(cancelled.Succeeded);

        var mo2ResAfterCancel = await resUc.ListOrderReservations(scope, orgId, mo2.Summary.Id);
        Assert.True(mo2ResAfterCancel.Succeeded);
        Assert.All(mo2ResAfterCancel.Value!, r =>
        {
            Assert.Equal(ReservationStatus.Released, r.Status);
            Assert.Equal(0m, r.RemainingReservedBaseQuantity);
        });

        // Start & Complete MO-1 -> auto-releases remaining 10 kg on LOT-OLD + 10 kg on LOT-NEW
        mo1 = (await moUc.StartOrder(scope, new StartProductionOrderRequest(orgId, mo1.Summary.Id, mo1.Summary.RowVersion))).Value!;
        var completed = await moUc.CompleteOrder(scope, new CompleteProductionOrderRequest(orgId, mo1.Summary.Id, 10m, 0m, "done", mo1.Summary.RowVersion));
        Assert.True(completed.Succeeded);

        var mo1ResAfterComplete = await resUc.ListOrderReservations(scope, orgId, mo1.Summary.Id);
        Assert.True(mo1ResAfterComplete.Succeeded);
        Assert.All(mo1ResAfterComplete.Value!, r =>
        {
            Assert.Equal(ReservationStatus.Released, r.Status);
            Assert.Equal(0m, r.RemainingReservedBaseQuantity);
        });

        // Balances should now have zero ReservedBaseQuantity across all rows
        var balances = await invUc.ListBalances(scope, orgId, new InventoryBalanceListQuery(IncludeZeroBalances: true));
        Assert.True(balances.Succeeded);
        Assert.All(balances.Value!.Items, b => Assert.Equal(0m, b.ReservedBaseQuantity));
    }

    [OrganizationSqlServerFact]
    public async Task S02_Dashboard_query_store_returns_capability_filtered_live_metrics_and_queues()
    {
        await using var database = await DisposableReservationDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var moStore = new SqlProductionOrderOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var qcStore = new SqlQualityOperationsStore(database.Factory);
        var procStore = new SqlProcurementOperationsStore(database.Factory);
        var rtStore = new SqlRoutingOperationsStore(database.Factory);
        var dashStore = new SqlDashboardQueryStore(database.Factory);

        long orgId, siteId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-DASH", "Dashboard Org");
            var site = org.CreateSite("SITE-D", "Dash Plant");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(Attribution());
            orgId = org.Id;
            siteId = site.Id;
        }

        var access = new OrganizationWriteAccess(Attribution(), _ => ValueTask.FromResult(true));
        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var units = await unitStore.ListActiveUnits(orgId, default);
        var pcs = units.Single(u => u.Code == "PCS");

        var raw = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "RAW-D", "Raw Dash", null, pcs.Id, true, false, false, true, false, QualityControlMode.None), access, default)).Value!.Summary;
        var fg = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "FG-D", "FG Dash", null, pcs.Id, false, true, true, false, false, QualityControlMode.None), access, default)).Value!.Summary;

        var bom = (await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-D", "Dash BOM", fg.Id), access, default)).Value!;
        var bv = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 1, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, null), access, default)).Value!;
        bv = (await bomStore.SaveDraftBomLines(orgId, bv.Summary.Id, Convert.FromBase64String(bv.Summary.RowVersion),
            [new BomInputLineInput(1, raw.Id, 4m, pcs.Id, 0m, false, null)],
            [new BomOutputLineInput(1, fg.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        bv = (await bomStore.TransitionBomVersionStatus(orgId, bv.Summary.Id, Convert.FromBase64String(bv.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        bv = (await bomStore.TransitionBomVersionStatus(orgId, bv.Summary.Id, Convert.FromBase64String(bv.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;

        var actor = Attribution();
        var allPerms = new AllowPermissions(_ => true);
        var scope = new OrganizationScope.Organization(orgId);

        var wh = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgId, siteId, "WH-D", "Dash WH", true), access, default)).Value!;
        var invUc = new InventoryUseCases(invStore, new FixedActor(actor), allPerms);
        Assert.True((await invUc.PostTransaction(scope, new PostInventoryTransactionRequest(
            orgId, wh.Id, null, "REC-D", InventoryTransactionType.Receipt, null, null, null,
            [new InventoryLineInput(raw.Id, "LOT-D", 10m, pcs.Id, null, null)]))).Succeeded);

        // Create WorkCenter + Active Routing -> Overdue Released MO (needs 40 raw, only 10 on hand -> shortage)
        var rtUc = new RoutingUseCases(rtStore, new FixedActor(actor), allPerms);
        var wc = (await rtUc.CreateWorkCenter(scope, new CreateWorkCenterRequest(orgId, siteId, "WC-1", "CNC Cell", WorkCenterType.Machine, 1, 8m, 50m, null))).Value!;
        var rt = (await rtUc.CreateDraftRouting(scope, new CreateRoutingRequest(orgId, siteId, "RT-D", "FG Dash Route", fg.Id, bv.Summary.Id, 1, true, null))).Value!;
        rt = (await rtUc.SaveRoutingOperations(scope, new SaveRoutingOperationsRequest(orgId, rt.Summary.Id,
            [new RoutingOperationInput(10, "OP-10", "Machining", wc.Id, 30m, 15m, 0m, 0m, false, null)], rt.Summary.RowVersion))).Value!;
        await rtUc.ActivateRouting(scope, new ActivateRoutingRequest(orgId, rt.Summary.Id, rt.Summary.RowVersion));

        var moUc = new ProductionOrderUseCases(moStore, bomStore, new FixedActor(actor), allPerms, null, null, rtStore);
        var mo = (await moUc.CreateDraftOrder(scope, new CreateProductionOrderRequest(
            orgId, siteId, "MO-DASH", fg.Id, 10m, pcs.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), null, null, rt.Summary.Id))).Value!;
        await moUc.ReleaseOrder(scope, new ReleaseProductionOrderRequest(orgId, mo.Summary.Id, mo.Summary.RowVersion));

        // Quality hold on raw lot
        var qcUc = new QualityUseCases(qcStore, new FixedActor(actor), allPerms);
        Assert.True((await qcUc.CreateManualHold(scope, new CreateManualQualityHoldRequest(
            orgId, wh.Id, raw.Id, "LOT-D", "QC-D1", 3m, pcs.Id, null, "sample check"))).Succeeded);

        // Overdue approved Purchase Order
        var procUc = new ProcurementUseCases(procStore, new FixedActor(actor), allPerms);
        var sup = (await procUc.CreateSupplier(scope, new CreateSupplierRequest(orgId, "SUP-1", "Alpha Metals", null, null, null, null))).Value!;
        var po = (await procUc.CreateDraft(scope, new CreatePurchaseOrderRequest(
            orgId, siteId, sup.Id, "PO-D1", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 4), wh.Id, "USD", null,
            [new PurchaseOrderLineInput(raw.Id, 25m, pcs.Id, 10m, null)]))).Value!;
        await procUc.ApproveOrder(scope, orgId, po.Summary.Id, po.Summary.RowVersion);

        // Full read permissions snapshot at 2026-11-11
        var dashFull = new DashboardUseCases(dashStore, new FixedActor(actor), allPerms, new FixedTime(new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero)));
        var fullRes = await dashFull.GetSnapshot(scope, orgId);
        Assert.True(fullRes.Succeeded);
        var s = fullRes.Value!;
        Assert.NotNull(s.ProductionStats);
        Assert.Equal(1, s.ProductionStats!.ReleasedCount);
        Assert.Equal(1, s.ProductionStats.OverdueCount);
        Assert.Single(s.AttentionOrders);
        Assert.True(s.AttentionOrders[0].IsOverdue);
        Assert.NotNull(s.ShortageStats);
        Assert.Equal(1, s.ShortageStats!.ShortageItemCount);
        Assert.NotNull(s.QualityStats);
        Assert.Equal(1, s.QualityStats!.PendingCount);
        Assert.Equal(3m, s.QualityStats.TotalBlockedBaseQuantity);
        Assert.NotNull(s.ProcurementStats);
        Assert.Equal(1, s.ProcurementStats!.OverduePoCount);
        Assert.Single(s.WorkCenterLoads);

        // Partial permission snapshot (QualityRead only)
        var dashQcOnly = new DashboardUseCases(dashStore, new FixedActor(actor),
            new AllowPermissions(p => p == QualityPermissions.QualityRead),
            new FixedTime(new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero)));
        var qcOnlyRes = await dashQcOnly.GetSnapshot(scope, orgId);
        Assert.True(qcOnlyRes.Succeeded);
        Assert.Null(qcOnlyRes.Value!.ProductionStats);
        Assert.Empty(qcOnlyRes.Value.AttentionOrders);
        Assert.Null(qcOnlyRes.Value.ShortageStats);
        Assert.Empty(qcOnlyRes.Value.TopShortages);
        Assert.Null(qcOnlyRes.Value.ProcurementStats);
        Assert.Empty(qcOnlyRes.Value.OpenPurchaseOrders);
        Assert.Empty(qcOnlyRes.Value.WorkCenterLoads);
        Assert.NotNull(qcOnlyRes.Value.QualityStats);
        Assert.Single(qcOnlyRes.Value.PendingInspections);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class DisposableReservationDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableReservationDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            var dbConn = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(dbConn).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableReservationDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_res_{Guid.NewGuid():N}";
            var db = new DisposableReservationDatabase(builder, name);
            try
            {
                await using var conn = new SqlConnection(db.server.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE [{db.name}]";
                await cmd.ExecuteNonQueryAsync();
                db.created = true;
                await using var ctx = await db.Factory.CreateDbContextAsync();
                await ctx.Database.MigrateAsync();
                return db;
            }
            catch
            {
                await db.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!created) return;
            await using var conn = new SqlConnection(server.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await cmd.ExecuteNonQueryAsync();
            created = false;
        }

        private sealed class TestDbContextFactory(DbContextOptions<OrganizationDbContext> options)
            : IDbContextFactory<OrganizationDbContext>
        {
            public OrganizationDbContext CreateDbContext() => new(options);
        }
    }
}
