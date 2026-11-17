using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Sales;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class StockConcurrencyIntegrationTests
{
    private static TrustedActor Attribution(long actorId = 1) => new(actorId, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
    private static OrganizationWriteAccess CreateAccess(TrustedActor actor) => new(actor, _ => ValueTask.FromResult(true));

    [OrganizationSqlServerFact]
    public async Task Concurrent_Reservations_Issues_And_Dispatches_Against_Same_Lot_Never_Cause_Negative_Available_Or_OverAllocation()
    {
        await using var database = await DisposableConcurrencyDatabase.Create();
        var actor = Attribution();
        var access = CreateAccess(actor);
        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        long orgId, siteId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("CONC-STK-ORG", "Concurrency Stock Org");
            var site = org.CreateSite("SITE-STK", "Stock Site");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(actor);
            orgId = org.Id;
            siteId = site.Id;
        }

        var unitStore = new UnitOperationsStore(database.Factory);
        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var pcs = (await unitStore.ListActiveUnits(orgId, default)).Single(u => u.Code == "PCS");

        var itemStore = new ItemOperationsStore(database.Factory);
        var item = (await itemStore.CreateItem(new CreateItemRequest(
            OrganizationId: orgId,
            Code: "CONC-LOT-ITEM",
            Name: "Lot Tracked Item",
            Description: null,
            BaseUnitId: pcs.Id,
            CanBePurchased: true,
            CanBeManufactured: true,
            CanBeSold: true,
            CanBeConsumed: true,
            RequiresLotTracking: true,
            QualityControlMode: QualityControlMode.None), access, default)).Value!;

        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var wh = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgId, siteId, "WH-SHARED", "Shared Warehouse", true), access, default)).Value!;

        // Initial Receipt: exactly 100 PCS of "LOT-SHARED-01"
        const decimal initialOnHand = 100m;
        var rcv = await invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgId, wh.Id, null, "RCV-INIT-01", InventoryTransactionType.Receipt, null, "Initial lot balance", null,
                [new InventoryLineInput(item.Summary.Id, "LOT-SHARED-01", initialOnHand, pcs.Id)]),
            siteId,
            [new ResolvedInventoryLineWrite(siteId, wh.Id, item.Summary.Id, "LOT-SHARED-01", initialOnHand, pcs.Id, initialOnHand, pcs.Id, null, false)],
            utcNow, access, default);
        Assert.True(rcv.Succeeded);

        // Setup 1 SalesOrder and 1 ProductionOrder
        var salesStore = new SqlSalesOperationsStore(database.Factory);
        var cust = (await salesStore.CreateCustomer(new CreateCustomerRequest(orgId, "CUST-STK", "Stock Cust", null, null, null, null, null, null), utcNow, access, default)).Value!;
        var so = (await salesStore.CreateSalesOrder(
            new CreateSalesOrderRequest(orgId, siteId, cust.Id, null, new DateOnly(2026, 11, 16), null, null, "IRR", null, null,
                [new SalesOrderLineRequest(item.Summary.Id, 100m, pcs.Id, 500m, null)]),
            [new ResolvedSalesOrderLine(item.Summary.Id, 100m, pcs.Id, pcs.Id, 100m, 500m, null)],
            utcNow, access, default)).Value!;
        Assert.True((await salesStore.ApproveSalesOrder(orgId, so.Summary.Id, Convert.FromBase64String(so.Summary.RowVersion), utcNow, access, default)).Succeeded);

        long poId;
        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            var bom = Bom.Create(orgId, "BOM-STK", "BOM Stock", item.Summary.Id);
            db.Boms.Add(bom);
            await db.SaveChangesAsync(actor);

            var poOrder = ProductionOrder.CreateDraft(
                orgId, siteId, "MO-CONC-01", item.Summary.Id, 100m, pcs.Id,
                new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20),
                bom.Id, bom.Code, 1, 1, 100m, pcs.Id, null);
            poOrder.Release(utcNow);
            db.ProductionOrders.Add(poOrder);
            await db.SaveChangesAsync(actor);
            poId = poOrder.Id;
        }

        var resStore = new SqlStockReservationStore(database.Factory);

        // Concurrently run 16 parallel tasks competing for the same 100 PCS lot:
        // - 4 Sales Reservations of 15 PCS each (= 60 PCS)
        // - 4 Production Reservations of 15 PCS each (= 60 PCS)
        // - 4 Direct Issues of 15 PCS each (= 60 PCS)
        // - 4 Sales Dispatches of 10 PCS each (= 40 PCS)
        // Total requested = 220 PCS (more than double the 100 PCS available!)
        var tasks = new List<Task>();

        for (var i = 1; i <= 4; i++)
        {
            var taskIdx = i;
            tasks.Add(Task.Run(async () =>
            {
                await resStore.ReserveSalesOrderStock(
                    new ReserveSalesOrderStockRequest(orgId, so.Summary.Id, null, item.Summary.Id, 15m, pcs.Id, wh.Id, "LOT-SHARED-01", $"SO-Res-{taskIdx}"),
                    15m, pcs.Id, utcNow, access, default);
            }));

            tasks.Add(Task.Run(async () =>
            {
                await resStore.ReserveStock(
                    new ReserveStockRequest(orgId, poId, null, item.Summary.Id, 15m, pcs.Id, wh.Id, "LOT-SHARED-01", $"PO-Res-{taskIdx}"),
                    15m, pcs.Id, utcNow, access, default);
            }));

            tasks.Add(Task.Run(async () =>
            {
                await invStore.PostTransaction(
                    new PostInventoryTransactionRequest(orgId, wh.Id, null, $"ISS-PARALLEL-{taskIdx}", InventoryTransactionType.Issue, null, $"Direct-Issue-{taskIdx}", null,
                        [new InventoryLineInput(item.Summary.Id, "LOT-SHARED-01", 15m, pcs.Id)]),
                    siteId,
                    [new ResolvedInventoryLineWrite(siteId, wh.Id, item.Summary.Id, "LOT-SHARED-01", 15m, pcs.Id, -15m, pcs.Id, null, false)],
                    utcNow, access, default);
            }));

            tasks.Add(Task.Run(async () =>
            {
                await salesStore.PostDeliveryOrder(
                    orgId, so.Summary.Id,
                    new PostDeliveryOrderRequest(wh.Id, null, new DateOnly(2026, 11, 16), null, null, $"Dispatch-{taskIdx}",
                        [new PostDeliveryOrderLineRequest(so.Lines[0].Id, "LOT-SHARED-01", 10m, pcs.Id, null)]),
                    [new ResolvedDeliveryOrderLine(so.Lines[0].Id, item.Summary.Id, "LOT-SHARED-01", 10m, pcs.Id, null)],
                    utcNow, access, default);
            }));
        }

        // Wait for all 16 concurrent operations to settle
        await Task.WhenAll(tasks);

        // Verification of Invariants against real SQL Server
        await using var verifyDb = await database.Factory.CreateDbContextAsync();
        var finalBalance = await verifyDb.InventoryBalances
            .SingleAsync(b => b.OrganizationId == orgId && b.WarehouseId == wh.Id && b.ItemId == item.Summary.Id && b.LotCode == "LOT-SHARED-01");

        // 1. Available quantity must NEVER be negative
        Assert.True(finalBalance.AvailableBaseQuantity >= 0m, $"Available quantity was negative: {finalBalance.AvailableBaseQuantity}");
        Assert.True(finalBalance.OnHandBaseQuantity >= 0m, $"OnHand quantity was negative: {finalBalance.OnHandBaseQuantity}");
        Assert.True(finalBalance.ReservedBaseQuantity >= 0m, $"Reserved quantity was negative: {finalBalance.ReservedBaseQuantity}");

        // 2. OnHand must always be >= Reserved + Blocked
        Assert.True(finalBalance.OnHandBaseQuantity >= finalBalance.ReservedBaseQuantity + finalBalance.BlockedBaseQuantity,
            $"OnHand ({finalBalance.OnHandBaseQuantity}) is less than Reserved ({finalBalance.ReservedBaseQuantity}) + Blocked ({finalBalance.BlockedBaseQuantity})");

        // 3. Total consumed + remaining on-hand must exactly equal initial 100 PCS
        var netLedgerDelta = await verifyDb.InventoryLedgerEntries
            .Where(l => l.OrganizationId == orgId && l.WarehouseId == wh.Id && l.ItemId == item.Summary.Id && l.LotCode == "LOT-SHARED-01")
            .SumAsync(l => l.BaseQuantityDelta);

        Assert.Equal(finalBalance.OnHandBaseQuantity, netLedgerDelta);
        Assert.True(finalBalance.OnHandBaseQuantity <= initialOnHand);
    }

    [OrganizationSqlServerFact]
    public async Task Concurrent_MultiLot_Allocations_With_Opposite_Ordering_Never_Deadlock()
    {
        await using var database = await DisposableConcurrencyDatabase.Create();
        var actor = Attribution();
        var access = CreateAccess(actor);
        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        long orgId, siteId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("CONC-DL-ORG", "Concurrency Deadlock Org");
            var site = org.CreateSite("SITE-DL", "Deadlock Site");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(actor);
            orgId = org.Id;
            siteId = site.Id;
        }

        var unitStore = new UnitOperationsStore(database.Factory);
        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var pcs = (await unitStore.ListActiveUnits(orgId, default)).Single(u => u.Code == "PCS");

        var itemStore = new ItemOperationsStore(database.Factory);
        var item = (await itemStore.CreateItem(new CreateItemRequest(
            OrganizationId: orgId,
            Code: "DL-ITEM",
            Name: "Multi-lot Item",
            Description: null,
            BaseUnitId: pcs.Id,
            CanBePurchased: true,
            CanBeManufactured: true,
            CanBeSold: true,
            CanBeConsumed: true,
            RequiresLotTracking: true,
            QualityControlMode: QualityControlMode.None), access, default)).Value!;

        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var wh1 = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgId, siteId, "WH-DL-1", "Warehouse 1", true), access, default)).Value!;
        var wh2 = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgId, siteId, "WH-DL-2", "Warehouse 2", false), access, default)).Value!;

        // Receipt 100 PCS into WH-1 (LOT-A) and 100 PCS into WH-2 (LOT-B)
        Assert.True((await invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgId, wh1.Id, null, "RCV-A", InventoryTransactionType.Receipt, null, null, null,
                [new InventoryLineInput(item.Summary.Id, "LOT-A", 100m, pcs.Id)]),
            siteId,
            [new ResolvedInventoryLineWrite(siteId, wh1.Id, item.Summary.Id, "LOT-A", 100m, pcs.Id, 100m, pcs.Id, null, false)],
            utcNow, access, default)).Succeeded);

        Assert.True((await invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgId, wh2.Id, null, "RCV-B", InventoryTransactionType.Receipt, null, null, null,
                [new InventoryLineInput(item.Summary.Id, "LOT-B", 100m, pcs.Id)]),
            siteId,
            [new ResolvedInventoryLineWrite(siteId, wh2.Id, item.Summary.Id, "LOT-B", 100m, pcs.Id, 100m, pcs.Id, null, false)],
            utcNow, access, default)).Succeeded);

        // 8 threads perform multi-lot adjustments or transfers with opposite orderings:
        // Threads 1-4: Touch WH-1 LOT-A then WH-2 LOT-B
        // Threads 5-8: Touch WH-2 LOT-B then WH-1 LOT-A
        var tasks = new List<Task<OrganizationResult<InventoryTransactionDetails>>>();

        for (var i = 1; i <= 4; i++)
        {
            var idx = i;
            // Order: WH-1 LOT-A then WH-2 LOT-B
            tasks.Add(Task.Run(() => invStore.PostTransaction(
                new PostInventoryTransactionRequest(orgId, wh1.Id, null, $"TXN-FWD-{idx}", InventoryTransactionType.Adjustment, null, $"Forward-{idx}", null,
                    [
                        new InventoryLineInput(item.Summary.Id, "LOT-A", -5m, pcs.Id),
                        new InventoryLineInput(item.Summary.Id, "LOT-B", 5m, pcs.Id)
                    ]),
                siteId,
                [
                    new ResolvedInventoryLineWrite(siteId, wh1.Id, item.Summary.Id, "LOT-A", -5m, pcs.Id, -5m, pcs.Id, null, false),
                    new ResolvedInventoryLineWrite(siteId, wh2.Id, item.Summary.Id, "LOT-B", 5m, pcs.Id, 5m, pcs.Id, null, false)
                ],
                utcNow, access, default)));

            // Opposite order: WH-2 LOT-B then WH-1 LOT-A
            tasks.Add(Task.Run(() => invStore.PostTransaction(
                new PostInventoryTransactionRequest(orgId, wh2.Id, null, $"TXN-REV-{idx}", InventoryTransactionType.Adjustment, null, $"Reverse-{idx}", null,
                    [
                        new InventoryLineInput(item.Summary.Id, "LOT-B", -5m, pcs.Id),
                        new InventoryLineInput(item.Summary.Id, "LOT-A", 5m, pcs.Id)
                    ]),
                siteId,
                [
                    new ResolvedInventoryLineWrite(siteId, wh2.Id, item.Summary.Id, "LOT-B", -5m, pcs.Id, -5m, pcs.Id, null, false),
                    new ResolvedInventoryLineWrite(siteId, wh1.Id, item.Summary.Id, "LOT-A", 5m, pcs.Id, 5m, pcs.Id, null, false)
                ],
                utcNow, access, default)));
        }

        var results = await Task.WhenAll(tasks);

        // All 8 transactions must complete successfully without dying from deadlocks
        Assert.All(results, r => Assert.True(r.Succeeded, $"Failed with: {r.Error?.Code}"));

        await using var verifyDb = await database.Factory.CreateDbContextAsync();
        var balA = await verifyDb.InventoryBalances.SingleAsync(b => b.OrganizationId == orgId && b.WarehouseId == wh1.Id && b.ItemId == item.Summary.Id && b.LotCode == "LOT-A");
        var balB = await verifyDb.InventoryBalances.SingleAsync(b => b.OrganizationId == orgId && b.WarehouseId == wh2.Id && b.ItemId == item.Summary.Id && b.LotCode == "LOT-B");

        // Net change for both is 0 (4 * -5 + 4 * +5 = 0)
        Assert.Equal(100m, balA.OnHandBaseQuantity);
        Assert.Equal(100m, balB.OnHandBaseQuantity);
    }

    [OrganizationSqlServerFact]
    public async Task Concurrent_CrossTenant_StockLots_Remain_Strictly_Isolated_Under_Load()
    {
        await using var database = await DisposableConcurrencyDatabase.Create();
        var actor = Attribution();
        var access = CreateAccess(actor);
        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        long orgAlphaId, siteAlphaId;
        long orgBetaId, siteBetaId;

        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var orgA = Organization.Create("CONC-ORG-ALPHA", "Org Alpha");
            var siteA = orgA.CreateSite("SITE-ALPHA", "Site Alpha");
            var orgB = Organization.Create("CONC-ORG-BETA", "Org Beta");
            var siteB = orgB.CreateSite("SITE-BETA", "Site Beta");

            setupDb.Organizations.AddRange(orgA, orgB);
            await setupDb.SaveChangesAsync(actor);

            orgAlphaId = orgA.Id;
            siteAlphaId = siteA.Id;
            orgBetaId = orgB.Id;
            siteBetaId = siteB.Id;
        }

        var unitStore = new UnitOperationsStore(database.Factory);
        Assert.True((await unitStore.SeedStandardUnits(orgAlphaId, access, default)).Succeeded);
        Assert.True((await unitStore.SeedStandardUnits(orgBetaId, access, default)).Succeeded);

        var pcsA = (await unitStore.ListActiveUnits(orgAlphaId, default)).Single(u => u.Code == "PCS");
        var pcsB = (await unitStore.ListActiveUnits(orgBetaId, default)).Single(u => u.Code == "PCS");

        var itemStore = new ItemOperationsStore(database.Factory);
        var itemA = (await itemStore.CreateItem(new CreateItemRequest(
            orgAlphaId, "SHARED-SKU", "Shared Item Alpha", null, pcsA.Id, true, true, true, true, true), access, default)).Value!;
        var itemB = (await itemStore.CreateItem(new CreateItemRequest(
            orgBetaId, "SHARED-SKU", "Shared Item Beta", null, pcsB.Id, true, true, true, true, true), access, default)).Value!;

        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var whA = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgAlphaId, siteAlphaId, "WH-SHARED", "WH Alpha", true), access, default)).Value!;
        var whB = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgBetaId, siteBetaId, "WH-SHARED", "WH Beta", true), access, default)).Value!;

        // Receipt 50 PCS in Org Alpha and 25 PCS in Org Beta for "LOT-ISOLATED"
        Assert.True((await invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgAlphaId, whA.Id, null, "RCV-A", InventoryTransactionType.Receipt, null, null, null,
                [new InventoryLineInput(itemA.Summary.Id, "LOT-ISOLATED", 50m, pcsA.Id)]),
            siteAlphaId,
            [new ResolvedInventoryLineWrite(siteAlphaId, whA.Id, itemA.Summary.Id, "LOT-ISOLATED", 50m, pcsA.Id, 50m, pcsA.Id, null, false)],
            utcNow, access, default)).Succeeded);

        Assert.True((await invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgBetaId, whB.Id, null, "RCV-B", InventoryTransactionType.Receipt, null, null, null,
                [new InventoryLineInput(itemB.Summary.Id, "LOT-ISOLATED", 25m, pcsB.Id)]),
            siteBetaId,
            [new ResolvedInventoryLineWrite(siteBetaId, whB.Id, itemB.Summary.Id, "LOT-ISOLATED", 25m, pcsB.Id, 25m, pcsB.Id, null, false)],
            utcNow, access, default)).Succeeded);

        var resStore = new SqlStockReservationStore(database.Factory);
        var salesStore = new SqlSalesOperationsStore(database.Factory);

        var custA = (await salesStore.CreateCustomer(new CreateCustomerRequest(orgAlphaId, "CUST-A", "Customer Alpha", null, null, null, null, null, null), utcNow, access, default)).Value!;
        var soA = (await salesStore.CreateSalesOrder(
            new CreateSalesOrderRequest(orgAlphaId, siteAlphaId, custA.Id, null, new DateOnly(2026, 11, 16), null, null, "IRR", null, null,
                [new SalesOrderLineRequest(itemA.Summary.Id, 50m, pcsA.Id, 100m, null)]),
            [new ResolvedSalesOrderLine(itemA.Summary.Id, 50m, pcsA.Id, pcsA.Id, 50m, 100m, null)],
            utcNow, access, default)).Value!;
        Assert.True((await salesStore.ApproveSalesOrder(orgAlphaId, soA.Summary.Id, Convert.FromBase64String(soA.Summary.RowVersion), utcNow, access, default)).Succeeded);

        // Run concurrent tasks across Alpha and Beta plus illegal cross-tenant attempts
        var alphaTasks = Enumerable.Range(1, 4).Select(i => (Task)resStore.ReserveSalesOrderStock(
            new ReserveSalesOrderStockRequest(orgAlphaId, soA.Summary.Id, null, itemA.Summary.Id, 10m, pcsA.Id, whA.Id, "LOT-ISOLATED", $"Alpha-Res-{i}"),
            10m, pcsA.Id, utcNow, access, default)).ToList();

        var betaTasks = Enumerable.Range(1, 4).Select(i => (Task)invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgBetaId, whB.Id, null, $"BETA-ISS-{i}", InventoryTransactionType.Issue, null, $"Beta-Issue-{i}", null,
                [new InventoryLineInput(itemB.Summary.Id, "LOT-ISOLATED", 5m, pcsB.Id)]),
            siteBetaId,
            [new ResolvedInventoryLineWrite(siteBetaId, whB.Id, itemB.Summary.Id, "LOT-ISOLATED", 5m, pcsB.Id, -5m, pcsB.Id, null, false)],
            utcNow, access, default)).ToList();

        // Illegal Cross-Tenant Attempt 1: Org Beta trying to reserve using Org Alpha's SalesOrder
        var illegalRes1 = resStore.ReserveSalesOrderStock(
            new ReserveSalesOrderStockRequest(orgBetaId, soA.Summary.Id, null, itemB.Summary.Id, 5m, pcsB.Id, whB.Id, "LOT-ISOLATED", "Cross-Tenant Hack"),
            5m, pcsB.Id, utcNow, access, default);

        // Illegal Cross-Tenant Attempt 2: Org Beta trying to issue using Org Alpha's Warehouse
        var illegalWh2 = invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgBetaId, whA.Id, null, "HACK-WH", InventoryTransactionType.Issue, null, "Cross-Tenant Hack", null,
                [new InventoryLineInput(itemB.Summary.Id, "LOT-ISOLATED", 5m, pcsB.Id)]),
            siteBetaId,
            [new ResolvedInventoryLineWrite(siteBetaId, whA.Id, itemB.Summary.Id, "LOT-ISOLATED", 5m, pcsB.Id, -5m, pcsB.Id, null, false)],
            utcNow, access, default);

        var allTasks = new List<Task>();
        allTasks.AddRange(alphaTasks);
        allTasks.AddRange(betaTasks);
        allTasks.Add(illegalRes1);
        allTasks.Add(illegalWh2);

        await Task.WhenAll(allTasks);

        var bad1 = await illegalRes1;
        var bad2 = await illegalWh2;

        Assert.False(bad1.Succeeded);
        Assert.Equal(InventoryErrors.ProductionOrderInvalid, bad1.Error!.Code);

        Assert.False(bad2.Succeeded);
        Assert.Equal(InventoryErrors.WarehouseNotFound, bad2.Error!.Code);

        // Verify that Beta's operations never touched Alpha's balances and vice-versa
        await using var verifyDb = await database.Factory.CreateDbContextAsync();
        var finalAlpha = await verifyDb.InventoryBalances.SingleAsync(b => b.OrganizationId == orgAlphaId && b.WarehouseId == whA.Id);
        var finalBeta = await verifyDb.InventoryBalances.SingleAsync(b => b.OrganizationId == orgBetaId && b.WarehouseId == whB.Id);

        Assert.Equal(50m, finalAlpha.OnHandBaseQuantity);
        Assert.Equal(40m, finalAlpha.ReservedBaseQuantity); // 4 tasks * 10 = 40 reserved
        Assert.Equal(10m, finalAlpha.AvailableBaseQuantity);

        Assert.Equal(5m, finalBeta.OnHandBaseQuantity); // 25 initial - (4 tasks * 5m) = 5
        Assert.Equal(0m, finalBeta.ReservedBaseQuantity);
        Assert.Equal(5m, finalBeta.AvailableBaseQuantity);
    }

    private sealed class DisposableConcurrencyDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public string ConnectionString { get; }
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableConcurrencyDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            ConnectionString = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(ConnectionString).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableConcurrencyDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_stkconc_{Guid.NewGuid():N}";
            var db = new DisposableConcurrencyDatabase(builder, name);
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
