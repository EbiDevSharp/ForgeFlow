using System.Text.Json;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Audit.Handlers;
using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Common;
using ForgeFlow.Domain.Events;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Infrastructure.Common;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DomainEventAuditUnitTests
{
    private sealed class TestAuditTrailSink : IAuditTrailSink
    {
        public List<(long OrganizationId, long? SiteId, string EventCode, string DetailsJson)> Records { get; } = [];

        public void Record(long organizationId, long? siteId, string eventCode, string detailsJson)
        {
            Records.Add((organizationId, siteId, eventCode, detailsJson));
        }
    }

    private sealed class StaticSinkAccessor(IAuditTrailSink? sink) : IAuditTrailSinkAccessor
    {
        public IAuditTrailSink? CurrentSink => sink;
    }

    [Fact]
    public void ProductionOrder_StateTransitions_Raise_Expected_DomainEvents()
    {
        var now = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var po = ProductionOrder.CreateDraft(
            organizationId: 10,
            siteId: 20,
            orderNumber: "PO-001",
            itemId: 30,
            requestedQuantity: 50m,
            requestedUnitId: 40,
            plannedStartDate: new DateOnly(2026, 11, 16),
            plannedDueDate: new DateOnly(2026, 11, 20),
            bomId: 1,
            bomCodeSnapshot: "BOM-001",
            bomVersionId: 2,
            bomVersionNumberSnapshot: 1,
            baseOutputQuantitySnapshot: 10m,
            baseOutputUnitIdSnapshot: 40,
            notes: "Test PO");

        Assert.Empty(po.DomainEvents);

        // 1. Release
        po.Release(now);
        Assert.Contains(po.DomainEvents, e => e is ProductionOrderReleasedEvent released &&
            released.OrganizationId == 10 &&
            released.SiteId == 20 &&
            released.OrderNumber == "PO-001" &&
            released.EventCode == "ProductionOrder.Released");

        // 2. Start
        po.Start(now.AddHours(1));
        Assert.Contains(po.DomainEvents, e => e is ProductionOrderStartedEvent started &&
            started.OrganizationId == 10 &&
            started.SiteId == 20 &&
            started.OrderNumber == "PO-001" &&
            started.EventCode == "ProductionOrder.Started");

        // 3. Complete
        po.Complete(producedQuantity: 48m, scrapQuantity: 2m, notes: "Finished batch", now.AddHours(4));
        Assert.Contains(po.DomainEvents, e => e is ProductionOrderCompletedEvent completed &&
            completed.OrganizationId == 10 &&
            completed.SiteId == 20 &&
            completed.OrderNumber == "PO-001" &&
            completed.ProducedQuantity == 48m &&
            completed.ScrapQuantity == 2m &&
            completed.EventCode == "ProductionOrder.Completed");

        // Clear events
        po.ClearDomainEvents();
        Assert.Empty(po.DomainEvents);
    }

    [Fact]
    public void SalesOrder_StateTransitions_Raise_Expected_DomainEvents()
    {
        var now = new DateTime(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc);
        var so = SalesOrder.CreateDraft(
            organizationId: 10,
            siteId: 20,
            customerId: 30,
            orderNumber: "SO-001",
            orderDate: new DateOnly(2026, 11, 16),
            requestedDeliveryDate: new DateOnly(2026, 11, 20),
            sourceWarehouseId: 40,
            currencyCode: "IRR",
            shippingAddressSnapshot: "Tehran",
            notes: "Sales test");

        so.ReplaceLines([(ItemId: 100, OrderedQuantity: 5m, OrderedUnitId: 1, BaseUnitIdSnapshot: 1, OrderedBaseQuantity: 5m, UnitPrice: 1000m, Notes: null)]);

        Assert.Empty(so.DomainEvents);

        // Approve
        so.Approve(now);
        Assert.Contains(so.DomainEvents, e => e is SalesOrderApprovedEvent approved &&
            approved.OrganizationId == 10 &&
            approved.SiteId == 20 &&
            approved.OrderNumber == "SO-001" &&
            approved.TotalAmount == 5000m &&
            approved.EventCode == "SalesOrder.Approved");

        // Dispatch
        so.RecordDispatch(deliveryOrderId: 77, deliveryOrderNumber: "DO-001", now.AddDays(1));
        Assert.Contains(so.DomainEvents, e => e is SalesOrderDispatchedEvent dispatched &&
            dispatched.OrganizationId == 10 &&
            dispatched.SiteId == 20 &&
            dispatched.OrderNumber == "SO-001" &&
            dispatched.DeliveryOrderId == 77 &&
            dispatched.DeliveryOrderNumber == "DO-001" &&
            dispatched.EventCode == "SalesOrder.Dispatched");
    }

    [Fact]
    public void QualityInspection_And_ItemLot_Raise_Expected_Events()
    {
        var now = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        var qi = QualityInspection.Create(
            organizationId: 10,
            siteId: 20,
            warehouseId: 30,
            itemId: 40,
            lotCode: "LOT-01",
            inspectionNumber: "QI-001",
            sourceType: QualityInspectionSourceType.Receipt,
            sourceTransactionId: 100,
            productionOrderId: null,
            baseUnitId: 1,
            inspectedBaseQuantity: 10m,
            notes: "Incoming inspection");

        qi.RecordDisposition(acceptBaseQuantity: 10m, rejectBaseQuantity: 0m, defectReasonCode: null, notes: "Accepted all", actorId: 99, now);

        Assert.Contains(qi.DomainEvents, e => e is QualityInspectionRecordedEvent recorded &&
            recorded.OrganizationId == 10 &&
            recorded.SiteId == 20 &&
            recorded.InspectionNumber == "QI-001" &&
            recorded.Disposition == "Accepted" &&
            recorded.EventCode == "QualityInspection.Disposed");

        var lot = ItemLot.Create(10, 40, "LOT-01");
        lot.RecordCoa("COA-999", now);

        Assert.Contains(lot.DomainEvents, e => e is LotCoaIssuedEvent coa &&
            coa.OrganizationId == 10 &&
            coa.SiteId == null &&
            coa.ItemId == 40 &&
            coa.LotNumber == "LOT-01" &&
            coa.CoaNumber == "COA-999" &&
            coa.EventCode == "ItemLot.Updated");
    }

    [Fact]
    public void BomVersion_Approval_Raises_BomApprovedEvent()
    {
        var bom = BomVersion.CreateDraft(
            organizationId: 10,
            bomId: 20,
            versionNumber: 1,
            baseOutputQuantity: 100m,
            baseOutputUnitId: 1,
            effectiveFromDate: new DateOnly(2026, 11, 1),
            effectiveToDate: null,
            notes: "Initial BOM");

        Assert.Empty(bom.DomainEvents);
        bom.TransitionTo(BomVersionStatus.Approved);

        Assert.Contains(bom.DomainEvents, e => e is BomApprovedEvent approved &&
            approved.OrganizationId == 10 &&
            approved.BomId == 20 &&
            approved.VersionNumber == 1 &&
            approved.EventCode == "BomVersion.StatusChanged");
    }

    [Fact]
    public async Task AuditHandlers_Record_Standardized_Audit_Payload_To_Sink()
    {
        var sink = new TestAuditTrailSink();
        var sinkAccessor = new StaticSinkAccessor(sink);
        var now = DateTime.UtcNow;

        // 1. ProductionOrderAuditHandler
        var poHandler = new ProductionOrderAuditHandler(sinkAccessor);
        await poHandler.HandleAsync(new ProductionOrderReleasedEvent(10, 20, 1, "PO-100", now));
        Assert.Single(sink.Records, r => r.OrganizationId == 10 && r.SiteId == 20 && r.EventCode == "ProductionOrder.Released");

        await poHandler.HandleAsync(new ProductionOrderCompletedEvent(10, 20, 1, "PO-100", 95m, 5m, now));
        Assert.Contains(sink.Records, r => r.OrganizationId == 10 && r.SiteId == 20 && r.EventCode == "ProductionOrder.Completed");

        // 2. SalesOrderAuditHandler
        var soHandler = new SalesOrderAuditHandler(sinkAccessor);
        await soHandler.HandleAsync(new SalesOrderApprovedEvent(10, 20, 2, "SO-200", 15000m, now));
        Assert.Contains(sink.Records, r => r.OrganizationId == 10 && r.SiteId == 20 && r.EventCode == "SalesOrder.Approved");

        await soHandler.HandleAsync(new SalesOrderDispatchedEvent(10, 20, 2, "SO-200", 55, "DO-55", now));
        Assert.Contains(sink.Records, r => r.OrganizationId == 10 && r.SiteId == 20 && r.EventCode == "SalesOrder.Dispatched");

        // 3. InventoryAuditHandler
        var invHandler = new InventoryAuditHandler(sinkAccessor);
        await invHandler.HandleAsync(new StockAdjustedEvent(10, 20, 30, 40, -10m, "Dispatch", "DO-55", now));
        Assert.Contains(sink.Records, r => r.OrganizationId == 10 && r.SiteId == 20 && r.EventCode == "InventoryTransaction.Posted");

        await invHandler.HandleAsync(new StockReservedEvent(10, 20, 40, 15m, "SalesOrder", 2, now));
        Assert.Contains(sink.Records, r => r.OrganizationId == 10 && r.SiteId == 20 && r.EventCode == "InventoryReservation.Created");

        // 4. QualityAuditHandler
        var qHandler = new QualityAuditHandler(sinkAccessor);
        await qHandler.HandleAsync(new QualityInspectionRecordedEvent(10, 20, 5, "QI-500", "Passed", now));
        Assert.Contains(sink.Records, r => r.OrganizationId == 10 && r.SiteId == 20 && r.EventCode == "QualityInspection.Disposed");

        // 5. BomAuditHandler
        var bomHandler = new BomAuditHandler(sinkAccessor);
        await bomHandler.HandleAsync(new BomApprovedEvent(10, 60, 61, 2, now));
        Assert.Contains(sink.Records, r => r.OrganizationId == 10 && r.SiteId == null && r.EventCode == "BomVersion.StatusChanged");

        // 6. SecurityAuditHandler
        var secHandler = new SecurityAuditHandler(sinkAccessor);
        await secHandler.HandleAsync(new RolePermissionsUpdatedEvent(10, 70, "InventoryClerk", ["Inventory.Read", "Inventory.Manage"], now));
        Assert.Contains(sink.Records, r => r.OrganizationId == 10 && r.SiteId == null && r.EventCode == "RoleDefinitionChanged");

        // Verify all payloads are valid JSON
        foreach (var record in sink.Records)
        {
            using var doc = JsonDocument.Parse(record.DetailsJson);
            Assert.NotNull(doc);
        }
    }

    [Fact]
    public async Task DomainEventDispatcher_Dispatches_To_All_Registered_Handlers()
    {
        var services = new ServiceCollection();
        var sink = new TestAuditTrailSink();
        services.AddSingleton<IAuditTrailSinkAccessor>(new StaticSinkAccessor(sink));

        services.AddScoped<ProductionOrderAuditHandler>();
        services.AddScoped<IDomainEventHandler<ProductionOrderReleasedEvent>>(sp => sp.GetRequiredService<ProductionOrderAuditHandler>());
        services.AddScoped<IDomainEventHandler<ProductionOrderCompletedEvent>>(sp => sp.GetRequiredService<ProductionOrderAuditHandler>());

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();

        var evt = new ProductionOrderReleasedEvent(100, 200, 1, "PO-DISP", DateTime.UtcNow);
        await dispatcher.DispatchAsync(evt);

        Assert.Single(sink.Records);
        Assert.Equal("ProductionOrder.Released", sink.Records[0].EventCode);
        Assert.Equal(100, sink.Records[0].OrganizationId);
        Assert.Equal(200, sink.Records[0].SiteId);
    }

    [Fact]
    public void AmbientAuditTrailSinkAccessor_Isolates_Scopes_Safely()
    {
        var accessor = new AmbientAuditTrailSinkAccessor();
        Assert.Null(accessor.CurrentSink);

        var sink1 = new TestAuditTrailSink();
        var sink2 = new TestAuditTrailSink();

        using (accessor.BeginScope(sink1))
        {
            Assert.Same(sink1, accessor.CurrentSink);

            using (accessor.BeginScope(sink2))
            {
                Assert.Same(sink2, accessor.CurrentSink);
            }

            Assert.Same(sink1, accessor.CurrentSink);
        }

        Assert.Null(accessor.CurrentSink);
    }
}
