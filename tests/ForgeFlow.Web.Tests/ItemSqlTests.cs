using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ItemSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task S01_S04_items_classifications_attributes_conversions_and_audit_are_enforced_in_sql()
    {
        await using var database = await DisposableItemDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);

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

        Assert.True((await unitStore.SeedStandardUnits(org1Id, access1, default)).Succeeded);
        Assert.True((await unitStore.SeedStandardUnits(org2Id, access2, default)).Succeeded);
        var org1Units = await unitStore.ListActiveUnits(org1Id, default);
        var org2Units = await unitStore.ListActiveUnits(org2Id, default);

        var kg1 = org1Units.Single(u => u.Code == "KG");
        var g1 = org1Units.Single(u => u.Code == "G");
        var pcs1 = org1Units.Single(u => u.Code == "PCS");
        var box1 = org1Units.Single(u => u.Code == "BOX");
        var kg2 = org2Units.Single(u => u.Code == "KG");

        // Create ClassificationSet and hierarchy
        var setRes = await itemStore.CreateClassificationSet(new CreateClassificationSetRequest(org1Id, "CAT", "Category", false, false), access1, default);
        Assert.True(setRes.Succeeded);
        var parentRes = await itemStore.CreateClassification(new CreateClassificationRequest(org1Id, setRes.Value!.Id, null, "POLY", "Polymer"), access1, default);
        Assert.True(parentRes.Succeeded);
        var childRes = await itemStore.CreateClassification(new CreateClassificationRequest(org1Id, setRes.Value.Id, parentRes.Value!.Id, "HIPS", "HIPS"), access1, default);
        Assert.True(childRes.Succeeded);

        // Circular classification parent rejected
        var cycleRes = await itemStore.UpdateClassification(org1Id, parentRes.Value.Id, Convert.FromBase64String(parentRes.Value.RowVersion), "Polymer", childRes.Value!.Id, access1, default);
        Assert.False(cycleRes.Succeeded);
        Assert.Equal(ItemErrors.ClassificationCircularParent, cycleRes.Error!.Code);

        // Attribute definition + required check
        var attrRes = await itemStore.CreateAttributeDefinition(new CreateAttributeDefinitionRequest(org1Id, "MFI", "Melt Flow Index", AttributeDataType.Decimal, g1.Id, IsRequired: true), access1, default);
        Assert.True(attrRes.Succeeded);

        // Cross-org BaseUnit FK rejected
        var crossUnitItem = await itemStore.CreateItem(new CreateItemRequest(org1Id, "RESIN1", "Resin", null, kg2.Id, true, false, false, true), access1, default);
        Assert.False(crossUnitItem.Succeeded);
        Assert.Equal(ItemErrors.BaseUnitNotFound, crossUnitItem.Error!.Code);

        // Create valid Item + assign classification + item-specific conversion (BOX -> KG = 25) + attribute value
        var itemRes = await itemStore.CreateItem(new CreateItemRequest(org1Id, "RESIN1", "HIPS Resin", "High impact", kg1.Id, true, false, false, true), access1, default);
        Assert.True(itemRes.Succeeded);
        var item = itemRes.Value!;

        var classSave = await itemStore.SaveItemClassifications(org1Id, item.Summary.Id, Convert.FromBase64String(item.Summary.RowVersion), [childRes.Value.Id], access1, default);
        Assert.True(classSave.Succeeded);
        Assert.Single(classSave.Value!.Classifications);

        var convSave = await itemStore.CreateItemConversion(new CreateItemUnitConversionRequest(org1Id, item.Summary.Id, box1.Id, kg1.Id, 25m), access1, default);
        Assert.True(convSave.Succeeded);

        // Required attribute missing fails, valid value succeeds
        var missingAttr = await itemStore.SaveItemAttributeValues(org1Id, item.Summary.Id, Convert.FromBase64String(item.Summary.RowVersion), [], access1, default);
        Assert.False(missingAttr.Succeeded);
        Assert.Equal(ItemErrors.AttributeRequired, missingAttr.Error!.Code);

        var attrSave = await itemStore.SaveItemAttributeValues(org1Id, item.Summary.Id, Convert.FromBase64String(item.Summary.RowVersion),
            [new ItemAttributeValueInput(attrRes.Value!.Id, null, 4.5m, null)], access1, default);
        Assert.True(attrSave.Succeeded);

        // Audit trail populated in OrganizationChange
        await using var auditDb = await database.Factory.CreateDbContextAsync();
        var itemEvents = await auditDb.OrganizationChanges
            .Where(c => c.OrganizationId == org1Id && (c.EventCode.StartsWith("Item") || c.EventCode.StartsWith("Classification") || c.EventCode.StartsWith("Attribute")))
            .Select(c => c.EventCode)
            .ToListAsync();
        Assert.Contains("Item.Created", itemEvents);
        Assert.Contains("Classification.Created", itemEvents);
        Assert.Contains("ItemUnitConversion.Created", itemEvents);
        Assert.Contains("ItemAttributeValue.Updated", itemEvents);
    }

    private sealed class DisposableItemDatabase : IAsyncDisposable
    {
        private readonly string name = "ForgeFlow_ItemSql_Test_" + Guid.NewGuid().ToString("N");
        private readonly SqlConnectionStringBuilder server;
        private bool created;

        private DisposableItemDatabase(string connection)
        {
            server = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", ConnectTimeout = 5, Pooling = false };
            var target = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name };
            Factory = new TestDbContextFactory(new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(target.ConnectionString).Options);
        }

        internal IDbContextFactory<OrganizationDbContext> Factory { get; }

        internal static async Task<DisposableItemDatabase> Create()
        {
            var db = new DisposableItemDatabase(Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER")!);
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
