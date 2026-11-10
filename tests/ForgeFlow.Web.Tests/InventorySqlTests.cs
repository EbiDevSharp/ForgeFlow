using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class InventorySqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task S01_S04_warehouses_append_only_ledger_concurrent_lock_and_production_issue_receipt_in_sql()
    {
        await using var database = await DisposableInventoryDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var orderStore = new SqlProductionOrderOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);

        long orgId, siteAId, siteBId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-INV", "Inventory Org");
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
        var ton = units.Single(u => u.Code == "TON");

        var steel = (await itemStore.CreateItem(new CreateItemRequest(orgId, "RAW-STL", "Raw Steel", null, kg.Id, true, false, false, true), access, default)).Value!.Summary;
        var frame = (await itemStore.CreateItem(new CreateItemRequest(orgId, "FG-FRM", "Steel Frame", null, pcs.Id, false, true, true, false), access, default)).Value!.Summary;

        Assert.True((await bomStore.SeedStandardContextDimensions(orgId, access, default)).Succeeded);
        var bom = (await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-FRM", "Frame", frame.Id), access, default)).Value!;
        var v1 = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 1, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, "v1"), access, default)).Value!;
        v1 = (await bomStore.SaveDraftBomLines(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion),
            [new BomInputLineInput(1, steel.Id, 5m, kg.Id, 0m, false, null)],
            [new BomOutputLineInput(1, frame.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;

        var actor = Attribution();
        var siteAScope = new OrganizationScope.Site(orgId, siteAId);
        var orgScope = new OrganizationScope.Organization(orgId);
        var perm = new ScopePerm((s, _) => (s is OrganizationScope.Site st && st.SiteId == siteAId) || s is OrganizationScope.Organization);
        var orderUseCases = new ProductionOrderUseCases(orderStore, bomStore, new StaticActor(actor), perm);
        var invUseCases = new InventoryUseCases(invStore, new StaticActor(actor), perm);

        // S01/S02: Create Warehouse A1/B1 and receive 0.1 TON steel = 100 KG into A1
        var whA = (await invUseCases.CreateWarehouse(siteAScope, new CreateWarehouseRequest(orgId, siteAId, "WH-A1", "Site A Main", true))).Value!;
        var whB = (await invUseCases.CreateWarehouse(orgScope, new CreateWarehouseRequest(orgId, siteBId, "WH-B1", "Site B Main", true))).Value!;

        var rcv = (await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "RCV-100", InventoryTransactionType.Receipt, null, "Steel arrival", null,
            [new InventoryLineInput(steel.Id, "HEAT-01", 0.1m, ton.Id)]))).Value!;
        Assert.Equal(100m, rcv.Entries.Single().BaseQuantityDelta);

        // S03: Barrier-style concurrent race: two 60 KG issues against 100 KG balance -> 1 succeeds (40 KG left), 1 fails InsufficientStock
        var t1 = invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "ISS-R1", InventoryTransactionType.Issue, null, null, null,
            [new InventoryLineInput(steel.Id, "HEAT-01", 60m, kg.Id)]));
        var t2 = invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "ISS-R2", InventoryTransactionType.Issue, null, null, null,
            [new InventoryLineInput(steel.Id, "HEAT-01", 60m, kg.Id)]));
        var results = await Task.WhenAll(t1, t2);
        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => !r.Succeeded && r.Error!.Code == InventoryErrors.InsufficientStock);

        var balAfterRace = (await invUseCases.ListBalances(siteAScope, orgId, new InventoryBalanceListQuery(WarehouseId: whA.Id))).Value!;
        Assert.Equal(40m, balAfterRace.Items.Single(b => b.ItemId == steel.Id).OnHandBaseQuantity);

        // S04: Release PO at Site A -> Post ProductionIssue (consume 20 KG Steel) + ProductionReceipt (4 PCS Frame)
        var po = (await orderUseCases.CreateDraftOrder(siteAScope, new CreateProductionOrderRequest(orgId, siteAId, "PO-A1", frame.Id, 4m, pcs.Id, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 2)))).Value!;
        po = (await orderUseCases.ReleaseOrder(siteAScope, new ReleaseProductionOrderRequest(orgId, po.Summary.Id, po.Summary.RowVersion))).Value!;

        var pIssue = await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "PI-PO1", InventoryTransactionType.ProductionIssue, po.Summary.Id, null, null,
            [new InventoryLineInput(steel.Id, "HEAT-01", 20m, kg.Id)]));
        Assert.True(pIssue.Succeeded);

        var pReceipt = await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "PR-PO1", InventoryTransactionType.ProductionReceipt, po.Summary.Id, null, null,
            [new InventoryLineInput(frame.Id, "LOT-FG1", 4m, pcs.Id)]));
        Assert.True(pReceipt.Succeeded);

        // Site-A operator cannot see or write Site-B warehouse
        var denied = await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whB.Id, null, "RCV-DENIED", InventoryTransactionType.Receipt, null, null, null,
            [new InventoryLineInput(steel.Id, null, 5m, kg.Id)]));
        Assert.False(denied.Succeeded);
        Assert.Equal(InventoryErrors.AccessDenied, denied.Error!.Code);

        // Reverse pIssue -> restores 20 KG Steel back to 40 KG
        var rev = await invUseCases.ReverseTransaction(siteAScope, new ReverseInventoryTransactionRequest(orgId, pIssue.Value!.Summary.Id, "PI-PO1-REV"));
        Assert.True(rev.Succeeded);
        var balFinal = (await invUseCases.ListBalances(siteAScope, orgId, new InventoryBalanceListQuery(WarehouseId: whA.Id))).Value!;
        Assert.Equal(40m, balFinal.Items.Single(b => b.ItemId == steel.Id).OnHandBaseQuantity);
        Assert.Equal(4m, balFinal.Items.Single(b => b.ItemId == frame.Id).OnHandBaseQuantity);

        await using var verifyDb = await database.Factory.CreateDbContextAsync();
        var events = await verifyDb.OrganizationChanges
            .Where(c => c.OrganizationId == orgId && (c.EventCode.StartsWith("Warehouse.") || c.EventCode.StartsWith("InventoryTransaction.")))
            .ToListAsync();
        Assert.Contains(events, e => e.EventCode == "Warehouse.Created" && e.SiteId == siteAId);
        Assert.Contains(events, e => e.EventCode == "InventoryTransaction.Posted" && e.SiteId == siteAId);
        Assert.Contains(events, e => e.EventCode == "InventoryTransaction.Reversed" && e.SiteId == siteAId);
    }

    private sealed class StaticActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class ScopePerm(Func<OrganizationScope, string, bool> predicate) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope requestedScope, string permission, CancellationToken ct)
            => ValueTask.FromResult(new PermissionDecision(predicate(requestedScope, permission)));
    }

    private sealed class DisposableInventoryDatabase : IAsyncDisposable
    {
        private readonly string name = "ForgeFlow_Inv_Test_" + Guid.NewGuid().ToString("N");
        private readonly SqlConnectionStringBuilder server;
        private bool created;

        private DisposableInventoryDatabase(string connection)
        {
            server = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", ConnectTimeout = 5, Pooling = false };
            var target = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name };
            Factory = new TestDbContextFactory(new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(target.ConnectionString).Options);
        }

        internal IDbContextFactory<OrganizationDbContext> Factory { get; }

        internal static async Task<DisposableInventoryDatabase> Create()
        {
            var db = new DisposableInventoryDatabase(Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER")!);
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
