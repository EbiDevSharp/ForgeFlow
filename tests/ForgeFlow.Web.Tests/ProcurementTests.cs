using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Units;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ProcurementTests
{
    private static readonly TrustedActor Actor1 = new(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
    private static readonly OrganizationScope.Organization OrgScope = new(10);
    private static readonly OrganizationScope.Site Site1Scope = new(10, 1);

    [Fact]
    public void P01_supplier_and_purchase_order_domain_invariants_and_transitions_hold()
    {
        var sup = Supplier.Create(10, "sup-01", " Steel Co ", "Ali Rezaei", "+98-21-555", "ali@steel.local", "Tehran", "Primary vendor");
        Assert.Equal("SUP-01", sup.Code);
        Assert.Equal("Steel Co", sup.Name);
        Assert.True(sup.SetActive(false));
        Assert.Throws<ProcurementRuleException>(() => sup.Update("Updated", null, null, null, null, null));

        var cur = Currency.Create(10, "aed", "UAE Dirham", "د.إ", 2);
        Assert.Equal("AED", cur.Code);
        Assert.Throws<UnitRuleException>(() => Currency.Create(10, "A1D", "Bad", "$", 2));
        Assert.Throws<ProcurementRuleException>(() =>
            PurchaseOrder.CreateDraft(10, 1, 7, "PO-BAD-C", new DateOnly(2026, 11, 10), null, null, "US1", null));

        var po = PurchaseOrder.CreateDraft(10, 1, 7, "po-1001", new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 15), 2, "usd", "Urgent");
        Assert.Equal("PO-1001", po.OrderNumber);
        Assert.Equal("USD", po.CurrencyCode);
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Throws<ProcurementRuleException>(() => po.Approve(DateTime.UtcNow));

        po.ReplaceLines(
        [
            (100, 10m, 2, 1, 10000m, 12.5m, "First line"),
            (101, 5m, 1, 1, 5m, null, null)
        ]);
        Assert.Equal(2, po.Lines.Count);
        Assert.Equal(10000m, po.Lines[0].RemainingBaseQuantity);

        var now = new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);
        po.Approve(now);
        Assert.Equal(PurchaseOrderStatus.Approved, po.Status);
        Assert.Throws<ProcurementRuleException>(() => po.UpdateDraft(7, new DateOnly(2026, 11, 10), null, null, "USD", null));

        // Partial receipt
        po.ApplyReceipts([(0L, 4000m)]); // line 0 has Id=0 in unit test
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, po.Status);
        Assert.Equal(6000m, po.Lines[0].RemainingBaseQuantity);

        // Over-receipt must fail
        Assert.Throws<ProcurementRuleException>(() => po.ApplyReceipts([(0L, 6001m)]));

        // Cannot cancel once partial receipts exist
        Assert.Throws<ProcurementRuleException>(() => po.Cancel("Changed mind", now));

        // Short close from PartiallyReceived
        po.CloseRemaining(now);
        Assert.Equal(PurchaseOrderStatus.Closed, po.Status);
        Assert.Throws<ProcurementRuleException>(() => po.ApplyReceipts([(0L, 1m)]));

        // Reversal reopens to PartiallyReceived
        po.RevertReceipts([(0L, 4000m)]);
        Assert.Equal(PurchaseOrderStatus.Approved, po.Status);
        po.Cancel("Cancelled after reversal", now);
        Assert.Equal(PurchaseOrderStatus.Cancelled, po.Status);
    }

    [Fact]
    public async Task P02_purchase_order_use_cases_enforce_permissions_site_scope_conversion_and_lot_qc()
    {
        var store = new CapturingProcurementStore
        {
            ItemContexts = new Dictionary<long, ProcurementItemLookup>
            {
                [100] = new(100, true, true, BaseUnitId: 1, RequiresLotTracking: true, QualityControlMode: QualityControlMode.IncomingReceipts,
                    Edges: [new UnitConversionEdge(FromUnitId: 2, ToUnitId: 1, Factor: 1000m)]),
                [101] = new(101, true, CanBePurchased: false, BaseUnitId: 1, RequiresLotTracking: false, QualityControlMode: QualityControlMode.None, Edges: [])
            },
            ActiveUnitIds = new HashSet<long> { 1, 2 }
        };

        var denyAll = new ProcurementUseCases(store, new FixedActor(Actor1), new DenyAllPermissions());
        var denied = await denyAll.CreateSupplier(OrgScope, new CreateSupplierRequest(10, "SUP1", "Supplier 1"));
        Assert.False(denied.Succeeded);
        Assert.Equal(ProcurementErrors.AccessDenied, denied.Error!.Code);

        var useCases = new ProcurementUseCases(store, new FixedActor(Actor1), new AllowAllPermissions());

        // Supplier cannot be created under Site scope (Organization-shared)
        var siteSupplier = await useCases.CreateSupplier(Site1Scope, new CreateSupplierRequest(10, "SUP1", "Supplier 1"));
        Assert.False(siteSupplier.Succeeded);
        Assert.Equal(ProcurementErrors.AccessDenied, siteSupplier.Error!.Code);

        // Non-purchasable item rejected in CreateDraft
        var badItem = await useCases.CreateDraft(Site1Scope, new CreatePurchaseOrderRequest(
            10, 1, 5, "PO-BAD", new DateOnly(2026, 11, 11), null, null, "IRR", null,
            [new PurchaseOrderLineInput(101, 2m, 1, null, null)]));
        Assert.False(badItem.Succeeded);
        Assert.Equal(ProcurementErrors.ItemNotPurchasable, badItem.Error!.Code);

        // Draft creation converts Ton(2) -> Kg(1) using x1000 factor
        var created = await useCases.CreateDraft(Site1Scope, new CreatePurchaseOrderRequest(
            10, 1, 5, "PO-10", new DateOnly(2026, 11, 11), null, 20, "USD", "Monthly",
            [new PurchaseOrderLineInput(100, 2m, 2, 50m, null)]));
        Assert.True(created.Succeeded);
        var capturedLine = Assert.Single(store.LastCreatedLines!);
        Assert.Equal(2000m, capturedLine.OrderedBaseQuantity);

        // Site 2 cannot view or receive Site 1 PO
        var crossSite = await useCases.GetPurchaseOrder(new OrganizationScope.Site(10, 2), 10, created.Value!.Summary.Id);
        Assert.False(crossSite.Succeeded);
        Assert.Equal(ProcurementErrors.AccessDenied, crossSite.Error!.Code);

        // Receipt missing LotCode fails cleanly when item requires lot tracking
        var missingLot = await useCases.ReceiveOrder(Site1Scope, new ReceivePurchaseOrderRequest(
            10, created.Value.Summary.Id, 20, "RCV-PO1", null, null, created.Value.Summary.RowVersion,
            [new ReceivePurchaseOrderLineInput(created.Value.Lines[0].Id, 1m, 2, null, null, null, null)]));
        Assert.False(missingLot.Succeeded);
        Assert.Equal(InventoryErrors.LotRequired, missingLot.Error!.Code);

        // Valid lot converts 1 Ton -> 1000 KG base & triggers QC
        var okReceipt = await useCases.ReceiveOrder(Site1Scope, new ReceivePurchaseOrderRequest(
            10, created.Value.Summary.Id, 20, "RCV-PO1", "First truck", "idem-1", created.Value.Summary.RowVersion,
            [new ReceivePurchaseOrderLineInput(created.Value.Lines[0].Id, 1m, 2, "LOT-PO1", new DateOnly(2026, 11, 1), new DateOnly(2027, 11, 1), null)]));
        Assert.True(okReceipt.Succeeded);
        var recLine = Assert.Single(store.LastReceiptLines!);
        Assert.Equal(1000m, recLine.BaseQuantityDelta);
        Assert.Equal("LOT-PO1", recLine.LotCode);
        Assert.True(recLine.TriggersQualityControl);
    }

    [Fact]
    public async Task P03_material_shortage_report_calculates_informational_on_order_quantity_without_altering_shortage()
    {
        var reportingStore = new FakeShortageStore(new ShortageDataset(
            Orders:
            [
                new ReportOpenOrderRow(1, "MO-1", 1, "S1", new DateOnly(2026, 11, 15), ProductionOrderStatus.Released)
            ],
            Inputs:
            [
                new ReportOpenOrderInputRow(1, 100, 500m, 1)
            ],
            Balances:
            [
                new ReportStockBalanceRow(1, 100, 100m, BlockedBaseQuantity: 20m) // 80 available
            ],
            Items:
            [
                new ReportItemRef(100, "RAW-1", "Raw Material", 1, "KG", "kg")
            ],
            Conversions: [],
            OpenPurchaseLines:
            [
                new ReportOpenPurchaseLineRow(1, 100, 250m)
            ]));

        var useCases = new ReportingUseCases(reportingStore, new FixedActor(Actor1), new AllowAllPermissions());
        var result = await useCases.GetMaterialShortageReport(OrgScope, 10, new MaterialShortageQuery(SiteId: 1));
        Assert.True(result.Succeeded);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(500m, row.RequiredBaseQuantity);
        Assert.Equal(80m, row.AvailableBaseQuantity);
        Assert.Equal(420m, row.ShortageBaseQuantity); // OnOrder does NOT reduce shortage
        Assert.Equal(250m, row.OnOrderBaseQuantity);
        Assert.Equal(250m, result.Value.TotalOnOrderBaseQuantity);
    }

    private sealed class FixedActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class AllowAllPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(true));
    }

    private sealed class DenyAllPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(false));
    }

    private sealed class CapturingProcurementStore : IProcurementOperationsStore
    {
        public IReadOnlyDictionary<long, ProcurementItemLookup> ItemContexts { get; init; } = new Dictionary<long, ProcurementItemLookup>();
        public IReadOnlySet<long> ActiveUnitIds { get; init; } = new HashSet<long>();
        public IReadOnlyList<ResolvedPurchaseOrderLineWrite>? LastCreatedLines { get; private set; }
        public IReadOnlyList<ResolvedPurchaseOrderReceiptLine>? LastReceiptLines { get; private set; }
        private PurchaseOrderDetails? _savedOrder;

        public Task<OrganizationPage<SupplierSummary>> ListSuppliers(long organizationId, SupplierListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<SupplierSummary?> GetSupplier(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SupplierSummary>> CreateSupplier(CreateSupplierRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SupplierSummary>> UpdateSupplier(long organizationId, long id, byte[] version, UpdateSupplierRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SupplierSummary>> SetSupplierActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<PurchaseOrderSummary>> ListPurchaseOrders(long organizationId, long? forcedSiteId, PurchaseOrderListQuery query, CancellationToken ct) => throw new NotImplementedException();

        public Task<PurchaseOrderDetails?> GetPurchaseOrder(long organizationId, long id, CancellationToken ct)
            => Task.FromResult(_savedOrder);

        public Task<PurchaseOrderSiteLookup?> GetSiteLookup(long organizationId, long siteId, CancellationToken ct)
            => Task.FromResult<PurchaseOrderSiteLookup?>(new(siteId, organizationId, true, true));

        public Task<PurchaseOrderWarehouseLookup?> GetWarehouseLookup(long organizationId, long warehouseId, CancellationToken ct)
            => Task.FromResult<PurchaseOrderWarehouseLookup?>(new(warehouseId, organizationId, 1, true, true, true));

        public Task<IReadOnlyDictionary<long, ProcurementItemLookup>> LoadItemContexts(long organizationId, IReadOnlyCollection<long> itemIds, CancellationToken ct)
            => Task.FromResult(ItemContexts);

        public Task<IReadOnlySet<long>> LoadActiveUnitIds(long organizationId, IReadOnlyCollection<long> unitIds, CancellationToken ct)
            => Task.FromResult(ActiveUnitIds);

        public Task<OrganizationResult<PurchaseOrderDetails>> CreateDraft(CreatePurchaseOrderRequest request, IReadOnlyList<ResolvedPurchaseOrderLineWrite> resolvedLines, OrganizationWriteAccess access, CancellationToken ct)
        {
            LastCreatedLines = resolvedLines;
            var rv = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            _savedOrder = new PurchaseOrderDetails(
                new PurchaseOrderSummary(55, request.OrganizationId, request.SiteId, "S1", "Site 1", request.SupplierId, "SUP1", "Supplier 1",
                    request.OrderNumber ?? "PO-10", PurchaseOrderStatus.Approved, request.OrderDate, request.ExpectedDeliveryDate,
                    request.TargetWarehouseId, "WH1", "Main", request.CurrencyCode ?? "USD",
                    resolvedLines.Sum(l => l.OrderedBaseQuantity), 0m, null, resolvedLines.Count, request.Notes, null,
                    DateTime.UtcNow, DateTime.UtcNow, null, null, rv),
                resolvedLines.Select((l, idx) => new PurchaseOrderLineDetails(
                    1000 + idx, idx + 1, l.ItemId, "ITM", "Item", true, QualityControlMode.IncomingReceipts,
                    l.OrderedQuantity, l.OrderedUnitId, "TON", "t", l.BaseUnitIdSnapshot, "KG", "kg",
                    l.OrderedBaseQuantity, 0m, l.OrderedBaseQuantity, l.UnitPrice, l.Notes)).ToArray(),
                []);
            return Task.FromResult(OrganizationResult<PurchaseOrderDetails>.Success(_savedOrder));
        }

        public Task<OrganizationResult<PurchaseOrderDetails>> UpdateDraft(long organizationId, long id, byte[] version, UpdatePurchaseOrderRequest request, IReadOnlyList<ResolvedPurchaseOrderLineWrite> resolvedLines, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<PurchaseOrderDetails>> ApproveOrder(long organizationId, long id, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<PurchaseOrderDetails>> CloseOrder(long organizationId, long id, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<PurchaseOrderDetails>> CancelOrder(long organizationId, long id, byte[] version, string? reason, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();

        public Task<OrganizationResult<PurchaseOrderReceiptResult>> ReceiveOrder(ReceivePurchaseOrderRequest request, long siteId, byte[] version, string documentNumber, IReadOnlyList<ResolvedPurchaseOrderReceiptLine> resolvedLines, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct)
        {
            LastReceiptLines = resolvedLines;
            return Task.FromResult(OrganizationResult<PurchaseOrderReceiptResult>.Success(
                new PurchaseOrderReceiptResult(_savedOrder!, 900, documentNumber)));
        }
    }

    private sealed class FakeShortageStore(ShortageDataset dataset) : IReportingQueryStore
    {
        public Task<IReadOnlyList<ReportSiteInfo>> ListAuthorizedSites(long organizationId, long? forcedSiteId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ShortageDataset> GetShortageDataset(long organizationId, long? siteId, DateOnly? dueFrom, DateOnly? dueTo, CancellationToken ct) => Task.FromResult(dataset);
        public Task<VarianceDataset> GetVarianceDataset(long organizationId, long? siteId, long? productionOrderId, ProductionOrderStatus? status, DateOnly? dueFrom, DateOnly? dueTo, string? search, CancellationToken ct) => throw new NotImplementedException();
    }
}
