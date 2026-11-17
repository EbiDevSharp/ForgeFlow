using System.Text.RegularExpressions;
using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.Sales;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ConcurrencyIntegrationTests
{
    private static TrustedActor Attribution(long actorId = 1) => new(actorId, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
    private static OrganizationWriteAccess CreateAccess(TrustedActor actor) => new(actor, _ => ValueTask.FromResult(true));

    [OrganizationSqlServerFact]
    public async Task Concurrent_DocumentSequence_Allocations_Yield_Zero_Duplicates_And_Zero_Deadlocks()
    {
        await using var database = await DisposableConcurrencyDatabase.Create();
        var actor = Attribution();
        var access = CreateAccess(actor);

        long orgId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("CONC-SEQ-ORG", "Concurrency Seq Org");
            org.CreateSite("SITE-1", "Main Site");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(actor);
            orgId = org.Id;
        }

        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var store = new SqlDocumentSequenceStore(database.Factory);

        // Pre-configure SalesOrder sequence with Persian year, 5 digits, initial 1
        var list = await store.ListSequences(orgId, utcNow, access, default);
        var soSeq = list.Single(s => s.DocumentType == DocumentSequenceType.SalesOrder);
        var upd = await store.UpdateSequence(
            orgId,
            DocumentSequenceType.SalesOrder,
            new UpdateDocumentSequenceRequest("SO", "-", DocumentYearMode.Persian, 5, 1, false),
            Convert.FromBase64String(soSeq.RowVersion),
            utcNow,
            access,
            default);
        Assert.True(upd.Succeeded);

        // Launch 20 concurrent allocation tasks simultaneously
        const int concurrencyLevel = 20;
        var tasks = Enumerable.Range(0, concurrencyLevel).Select(async _ =>
        {
            await using var db = await database.Factory.CreateDbContextAsync();
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var number = await SqlDocumentSequenceStore.AllocateAsync(
                    db, orgId, DocumentSequenceType.SalesOrder, null, utcNow, default);
                await db.SaveInOperation(actor, default);
                await tx.CommitAsync();
                return number;
            });
        });

        var numbers = await Task.WhenAll(tasks);

        Assert.Equal(concurrencyLevel, numbers.Length);
        Assert.Equal(concurrencyLevel, numbers.Distinct(StringComparer.Ordinal).Count());
        Assert.All(numbers, n => Assert.Matches(@"^SO-1405-\d{5}$", n));

        // Verify state in DB
        await using var verifyDb = await database.Factory.CreateDbContextAsync();
        var finalSeq = await verifyDb.DocumentSequences.SingleAsync(s => s.OrganizationId == orgId && s.DocumentType == DocumentSequenceType.SalesOrder);
        Assert.Equal(21, finalSeq.NextNumber);
    }

    [OrganizationSqlServerFact]
    public async Task Concurrent_Stock_Reservations_Against_Same_Lot_Never_Exceed_Available_Quantity_And_Never_Go_Negative()
    {
        await using var database = await DisposableConcurrencyDatabase.Create();
        var actor = Attribution();
        var access = CreateAccess(actor);
        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        long orgId, siteId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("CONC-RES-ORG", "Concurrency Reservation Org");
            var site = org.CreateSite("SITE-RES", "Reservation Site");
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
            Code: "RES-ITEM",
            Name: "Reservable Item",
            Description: null,
            BaseUnitId: pcs.Id,
            CanBePurchased: false,
            CanBeManufactured: false,
            CanBeSold: true,
            CanBeConsumed: false,
            RequiresLotTracking: true,
            QualityControlMode: QualityControlMode.None), access, default)).Value!;

        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var wh = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgId, siteId, "WH-RES", "Reservation Warehouse", true), access, default)).Value!;

        // Receipt 10 PCS of lot "LOT-CONC-01" into WH
        var rcv = await invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgId, wh.Id, null, "RCV-CONC-01", InventoryTransactionType.Receipt, null, "Initial lot balance", null,
                [new InventoryLineInput(item.Summary.Id, "LOT-CONC-01", 10m, pcs.Id)]),
            siteId,
            [new ResolvedInventoryLineWrite(siteId, wh.Id, item.Summary.Id, "LOT-CONC-01", 10m, pcs.Id, 10m, pcs.Id, null, false)],
            utcNow, access, default);
        Assert.True(rcv.Succeeded);

        var salesStore = new SqlSalesOperationsStore(database.Factory);
        var cust = (await salesStore.CreateCustomer(new CreateCustomerRequest(orgId, "CUST-CONC", "Conc Customer", null, null, null, null, null, null), utcNow, access, default)).Value!;
        var so = (await salesStore.CreateSalesOrder(
            new CreateSalesOrderRequest(orgId, siteId, cust.Id, null, new DateOnly(2026, 11, 16), null, null, "IRR", null, null,
                [new SalesOrderLineRequest(item.Summary.Id, 20m, pcs.Id, 1000m, null)]),
            [new ResolvedSalesOrderLine(item.Summary.Id, 20m, pcs.Id, pcs.Id, 20m, 1000m, null)],
            utcNow, access, default)).Value!;
        var approved = await salesStore.ApproveSalesOrder(orgId, so.Summary.Id, Convert.FromBase64String(so.Summary.RowVersion), utcNow, access, default);
        Assert.True(approved.Succeeded);

        // 5 concurrent tasks each request to reserve 3 PCS from "LOT-CONC-01" (total requested = 15 PCS, only 10 PCS available)
        var resStore = new SqlStockReservationStore(database.Factory);
        var tasks = Enumerable.Range(0, 5).Select(_ => resStore.ReserveSalesOrderStock(
            new ReserveSalesOrderStockRequest(orgId, so.Summary.Id, null, item.Summary.Id, 3m, pcs.Id, wh.Id, "LOT-CONC-01", "Race reservation"),
            3m, pcs.Id, utcNow, access, default));

        var results = await Task.WhenAll(tasks);

        // Exactly 3 succeed (3 * 3 = 9 <= 10) and exactly 2 fail with ReservationExceedsAvailable
        Assert.Equal(3, results.Count(r => r.Succeeded));
        Assert.Equal(2, results.Count(r => !r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal(InventoryErrors.ReservationExceedsAvailable, r.Error!.Code));

        // Direct DB verification
        await using var verifyDb = await database.Factory.CreateDbContextAsync();
        var bal = await verifyDb.InventoryBalances.SingleAsync(b => b.OrganizationId == orgId && b.WarehouseId == wh.Id && b.ItemId == item.Summary.Id);
        Assert.Equal(10m, bal.OnHandBaseQuantity);
        Assert.Equal(9m, bal.ReservedBaseQuantity);
        Assert.Equal(1m, bal.AvailableBaseQuantity);
        Assert.True(bal.AvailableBaseQuantity >= 0m);

        var totalActiveReserved = await verifyDb.StockReservations
            .Where(r => r.OrganizationId == orgId && r.SalesOrderId == so.Summary.Id && r.Status == ReservationStatus.Active)
            .SumAsync(r => r.ReservedBaseQuantity);
        Assert.Equal(9m, totalActiveReserved);
    }

    [OrganizationSqlServerFact]
    public async Task Concurrent_Stock_Issues_And_Receipts_Preserve_Balance_And_Ledger_Integrity()
    {
        await using var database = await DisposableConcurrencyDatabase.Create();
        var actor = Attribution();
        var access = CreateAccess(actor);
        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        long orgId, siteId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("CONC-INV-ORG", "Concurrency Inv Org");
            var site = org.CreateSite("SITE-INV", "Inventory Site");
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
            Code: "STK-ITEM",
            Name: "Stock Item",
            Description: null,
            BaseUnitId: pcs.Id,
            CanBePurchased: false,
            CanBeManufactured: false,
            CanBeSold: true,
            CanBeConsumed: true,
            RequiresLotTracking: false,
            QualityControlMode: QualityControlMode.None), access, default)).Value!;

        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var wh = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgId, siteId, "WH-STK", "Stock Warehouse", true), access, default)).Value!;

        // Initial balance: 100 PCS
        var initRcv = await invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgId, wh.Id, null, "RCV-INIT", InventoryTransactionType.Receipt, null, "Initial stock", null,
                [new InventoryLineInput(item.Summary.Id, string.Empty, 100m, pcs.Id)]),
            siteId,
            [new ResolvedInventoryLineWrite(siteId, wh.Id, item.Summary.Id, string.Empty, 100m, pcs.Id, 100m, pcs.Id, null, false)],
            utcNow, access, default);
        Assert.True(initRcv.Succeeded);

        // 4 tasks issue 15 PCS each (-60 PCS)
        // 4 tasks receive 10 PCS each (+40 PCS)
        // Net change = -20 PCS -> final expected = 80 PCS
        var issueTasks = Enumerable.Range(1, 4).Select(i => invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgId, wh.Id, null, $"ISS-CONC-{i}", InventoryTransactionType.Issue, null, $"Issue {i}", null,
                [new InventoryLineInput(item.Summary.Id, string.Empty, 15m, pcs.Id)]),
            siteId,
            [new ResolvedInventoryLineWrite(siteId, wh.Id, item.Summary.Id, string.Empty, 15m, pcs.Id, -15m, pcs.Id, null, false)],
            utcNow, access, default));

        var receiptTasks = Enumerable.Range(1, 4).Select(i => invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgId, wh.Id, null, $"RCV-CONC-{i}", InventoryTransactionType.Receipt, null, $"Receipt {i}", null,
                [new InventoryLineInput(item.Summary.Id, string.Empty, 10m, pcs.Id)]),
            siteId,
            [new ResolvedInventoryLineWrite(siteId, wh.Id, item.Summary.Id, string.Empty, 10m, pcs.Id, 10m, pcs.Id, null, false)],
            utcNow, access, default));

        var allResults = await Task.WhenAll(issueTasks.Concat(receiptTasks));

        Assert.Equal(8, allResults.Length);
        Assert.All(allResults, r => Assert.True(r.Succeeded, $"Failed: {r.Error?.Code}"));

        await using var verifyDb = await database.Factory.CreateDbContextAsync();
        var bal = await verifyDb.InventoryBalances.SingleAsync(b => b.OrganizationId == orgId && b.WarehouseId == wh.Id && b.ItemId == item.Summary.Id);
        Assert.Equal(80m, bal.OnHandBaseQuantity);

        var netLedgerDelta = await verifyDb.InventoryLedgerEntries
            .Where(l => l.OrganizationId == orgId && l.WarehouseId == wh.Id && l.ItemId == item.Summary.Id)
            .SumAsync(l => l.BaseQuantityDelta);
        Assert.Equal(80m, netLedgerDelta); // 100 initial - 60 issues + 40 receipts = 80
    }

    [OrganizationSqlServerFact]
    public async Task Concurrent_Entity_Creations_With_Duplicate_Codes_Return_Domain_Errors_Instead_Of_Unhandled_Exceptions()
    {
        await using var database = await DisposableConcurrencyDatabase.Create();
        var actor = Attribution();
        var access = CreateAccess(actor);

        long orgId, siteId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("CONC-DUP-ORG", "Concurrency Dup Org");
            var site = org.CreateSite("SITE-DUP", "Dup Site");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(actor);
            orgId = org.Id;
            siteId = site.Id;
        }

        var unitStore = new UnitOperationsStore(database.Factory);
        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var pcs = (await unitStore.ListActiveUnits(orgId, default)).Single(u => u.Code == "PCS");

        // A. Concurrent Item creations with duplicate code
        var itemStore = new ItemOperationsStore(database.Factory);
        var itemTasks = Enumerable.Range(0, 4).Select(_ => itemStore.CreateItem(
            new CreateItemRequest(
                OrganizationId: orgId,
                Code: "DUP-ITEM-RACE",
                Name: "Race Item",
                Description: null,
                BaseUnitId: pcs.Id,
                CanBePurchased: true,
                CanBeManufactured: false,
                CanBeSold: false,
                CanBeConsumed: false),
            access, default));
        var itemResults = await Task.WhenAll(itemTasks);

        Assert.Single(itemResults, r => r.Succeeded);
        Assert.Equal(3, itemResults.Count(r => !r.Succeeded && r.Error!.Code == ItemErrors.CodeDuplicate));

        // B. Concurrent Warehouse creations with duplicate code
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var whTasks = Enumerable.Range(0, 4).Select(_ => invStore.CreateWarehouse(
            new CreateWarehouseRequest(orgId, siteId, "DUP-WH-RACE", "Race Warehouse", false),
            access, default));
        var whResults = await Task.WhenAll(whTasks);

        Assert.Single(whResults, r => r.Succeeded);
        Assert.Equal(3, whResults.Count(r => !r.Succeeded && r.Error!.Code == InventoryErrors.CodeDuplicate));

        // C. Concurrent Supplier creations with duplicate code
        var procStore = new SqlProcurementOperationsStore(database.Factory);
        var suppTasks = Enumerable.Range(0, 4).Select(_ => procStore.CreateSupplier(
            new CreateSupplierRequest(orgId, "DUP-SUPP-RACE", "Race Supplier", null, null, null, null, null),
            access, default));
        var suppResults = await Task.WhenAll(suppTasks);

        Assert.Single(suppResults, r => r.Succeeded);
        Assert.Equal(3, suppResults.Count(r => !r.Succeeded && r.Error!.Code == ProcurementErrors.SupplierCodeDuplicate));
    }

    [OrganizationSqlServerFact]
    public async Task Concurrent_Updates_On_Same_Entity_Detect_RowVersion_Conflict_Preventing_Lost_Updates()
    {
        await using var database = await DisposableConcurrencyDatabase.Create();
        var actor = Attribution();
        var access = CreateAccess(actor);
        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        long orgId, siteId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("CONC-ROWV-ORG", "Concurrency RowVersion Org");
            var site = org.CreateSite("SITE-ROWV", "RowVersion Site");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(actor);
            orgId = org.Id;
            siteId = site.Id;
        }

        var unitStore = new UnitOperationsStore(database.Factory);
        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var pcs = (await unitStore.ListActiveUnits(orgId, default)).Single(u => u.Code == "PCS");

        // A. Item concurrent update
        var itemStore = new ItemOperationsStore(database.Factory);
        var item = (await itemStore.CreateItem(
            new CreateItemRequest(
                OrganizationId: orgId,
                Code: "ROWV-ITEM",
                Name: "Initial Name",
                Description: null,
                BaseUnitId: pcs.Id,
                CanBePurchased: true,
                CanBeManufactured: true,
                CanBeSold: true,
                CanBeConsumed: false),
            access, default)).Value!;

        var itemVersionBytes = Convert.FromBase64String(item.Summary.RowVersion);

        var itemTask1 = itemStore.UpdateItem(
            orgId, item.Summary.Id, itemVersionBytes,
            new UpdateItemRequest(orgId, item.Summary.Id, "Updated by Worker 1", null, pcs.Id, true, true, true, false, item.Summary.RowVersion, false, QualityControlMode.None),
            access, default);

        var itemTask2 = itemStore.UpdateItem(
            orgId, item.Summary.Id, itemVersionBytes,
            new UpdateItemRequest(orgId, item.Summary.Id, "Updated by Worker 2", null, pcs.Id, true, true, true, false, item.Summary.RowVersion, false, QualityControlMode.None),
            access, default);

        var itemUpdateResults = await Task.WhenAll(itemTask1, itemTask2);
        Assert.Single(itemUpdateResults, r => r.Succeeded);
        Assert.Single(itemUpdateResults, r => !r.Succeeded && r.Error!.Code == ItemErrors.ConcurrencyConflict);

        // B. SalesOrder concurrent update
        var salesStore = new SqlSalesOperationsStore(database.Factory);
        var cust = (await salesStore.CreateCustomer(
            new CreateCustomerRequest(orgId, "CUST-ROWV", "RowVersion Cust", null, null, null, null, null, null),
            utcNow, access, default)).Value!;

        var so = (await salesStore.CreateSalesOrder(
            new CreateSalesOrderRequest(orgId, siteId, cust.Id, null, new DateOnly(2026, 11, 16), null, null, "IRR", null, "Initial notes",
                [new SalesOrderLineRequest(item.Summary.Id, 5m, pcs.Id, 100m, null)]),
            [new ResolvedSalesOrderLine(item.Summary.Id, 5m, pcs.Id, pcs.Id, 5m, 100m, null)],
            utcNow, access, default)).Value!;

        var soVersionBytes = Convert.FromBase64String(so.Summary.RowVersion);

        var soTask1 = salesStore.UpdateSalesOrder(
            orgId, so.Summary.Id,
            new UpdateSalesOrderRequest(cust.Id, new DateOnly(2026, 11, 16), null, null, "IRR", null, "Notes by Worker 1",
                [new SalesOrderLineRequest(item.Summary.Id, 5m, pcs.Id, 100m, null)]),
            [new ResolvedSalesOrderLine(item.Summary.Id, 5m, pcs.Id, pcs.Id, 5m, 100m, null)],
            soVersionBytes, utcNow, access, default);

        var soTask2 = salesStore.UpdateSalesOrder(
            orgId, so.Summary.Id,
            new UpdateSalesOrderRequest(cust.Id, new DateOnly(2026, 11, 16), null, null, "IRR", null, "Notes by Worker 2",
                [new SalesOrderLineRequest(item.Summary.Id, 5m, pcs.Id, 100m, null)]),
            [new ResolvedSalesOrderLine(item.Summary.Id, 5m, pcs.Id, pcs.Id, 5m, 100m, null)],
            soVersionBytes, utcNow, access, default);

        var soUpdateResults = await Task.WhenAll(soTask1, soTask2);
        Assert.Single(soUpdateResults, r => r.Succeeded);
        Assert.Single(soUpdateResults, r => !r.Succeeded && r.Error!.Code == SalesErrors.ConcurrencyConflict);

        // C. Bom concurrent update
        var bomStore = new BomOperationsStore(database.Factory);
        var bom = (await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-ROWV", "RowVersion Bom", item.Summary.Id), access, default)).Value!;
        var bomVersionBytes = Convert.FromBase64String(bom.Summary.RowVersion);

        var bomTask1 = bomStore.UpdateBom(
            orgId, bom.Summary.Id, bomVersionBytes,
            "Updated Bom by Worker 1",
            access, default);

        var bomTask2 = bomStore.UpdateBom(
            orgId, bom.Summary.Id, bomVersionBytes,
            "Updated Bom by Worker 2",
            access, default);

        var bomUpdateResults = await Task.WhenAll(bomTask1, bomTask2);
        Assert.Single(bomUpdateResults, r => r.Succeeded);
        Assert.Single(bomUpdateResults, r => !r.Succeeded && r.Error!.Code == BomErrors.ConcurrencyConflict);
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
            var name = $"forgeflow_conc_{Guid.NewGuid():N}";
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
