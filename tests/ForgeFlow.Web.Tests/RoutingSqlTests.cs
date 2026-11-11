using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Routings;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class RoutingSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

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

    [OrganizationSqlServerFact]
    public async Task S01_S02_work_centers_routings_production_snapshot_and_operation_execution_work_end_to_end()
    {
        await using var database = await DisposableRoutingDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var routingStore = new SqlRoutingOperationsStore(database.Factory);
        var moStore = new SqlProductionOrderOperationsStore(database.Factory);

        long orgId, siteAId, siteBId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-RT", "Routing Org");
            var siteA = org.CreateSite("SITE-A", "Plant Alpha");
            var siteB = org.CreateSite("SITE-B", "Plant Beta");
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

        var rawSteel = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "RAW-BAR", "Steel Bar", null, kg.Id, true, false, false, true, false, QualityControlMode.None), access, default)).Value!.Summary;
        var fgGear = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "FG-GEAR", "Drive Gear", null, pcs.Id, false, true, true, false, false, QualityControlMode.None), access, default)).Value!.Summary;

        Assert.True((await bomStore.SeedStandardContextDimensions(orgId, access, default)).Succeeded);
        var bom = (await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-GEAR", "Gear BOM", fgGear.Id), access, default)).Value!;
        var bv1 = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 1, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, "v1"), access, default)).Value!;
        bv1 = (await bomStore.SaveDraftBomLines(orgId, bv1.Summary.Id, Convert.FromBase64String(bv1.Summary.RowVersion),
            [new BomInputLineInput(1, rawSteel.Id, 2m, kg.Id, 0m, false, null)],
            [new BomOutputLineInput(1, fgGear.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        bv1 = (await bomStore.TransitionBomVersionStatus(orgId, bv1.Summary.Id, Convert.FromBase64String(bv1.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        bv1 = (await bomStore.TransitionBomVersionStatus(orgId, bv1.Summary.Id, Convert.FromBase64String(bv1.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;

        var actor = Attribution();
        var perm = new AllowAllPermissions();
        var rtUseCases = new RoutingUseCases(routingStore, new FixedActor(actor), perm);
        var moUseCases = new ProductionOrderUseCases(moStore, bomStore, new FixedActor(actor), perm, null, null, routingStore);
        var siteAScope = new OrganizationScope.Site(orgId, siteAId);
        var siteBScope = new OrganizationScope.Site(orgId, siteBId);

        // 1. Work centers per site (same code CNC-01 allowed on Site A and Site B; duplicate on same site rejected)
        var wcA1 = (await rtUseCases.CreateWorkCenter(siteAScope, new CreateWorkCenterRequest(orgId, siteAId, "CNC-01", "CNC Turning A", WorkCenterType.Machine, 20m, 8m, 95m, null))).Value!;
        var wcA2 = (await rtUseCases.CreateWorkCenter(siteAScope, new CreateWorkCenterRequest(orgId, siteAId, "QC-01", "Gear Inspection", WorkCenterType.QualityStation, 50m, 8m, null, null))).Value!;
        var wcB1 = (await rtUseCases.CreateWorkCenter(siteBScope, new CreateWorkCenterRequest(orgId, siteBId, "CNC-01", "CNC Turning B", WorkCenterType.Machine, 15m, 8m, null, null))).Value!;

        var dupWc = await rtUseCases.CreateWorkCenter(siteAScope, new CreateWorkCenterRequest(orgId, siteAId, "CNC-01", "Dup", WorkCenterType.Machine, 10m, 8m, null, null));
        Assert.False(dupWc.Succeeded);
        Assert.Equal(RoutingErrors.WorkCenterCodeDuplicate, dupWc.Error!.Code);

        // 2. Create Draft Routing on Site A, verify cross-site WorkCenter rejection (W-Q01)
        var rt1 = (await rtUseCases.CreateDraftRouting(siteAScope, new CreateRoutingRequest(orgId, siteAId, "RT-GEAR", "Standard Gear Routing", fgGear.Id, bv1.Summary.Id, 1, true, "Main path"))).Value!;
        var badSiteOp = await rtUseCases.SaveRoutingOperations(siteAScope, new SaveRoutingOperationsRequest(orgId, rt1.Summary.Id,
            [new RoutingOperationInput(10, "OP-10", "Turn", wcB1.Id, 15m, 3m)], rt1.Summary.RowVersion));
        Assert.False(badSiteOp.Succeeded);
        Assert.Equal(RoutingErrors.WorkCenterNotFound, badSiteOp.Error!.Code);

        rt1 = (await rtUseCases.SaveRoutingOperations(siteAScope, new SaveRoutingOperationsRequest(orgId, rt1.Summary.Id,
            [
                new RoutingOperationInput(10, "OP-10", "CNC Turning", wcA1.Id, 20m, 4m, 5m, 0m, false, "Rough & finish cut"),
                new RoutingOperationInput(20, "OP-20", "Final Check", wcA2.Id, 5m, 1m, 0m, 5m, true, "Micrometer check")
            ], rt1.Summary.RowVersion))).Value!;
        Assert.Equal(2, rt1.Operations.Count);
        Assert.Equal(25m, rt1.Summary.TotalSetupMinutes);
        Assert.Equal(5m, rt1.Summary.TotalRunMinutesPerUnit);

        rt1 = (await rtUseCases.ActivateRouting(siteAScope, new ActivateRoutingRequest(orgId, rt1.Summary.Id, rt1.Summary.RowVersion))).Value!;
        Assert.Equal(RoutingStatus.Active, rt1.Summary.Status);

        // Activating a second IsDefault routing on same (site, item) clears default flag on v1 without violating UX_Routing_Org_Site_Item_Default
        var rt2 = (await rtUseCases.CreateDraftRouting(siteAScope, new CreateRoutingRequest(orgId, siteAId, "RT-GEAR", "Fast Gear Routing", fgGear.Id, bv1.Summary.Id, 2, true, null))).Value!;
        rt2 = (await rtUseCases.SaveRoutingOperations(siteAScope, new SaveRoutingOperationsRequest(orgId, rt2.Summary.Id,
            [new RoutingOperationInput(10, "OP-10", "Fast CNC", wcA1.Id, 10m, 2m, 0m, 0m, false, null)], rt2.Summary.RowVersion))).Value!;
        rt2 = (await rtUseCases.ActivateRouting(siteAScope, new ActivateRoutingRequest(orgId, rt2.Summary.Id, rt2.Summary.RowVersion))).Value!;

        var reloadedV1 = (await rtUseCases.GetRouting(siteAScope, orgId, rt1.Summary.Id)).Value!;
        Assert.False(reloadedV1.Summary.IsDefault);
        Assert.True(rt2.Summary.IsDefault);

        // 3. Create ProductionOrder WITHOUT explicit RoutingId -> auto-resolves default rt2, freezes 10 min setup + 2*10=20 min run = 30 min
        var mo1 = (await moUseCases.CreateDraftOrder(siteAScope, new CreateProductionOrderRequest(
            orgId, siteAId, "MO-GEAR-01", fgGear.Id, 10m, pcs.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 5), null, "Auto default routing"))).Value!;
        Assert.Equal(rt2.Summary.Id, mo1.Summary.RoutingId);
        Assert.Equal("RT-GEAR", mo1.Summary.RoutingCodeSnapshot);
        var autoOp = Assert.Single(mo1.Operations!);
        Assert.Equal(10m, autoOp.PlannedSetupMinutes);
        Assert.Equal(20m, autoOp.PlannedRunMinutes);
        Assert.Equal(ProductionOrderOperationStatus.Pending, autoOp.Status);

        // Update Draft to explicitly choose rt1 (v1) -> replaces operations cleanly
        mo1 = (await moUseCases.UpdateDraftOrder(siteAScope, new UpdateDraftProductionOrderRequest(
            orgId, mo1.Summary.Id, 10m, pcs.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 5), null, "Explicit v1", mo1.Summary.RowVersion, rt1.Summary.Id))).Value!;
        Assert.Equal(rt1.Summary.Id, mo1.Summary.RoutingId);
        Assert.Equal(2, mo1.Operations!.Count);
        Assert.Equal(20m, mo1.Operations[0].PlannedSetupMinutes);
        Assert.Equal(40m, mo1.Operations[0].PlannedRunMinutes);

        // Work Center planned load includes MO-GEAR-01 Pending/Ready ops (wcA1: 20+40+5 = 65 min -> 65/480 = 13.5%)
        var loadsBeforeRelease = (await rtUseCases.GetWorkCenterLoads(siteAScope, orgId, siteAId)).Value!;
        var wcA1Load = loadsBeforeRelease.Single(l => l.WorkCenterId == wcA1.Id);
        Assert.Equal(1, wcA1Load.ActiveOperationCount);
        Assert.Equal(65m, wcA1Load.TotalPlannedMinutes);
        Assert.Equal(13.5m, wcA1Load.UtilizationPercent);

        // Release order -> operations transition Pending -> Ready
        mo1 = (await moUseCases.ReleaseOrder(siteAScope, new ReleaseProductionOrderRequest(orgId, mo1.Summary.Id, mo1.Summary.RowVersion))).Value!;
        Assert.All(mo1.Operations!, o => Assert.Equal(ProductionOrderOperationStatus.Ready, o.Status));

        // Shop-floor operation execution: OP-10 Start -> Pause -> Complete; OP-20 Skip
        var op10 = mo1.Operations![0];
        var op20 = mo1.Operations![1];

        var started = (await rtUseCases.StartOperation(siteAScope, new StartProductionOrderOperationRequest(orgId, op10.Id, "Started CNC", op10.RowVersion))).Value!;
        Assert.Equal(ProductionOrderOperationStatus.InProgress, started.Status);
        Assert.NotNull(started.StartedAtUtc);

        var paused = (await rtUseCases.PauseOperation(siteAScope, new PauseProductionOrderOperationRequest(orgId, op10.Id, 18m, 20m, 5m, 0m, "Tool check", started.RowVersion))).Value!;
        Assert.Equal(ProductionOrderOperationStatus.Paused, paused.Status);

        var restarted = (await rtUseCases.StartOperation(siteAScope, new StartProductionOrderOperationRequest(orgId, op10.Id, "Resumed", paused.RowVersion))).Value!;
        var completed = (await rtUseCases.CompleteOperation(siteAScope, new CompleteProductionOrderOperationRequest(orgId, op10.Id, 18m, 39m, 10m, 0m, "All good", restarted.RowVersion))).Value!;
        Assert.Equal(ProductionOrderOperationStatus.Completed, completed.Status);
        Assert.Equal(10m, completed.CompletedQuantity);

        var skipped = (await rtUseCases.SkipOperation(siteAScope, new SkipProductionOrderOperationRequest(orgId, op20.Id, "Sampled on line", op20.RowVersion))).Value!;
        Assert.Equal(ProductionOrderOperationStatus.Skipped, skipped.Status);

        // Once all ops Completed/Skipped, active load drops to 0!
        var loadsAfterOps = (await rtUseCases.GetWorkCenterLoads(siteAScope, orgId, siteAId)).Value!;
        Assert.All(loadsAfterOps, l => Assert.Equal(0, l.ActiveOperationCount));

        // 4. Routing-less order on Site B (no active routing for fgGear on Site B) still works seamlessly!
        var moB = (await moUseCases.CreateDraftOrder(siteBScope, new CreateProductionOrderRequest(
            orgId, siteBId, "MO-NO-RT", fgGear.Id, 5m, pcs.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 3), null, "No routing on Site B"))).Value!;
        Assert.Null(moB.Summary.RoutingId);
        Assert.Null(moB.Summary.RoutingCodeSnapshot);
        Assert.Empty(moB.Operations!);

        // Audit logs verify organization.OrganizationChange accepted every Slice 11 event
        await using var auditDb = await database.Factory.CreateDbContextAsync();
        var codes = await auditDb.OrganizationChanges.AsNoTracking().Select(c => c.EventCode).ToListAsync();
        Assert.Contains("WorkCenter.Created", codes);
        Assert.Contains("Routing.Created", codes);
        Assert.Contains("Routing.OperationsSaved", codes);
        Assert.Contains("Routing.Activated", codes);
        Assert.Contains("ProductionOrderOperation.Started", codes);
        Assert.Contains("ProductionOrderOperation.Paused", codes);
        Assert.Contains("ProductionOrderOperation.Completed", codes);
        Assert.Contains("ProductionOrderOperation.Skipped", codes);
    }

    private sealed class DisposableRoutingDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableRoutingDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            var dbConn = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(dbConn).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableRoutingDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_rt_{Guid.NewGuid():N}";
            var db = new DisposableRoutingDatabase(builder, name);
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
