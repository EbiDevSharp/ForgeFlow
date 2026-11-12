using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Sales;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class SalesDispatchSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task S02_sales_order_reservation_expiry_quarantine_dispatch_coa_reversal_and_cancel_work_end_to_end_in_sql()
    {
        await using var database = await DisposableSalesDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var qcStore = new SqlQualityOperationsStore(database.Factory);
        var specStore = new SqlQualitySpecStore(() => database.Factory);
        var resStore = new SqlStockReservationStore(database.Factory);
        var salesStore = new SqlSalesOperationsStore(database.Factory);

        long orgId, siteId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-SAL", "Sales Petrochem");
            var site = org.CreateSite("SITE-1", "Main Plant");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(Attribution());
            orgId = org.Id;
            siteId = site.Id;
        }

        var actor = Attribution();
        var allowAll = new AllowAllSalesPermissions();
        var actors = new FixedSalesActor(actor);
        var access = new OrganizationWriteAccess(actor, _ => ValueTask.FromResult(true));
        var siteScope = new OrganizationScope.Site(orgId, siteId);
        var orgScope = new OrganizationScope.Organization(orgId);

        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var kg = (await unitStore.ListActiveUnits(orgId, default)).Single(u => u.Code == "KG");
        var ton = (await unitStore.ListActiveUnits(orgId, default)).Single(u => u.Code == "TON");
        await unitStore.CreateCurrency(new CreateCurrencyRequest(orgId, "IRR", "Iranian Rial", "IRR", 0), access, default);

        var invUseCases = new InventoryUseCases(invStore, actors, allowAll);
        var qcUseCases = new QualityUseCases(qcStore, actors, allowAll);
        var specUseCases = new QualitySpecUseCases(specStore, actors, allowAll);
        var resUseCases = new StockReservationUseCases(resStore, invStore, actors, allowAll);
        var salesUseCases = new SalesUseCases(salesStore, actors, allowAll);

        var wh = (await invUseCases.CreateWarehouse(siteScope, new CreateWarehouseRequest(orgId, siteId, "FG-WH", "Finished Goods WH", true))).Value!;
        var resin = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "HDPE-RESIN", "HDPE Resin", null, kg.Id,
            CanBePurchased: false, CanBeManufactured: true, CanBeSold: true, CanBeConsumed: false,
            RequiresLotTracking: true, QualityControlMode: QualityControlMode.IncomingReceipts), access, default)).Value!;
        var resinId = resin.Summary.Id;

        // 1. Receipt 3 lots: LOT-GOOD (with COA), LOT-EXP (expired), LOT-HOLD (blocked in QC)
        Assert.True((await invUseCases.PostTransaction(siteScope, new PostInventoryTransactionRequest(
            orgId, wh.Id, null, "REC-01", InventoryTransactionType.Receipt, null, "Good lot", null,
            [new InventoryLineInput(resinId, "LOT-GOOD", 5000m, kg.Id, null)]))).Succeeded);

        Assert.True((await invUseCases.PostTransaction(siteScope, new PostInventoryTransactionRequest(
            orgId, wh.Id, null, "REC-02", InventoryTransactionType.Receipt, null, "Expired lot", null,
            [new InventoryLineInput(resinId, "LOT-EXP", 1000m, kg.Id, null)]))).Succeeded);

        Assert.True((await invUseCases.PostTransaction(siteScope, new PostInventoryTransactionRequest(
            orgId, wh.Id, null, "REC-03", InventoryTransactionType.Receipt, null, "Hold lot", null,
            [new InventoryLineInput(resinId, "LOT-HOLD", 1000m, kg.Id, null)]))).Succeeded);

        // Mark LOT-EXP as expired
        Assert.True((await qcUseCases.UpsertItemLotMetadata(siteScope, new UpsertItemLotMetadataRequest(
            orgId, resinId, "LOT-EXP", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "Expired"))).Succeeded);

        // Complete QC inspection for LOT-GOOD with CertificateNumber
        var qcGood = (await qcUseCases.ListInspections(siteScope, orgId, new QualityInspectionListQuery(Search: "LOT-GOOD"))).Value!.Items.Single();
        var measGood = await qcUseCases.UpdateMeasurements(siteScope, new UpdateQualityMeasurementsRequest(
            orgId, qcGood.Id, null, "Passed", [], qcGood.RowVersion, "COA-2026-99"));
        Assert.True(measGood.Succeeded);
        var dispGood = await qcUseCases.RecordDisposition(siteScope, new RecordQualityDispositionRequest(
            orgId, qcGood.Id, 5000m, 0m, kg.Id, null, "Passed", null, measGood.Value!.Summary.RowVersion));
        Assert.True(dispGood.Succeeded);

        // 2. Create Customer & Approved SalesOrder (2 TON = 2000 KG)
        var cust = (await salesUseCases.CreateCustomer(orgScope, new CreateCustomerRequest(
            orgId, "CUST-01", "Tehran Plastics", "Reza", "+98-21-1234", "reza@tp.example", "Tehran", "TX-1", null))).Value!;

        var so = (await salesUseCases.CreateSalesOrder(siteScope, new CreateSalesOrderRequest(
            orgId, siteId, cust.Id, "SO-2026-01", new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 20), wh.Id, "IRR", null, null,
            [new SalesOrderLineRequest(resinId, 2m, ton.Id, 500000m, null)]))).Value!;

        so = (await salesUseCases.ApproveSalesOrder(siteScope, orgId, so.Summary.Id, so.Summary.RowVersion)).Value!;
        Assert.Equal(SalesOrderStatus.Approved, so.Summary.Status);

        // 3. Reserve 1500 KG of LOT-GOOD for SO-2026-01
        var soLineId = so.Lines[0].Id;
        var resResult = await resUseCases.ReserveSalesOrderStock(siteScope,
            new ReserveSalesOrderStockRequest(orgId, so.Summary.Id, soLineId, resinId, 1500m, kg.Id, wh.Id, "LOT-GOOD"));
        Assert.True(resResult.Succeeded);

        // 4. Reject expired lot & quarantined/blocked lot
        var expDispatch = await salesUseCases.PostDeliveryOrder(siteScope, orgId, so.Summary.Id,
            new PostDeliveryOrderRequest(wh.Id, "DO-EXP", new DateOnly(2026, 11, 12), null, null, null,
                [new PostDeliveryOrderLineRequest(soLineId, "LOT-EXP", 500m, kg.Id)]));
        Assert.False(expDispatch.Succeeded);
        Assert.Equal(SalesErrors.LotExpired, expDispatch.Error!.Code);

        var holdDispatch = await salesUseCases.PostDeliveryOrder(siteScope, orgId, so.Summary.Id,
            new PostDeliveryOrderRequest(wh.Id, "DO-HOLD", new DateOnly(2026, 11, 12), null, null, null,
                [new PostDeliveryOrderLineRequest(soLineId, "LOT-HOLD", 500m, kg.Id)]));
        Assert.False(holdDispatch.Succeeded);
        Assert.Equal(SalesErrors.LotQuarantinedOrInsufficient, holdDispatch.Error!.Code);

        // 5. Post valid DeliveryOrder for 2000 KG (1500 from reservation + 500 unreserved)
        var doResult = await salesUseCases.PostDeliveryOrder(siteScope, orgId, so.Summary.Id,
            new PostDeliveryOrderRequest(wh.Id, "DO-001", new DateOnly(2026, 11, 12), "Barbar", "TRK-1", null,
                [new PostDeliveryOrderLineRequest(soLineId, "LOT-GOOD", 2000m, kg.Id)]));
        Assert.True(doResult.Succeeded);
        Assert.Equal("COA-2026-99", doResult.Value!.Lines[0].CertificateNumberSnapshot);

        var coa = await specUseCases.GetLotCertificate(siteScope, orgId, resinId, "LOT-GOOD");
        Assert.True(coa.Succeeded);
        Assert.Equal("COA-2026-99", coa.Value!.Inspections[0].CertificateNumber);

        var updatedSo = (await salesUseCases.GetSalesOrder(siteScope, orgId, so.Summary.Id)).Value!;
        Assert.Equal(SalesOrderStatus.Dispatched, updatedSo.Summary.Status);

        var remainingRes = (await resUseCases.ListSalesOrderReservations(siteScope, orgId, so.Summary.Id)).Value!;
        Assert.Single(remainingRes);
        Assert.Equal(ReservationStatus.Fulfilled, remainingRes[0].Status);

        // 6. Reverse DeliveryOrder -> restores balance and order status
        var revResult = await salesUseCases.ReverseDeliveryOrder(siteScope, orgId, doResult.Value.Summary.Id,
            new ReverseDeliveryOrderRequest("Wrong truck"), doResult.Value.Summary.RowVersion);
        Assert.True(revResult.Succeeded);
        Assert.Equal(DeliveryOrderStatus.Reversed, revResult.Value!.Summary.Status);

        var revertedSo = (await salesUseCases.GetSalesOrder(siteScope, orgId, so.Summary.Id)).Value!;
        Assert.Equal(SalesOrderStatus.Approved, revertedSo.Summary.Status);

        // 7. Reserve again and Cancel SO -> auto-releases reservation
        Assert.True((await resUseCases.ReserveSalesOrderStock(siteScope,
            new ReserveSalesOrderStockRequest(orgId, so.Summary.Id, soLineId, resinId, 500m, kg.Id, wh.Id, "LOT-GOOD"))).Succeeded);

        var cancelRes = await salesUseCases.CancelSalesOrder(siteScope, orgId, so.Summary.Id,
            new CancelSalesOrderRequest("Customer cancelled"), revertedSo.Summary.RowVersion);
        Assert.True(cancelRes.Succeeded);
        Assert.Equal(SalesOrderStatus.Cancelled, cancelRes.Value!.Summary.Status);

        var afterCancelRes = (await resUseCases.ListSalesOrderReservations(siteScope, orgId, so.Summary.Id)).Value!;
        Assert.Equal(2, afterCancelRes.Count);
        Assert.Equal(ReservationStatus.Fulfilled, afterCancelRes[0].Status);
        Assert.Equal(ReservationStatus.Released, afterCancelRes[1].Status);
    }

    private sealed class FixedSalesActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class AllowAllSalesPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permissionCode, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(true));
    }

    private sealed class DisposableSalesDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableSalesDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            var dbConn = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(dbConn).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableSalesDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_sales_{Guid.NewGuid():N}";
            var db = new DisposableSalesDatabase(builder, name);
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
