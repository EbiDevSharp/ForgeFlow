using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class UnitSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task S01_S04_units_conversions_composite_fks_audit_and_concurrency_are_enforced_in_sql()
    {
        await using var database = await DisposableUnitDatabase.Create();
        var store = new UnitOperationsStore(database.Factory);

        long org1Id;
        long org2Id;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org1 = Organization.Create("ORG1", "Organization One");
            var org2 = Organization.Create("ORG2", "Organization Two");
            setupDb.Organizations.AddRange(org1, org2);
            await setupDb.SaveChangesAsync(Attribution());
            org1Id = org1.Id;
            org2Id = org2.Id;
        }

        var access1 = new OrganizationWriteAccess(Attribution(), _ => ValueTask.FromResult(true));
        var access2 = new OrganizationWriteAccess(Attribution(), _ => ValueTask.FromResult(true));

        // Seed standard units in both organizations independently (same codes allowed across distinct orgs).
        var seed1 = await store.SeedStandardUnits(org1Id, access1, default);
        var seed2 = await store.SeedStandardUnits(org2Id, access2, default);
        Assert.True(seed1.Succeeded && seed1.Value!.UnitsCreated == 14 && seed1.Value.ConversionsCreated == 7);
        Assert.True(seed2.Succeeded && seed2.Value!.UnitsCreated == 14 && seed2.Value.ConversionsCreated == 7);

        // Idempotent re-seed creates zero duplicates.
        var reseed1 = await store.SeedStandardUnits(org1Id, access1, default);
        Assert.True(reseed1.Succeeded && reseed1.Value!.UnitsCreated == 0 && reseed1.Value.ConversionsCreated == 0);

        // Verify duplicate unit code rejected within same org.
        var dup = await store.CreateUnit(new CreateUnitRequest(org1Id, "kg", "Dup KG", "kg", UnitDimension.Mass, 3), access1, default);
        Assert.False(dup.Succeeded);
        Assert.Equal(UnitErrors.CodeDuplicate, dup.Error!.Code);

        // Verify SQL composite FK rejects cross-organization UnitConversion endpoints.
        var org1Units = await store.ListActiveUnits(org1Id, default);
        var org2Units = await store.ListActiveUnits(org2Id, default);
        var crossOrgConversion = await store.CreateConversion(new CreateUnitConversionRequest(org1Id, org1Units[0].Id, org2Units[0].Id, 10m), access1, default);
        Assert.False(crossOrgConversion.Succeeded);
        Assert.Equal(UnitErrors.NotFound, crossOrgConversion.Error!.Code);

        // Verify update + RowVersion concurrency conflict + audit trail in OrganizationChange.
        var customCreate = await store.CreateUnit(new CreateUnitRequest(org1Id, "PAL", "Pallet", "plt", UnitDimension.Count, 0), access1, default);
        Assert.True(customCreate.Succeeded);
        var created = customCreate.Value!;

        var updateOk = await store.UpdateUnit(org1Id, created.Id, Convert.FromBase64String(created.RowVersion), "پالت استاندارد", "plt", 0, access1, default);
        Assert.True(updateOk.Succeeded);
        Assert.Equal("پالت استاندارد", updateOk.Value!.Name);

        var staleUpdate = await store.UpdateUnit(org1Id, created.Id, Convert.FromBase64String(created.RowVersion), "Stale", "plt", 0, access1, default);
        Assert.False(staleUpdate.Succeeded);
        Assert.Equal(UnitErrors.ConcurrencyConflict, staleUpdate.Error!.Code);

        await using var auditDb = await database.Factory.CreateDbContextAsync();
        var unitEvents = await auditDb.OrganizationChanges
            .Where(c => c.OrganizationId == org1Id && c.EventCode.StartsWith("Unit"))
            .Select(c => c.EventCode)
            .ToListAsync();
        Assert.Contains("Unit.Created", unitEvents);
        Assert.Contains("Unit.Updated", unitEvents);
        Assert.Contains("UnitConversion.Created", unitEvents);
    }

    private sealed class DisposableUnitDatabase : IAsyncDisposable
    {
        private readonly string name = "ForgeFlow_UnitSql_Test_" + Guid.NewGuid().ToString("N");
        private readonly SqlConnectionStringBuilder server;
        private bool created;

        private DisposableUnitDatabase(string connection)
        {
            server = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", ConnectTimeout = 5, Pooling = false };
            var target = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name };
            Factory = new TestDbContextFactory(new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(target.ConnectionString).Options);
        }

        internal IDbContextFactory<OrganizationDbContext> Factory { get; }

        internal static async Task<DisposableUnitDatabase> Create()
        {
            var db = new DisposableUnitDatabase(Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER")!);
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
