using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Sales;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DocumentSequenceTests
{
    [Fact]
    public void DocumentSequence_Formats_Persian_Gregorian_And_Plain_Numbers()
    {
        var utc = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc); // 1405-08-21

        var plain = DocumentSequence.CreateDefault(1, DocumentSequenceType.SalesOrder);
        Assert.Equal("SO-00001", plain.FormatPreview(utc));
        Assert.Equal("SO-00001", plain.AllocateNextNumber(utc));
        Assert.Equal("SO-00002", plain.FormatPreview(utc));

        var persian = DocumentSequence.Create(1, DocumentSequenceType.DeliveryOrder, "do", "-", DocumentYearMode.Persian, 4, 42, false);
        Assert.Equal("DO-1405-0042", persian.FormatPreview(utc));
        Assert.Equal("DO-1405-0042", persian.AllocateNextNumber(utc));
        Assert.Equal("DO-1405-0043", persian.FormatPreview(utc));

        var persianNextYear = new DateTime(2027, 3, 22, 10, 0, 0, DateTimeKind.Utc); // 1406-01-02
        Assert.Equal("DO-1406-0001", persian.FormatPreview(persianNextYear));
        Assert.Equal("DO-1406-0001", persian.AllocateNextNumber(persianNextYear));
        Assert.Equal("DO-1406-0002", persian.FormatPreview(persianNextYear));

        var gregorian = DocumentSequence.Create(1, DocumentSequenceType.PurchaseOrder, "po", "_", DocumentYearMode.Gregorian, 6, 7, true);
        Assert.Equal("PO_2026_000007", gregorian.FormatPreview(utc));
        Assert.Equal("PO_2026_000007", gregorian.AllocateNextNumber(utc));
        var gregNextYear = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal("PO_2027_000001", gregorian.FormatPreview(gregNextYear));
        Assert.Equal("PO_2027_000001", gregorian.AllocateNextNumber(gregNextYear));

        Assert.Throws<DocumentSequenceRuleException>(() => DocumentSequence.Create(1, DocumentSequenceType.SalesOrder, "-BAD", "-", DocumentYearMode.None, 5, 1, false));
        Assert.Throws<DocumentSequenceRuleException>(() => DocumentSequence.Create(1, DocumentSequenceType.SalesOrder, "SO", "/", DocumentYearMode.None, 5, 1, false));
        Assert.Throws<DocumentSequenceRuleException>(() => DocumentSequence.Create(1, DocumentSequenceType.SalesOrder, "SO", "-", DocumentYearMode.None, 2, 1, false));
        Assert.Throws<DocumentSequenceRuleException>(() => DocumentSequence.Create(1, DocumentSequenceType.SalesOrder, "SO", "-", DocumentYearMode.None, 5, 0, false));
    }

    [Fact]
    public void DocumentSequence_Permissions_And_AuditEvents_Are_Registered()
    {
        Assert.True(PermissionCatalog.TryGet(DocumentSequencePermissions.Read, out var read));
        Assert.Equal(PermissionScopeApplicability.OrganizationOnly, read.Applicability);

        Assert.True(PermissionCatalog.TryGet(DocumentSequencePermissions.Manage, out var manage));
        Assert.Equal(PermissionScopeApplicability.OrganizationOnly, manage.Applicability);

        Assert.True(AuditEventCatalog.TryGet("DocumentSequence.Created", out _));
        Assert.True(AuditEventCatalog.TryGet("DocumentSequence.Updated", out _));
        Assert.True(AuditEventCatalog.TryGet(UserOperationCatalog.DocumentSequenceActionRejected, out _));
    }

    [OrganizationSqlServerFact]
    public async Task DocumentSequence_Allocates_Monotonic_Numbers_And_Updates_In_Sql()
    {
        var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");

        var masterBuilder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
        var dbName = $"forgeflow_docseq_{Guid.NewGuid():N}";

        await using (var conn = new SqlConnection(masterBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}]";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var dbConn = new SqlConnectionStringBuilder(masterBuilder.ConnectionString) { InitialCatalog = dbName }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(dbConn).Options;
            var factory = new SimpleFactory(options);

            await using (var ctx = factory.CreateDbContext())
                await ctx.Database.MigrateAsync();

            var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
            var access = new OrganizationWriteAccess(actor, _ => ValueTask.FromResult(true));

            long orgId, siteId;
            await using (var setupDb = factory.CreateDbContext())
            {
                var org = Organization.Create("ORG-SEQ", "Seq Org");
                var site = org.CreateSite("SITE-SEQ", "Seq Site");
                setupDb.Organizations.Add(org);
                await setupDb.SaveChangesAsync(actor);
                orgId = org.Id;
                siteId = site.Id;
            }

            var store = new SqlDocumentSequenceStore(factory);
            var list = await store.ListSequences(orgId, new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc), access, default);
            Assert.Equal(9, list.Count);

            var soSeq = list.Single(s => s.DocumentType == DocumentSequenceType.SalesOrder);
            var upd = await store.UpdateSequence(
                orgId,
                DocumentSequenceType.SalesOrder,
                new UpdateDocumentSequenceRequest("SLS", "-", DocumentYearMode.Persian, 4, 100, false),
                Convert.FromBase64String(soSeq.RowVersion),
                new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc),
                access,
                default);
            Assert.True(upd.Succeeded);
            Assert.Equal("SLS-1405-0100", upd.Value!.NextPreview);

            var unitStore = new UnitOperationsStore(factory);
            Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
            var kg = (await unitStore.ListActiveUnits(orgId, default)).Single(u => u.Code == "KG");
            var itemStore = new ItemOperationsStore(factory);
            var item = (await itemStore.CreateItem(new CreateItemRequest(
                orgId, "SEQ-ITEM", "Seq Item", null, kg.Id,
                CanBePurchased: false, CanBeManufactured: false, CanBeSold: true, CanBeConsumed: false,
                RequiresLotTracking: false, QualityControlMode: QualityControlMode.None), access, default)).Value!;

            var salesStore = new SqlSalesOperationsStore(factory);
            var utcNow = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
            var cust = (await salesStore.CreateCustomer(new CreateCustomerRequest(
                orgId, "CUST-SEQ", "Seq Customer", null, null, null, null, null, null), utcNow, access, default)).Value!;

            var so1 = await salesStore.CreateSalesOrder(
                new CreateSalesOrderRequest(
                    orgId, siteId, cust.Id, null, new DateOnly(2026, 11, 12), null, null, "IRR", null, null,
                    [new SalesOrderLineRequest(item.Summary.Id, 10m, kg.Id, 1000m, null)]),
                [new ResolvedSalesOrderLine(item.Summary.Id, 10m, kg.Id, kg.Id, 10m, 1000m, null)],
                utcNow, access, default);
            var so2 = await salesStore.CreateSalesOrder(
                new CreateSalesOrderRequest(
                    orgId, siteId, cust.Id, null, new DateOnly(2026, 11, 12), null, null, "IRR", null, null,
                    [new SalesOrderLineRequest(item.Summary.Id, 5m, kg.Id, 1000m, null)]),
                [new ResolvedSalesOrderLine(item.Summary.Id, 5m, kg.Id, kg.Id, 5m, 1000m, null)],
                utcNow, access, default);

            Assert.True(so1.Succeeded);
            Assert.True(so2.Succeeded);
            Assert.Equal("SLS-1405-0100", so1.Value!.Summary.OrderNumber);
            Assert.Equal("SLS-1405-0101", so2.Value!.Summary.OrderNumber);
        }
        finally
        {
            await using var conn = new SqlConnection(masterBuilder.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private sealed class SimpleFactory(DbContextOptions<OrganizationDbContext> options)
        : IDbContextFactory<OrganizationDbContext>
    {
        public OrganizationDbContext CreateDbContext() => new(options);
    }
}
