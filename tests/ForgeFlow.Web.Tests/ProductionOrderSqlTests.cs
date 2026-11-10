using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ProductionOrderSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task S01_S04_production_orders_frozen_snapshots_site_isolation_and_lifecycle_in_sql()
    {
        await using var database = await DisposableProductionOrderDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var orderStore = new SqlProductionOrderOperationsStore(database.Factory);

        long orgId;
        long siteAId;
        long siteBId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-PO", "Production Org");
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

        var steel = (await itemStore.CreateItem(new CreateItemRequest(orgId, "RAW-STL", "Raw Steel", null, kg.Id, true, false, false, true), access, default)).Value!.Summary;
        var alum = (await itemStore.CreateItem(new CreateItemRequest(orgId, "RAW-ALU", "Raw Aluminum", null, kg.Id, true, false, false, true), access, default)).Value!.Summary;
        var titanium = (await itemStore.CreateItem(new CreateItemRequest(orgId, "RAW-TI", "Raw Titanium", null, kg.Id, true, false, false, true), access, default)).Value!.Summary;
        var frame = (await itemStore.CreateItem(new CreateItemRequest(orgId, "FG-FRAME", "Main Frame", null, pcs.Id, false, true, true, false), access, default)).Value!.Summary;

        Assert.True((await bomStore.SeedStandardContextDimensions(orgId, access, default)).Succeeded);
        var plantDim = (await bomStore.ListActiveContextDimensions(orgId, default)).Single(d => d.Code == "PLANT");

        var bom = (await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-FRAME", "Frame Recipe", frame.Id), access, default)).Value!;

        // Create and activate BOM v1 (2 kg Raw Steel per PCS for PLANT=SITE-A)
        var v1 = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 1, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, "v1"), access, default)).Value!;
        v1 = (await bomStore.SaveDraftBomLines(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion),
            [new BomInputLineInput(1, steel.Id, 2m, kg.Id, 0m, false, null)],
            [new BomOutputLineInput(1, frame.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        v1 = (await bomStore.SaveDraftBomApplicability(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion),
            new SaveDraftBomApplicabilityRequest(orgId, v1.Summary.Id, true, "Site A", true, [new BomApplicabilityConditionInput(plantDim.Id, ApplicabilityOperator.Equals, "SITE-A")], v1.Summary.RowVersion), access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;

        var actor = Attribution();
        var siteAScope = new OrganizationScope.Site(orgId, siteAId);
        var siteBScope = new OrganizationScope.Site(orgId, siteBId);
        var orgScope = new OrganizationScope.Organization(orgId);
        var orderUseCases = new ProductionOrderUseCases(
            orderStore,
            bomStore,
            new StaticActor(actor),
            new ScopePerm((scope, _) => scope is OrganizationScope.Site s && s.SiteId == siteAId || scope is OrganizationScope.Organization));

        // Site-A operator cannot create for Site-B
        var deniedCreate = await orderUseCases.CreateDraftOrder(siteAScope, new CreateProductionOrderRequest(orgId, siteBId, "PO-B1", frame.Id, 10m, pcs.Id, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 10)));
        Assert.False(deniedCreate.Succeeded);
        Assert.Equal(ProductionOrderErrors.AccessDenied, deniedCreate.Error!.Code);

        // Create Draft PO-001 at Site-A (resolves v1: 10 * 2 kg = 20 kg Steel)
        var draft = (await orderUseCases.CreateDraftOrder(siteAScope, new CreateProductionOrderRequest(orgId, siteAId, "PO-001", frame.Id, 10m, pcs.Id, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 10), null, "Initial draft"))).Value!;
        Assert.Equal(1, draft.Summary.BomVersionNumberSnapshot);
        Assert.Equal(steel.Id, draft.Inputs.Single().ItemId);
        Assert.Equal(20m, draft.Inputs.Single().GrossQuantity);
        Assert.Equal("SITE-A", draft.Contexts.Single(c => c.DimensionCode == "PLANT").DimensionValue);

        // Activate v2 (5 kg Aluminum), supersede v1 -> RefreshDraftSnapshot upgrades Draft PO-001 to v2
        var v2 = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 2, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, "v2"), access, default)).Value!;
        v2 = (await bomStore.SaveDraftBomLines(orgId, v2.Summary.Id, Convert.FromBase64String(v2.Summary.RowVersion),
            [new BomInputLineInput(1, alum.Id, 5m, kg.Id, 0m, false, null)],
            [new BomOutputLineInput(1, frame.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        v2 = (await bomStore.TransitionBomVersionStatus(orgId, v2.Summary.Id, Convert.FromBase64String(v2.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        v2 = (await bomStore.TransitionBomVersionStatus(orgId, v2.Summary.Id, Convert.FromBase64String(v2.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;
        _ = await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Superseded, access, default);

        draft = (await orderUseCases.RefreshDraftSnapshot(siteAScope, new RefreshProductionOrderSnapshotRequest(orgId, draft.Summary.Id, draft.Summary.RowVersion))).Value!;
        Assert.Equal(2, draft.Summary.BomVersionNumberSnapshot);
        Assert.Equal(alum.Id, draft.Inputs.Single().ItemId);
        Assert.Equal(50m, draft.Inputs.Single().GrossQuantity);

        // Release PO-001 (permanently freezes v2)
        var released = (await orderUseCases.ReleaseOrder(siteAScope, new ReleaseProductionOrderRequest(orgId, draft.Summary.Id, draft.Summary.RowVersion))).Value!;
        Assert.Equal(ProductionOrderStatus.Released, released.Summary.Status);
        Assert.NotNull(released.Summary.ReleasedAtUtc);

        // Activate v3 (10 kg Titanium) + rename BOM header after PO-001 is Released
        var v3 = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 3, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, "v3"), access, default)).Value!;
        v3 = (await bomStore.SaveDraftBomLines(orgId, v3.Summary.Id, Convert.FromBase64String(v3.Summary.RowVersion),
            [new BomInputLineInput(1, titanium.Id, 10m, kg.Id, 0m, false, null)],
            [new BomOutputLineInput(1, frame.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        v3 = (await bomStore.TransitionBomVersionStatus(orgId, v3.Summary.Id, Convert.FromBase64String(v3.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        v3 = (await bomStore.TransitionBomVersionStatus(orgId, v3.Summary.Id, Convert.FromBase64String(v3.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;
        _ = await bomStore.TransitionBomVersionStatus(orgId, v2.Summary.Id, Convert.FromBase64String(v2.Summary.RowVersion), BomVersionStatus.Superseded, access, default);

        // Start, RecordActuals, Complete -> Snapshot still holds frozen v2 (Aluminum 50 kg)!
        var started = (await orderUseCases.StartOrder(siteAScope, new StartProductionOrderRequest(orgId, released.Summary.Id, released.Summary.RowVersion))).Value!;
        Assert.Equal(ProductionOrderStatus.InProgress, started.Summary.Status);
        var actuals = (await orderUseCases.RecordActuals(siteAScope, new RecordProductionOrderActualsRequest(orgId, started.Summary.Id, 6m, 0.5m, "Shift 1", started.Summary.RowVersion))).Value!;
        Assert.Equal(6m, actuals.Summary.ActualProducedQuantity);
        var completed = (await orderUseCases.CompleteOrder(siteAScope, new CompleteProductionOrderRequest(orgId, actuals.Summary.Id, 9.5m, 0.5m, "Completed run", actuals.Summary.RowVersion))).Value!;
        Assert.Equal(ProductionOrderStatus.Completed, completed.Summary.Status);
        Assert.Equal(2, completed.Summary.BomVersionNumberSnapshot);
        Assert.Equal(alum.Id, completed.Inputs.Single().ItemId);
        Assert.Equal(50m, completed.Inputs.Single().GrossQuantity);

        // Create second order at Site-B (via orgScope), verify Site-A operator is isolated and CancelOrder records reason
        var poSiteB = (await orderUseCases.CreateDraftOrder(orgScope, new CreateProductionOrderRequest(orgId, siteBId, "PO-B1", frame.Id, 4m, pcs.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3)))).Value!;
        var deniedSiteBRead = await orderUseCases.GetOrder(siteAScope, orgId, poSiteB.Summary.Id);
        Assert.False(deniedSiteBRead.Succeeded);
        Assert.Equal(ProductionOrderErrors.AccessDenied, deniedSiteBRead.Error!.Code);

        var siteAList = (await orderUseCases.ListOrders(siteAScope, orgId, new ProductionOrderListQuery())).Value!;
        Assert.Single(siteAList.Items);
        Assert.Equal("PO-001", siteAList.Items[0].OrderNumber);

        var cancelled = (await orderUseCases.CancelOrder(orgScope, new CancelProductionOrderRequest(orgId, poSiteB.Summary.Id, "Material shortage", poSiteB.Summary.RowVersion))).Value!;
        Assert.Equal(ProductionOrderStatus.Cancelled, cancelled.Summary.Status);
        Assert.Equal("Material shortage", cancelled.Summary.CancellationReason);
        Assert.NotNull(cancelled.Summary.CancelledAtUtc);

        // Verify all audit events were persisted with non-null SiteId matching the order's site
        await using var verifyDb = await database.Factory.CreateDbContextAsync();
        var poEvents = await verifyDb.OrganizationChanges
            .Where(c => c.OrganizationId == orgId && c.EventCode.StartsWith("ProductionOrder."))
            .OrderBy(c => c.Id)
            .ToListAsync();
        Assert.Contains(poEvents, e => e.EventCode == "ProductionOrder.Created" && e.SiteId == siteAId);
        Assert.Contains(poEvents, e => e.EventCode == "ProductionOrder.Refreshed" && e.SiteId == siteAId);
        Assert.Contains(poEvents, e => e.EventCode == "ProductionOrder.Released" && e.SiteId == siteAId);
        Assert.Contains(poEvents, e => e.EventCode == "ProductionOrder.Started" && e.SiteId == siteAId);
        Assert.Contains(poEvents, e => e.EventCode == "ProductionOrder.Completed" && e.SiteId == siteAId);
        Assert.Contains(poEvents, e => e.EventCode == "ProductionOrder.Cancelled" && e.SiteId == siteBId);
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

    private sealed class DisposableProductionOrderDatabase : IAsyncDisposable
    {
        private readonly string name = "ForgeFlow_Po_Test_" + Guid.NewGuid().ToString("N");
        private readonly SqlConnectionStringBuilder server;
        private bool created;

        private DisposableProductionOrderDatabase(string connection)
        {
            server = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", ConnectTimeout = 5, Pooling = false };
            var target = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name };
            Factory = new TestDbContextFactory(new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(target.ConnectionString).Options);
        }

        internal IDbContextFactory<OrganizationDbContext> Factory { get; }

        internal static async Task<DisposableProductionOrderDatabase> Create()
        {
            var db = new DisposableProductionOrderDatabase(Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER")!);
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
