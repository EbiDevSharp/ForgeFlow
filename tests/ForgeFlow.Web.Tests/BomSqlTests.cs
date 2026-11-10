using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class BomSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task M01_M07_boms_versions_applicability_audit_and_explosion_are_enforced_in_sql()
    {
        await using var database = await DisposableBomDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);

        long orgId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-BOM", "BOM Org");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(Attribution());
            orgId = org.Id;
        }

        var access = new OrganizationWriteAccess(Attribution(), _ => ValueTask.FromResult(true));
        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var units = await unitStore.ListActiveUnits(orgId, default);
        var pcs = units.Single(u => u.Code == "PCS");
        var kg = units.Single(u => u.Code == "KG");

        // Create raw material (not manufacturable) and finished good (manufacturable)
        var rawRes = await itemStore.CreateItem(new CreateItemRequest(orgId, "RAW-STEEL", "Raw Steel", null, kg.Id, true, false, false, true), access, default);
        var fgRes = await itemStore.CreateItem(new CreateItemRequest(orgId, "FG-FRAME", "Steel Frame", null, pcs.Id, false, true, true, false), access, default);
        Assert.True(rawRes.Succeeded);
        Assert.True(fgRes.Succeeded);

        // Non-manufacturable primary output item is rejected
        var badBom = await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-RAW", "Invalid Raw BOM", rawRes.Value!.Summary.Id), access, default);
        Assert.False(badBom.Succeeded);
        Assert.Equal(BomErrors.PrimaryItemNotManufacturable, badBom.Error!.Code);

        // Seed context dimensions idempotently
        var seed1 = await bomStore.SeedStandardContextDimensions(orgId, access, default);
        var seed2 = await bomStore.SeedStandardContextDimensions(orgId, access, default);
        Assert.True(seed1.Succeeded && seed1.Value!.CreatedCount == 5);
        Assert.True(seed2.Succeeded && seed2.Value!.CreatedCount == 0);
        var plantDim = (await bomStore.ListActiveContextDimensions(orgId, default)).Single(d => d.Code == "PLANT");

        // Create valid BOM + v1 Draft -> fails transition without lines -> save lines + applicability -> transition Active
        var bomRes = await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-FRAME", "Frame BOM", fgRes.Value!.Summary.Id), access, default);
        Assert.True(bomRes.Succeeded);

        var verRes = await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bomRes.Value!.Summary.Id, 1, 2m, pcs.Id, new DateOnly(2026, 1, 1), null, "v1"), access, default);
        Assert.True(verRes.Succeeded);

        var emptyApprove = await bomStore.TransitionBomVersionStatus(orgId, verRes.Value!.Summary.Id, Convert.FromBase64String(verRes.Value.Summary.RowVersion), BomVersionStatus.Active, access, default);
        Assert.False(emptyApprove.Succeeded);
        Assert.Equal(BomErrors.InputsRequired, emptyApprove.Error!.Code);

        var linesSaved = await bomStore.SaveDraftBomLines(
            orgId,
            verRes.Value.Summary.Id,
            Convert.FromBase64String(verRes.Value.Summary.RowVersion),
            [new BomInputLineInput(1, rawRes.Value.Summary.Id, 4m, kg.Id, 10m, false, null)],
            [new BomOutputLineInput(1, fgRes.Value.Summary.Id, BomOutputType.MainProduct, 2m, pcs.Id)],
            access,
            default);
        Assert.True(linesSaved.Succeeded);

        var appSaved = await bomStore.SaveDraftBomApplicability(
            orgId,
            verRes.Value.Summary.Id,
            Convert.FromBase64String(linesSaved.Value!.Summary.RowVersion),
            new SaveDraftBomApplicabilityRequest(orgId, verRes.Value.Summary.Id, true, "Tehran Plant", true, [new BomApplicabilityConditionInput(plantDim.Id, ApplicabilityOperator.Equals, "TEH")], null),
            access,
            default);
        Assert.True(appSaved.Succeeded);

        var activated = await bomStore.TransitionBomVersionStatus(orgId, verRes.Value.Summary.Id, Convert.FromBase64String(appSaved.Value!.Summary.RowVersion), BomVersionStatus.Active, access, default);
        Assert.True(activated.Succeeded);
        Assert.Equal(BomVersionStatus.Active, activated.Value!.Summary.Status);

        // Runtime DB role cannot DELETE Bom / BomVersion / ContextDimension
        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync("CREATE USER [BomPermissionFixture] WITHOUT LOGIN; ALTER ROLE [ForgeFlowOrganizationRuntime] ADD MEMBER [BomPermissionFixture];");
            foreach (var stmt in new[] { "DELETE FROM [organization].[Bom]", "DELETE FROM [organization].[BomVersion]", "DELETE FROM [organization].[ContextDimension]" })
            {
                var sql = "EXECUTE AS USER = 'BomPermissionFixture'; BEGIN TRY " + stmt + "; END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;";
                Assert.Equal(229, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql))).Number);
            }

            // Audit events recorded
            var events = await db.OrganizationChanges.AsNoTracking().Where(c => c.OrganizationId == orgId).Select(c => c.EventCode).ToListAsync();
            Assert.Contains("Bom.Created", events);
            Assert.Contains("BomVersion.Created", events);
            Assert.Contains("BomVersion.LinesSaved", events);
            Assert.Contains("BomVersion.ApplicabilitySaved", events);
            Assert.Contains("BomVersion.StatusChanged", events);
            Assert.Contains("ContextDimension.Created", events);
        }
    }

    private sealed class DisposableBomDatabase : IAsyncDisposable
    {
        private readonly string name = "ForgeFlow_Bom_Test_" + Guid.NewGuid().ToString("N");
        private readonly SqlConnectionStringBuilder server;
        private bool created;

        private DisposableBomDatabase(string connection)
        {
            server = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", ConnectTimeout = 5, Pooling = false };
            var target = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name };
            Factory = new TestDbContextFactory(new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(target.ConnectionString).Options);
        }

        internal IDbContextFactory<OrganizationDbContext> Factory { get; }

        internal static async Task<DisposableBomDatabase> Create()
        {
            var db = new DisposableBomDatabase(Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER")!);
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
