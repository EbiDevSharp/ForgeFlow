using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ReportSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task S01_S02_shortage_and_production_variance_queries_execute_end_to_end_with_site_isolation_in_sql()
    {
        await using var database = await DisposableReportDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var orderStore = new SqlProductionOrderOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var rptStore = new SqlReportingQueryStore(database.Factory);

        long orgId, siteAId, siteBId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-RPT", "Reporting Org");
            var siteA = org.CreateSite("SITE-A", "Plant A");
            var siteB = org.CreateSite("SITE-B", "Plant B");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(Attribution());
            orgId = org.Id;
            siteAId = siteA.Id;
            siteBId = siteB.Id;
        }

        var access = new OrganizationWriteAccess(Attribution(), _ => ValueTask.FromResult(true));
        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var units = await unitStore.ListActiveUnits(orgId, default);
        var pcs = units.Single(u => u.Code == "PCS");
        var kg = units.Single(u => u.Code == "KG");

        var steel = (await itemStore.CreateItem(new CreateItemRequest(orgId, "RAW-STL", "Steel Bar", null, kg.Id, true, false, false, true), access, default)).Value!.Summary;
        var widget = (await itemStore.CreateItem(new CreateItemRequest(orgId, "FG-WDG", "Widget", null, pcs.Id, false, true, true, false), access, default)).Value!.Summary;

        Assert.True((await bomStore.SeedStandardContextDimensions(orgId, access, default)).Succeeded);
        var bom = (await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-WDG", "Widget BOM", widget.Id), access, default)).Value!;
        var v1 = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 1, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, "v1"), access, default)).Value!;
        v1 = (await bomStore.SaveDraftBomLines(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion),
            [new BomInputLineInput(1, steel.Id, 5m, kg.Id, 0m, false, null)],
            [new BomOutputLineInput(1, widget.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;

        var actor = Attribution();
        var perm = new AllowAllPermissions();
        var poUseCases = new ProductionOrderUseCases(orderStore, bomStore, new FixedActor(actor), perm);
        var invUseCases = new InventoryUseCases(invStore, new FixedActor(actor), perm);
        var siteAScope = new OrganizationScope.Site(orgId, siteAId);
        var siteBScope = new OrganizationScope.Site(orgId, siteBId);

        var whA = (await invUseCases.CreateWarehouse(siteAScope, new CreateWarehouseRequest(orgId, siteAId, "WH-A", "Main Warehouse A", true))).Value!;
        var whB = (await invUseCases.CreateWarehouse(siteBScope, new CreateWarehouseRequest(orgId, siteBId, "WH-B", "Main Warehouse B", true))).Value!;

        // Stock Site A with 20 KG Steel and Site B with 100 KG Steel.
        Assert.True((await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "RCV-A1", InventoryTransactionType.Receipt, null, "Opening A", null,
            [new InventoryLineInput(steel.Id, "LOT-1", 20m, kg.Id)]))).Succeeded);
        Assert.True((await invUseCases.PostTransaction(siteBScope, new PostInventoryTransactionRequest(
            orgId, whB.Id, null, "RCV-B1", InventoryTransactionType.Receipt, null, "Opening B", null,
            [new InventoryLineInput(steel.Id, "LOT-2", 100m, kg.Id)]))).Succeeded);

        // Site A order PO-A1 requests 10 Widgets -> requires 50 KG Steel!
        var poA = (await poUseCases.CreateDraftOrder(siteAScope,
            new CreateProductionOrderRequest(orgId, siteAId, "PO-A1", widget.Id, 10m, pcs.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 20), Notes: "Order A"))).Value!;
        poA = (await poUseCases.ReleaseOrder(siteAScope,
            new ReleaseProductionOrderRequest(orgId, poA.Summary.Id, poA.Summary.RowVersion))).Value!;
        poA = (await poUseCases.StartOrder(siteAScope,
            new StartProductionOrderRequest(orgId, poA.Summary.Id, poA.Summary.RowVersion))).Value!;

        // Site B order PO-B1 requests 2 Widgets -> requires 10 KG Steel.
        var poB = (await poUseCases.CreateDraftOrder(siteBScope,
            new CreateProductionOrderRequest(orgId, siteBId, "PO-B1", widget.Id, 2m, pcs.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 20), Notes: "Order B"))).Value!;
        poB = (await poUseCases.ReleaseOrder(siteBScope,
            new ReleaseProductionOrderRequest(orgId, poB.Summary.Id, poB.Summary.RowVersion))).Value!;

        // Post Issue 18 KG to PO-A1, then Reverse, then Issue 12 KG (so physical balance on Site A = 20 - 12 = 8 KG; Net Issued = 12 KG).
        var issue1 = (await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "ISS-1", InventoryTransactionType.ProductionIssue, poA.Summary.Id, "Issue 1", null,
            [new InventoryLineInput(steel.Id, "LOT-1", 18m, kg.Id)]))).Value!;
        Assert.True((await invUseCases.ReverseTransaction(siteAScope, new ReverseInventoryTransactionRequest(
            orgId, issue1.Summary.Id, "REV-1", "Wrong weight"))).Succeeded);
        Assert.True((await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "ISS-2", InventoryTransactionType.ProductionIssue, poA.Summary.Id, "Issue 2", null,
            [new InventoryLineInput(steel.Id, "LOT-1", 12m, kg.Id)]))).Succeeded);

        // Receive 8 Widgets and record ActualProduced=8, ActualScrap=1 on PO-A1.
        Assert.True((await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "REC-1", InventoryTransactionType.ProductionReceipt, poA.Summary.Id, "Rcpt 1", null,
            [new InventoryLineInput(widget.Id, "FG-1", 8m, pcs.Id)]))).Succeeded);
        var execRes = await poUseCases.RecordActuals(siteAScope,
            new RecordProductionOrderActualsRequest(orgId, poA.Summary.Id, 8m, 1m, "Partial run", poA.Summary.RowVersion));
        Assert.True(execRes.Succeeded);

        // Query Reports via ReportingUseCases for Site A only!
        var rptUseCases = new ReportingUseCases(rptStore, new FixedActor(Attribution()), new AllowAllPermissions());
        var shortageRes = await rptUseCases.GetMaterialShortageReport(new OrganizationScope.Site(orgId, siteAId), orgId, new MaterialShortageQuery());
        Assert.True(shortageRes.Succeeded);
        Assert.Equal(1, shortageRes.Value!.OpenOrderCount); // PO-B1 on Site B excluded!
        var steelShortage = shortageRes.Value!.Items.Single(x => x.ItemCode == "RAW-STL");
        Assert.Equal(50m, steelShortage.RequiredBaseQuantity);
        Assert.Equal(8m, steelShortage.OnHandBaseQuantity);
        Assert.Equal(42m, steelShortage.ShortageBaseQuantity);

        var varRes = await rptUseCases.GetProductionVarianceReport(new OrganizationScope.Site(orgId, siteAId), orgId, new ProductionVarianceQuery());
        Assert.True(varRes.Succeeded);
        var orderSummary = Assert.Single(varRes.Value!.Orders); // PO-B1 excluded!
        Assert.Equal("PO-A1", orderSummary.OrderNumber);
        Assert.Equal(10m, orderSummary.PlannedBaseQuantity);
        Assert.Equal(8m, orderSummary.ActualProducedBaseQuantity);
        Assert.Equal(1m, orderSummary.ActualScrapBaseQuantity);
        Assert.Equal(8m, orderSummary.PostedReceivedBaseQuantity);
        Assert.Equal(80m, orderSummary.YieldPercent);
        Assert.Equal(50m, orderSummary.TotalPlannedInputBaseQuantity);
        Assert.Equal(12m, orderSummary.TotalIssuedInputBaseQuantity); // Reversal netted accurately!
        Assert.Equal(-38m, orderSummary.TotalInputVarianceBaseQuantity);
    }

    private sealed class FixedActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class AllowAllPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor,
            OrganizationScope scope,
            string permission,
            CancellationToken ct = default) =>
            ValueTask.FromResult(new PermissionDecision(true));
    }

    private sealed class DisposableReportDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableReportDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            var dbConn = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(dbConn).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableReportDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_rpt_{Guid.NewGuid():N}";
            var db = new DisposableReportDatabase(builder, name);
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
