using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Sales;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class CrossTenantIsolationIntegrationTests
{
    private static TrustedActor Attribution(long actorId = 1) => new(actorId, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
    private static OrganizationWriteAccess CreateAccess(TrustedActor actor) => new(actor, _ => ValueTask.FromResult(true));

    private sealed class StaticActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class AllowAllPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(true));
    }

    [OrganizationSqlServerFact]
    public async Task CrossTenant_Data_Isolation_Prevents_Reading_Listing_And_Mutating_Across_Organizations()
    {
        await using var database = await DisposableCrossTenantDatabase.Create();
        var actorA = Attribution(101);
        var accessA = CreateAccess(actorA);
        var actorB = Attribution(202);
        var accessB = CreateAccess(actorB);
        var perm = new AllowAllPermissions();
        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        long orgAlphaId, siteAlphaId, orgBetaId, siteBetaId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var orgA = Organization.Create("ORG-ALPHA", "Alpha Corp");
            var siteA = orgA.CreateSite("SITE-A", "Alpha Plant");
            var orgB = Organization.Create("ORG-BETA", "Beta Industries");
            var siteB = orgB.CreateSite("SITE-B", "Beta Plant");

            setupDb.Organizations.AddRange(orgA, orgB);
            await setupDb.SaveChangesAsync(actorA);

            orgAlphaId = orgA.Id;
            siteAlphaId = siteA.Id;
            orgBetaId = orgB.Id;
            siteBetaId = siteB.Id;
        }

        // Stores
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var prodStore = new SqlProductionOrderOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var procStore = new SqlProcurementOperationsStore(database.Factory);
        var salesStore = new SqlSalesOperationsStore(database.Factory);
        var qcStore = new SqlQualityOperationsStore(database.Factory);

        // Seed Units for both orgs
        Assert.True((await unitStore.SeedStandardUnits(orgAlphaId, accessA, default)).Succeeded);
        Assert.True((await unitStore.SeedStandardUnits(orgBetaId, accessB, default)).Succeeded);

        var pcsA = (await unitStore.ListActiveUnits(orgAlphaId, default)).Single(u => u.Code == "PCS");
        var pcsB = (await unitStore.ListActiveUnits(orgBetaId, default)).Single(u => u.Code == "PCS");

        // Seed Warehouses
        var whA = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgAlphaId, siteAlphaId, "WH-A1", "Alpha Main WH", true), accessA, default)).Value!;
        var whB = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgBetaId, siteBetaId, "WH-B1", "Beta Main WH", true), accessB, default)).Value!;

        // 1. Org Alpha Item
        var itemA = (await itemStore.CreateItem(new CreateItemRequest(
            OrganizationId: orgAlphaId,
            Code: "ALPHA-ITM",
            Name: "Alpha Item",
            Description: null,
            BaseUnitId: pcsA.Id,
            CanBePurchased: true,
            CanBeManufactured: true,
            CanBeSold: true,
            CanBeConsumed: true,
            RequiresLotTracking: true,
            QualityControlMode: QualityControlMode.IncomingReceipts), accessA, default)).Value!;

        var rawItemA = (await itemStore.CreateItem(new CreateItemRequest(
            OrganizationId: orgAlphaId,
            Code: "ALPHA-RAW",
            Name: "Alpha Raw",
            Description: null,
            BaseUnitId: pcsA.Id,
            CanBePurchased: true,
            CanBeManufactured: false,
            CanBeSold: false,
            CanBeConsumed: true,
            RequiresLotTracking: false,
            QualityControlMode: QualityControlMode.None), accessA, default)).Value!;

        // 2. Org Alpha BOM
        Assert.True((await bomStore.SeedStandardContextDimensions(orgAlphaId, accessA, default)).Succeeded);
        var bomA = (await bomStore.CreateBom(new CreateBomRequest(orgAlphaId, "ALPHA-BOM", "Alpha BOM", itemA.Summary.Id), accessA, default)).Value!;
        var bomVerA = (await bomStore.CreateBomVersion(new CreateBomVersionRequest(orgAlphaId, bomA.Summary.Id, 1, 1m, pcsA.Id, new DateOnly(2026, 1, 1), null, "v1"), accessA, default)).Value!;
        bomVerA = (await bomStore.SaveDraftBomLines(orgAlphaId, bomVerA.Summary.Id, Convert.FromBase64String(bomVerA.Summary.RowVersion),
            [new BomInputLineInput(1, rawItemA.Summary.Id, 1m, pcsA.Id, 0m, false, null)], [new BomOutputLineInput(1, itemA.Summary.Id, BomOutputType.MainProduct, 1m, pcsA.Id)], accessA, default)).Value!;
        bomVerA = (await bomStore.TransitionBomVersionStatus(orgAlphaId, bomVerA.Summary.Id, Convert.FromBase64String(bomVerA.Summary.RowVersion), BomVersionStatus.Approved, accessA, default)).Value!;
        bomVerA = (await bomStore.TransitionBomVersionStatus(orgAlphaId, bomVerA.Summary.Id, Convert.FromBase64String(bomVerA.Summary.RowVersion), BomVersionStatus.Active, accessA, default)).Value!;

        // 3. Org Alpha Production Order (via UseCases)
        var siteAScope = new OrganizationScope.Site(orgAlphaId, siteAlphaId);
        var poUseCasesA = new ProductionOrderUseCases(prodStore, bomStore, new StaticActor(actorA), perm);
        var poA = (await poUseCasesA.CreateDraftOrder(siteAScope, new CreateProductionOrderRequest(
            orgAlphaId, siteAlphaId, "ALPHA-PO-01", itemA.Summary.Id, 10m, pcsA.Id, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20)))).Value!;

        // 4. Org Alpha Inventory Receipt (triggers QC inspection due to QualityControlMode.IncomingReceipts)
        var rcvA = (await invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgAlphaId, whA.Id, null, "RCV-ALPHA-01", InventoryTransactionType.Receipt, null, "Alpha arrival", null,
                [new InventoryLineInput(itemA.Summary.Id, "LOT-ALPHA", 50m, pcsA.Id)]),
            siteAlphaId,
            [new ResolvedInventoryLineWrite(siteAlphaId, whA.Id, itemA.Summary.Id, "LOT-ALPHA", 50m, pcsA.Id, 50m, pcsA.Id, null, true)],
            utcNow, accessA, default)).Value!;

        // 5. Org Alpha Supplier & Purchase Order (via ProcurementUseCases)
        var orgAScope = new OrganizationScope.Organization(orgAlphaId);
        var procUseCasesA = new ProcurementUseCases(procStore, new StaticActor(actorA), perm);
        var suppA = (await procUseCasesA.CreateSupplier(orgAScope, new CreateSupplierRequest(
            orgAlphaId, "SUPP-ALPHA", "Alpha Supplier", null, null, null, null, null))).Value!;
        var purA = (await procUseCasesA.CreateDraft(siteAScope, new CreatePurchaseOrderRequest(
            orgAlphaId, siteAlphaId, suppA.Id, "PUR-ALPHA-01", new DateOnly(2026, 11, 16), null, whA.Id, "IRR", null,
            [new PurchaseOrderLineInput(itemA.Summary.Id, 20m, pcsA.Id, 100m, null)]))).Value!;

        // 6. Org Alpha Customer & Sales Order (via SalesUseCases)
        var salesUseCasesA = new SalesUseCases(salesStore, new StaticActor(actorA), perm);
        var custA = (await salesUseCasesA.CreateCustomer(orgAScope, new CreateCustomerRequest(
            orgAlphaId, "CUST-ALPHA", "Alpha Customer", null, null, null, null, null, null))).Value!;
        var soA = (await salesUseCasesA.CreateSalesOrder(siteAScope, new CreateSalesOrderRequest(
            orgAlphaId, siteAlphaId, custA.Id, "SO-ALPHA-01", new DateOnly(2026, 11, 16), null, whA.Id, "IRR", null, null,
            [new SalesOrderLineRequest(itemA.Summary.Id, 10m, pcsA.Id, 150m, null)]))).Value!;

        // 7. Org Alpha Quality Inspection
        var qisA = await qcStore.ListInspections(orgAlphaId, new QualityInspectionListQuery(), default);
        var qiA = Assert.Single(qisA.Items);

        // =========================================================================
        // STRICT CROSS-TENANT ISOLATION CHECKS UNDER ORG BETA SCOPE
        // =========================================================================

        // A. Item Isolation
        Assert.Null(await itemStore.GetItem(orgBetaId, itemA.Summary.Id, default));
        var betaItems = await itemStore.ListItems(orgBetaId, new ItemListQuery(), default);
        Assert.DoesNotContain(betaItems.Items, i => i.Id == itemA.Summary.Id || i.Code == "ALPHA-ITM");

        var updateItemCrossTenant = await itemStore.UpdateItem(
            orgBetaId, itemA.Summary.Id, Convert.FromBase64String(itemA.Summary.RowVersion),
            new UpdateItemRequest(orgBetaId, itemA.Summary.Id, "Hacked Name", null, pcsB.Id, true, false, false, false, itemA.Summary.RowVersion, false, QualityControlMode.None),
            accessB, default);
        Assert.False(updateItemCrossTenant.Succeeded);
        Assert.Equal(ItemErrors.NotFound, updateItemCrossTenant.Error!.Code);

        var setActiveCrossTenant = await itemStore.SetItemActive(
            orgBetaId, itemA.Summary.Id, Convert.FromBase64String(itemA.Summary.RowVersion), false, accessB, default);
        Assert.False(setActiveCrossTenant.Succeeded);
        Assert.Equal(ItemErrors.NotFound, setActiveCrossTenant.Error!.Code);

        // B. BOM Isolation
        Assert.Null(await bomStore.GetBom(orgBetaId, bomA.Summary.Id, default));
        var betaBoms = await bomStore.ListBoms(orgBetaId, new BomListQuery(), default);
        Assert.DoesNotContain(betaBoms.Items, b => b.Id == bomA.Summary.Id || b.Code == "ALPHA-BOM");

        var updateBomCrossTenant = await bomStore.UpdateBom(
            orgBetaId, bomA.Summary.Id, Convert.FromBase64String(bomA.Summary.RowVersion),
            "Hacked BOM", accessB, default);
        Assert.False(updateBomCrossTenant.Succeeded);
        Assert.Equal(BomErrors.NotFound, updateBomCrossTenant.Error!.Code);

        // C. Production Order Isolation
        Assert.Null(await prodStore.GetOrder(orgBetaId, poA.Summary.Id, default));
        var betaOrders = await prodStore.ListOrders(orgBetaId, new ProductionOrderListQuery(), default);
        Assert.DoesNotContain(betaOrders.Items, o => o.Id == poA.Summary.Id || o.OrderNumber == "ALPHA-PO-01");

        var cancelPoCrossTenant = await prodStore.CancelOrder(
            orgBetaId, poA.Summary.Id, Convert.FromBase64String(poA.Summary.RowVersion),
            "Hacked cancel", utcNow, accessB, default);
        Assert.False(cancelPoCrossTenant.Succeeded);
        Assert.Equal(ProductionOrderErrors.NotFound, cancelPoCrossTenant.Error!.Code);

        // D. Inventory Balance & Transaction Isolation
        var betaBalances = await invStore.ListBalances(orgBetaId, new InventoryBalanceListQuery(), default);
        Assert.DoesNotContain(betaBalances.Items, b => b.ItemId == itemA.Summary.Id || b.WarehouseId == whA.Id || b.LotCode == "LOT-ALPHA");

        var betaTxns = await invStore.ListTransactions(orgBetaId, new InventoryTransactionListQuery(), default);
        Assert.DoesNotContain(betaTxns.Items, t => t.Id == rcvA.Summary.Id || t.WarehouseId == whA.Id);

        var crossTenantMove = await invStore.PostTransaction(
            new PostInventoryTransactionRequest(orgBetaId, whA.Id, null, "RCV-HACK", InventoryTransactionType.Issue, null, "Cross-tenant theft", null,
                [new InventoryLineInput(itemA.Summary.Id, "LOT-ALPHA", 10m, pcsA.Id)]),
            siteBetaId,
            [new ResolvedInventoryLineWrite(siteBetaId, whA.Id, itemA.Summary.Id, "LOT-ALPHA", 10m, pcsA.Id, -10m, pcsA.Id, null, false)],
            utcNow, accessB, default);
        Assert.False(crossTenantMove.Succeeded);
        Assert.Equal(InventoryErrors.WarehouseNotFound, crossTenantMove.Error!.Code);

        // E. Procurement Isolation (Supplier & Purchase Order)
        Assert.Null(await procStore.GetSupplier(orgBetaId, suppA.Id, default));
        var betaSuppliers = await procStore.ListSuppliers(orgBetaId, new SupplierListQuery(), default);
        Assert.DoesNotContain(betaSuppliers.Items, s => s.Id == suppA.Id || s.Code == "SUPP-ALPHA");

        Assert.Null(await procStore.GetPurchaseOrder(orgBetaId, purA.Summary.Id, default));
        var betaPurchaseOrders = await procStore.ListPurchaseOrders(orgBetaId, null, new PurchaseOrderListQuery(), default);
        Assert.DoesNotContain(betaPurchaseOrders.Items, p => p.Id == purA.Summary.Id || p.OrderNumber == "PUR-ALPHA-01");

        var cancelPurCrossTenant = await procStore.CancelOrder(
            orgBetaId, purA.Summary.Id, Convert.FromBase64String(purA.Summary.RowVersion),
            "Hacked cancel", utcNow, accessB, default);
        Assert.False(cancelPurCrossTenant.Succeeded);
        Assert.Equal(ProcurementErrors.NotFound, cancelPurCrossTenant.Error!.Code);

        // F. Sales Isolation (Customer & Sales Order)
        var betaCustomers = await salesStore.ListCustomers(orgBetaId, new CustomerListQuery(), default);
        Assert.DoesNotContain(betaCustomers.Items, c => c.Id == custA.Id || c.Code == "CUST-ALPHA");

        Assert.Null(await salesStore.GetSalesOrder(orgBetaId, soA.Summary.Id, default));
        var betaSalesOrders = await salesStore.ListSalesOrders(orgBetaId, new SalesOrderListQuery(), default);
        Assert.DoesNotContain(betaSalesOrders.Items, s => s.Id == soA.Summary.Id || s.OrderNumber == "SO-ALPHA-01");

        var cancelSoCrossTenant = await salesStore.CancelSalesOrder(
            orgBetaId, soA.Summary.Id, "Hacked cancel", Convert.FromBase64String(soA.Summary.RowVersion),
            utcNow, accessB, default);
        Assert.False(cancelSoCrossTenant.Succeeded);
        Assert.Equal(SalesErrors.NotFound, cancelSoCrossTenant.Error!.Code);

        // G. Quality Inspection Isolation
        Assert.Null(await qcStore.GetInspection(orgBetaId, qiA.Id, default));
        var betaInspections = await qcStore.ListInspections(orgBetaId, new QualityInspectionListQuery(), default);
        Assert.DoesNotContain(betaInspections.Items, q => q.Id == qiA.Id || q.InspectionNumber == qiA.InspectionNumber);

        var cancelQcCrossTenant = await qcStore.CancelHold(
            orgBetaId, qiA.Id, Convert.FromBase64String(qiA.RowVersion), "Hacked cancel", utcNow, accessB, default);
        Assert.False(cancelQcCrossTenant.Succeeded);
        Assert.Equal(QualityErrors.InspectionNotFound, cancelQcCrossTenant.Error!.Code);

        // H. Audit Log Isolation
        await using var verifyDb = await database.Factory.CreateDbContextAsync();
        var alphaChanges = await verifyDb.OrganizationChanges.Where(c => c.OrganizationId == orgAlphaId).ToListAsync();
        Assert.NotEmpty(alphaChanges);

        var betaChanges = await verifyDb.OrganizationChanges.Where(c => c.OrganizationId == orgBetaId).ToListAsync();
        Assert.DoesNotContain(betaChanges, c => c.OrganizationId == orgAlphaId);
    }

    [OrganizationSqlServerFact]
    public async Task CrossTenant_Foreign_Key_Linking_Is_Strictly_Rejected_At_Application_And_Database_Level()
    {
        await using var database = await DisposableCrossTenantDatabase.Create();
        var actorA = Attribution(101);
        var accessA = CreateAccess(actorA);
        var actorB = Attribution(202);
        var accessB = CreateAccess(actorB);
        var perm = new AllowAllPermissions();
        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        long orgAlphaId, siteAlphaId, orgBetaId, siteBetaId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var orgA = Organization.Create("ORG-FK-A", "FK Alpha");
            var siteA = orgA.CreateSite("SITE-A", "Plant A");
            var orgB = Organization.Create("ORG-FK-B", "FK Beta");
            var siteB = orgB.CreateSite("SITE-B", "Plant B");

            setupDb.Organizations.AddRange(orgA, orgB);
            await setupDb.SaveChangesAsync(actorA);

            orgAlphaId = orgA.Id;
            siteAlphaId = siteA.Id;
            orgBetaId = orgB.Id;
            siteBetaId = siteB.Id;
        }

        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var bomStore = new BomOperationsStore(database.Factory);
        var prodStore = new SqlProductionOrderOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var procStore = new SqlProcurementOperationsStore(database.Factory);
        var salesStore = new SqlSalesOperationsStore(database.Factory);
        var resStore = new SqlStockReservationStore(database.Factory);

        Assert.True((await unitStore.SeedStandardUnits(orgAlphaId, accessA, default)).Succeeded);
        Assert.True((await unitStore.SeedStandardUnits(orgBetaId, accessB, default)).Succeeded);

        var pcsA = (await unitStore.ListActiveUnits(orgAlphaId, default)).Single(u => u.Code == "PCS");
        var pcsB = (await unitStore.ListActiveUnits(orgBetaId, default)).Single(u => u.Code == "PCS");

        var whA = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgAlphaId, siteAlphaId, "WH-A", "Alpha WH", true), accessA, default)).Value!;
        var whB = (await invStore.CreateWarehouse(new CreateWarehouseRequest(orgBetaId, siteBetaId, "WH-B", "Beta WH", true), accessB, default)).Value!;

        var itemA = (await itemStore.CreateItem(new CreateItemRequest(
            OrganizationId: orgAlphaId,
            Code: "ITEM-ALPHA",
            Name: "Alpha Item",
            Description: null,
            BaseUnitId: pcsA.Id,
            CanBePurchased: true,
            CanBeManufactured: true,
            CanBeSold: true,
            CanBeConsumed: true), accessA, default)).Value!;
        var itemB = (await itemStore.CreateItem(new CreateItemRequest(
            OrganizationId: orgBetaId,
            Code: "ITEM-BETA",
            Name: "Beta Item",
            Description: null,
            BaseUnitId: pcsB.Id,
            CanBePurchased: true,
            CanBeManufactured: true,
            CanBeSold: true,
            CanBeConsumed: true), accessB, default)).Value!;

        var suppA = (await procStore.CreateSupplier(
            new CreateSupplierRequest(orgAlphaId, "SUPP-A", "Alpha Supp", null, null, null, null, null), accessA, default)).Value!;
        var suppB = (await procStore.CreateSupplier(
            new CreateSupplierRequest(orgBetaId, "SUPP-B", "Beta Supp", null, null, null, null, null), accessB, default)).Value!;

        var custA = (await salesStore.CreateCustomer(
            new CreateCustomerRequest(orgAlphaId, "CUST-A", "Alpha Cust", null, null, null, null, null, null), utcNow, accessA, default)).Value!;
        var custB = (await salesStore.CreateCustomer(
            new CreateCustomerRequest(orgBetaId, "CUST-B", "Beta Cust", null, null, null, null, null, null), utcNow, accessB, default)).Value!;

        var soA = (await salesStore.CreateSalesOrder(
            new CreateSalesOrderRequest(orgAlphaId, siteAlphaId, custA.Id, null, new DateOnly(2026, 11, 16), null, null, "IRR", null, null,
                [new SalesOrderLineRequest(itemA.Summary.Id, 10m, pcsA.Id, 100m, null)]),
            [new ResolvedSalesOrderLine(itemA.Summary.Id, 10m, pcsA.Id, pcsA.Id, 10m, 100m, null)],
            utcNow, accessA, default)).Value!;

        var siteBScope = new OrganizationScope.Site(orgBetaId, siteBetaId);
        var salesUseCasesB = new SalesUseCases(salesStore, new StaticActor(actorB), perm);
        var poUseCasesB = new ProductionOrderUseCases(prodStore, bomStore, new StaticActor(actorB), perm);
        var procUseCasesB = new ProcurementUseCases(procStore, new StaticActor(actorB), perm);

        // =========================================================================
        // PART 1: APPLICATION LAYER CROSS-TENANT FOREIGN KEY REJECTION
        // =========================================================================

        // 1. SalesOrder in Org Beta referencing Customer from Org Alpha -> CustomerNotFound
        var soInvalidCust = await salesUseCasesB.CreateSalesOrder(siteBScope, new CreateSalesOrderRequest(
            orgBetaId, siteBetaId, custA.Id, null, new DateOnly(2026, 11, 16), null, whB.Id, "IRR", null, null,
            [new SalesOrderLineRequest(itemB.Summary.Id, 5m, pcsB.Id, 200m, null)]));
        Assert.False(soInvalidCust.Succeeded);
        Assert.Equal(SalesErrors.CustomerNotFound, soInvalidCust.Error!.Code);

        // 2. SalesOrder in Org Beta referencing SourceWarehouse from Org Alpha -> WarehouseInvalid
        var soInvalidWh = await salesUseCasesB.CreateSalesOrder(siteBScope, new CreateSalesOrderRequest(
            orgBetaId, siteBetaId, custB.Id, null, new DateOnly(2026, 11, 16), null, whA.Id, "IRR", null, null,
            [new SalesOrderLineRequest(itemB.Summary.Id, 5m, pcsB.Id, 200m, null)]));
        Assert.False(soInvalidWh.Succeeded);
        Assert.Equal(SalesErrors.WarehouseInvalid, soInvalidWh.Error!.Code);

        // 3. SalesOrder in Org Beta with line referencing Item from Org Alpha -> ItemInvalid
        var soInvalidItem = await salesUseCasesB.CreateSalesOrder(siteBScope, new CreateSalesOrderRequest(
            orgBetaId, siteBetaId, custB.Id, null, new DateOnly(2026, 11, 16), null, whB.Id, "IRR", null, null,
            [new SalesOrderLineRequest(itemA.Summary.Id, 5m, pcsB.Id, 200m, null)]));
        Assert.False(soInvalidItem.Succeeded);
        Assert.Equal(SalesErrors.ItemInvalid, soInvalidItem.Error!.Code);

        // 4. ProductionOrder in Org Beta referencing Item from Org Alpha -> ItemInvalid
        var poInvalidItem = await poUseCasesB.CreateDraftOrder(siteBScope, new CreateProductionOrderRequest(
            orgBetaId, siteBetaId, "BETA-PO-X", itemA.Summary.Id, 10m, pcsB.Id, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20)));
        Assert.False(poInvalidItem.Succeeded);
        Assert.Equal(ProductionOrderErrors.ItemInvalid, poInvalidItem.Error!.Code);

        // 5. ProductionOrder in Org Beta referencing Site from Org Alpha -> AccessDenied
        var poInvalidSite = await poUseCasesB.CreateDraftOrder(new OrganizationScope.Site(orgBetaId, siteAlphaId), new CreateProductionOrderRequest(
            orgBetaId, siteAlphaId, "BETA-PO-Y", itemB.Summary.Id, 10m, pcsB.Id, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20)));
        Assert.False(poInvalidSite.Succeeded);

        // 6. PurchaseOrder in Org Beta referencing Supplier from Org Alpha -> SupplierNotFound
        var purInvalidSupp = await procUseCasesB.CreateDraft(siteBScope, new CreatePurchaseOrderRequest(
            orgBetaId, siteBetaId, suppA.Id, null, new DateOnly(2026, 11, 16), null, whB.Id, "IRR", null,
            [new PurchaseOrderLineInput(itemB.Summary.Id, 10m, pcsB.Id, 50m, null)]));
        Assert.False(purInvalidSupp.Succeeded);
        Assert.Equal(ProcurementErrors.SupplierNotFound, purInvalidSupp.Error!.Code);

        // 7. PurchaseOrder in Org Beta referencing Site from Org Alpha -> AccessDenied
        var purInvalidSite = await procUseCasesB.CreateDraft(new OrganizationScope.Site(orgBetaId, siteAlphaId), new CreatePurchaseOrderRequest(
            orgBetaId, siteAlphaId, suppB.Id, null, new DateOnly(2026, 11, 16), null, whB.Id, "IRR", null,
            [new PurchaseOrderLineInput(itemB.Summary.Id, 10m, pcsB.Id, 50m, null)]));
        Assert.False(purInvalidSite.Succeeded);

        // 8. Bom in Org Beta referencing OutputItem from Org Alpha -> PrimaryItemInvalid
        var bomInvalidItem = await bomStore.CreateBom(
            new CreateBomRequest(orgBetaId, "BETA-BOM-X", "Invalid Item BOM", itemA.Summary.Id), accessB, default);
        Assert.False(bomInvalidItem.Succeeded);
        Assert.Equal(BomErrors.PrimaryItemInvalid, bomInvalidItem.Error!.Code);

        // 9. BomVersion in Org Beta with input line referencing Item from Org Alpha -> InputItemInvalid
        var bomB = (await bomStore.CreateBom(
            new CreateBomRequest(orgBetaId, "BETA-BOM-OK", "Valid Item BOM", itemB.Summary.Id), accessB, default)).Value!;
        var bomVerB = (await bomStore.CreateBomVersion(
            new CreateBomVersionRequest(orgBetaId, bomB.Summary.Id, 1, 1m, pcsB.Id, new DateOnly(2026, 1, 1), null, "v1"), accessB, default)).Value!;
        var bomLineInvalid = await bomStore.SaveDraftBomLines(
            orgBetaId, bomVerB.Summary.Id, Convert.FromBase64String(bomVerB.Summary.RowVersion),
            [new BomInputLineInput(1, itemA.Summary.Id, 2m, pcsB.Id, 0m, false, null)],
            [new BomOutputLineInput(1, itemB.Summary.Id, BomOutputType.MainProduct, 1m, pcsB.Id)],
            accessB, default);
        Assert.False(bomLineInvalid.Succeeded);
        Assert.Equal(BomErrors.InputItemInvalid, bomLineInvalid.Error!.Code);

        // 10. StockReservation in Org Beta referencing SalesOrder from Org Alpha -> ProductionOrderInvalid
        var resInvalidOrder = await resStore.ReserveSalesOrderStock(
            new ReserveSalesOrderStockRequest(orgBetaId, soA.Summary.Id, null, itemB.Summary.Id, 2m, pcsB.Id, whB.Id, null, null),
            2m, pcsB.Id, utcNow, accessB, default);
        Assert.False(resInvalidOrder.Succeeded);
        Assert.Equal(InventoryErrors.ProductionOrderInvalid, resInvalidOrder.Error!.Code);

        // =========================================================================
        // PART 2: DATABASE LEVEL COMPOSITE FOREIGN KEY CONSTRAINTS (FFA-010 DEFENSE)
        // =========================================================================
        await using var sqlDb = await database.Factory.CreateDbContextAsync();

        // Direct SQL Item with BaseUnitId from Org Alpha -> SQL 547 (FK_Item_BaseUnit)
        var sqlExItem = await Assert.ThrowsAsync<SqlException>(() => sqlDb.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT [organization].[Item] ([OrganizationId],[Code],[Name],[BaseUnitId],[CanBePurchased],[CanBeManufactured],[CanBeSold],[CanBeConsumed],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({orgBetaId},'HACK-ITM',N'Hack',{pcsA.Id},1,0,0,0,1,SYSUTCDATETIME(),1)"));
        Assert.Equal(547, sqlExItem.Number);
        Assert.Contains("FK_Item_BaseUnit", sqlExItem.Message, StringComparison.Ordinal);

        // Direct SQL Warehouse with SiteId from Org Alpha -> SQL 547 (FK_Warehouse_Site)
        var sqlExWh = await Assert.ThrowsAsync<SqlException>(() => sqlDb.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT [organization].[Warehouse] ([OrganizationId],[SiteId],[Code],[Name],[IsDefaultForSite],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({orgBetaId},{siteAlphaId},'HACK-WH',N'Hack',0,1,SYSUTCDATETIME(),1)"));
        Assert.Equal(547, sqlExWh.Number);
        Assert.Contains("FK_Warehouse_Site", sqlExWh.Message, StringComparison.Ordinal);

        // Direct SQL SalesOrder with CustomerId from Org Alpha -> SQL 547 (FK_SalesOrder_Customer)
        var sqlExSo = await Assert.ThrowsAsync<SqlException>(() => sqlDb.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT [organization].[SalesOrder] ([OrganizationId],[SiteId],[CustomerId],[OrderNumber],[OrderDate],[CurrencyCode],[Status],[CreatedAtUtc],[CreatedByActorId]) VALUES ({orgBetaId},{siteBetaId},{custA.Id},'HACK-SO',CAST(SYSUTCDATETIME() AS date),'IRR',1,SYSUTCDATETIME(),1)"));
        Assert.Equal(547, sqlExSo.Number);
        Assert.Contains("FK_SalesOrder_Customer", sqlExSo.Message, StringComparison.Ordinal);

        // Direct SQL InventoryBalance with WarehouseId from Org Alpha -> SQL 547 (FK_InventoryBalance_Warehouse)
        var sqlExBal = await Assert.ThrowsAsync<SqlException>(() => sqlDb.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT [organization].[InventoryBalance] ([OrganizationId],[SiteId],[WarehouseId],[ItemId],[LotCode],[BaseUnitId],[OnHandBaseQuantity],[BlockedBaseQuantity],[ReservedBaseQuantity],[LastMovedAtUtc],[CreatedAtUtc],[CreatedByActorId]) VALUES ({orgBetaId},{siteBetaId},{whA.Id},{itemB.Summary.Id},'',{pcsB.Id},10,0,0,SYSUTCDATETIME(),SYSUTCDATETIME(),1)"));
        Assert.Equal(547, sqlExBal.Number);
        Assert.Contains("FK_InventoryBalance_Warehouse", sqlExBal.Message, StringComparison.Ordinal);
    }

    private sealed class DisposableCrossTenantDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public string ConnectionString { get; }
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableCrossTenantDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            ConnectionString = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(ConnectionString).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableCrossTenantDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_xten_{Guid.NewGuid():N}";
            var db = new DisposableCrossTenantDatabase(builder, name);
            try
            {
                await using var conn = new SqlConnection(db.server.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE [{db.name}]";
                await cmd.ExecuteNonQueryAsync();
                db.created = true;

                await using (var ctx = await db.Factory.CreateDbContextAsync())
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
