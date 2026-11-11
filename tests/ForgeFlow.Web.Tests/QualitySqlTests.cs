using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class QualitySqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task S01_S04_quality_blocking_partial_disposition_expiry_and_genealogy_work_end_to_end_in_sql()
    {
        await using var database = await DisposableQualityDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var orderStore = new SqlProductionOrderOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var qcStore = new SqlQualityOperationsStore(database.Factory);
        var rptStore = new SqlReportingQueryStore(database.Factory);

        long orgId, siteAId, siteBId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-QC", "Quality Org");
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
        var kg = units.Single(u => u.Code == "KG");
        var pcs = units.Single(u => u.Code == "PCS");

        var rawOil = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "RAW-OIL", "Crude Oil", null, kg.Id, true, false, false, true,
            RequiresLotTracking: true, QualityControlMode: QualityControlMode.IncomingReceipts), access, default)).Value!.Summary;
        var fgBottle = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "FG-OIL", "Oil Bottle", null, pcs.Id, false, true, true, false,
            RequiresLotTracking: true, QualityControlMode: QualityControlMode.ProductionReceipts), access, default)).Value!.Summary;

        Assert.True((await bomStore.SeedStandardContextDimensions(orgId, access, default)).Succeeded);
        var bom = (await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-OIL", "Bottle BOM", fgBottle.Id), access, default)).Value!;
        var v1 = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 1, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, "v1"), access, default)).Value!;
        v1 = (await bomStore.SaveDraftBomLines(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion),
            [new BomInputLineInput(1, rawOil.Id, 2m, kg.Id, 0m, false, null)],
            [new BomOutputLineInput(1, fgBottle.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;

        var actor = Attribution();
        var perm = new AllowAllPermissions();
        var poUseCases = new ProductionOrderUseCases(orderStore, bomStore, new FixedActor(actor), perm);
        var invUseCases = new InventoryUseCases(invStore, new FixedActor(actor), perm);
        var qcUseCases = new QualityUseCases(qcStore, new FixedActor(actor), perm);
        var rptUseCases = new ReportingUseCases(rptStore, new FixedActor(actor), perm);
        var siteAScope = new OrganizationScope.Site(orgId, siteAId);
        var siteBScope = new OrganizationScope.Site(orgId, siteBId);

        var whA = (await invUseCases.CreateWarehouse(siteAScope, new CreateWarehouseRequest(orgId, siteAId, "WH-A", "Main A", true))).Value!;
        _ = (await invUseCases.CreateWarehouse(siteBScope, new CreateWarehouseRequest(orgId, siteBId, "WH-B", "Main B", true))).Value!;

        // 1. Receipt of 100 KG RAW-OIL (LOT-R1) automatically blocks 100 KG and creates Pending QC inspection
        var rcv = await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "RCV-QC1", InventoryTransactionType.Receipt, null, "Raw oil delivery", null,
            [new InventoryLineInput(rawOil.Id, "LOT-R1", 100m, kg.Id, null, new DateOnly(2026, 10, 1), new DateOnly(2027, 10, 1))]));
        Assert.True(rcv.Succeeded);

        var balPage = await invUseCases.ListBalances(siteAScope, orgId, new InventoryBalanceListQuery());
        var rawBal = Assert.Single(balPage.Value!.Items);
        Assert.Equal(100m, rawBal.OnHandBaseQuantity);
        Assert.Equal(100m, rawBal.BlockedBaseQuantity);
        Assert.Equal(0m, rawBal.AvailableBaseQuantity);

        var inspPage = await qcUseCases.ListInspections(siteAScope, orgId, new QualityInspectionListQuery());
        var insp = Assert.Single(inspPage.Value!.Items);
        Assert.Equal(QualityInspectionStatus.Pending, insp.Status);

        // 2. Site B cannot view Site A's inspection
        var crossSite = await qcUseCases.GetInspection(siteBScope, orgId, insp.Id);
        Assert.False(crossSite.Succeeded);
        Assert.Equal(QualityErrors.AccessDenied, crossSite.Error!.Code);

        // 3. Create & Release PO for 20 Bottles (requires 40 KG RAW-OIL). While blocked, Issue fails and Shortage shows 40 KG short!
        var po = (await poUseCases.CreateDraftOrder(siteAScope,
            new CreateProductionOrderRequest(orgId, siteAId, "PO-Q1", fgBottle.Id, 20m, pcs.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 20)))).Value!;
        po = (await poUseCases.ReleaseOrder(siteAScope, new ReleaseProductionOrderRequest(orgId, po.Summary.Id, po.Summary.RowVersion))).Value!;
        po = (await poUseCases.StartOrder(siteAScope, new StartProductionOrderRequest(orgId, po.Summary.Id, po.Summary.RowVersion))).Value!;

        var shortageWhileBlocked = await rptUseCases.GetMaterialShortageReport(siteAScope, orgId, new MaterialShortageQuery());
        var shortRow = Assert.Single(shortageWhileBlocked.Value!.Items);
        Assert.Equal(100m, shortRow.OnHandBaseQuantity);
        Assert.Equal(100m, shortRow.BlockedBaseQuantity);
        Assert.Equal(0m, shortRow.AvailableBaseQuantity);
        Assert.Equal(40m, shortRow.ShortageBaseQuantity);

        var blockedIssue = await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "ISS-FAIL", InventoryTransactionType.ProductionIssue, po.Summary.Id, null, null,
            [new InventoryLineInput(rawOil.Id, "LOT-R1", 40m, kg.Id)]));
        Assert.False(blockedIssue.Succeeded);
        Assert.Equal(InventoryErrors.InsufficientStock, blockedIssue.Error!.Code);

        // 4. Record lab lines + Partial disposition: Accept 80 KG, Reject/Scrap 20 KG
        var measRes = await qcUseCases.UpdateMeasurements(siteAScope, new UpdateQualityMeasurementsRequest(
            orgId, insp.Id, "MOISTURE", "Moisture test",
            [new QualityInspectionLineInput("Moisture", "< 0.2%", "0.12%", true)], insp.RowVersion));
        Assert.True(measRes.Succeeded);

        var dispRes = await qcUseCases.RecordDisposition(siteAScope, new RecordQualityDispositionRequest(
            orgId, insp.Id, 80m, 20m, kg.Id, "SLUDGE", "Top sediment scrapped", "QSCR-1", measRes.Value!.Summary.RowVersion));
        Assert.True(dispRes.Succeeded);
        Assert.Equal(QualityInspectionStatus.Completed, dispRes.Value!.Summary.Status);

        balPage = await invUseCases.ListBalances(siteAScope, orgId, new InventoryBalanceListQuery());
        rawBal = Assert.Single(balPage.Value!.Items);
        Assert.Equal(80m, rawBal.OnHandBaseQuantity);
        Assert.Equal(0m, rawBal.BlockedBaseQuantity);
        Assert.Equal(80m, rawBal.AvailableBaseQuantity);

        // 5. Issue 40 KG LOT-R1 & Receive 20 PCS LOT-FG1 (auto-QC triggers for FG too!)
        var issueOk = await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "ISS-OK", InventoryTransactionType.ProductionIssue, po.Summary.Id, null, null,
            [new InventoryLineInput(rawOil.Id, "LOT-R1", 40m, kg.Id)]));
        Assert.True(issueOk.Succeeded);

        var recFg = await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "REC-FG1", InventoryTransactionType.ProductionReceipt, po.Summary.Id, null, null,
            [new InventoryLineInput(fgBottle.Id, "LOT-FG1", 20m, pcs.Id)]));
        Assert.True(recFg.Succeeded);

        // 6. TraceLot backward from LOT-FG1 resolves LOT-R1
        var trace = await qcUseCases.TraceLot(siteAScope, orgId, new LotTraceQuery(fgBottle.Id, "LOT-FG1", null, LotTraceDirection.Backward));
        Assert.True(trace.Succeeded);
        var edge = Assert.Single(trace.Value!.BackwardEdges);
        Assert.Equal("LOT-R1", edge.SourceLotCode);
        Assert.Equal(40m, edge.SourceNetBaseQuantity);

        // 7. Expired lot blocks outbound Issue, then succeeds after QC extends expiration date
        var expireRes = await qcUseCases.UpsertItemLotMetadata(siteAScope, new UpsertItemLotMetadataRequest(
            orgId, rawOil.Id, "LOT-R1", new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31), "Expired"));
        Assert.True(expireRes.Succeeded);

        var expiredIssue = await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "ISS-EXP", InventoryTransactionType.Issue, null, null, null,
            [new InventoryLineInput(rawOil.Id, "LOT-R1", 5m, kg.Id)]));
        Assert.False(expiredIssue.Succeeded);
        Assert.Equal(InventoryErrors.LotExpired, expiredIssue.Error!.Code);

        var extendRes = await qcUseCases.UpsertItemLotMetadata(siteAScope, new UpsertItemLotMetadataRequest(
            orgId, rawOil.Id, "LOT-R1", new DateOnly(2020, 1, 1), new DateOnly(2030, 12, 31), "Re-tested & extended", expireRes.Value!.RowVersion));
        Assert.True(extendRes.Succeeded);

        var afterExtendIssue = await invUseCases.PostTransaction(siteAScope, new PostInventoryTransactionRequest(
            orgId, whA.Id, null, "ISS-EXT", InventoryTransactionType.Issue, null, null, null,
            [new InventoryLineInput(rawOil.Id, "LOT-R1", 5m, kg.Id)]));
        Assert.True(afterExtendIssue.Succeeded);
    }

    private sealed class FixedActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class AllowAllPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default) =>
            ValueTask.FromResult(new PermissionDecision(true));
    }

    private sealed class DisposableQualityDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableQualityDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            var dbConn = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(dbConn).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableQualityDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_qc_{Guid.NewGuid():N}";
            var db = new DisposableQualityDatabase(builder, name);
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
