using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ProcurementSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task S01_S04_procurement_lifecycle_unit_conversion_qc_reversal_and_shortage_work_end_to_end_in_sql()
    {
        await using var database = await DisposableProcurementDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var moStore = new SqlProductionOrderOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var qcStore = new SqlQualityOperationsStore(database.Factory);
        var rptStore = new SqlReportingQueryStore(database.Factory);
        var procStore = new SqlProcurementOperationsStore(database.Factory);

        long orgId, siteAId, siteBId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-PO", "Procurement Org");
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
        var ton = units.Single(u => u.Code == "TON");
        var pcs = units.Single(u => u.Code == "PCS");

        var rawNoQc = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "RAW-STEEL", "Raw Steel", null, kg.Id, CanBePurchased: true, CanBeManufactured: false, CanBeSold: false, CanBeConsumed: true,
            RequiresLotTracking: false, QualityControlMode: QualityControlMode.None), access, default)).Value!.Summary;
        var rawQc = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "RAW-CHEM", "Raw Chemical", null, kg.Id, CanBePurchased: true, CanBeManufactured: false, CanBeSold: false, CanBeConsumed: true,
            RequiresLotTracking: true, QualityControlMode: QualityControlMode.IncomingReceipts), access, default)).Value!.Summary;
        var fgPart = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "FG-BOLT", "Finished Bolt", null, pcs.Id, CanBePurchased: false, CanBeManufactured: true, CanBeSold: true, CanBeConsumed: false,
            RequiresLotTracking: false, QualityControlMode: QualityControlMode.None), access, default)).Value!.Summary;

        // BOM + Released MO requiring 3000 KG RAW-STEEL so Material Shortage sees shortage + OnOrderBaseQuantity
        Assert.True((await bomStore.SeedStandardContextDimensions(orgId, access, default)).Succeeded);
        var bom = (await bomStore.CreateBom(new CreateBomRequest(orgId, "BOM-BOLT", "Bolt BOM", fgPart.Id), access, default)).Value!;
        var v1 = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgId, bom.Summary.Id, 1, 1m, pcs.Id, new DateOnly(2026, 1, 1), null, "v1"), access, default)).Value!;
        v1 = (await bomStore.SaveDraftBomLines(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion),
            [new BomInputLineInput(1, rawNoQc.Id, 3m, kg.Id, 0m, false, null)],
            [new BomOutputLineInput(1, fgPart.Id, BomOutputType.MainProduct, 1m, pcs.Id)], access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Approved, access, default)).Value!;
        v1 = (await bomStore.TransitionBomVersionStatus(orgId, v1.Summary.Id, Convert.FromBase64String(v1.Summary.RowVersion), BomVersionStatus.Active, access, default)).Value!;

        var actor = Attribution();
        var perm = new AllowAllPermissions();
        var procUseCases = new ProcurementUseCases(procStore, new FixedActor(actor), perm);
        var moUseCases = new ProductionOrderUseCases(moStore, bomStore, new FixedActor(actor), perm);
        var invUseCases = new InventoryUseCases(invStore, new FixedActor(actor), perm);
        var qcUseCases = new QualityUseCases(qcStore, new FixedActor(actor), perm);
        var rptUseCases = new ReportingUseCases(rptStore, new FixedActor(actor), perm);

        var orgScope = new OrganizationScope.Organization(orgId);
        var siteAScope = new OrganizationScope.Site(orgId, siteAId);
        var siteBScope = new OrganizationScope.Site(orgId, siteBId);

        var whA = (await invUseCases.CreateWarehouse(siteAScope, new CreateWarehouseRequest(orgId, siteAId, "WH-A", "Main A", true))).Value!;
        _ = (await invUseCases.CreateWarehouse(siteBScope, new CreateWarehouseRequest(orgId, siteBId, "WH-B", "Main B", true))).Value!;

        // 1. Create Suppliers & duplicate code check
        var sup1 = (await procUseCases.CreateSupplier(orgScope, new CreateSupplierRequest(
            orgId, "SUP-01", "Pars Steel", "M. Karimi", "+98211234", "sales@pars.local", "Tehran", "Grade A"))).Value!;
        var supDup = await procUseCases.CreateSupplier(orgScope, new CreateSupplierRequest(orgId, "sup-01", "Duplicate"));
        Assert.False(supDup.Succeeded);
        Assert.Equal(ProcurementErrors.SupplierCodeDuplicate, supDup.Error!.Code);

        // Site B can read Organization-shared Suppliers but cannot view Site A Purchase Orders
        var supReadFromSiteB = await procUseCases.ListSuppliers(siteBScope, orgId, new SupplierListQuery());
        Assert.True(supReadFromSiteB.Succeeded);
        Assert.Single(supReadFromSiteB.Value!.Items);

        // Create Released MO for 1000 FG-BOLT -> 3000 KG RAW-STEEL required
        var mo = (await moUseCases.CreateDraftOrder(siteAScope,
            new CreateProductionOrderRequest(orgId, siteAId, "MO-PO1", fgPart.Id, 1000m, pcs.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 20)))).Value!;
        _ = (await moUseCases.ReleaseOrder(siteAScope, new ReleaseProductionOrderRequest(orgId, mo.Summary.Id, mo.Summary.RowVersion))).Value!;

        // 2. Draft PO for 2 TON RAW-STEEL (= 2000 KG base), update draft to 2.5 TON (= 2500 KG base), approve
        var po1Res = await procUseCases.CreateDraft(siteAScope, new CreatePurchaseOrderRequest(
            orgId, siteAId, sup1.Id, "PO-101", new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 18), whA.Id, "USD", "Initial steel order",
            [new PurchaseOrderLineInput(rawNoQc.Id, 2m, ton.Id, 500m, null)]));
        Assert.True(po1Res.Succeeded);
        var po1 = po1Res.Value!;
        Assert.Equal(PurchaseOrderStatus.Draft, po1.Summary.Status);
        Assert.Equal(2000m, po1.Lines[0].OrderedBaseQuantity);

        var updRes = await procUseCases.UpdateDraft(siteAScope, orgId, po1.Summary.Id, po1.Summary.RowVersion, new UpdatePurchaseOrderRequest(
            sup1.Id, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 19), whA.Id, "USD", "Increased order",
            [new PurchaseOrderLineInput(rawNoQc.Id, 2.5m, ton.Id, 490m, "2.5 tons")]));
        Assert.True(updRes.Succeeded);
        po1 = updRes.Value!;
        Assert.Equal(2500m, po1.Lines[0].OrderedBaseQuantity);

        // Before Approve, OnOrderBaseQuantity in Material Shortage is 0 (Draft is excluded)
        var shortageDraft = await rptUseCases.GetMaterialShortageReport(siteAScope, orgId, new MaterialShortageQuery());
        Assert.Equal(0m, Assert.Single(shortageDraft.Value!.Items).OnOrderBaseQuantity);

        var appRes = await procUseCases.ApproveOrder(siteAScope, orgId, po1.Summary.Id, po1.Summary.RowVersion);
        Assert.True(appRes.Succeeded);
        po1 = appRes.Value!;
        Assert.Equal(PurchaseOrderStatus.Approved, po1.Summary.Status);

        // Site B cannot access Site A's PO
        var crossSitePo = await procUseCases.GetPurchaseOrder(siteBScope, orgId, po1.Summary.Id);
        Assert.False(crossSitePo.Succeeded);
        Assert.Equal(ProcurementErrors.AccessDenied, crossSitePo.Error!.Code);

        // After Approve, OnOrderBaseQuantity is 2500 KG, while Shortage remains 3000 KG!
        var shortageApproved = await rptUseCases.GetMaterialShortageReport(siteAScope, orgId, new MaterialShortageQuery());
        var rowApproved = Assert.Single(shortageApproved.Value!.Items);
        Assert.Equal(3000m, rowApproved.ShortageBaseQuantity);
        Assert.Equal(2500m, rowApproved.OnOrderBaseQuantity);

        // 3. Partial Receive 1 TON (= 1000 KG) -> PartiallyReceived; check idempotency key; reject over-receipt (> 1500 KG); reverse reverts PO!
        var rcv1Res = await procUseCases.ReceiveOrder(siteAScope, new ReceivePurchaseOrderRequest(
            orgId, po1.Summary.Id, whA.Id, "RCV-101", "Truck 1", "po-rcv-1", po1.Summary.RowVersion,
            [new ReceivePurchaseOrderLineInput(po1.Lines[0].Id, 1m, ton.Id, null, null, null, "Half batch")]));
        Assert.True(rcv1Res.Succeeded);
        po1 = rcv1Res.Value!.Order;
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, po1.Summary.Status);
        Assert.Equal(1000m, po1.Lines[0].ReceivedBaseQuantity);
        Assert.Equal(1500m, po1.Lines[0].RemainingBaseQuantity);

        // Idempotent retry with same key returns same receipt without double posting
        var idemRetry = await procUseCases.ReceiveOrder(siteAScope, new ReceivePurchaseOrderRequest(
            orgId, po1.Summary.Id, whA.Id, "RCV-101", "Truck 1", "po-rcv-1", po1.Summary.RowVersion,
            [new ReceivePurchaseOrderLineInput(po1.Lines[0].Id, 1m, ton.Id, null, null, null, null)]));
        Assert.True(idemRetry.Succeeded);
        Assert.Equal(rcv1Res.Value.InventoryTransactionId, idemRetry.Value!.InventoryTransactionId);

        // Material Shortage now shows OnHand = 1000, Shortage = 2000, OnOrder = 1500
        var shortagePartial = await rptUseCases.GetMaterialShortageReport(siteAScope, orgId, new MaterialShortageQuery());
        var rowPartial = Assert.Single(shortagePartial.Value!.Items);
        Assert.Equal(1000m, rowPartial.OnHandBaseQuantity);
        Assert.Equal(2000m, rowPartial.ShortageBaseQuantity);
        Assert.Equal(1500m, rowPartial.OnOrderBaseQuantity);

        // Over-receipt (1.6 TON = 1600 KG > 1500 KG remaining) fails
        var overRcv = await procUseCases.ReceiveOrder(siteAScope, new ReceivePurchaseOrderRequest(
            orgId, po1.Summary.Id, whA.Id, "RCV-OVER", null, null, po1.Summary.RowVersion,
            [new ReceivePurchaseOrderLineInput(po1.Lines[0].Id, 1.6m, ton.Id, null, null, null, null)]));
        Assert.False(overRcv.Succeeded);
        Assert.Equal(ProcurementErrors.OverReceipt, overRcv.Error!.Code);

        // Reverse RCV-101 in Inventory -> atomically reverts PO ReceivedBaseQuantity to 0 & Status back to Approved!
        var revRes = await invUseCases.ReverseTransaction(siteAScope, new ReverseInventoryTransactionRequest(
            orgId, rcv1Res.Value.InventoryTransactionId, "REV-101", "Wrong truck weight"));
        Assert.True(revRes.Succeeded);

        po1 = (await procUseCases.GetPurchaseOrder(siteAScope, orgId, po1.Summary.Id)).Value!;
        Assert.Equal(PurchaseOrderStatus.Approved, po1.Summary.Status);
        Assert.Equal(0m, po1.Lines[0].ReceivedBaseQuantity);
        Assert.Equal(2500m, po1.Lines[0].RemainingBaseQuantity);

        // Receive 1 TON again, then Short-Close remaining 1.5 TON
        var rcv2Res = await procUseCases.ReceiveOrder(siteAScope, new ReceivePurchaseOrderRequest(
            orgId, po1.Summary.Id, whA.Id, "RCV-102", "Confirmed truck", null, po1.Summary.RowVersion,
            [new ReceivePurchaseOrderLineInput(po1.Lines[0].Id, 1000m, kg.Id, null, null, null, null)]));
        Assert.True(rcv2Res.Succeeded);
        po1 = rcv2Res.Value!.Order;

        var closeRes = await procUseCases.CloseOrder(siteAScope, orgId, po1.Summary.Id, po1.Summary.RowVersion);
        Assert.True(closeRes.Succeeded);
        Assert.Equal(PurchaseOrderStatus.Closed, closeRes.Value!.Summary.Status);

        // 4. Lot-tracked & Incoming-QC PO triggers blocked balance, ItemLot dates, and Pending QualityInspection
        var po2 = (await procUseCases.CreateDraft(siteAScope, new CreatePurchaseOrderRequest(
            orgId, siteAId, sup1.Id, "PO-102", new DateOnly(2026, 11, 11), null, whA.Id, "IRR", null,
            [new PurchaseOrderLineInput(rawQc.Id, 50m, kg.Id, 2000m, null)]))).Value!;
        po2 = (await procUseCases.ApproveOrder(siteAScope, orgId, po2.Summary.Id, po2.Summary.RowVersion)).Value!;

        var rcvQc = await procUseCases.ReceiveOrder(siteAScope, new ReceivePurchaseOrderRequest(
            orgId, po2.Summary.Id, whA.Id, "RCV-103", "Chemical drum", null, po2.Summary.RowVersion,
            [new ReceivePurchaseOrderLineInput(po2.Lines[0].Id, 50m, kg.Id, "LOT-CHM-1", new DateOnly(2026, 11, 1), new DateOnly(2027, 11, 1), "Sealed")]));
        Assert.True(rcvQc.Succeeded);
        Assert.Equal(PurchaseOrderStatus.Received, rcvQc.Value!.Order.Summary.Status);

        var qcList = await qcUseCases.ListInspections(siteAScope, orgId, new QualityInspectionListQuery(ItemId: rawQc.Id));
        var qcItem = Assert.Single(qcList.Value!.Items);
        Assert.Equal(QualityInspectionStatus.Pending, qcItem.Status);
        Assert.Equal(50m, qcItem.InspectedBaseQuantity);
        Assert.Equal("LOT-CHM-1", qcItem.LotCode);

        var chemBal = Assert.Single((await invUseCases.ListBalances(siteAScope, orgId, new InventoryBalanceListQuery(ItemId: rawQc.Id))).Value!.Items);
        Assert.Equal(50m, chemBal.OnHandBaseQuantity);
        Assert.Equal(50m, chemBal.BlockedBaseQuantity);
        Assert.Equal(0m, chemBal.AvailableBaseQuantity);
        Assert.Equal(new DateOnly(2027, 11, 1), chemBal.ExpirationDate);

        // 5. Organization-scoped Currency CRUD: create custom AED currency, use on PO, deactivate blocks new POs
        var unitUseCases = new UnitUseCases(unitStore, new FixedActor(actor), perm);
        var aedRes = await unitUseCases.CreateCurrency(orgScope, new CreateCurrencyRequest(orgId, "aed", "UAE Dirham", "AED", 2));
        Assert.True(aedRes.Succeeded);
        var aed = aedRes.Value!;
        Assert.Equal("AED", aed.Code);

        var poAed = await procUseCases.CreateDraft(siteAScope, new CreatePurchaseOrderRequest(
            orgId, siteAId, sup1.Id, "PO-103", new DateOnly(2026, 11, 11), null, whA.Id, "AED", null,
            [new PurchaseOrderLineInput(rawNoQc.Id, 10m, kg.Id, 5m, null)]));
        Assert.True(poAed.Succeeded);
        Assert.Equal("AED", poAed.Value!.Summary.CurrencyCode);

        var deactAed = await unitUseCases.SetCurrencyActive(orgScope, new SetCurrencyActiveRequest(orgId, aed.Id, false, aed.RowVersion));
        Assert.True(deactAed.Succeeded);

        var poInactiveCur = await procUseCases.CreateDraft(siteAScope, new CreatePurchaseOrderRequest(
            orgId, siteAId, sup1.Id, "PO-104", new DateOnly(2026, 11, 11), null, whA.Id, "AED", null,
            [new PurchaseOrderLineInput(rawNoQc.Id, 10m, kg.Id, 5m, null)]));
        Assert.False(poInactiveCur.Succeeded);
        Assert.Equal(ProcurementErrors.CurrencyInvalid, poInactiveCur.Error!.Code);
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

    private sealed class DisposableProcurementDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableProcurementDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            var dbConn = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(dbConn).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableProcurementDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_po_{Guid.NewGuid():N}";
            var db = new DisposableProcurementDatabase(builder, name);
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
