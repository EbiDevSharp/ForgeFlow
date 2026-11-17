using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ParallelPageDataLoaderTests
{
    private static TrustedActor Attribution(long actorId = 1) =>
        new(actorId, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    private static OrganizationWriteAccess CreateAccess(TrustedActor actor) =>
        new(actor, _ => ValueTask.FromResult(true));

    [OrganizationSqlServerFact]
    public async Task Concurrent_Page_Subqueries_Execute_Without_DbContext_Concurrency_Conflicts()
    {
        await using var database = await DisposableParallelDatabase.Create();
        var actor = Attribution();
        var access = CreateAccess(actor);

        long orgId;
        long siteId;

        // Seed organization and site
        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("PAR-TEST-ORG", "Parallel Test Org");
            var site = org.CreateSite("PAR-SITE-1", "Parallel Site 1");
            db.Organizations.Add(org);
            await db.SaveChangesAsync(actor);

            orgId = org.Id;
            siteId = site.Id;
        }

        // Instantiate stores that use IDbContextFactory<OrganizationDbContext>
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var orderStore = new SqlProductionOrderOperationsStore(database.Factory);
        var orgStore = new OrganizationOperationsStore(database.Factory);

        // Seed domain items through stores
        var seedUnits = await unitStore.SeedStandardUnits(orgId, access, default);
        Assert.True(seedUnits.Succeeded);
        var pcs = (await unitStore.ListActiveUnits(orgId, default)).Single(u => u.Code == "PCS");

        var createdItem = await itemStore.CreateItem(
            new CreateItemRequest(orgId, "ITEM-PAR", "Parallel Item", "Description", pcs.Id, true, true, false, false),
            access, default);
        Assert.True(createdItem.Succeeded);

        var createdWh = await invStore.CreateWarehouse(
            new CreateWarehouseRequest(orgId, siteId, "WH-PAR", "Parallel Warehouse", true),
            access, default);
        Assert.True(createdWh.Succeeded);

        // Simulate 15 parallel user sessions hitting the page concurrently
        const int sessionCount = 15;
        var sessionTasks = Enumerable.Range(0, sessionCount).Select(async sessionId =>
        {
            // Within each session, execute independent queries in parallel via Task.WhenAll
            // Exactly matching ProductionOrders / Inventory / Quality page patterns
            var sitesTask = orgStore.ListSites(orgId, null, new OrganizationListQuery(IncludeInactive: false, Page: 1, PageSize: 100), default);
            var itemsTask = itemStore.ListItems(orgId, new ItemListQuery(CanBeManufactured: true, IncludeInactive: false, Page: 1, PageSize: 100), default);
            var unitsTask = unitStore.ListUnits(orgId, new UnitListQuery(IncludeInactive: false, Page: 1, PageSize: 100), default);
            var dimsTask = bomStore.ListContextDimensions(orgId, new ContextDimensionListQuery(IncludeInactive: false, Page: 1, PageSize: 100), default);
            var whTask = invStore.ListWarehouses(orgId, new WarehouseListQuery(IncludeInactive: false, Page: 1, PageSize: 100), default);
            var relOrdersTask = orderStore.ListOrders(orgId, new ProductionOrderListQuery(Status: ProductionOrderStatus.Released, Page: 1, PageSize: 50), default);
            var progOrdersTask = orderStore.ListOrders(orgId, new ProductionOrderListQuery(Status: ProductionOrderStatus.InProgress, Page: 1, PageSize: 50), default);

            await Task.WhenAll(sitesTask, itemsTask, unitsTask, dimsTask, whTask, relOrdersTask, progOrdersTask);

            var sites = await sitesTask;
            var items = await itemsTask;
            var units = await unitsTask;
            var dims = await dimsTask;
            var whs = await whTask;
            var rel = await relOrdersTask;
            var prog = await progOrdersTask;

            Assert.NotNull(sites);
            Assert.NotEmpty(sites.Items);
            Assert.NotNull(items);
            Assert.NotEmpty(items.Items);
            Assert.NotNull(units);
            Assert.NotEmpty(units.Items);
            Assert.NotNull(dims);
            Assert.NotNull(whs);
            Assert.NotEmpty(whs.Items);
            Assert.NotNull(rel);
            Assert.NotNull(prog);
        });

        // All 15 sessions x 7 parallel queries = 105 concurrent DbContext operations
        await Task.WhenAll(sessionTasks);
    }

    private sealed class DisposableParallelDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableParallelDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            var connectionString = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(connectionString).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableParallelDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_par_{Guid.NewGuid():N}";
            var db = new DisposableParallelDatabase(builder, name);
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
