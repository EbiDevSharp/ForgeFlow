using ForgeFlow.Domain.Events;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Infrastructure.Identity;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DomainStateTransitionTests
{
    private static readonly DateTime FixedUtc = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly DateOnly Tomorrow = new(2026, 11, 19);

    #region SalesOrder State Transitions

    [Fact]
    public void SalesOrder_Draft_transitions_through_lifecycle_correctly()
    {
        var so = SalesOrder.CreateDraft(1, 10, 100, "SO-001", Today, Tomorrow, null, "USD", "123 Main St", "Test notes");
        Assert.Equal(SalesOrderStatus.Draft, so.Status);
        Assert.False(so.CanReserveStock);

        so.UpdateDraft(100, Today, Tomorrow, null, "EUR", "456 Elm St", "Updated notes");
        Assert.Equal("EUR", so.CurrencyCode);

        so.ReplaceLines([(1001, 10m, 50, 50, 10m, 25.50m, "Line note")]);
        Assert.Single(so.Lines);
        Assert.Equal(255.00m, so.CalculateTotalAmount());

        so.Approve(FixedUtc);
        Assert.Equal(SalesOrderStatus.Approved, so.Status);
        Assert.True(so.CanReserveStock);
        Assert.Contains(so.DomainEvents, e => e is SalesOrderApprovedEvent);

        var lineId = so.Lines[0].Id;
        so.ApplyDispatches([(lineId, 4m)]);
        Assert.Equal(SalesOrderStatus.PartiallyDispatched, so.Status);
        Assert.True(so.CanReserveStock);
        Assert.Equal(4m, so.Lines[0].DispatchedBaseQuantity);
        Assert.Equal(6m, so.Lines[0].RemainingBaseQuantity);

        so.ApplyDispatches([(lineId, 6m)]);
        Assert.Equal(SalesOrderStatus.Dispatched, so.Status);
        Assert.False(so.CanReserveStock);
        Assert.Equal(10m, so.Lines[0].DispatchedBaseQuantity);
        Assert.Equal(0m, so.Lines[0].RemainingBaseQuantity);

        so.Close(FixedUtc);
        Assert.Equal(SalesOrderStatus.Closed, so.Status);
        Assert.False(so.CanReserveStock);
        Assert.Contains(so.DomainEvents, e => e is SalesOrderClosedEvent);
    }

    [Fact]
    public void SalesOrder_RevertDispatches_restores_status_and_quantities()
    {
        var so = SalesOrder.CreateDraft(1, 10, 100, "SO-002", Today, Tomorrow, null, "USD", null, null);
        so.ReplaceLines([(1001, 10m, 50, 50, 10m, 10m, null)]);
        so.Approve(FixedUtc);

        var lineId = so.Lines[0].Id;
        so.ApplyDispatches([(lineId, 10m)]);
        Assert.Equal(SalesOrderStatus.Dispatched, so.Status);

        so.RevertDispatches([(lineId, 4m)]);
        Assert.Equal(SalesOrderStatus.PartiallyDispatched, so.Status);
        Assert.Equal(6m, so.Lines[0].DispatchedBaseQuantity);

        so.RevertDispatches([(lineId, 6m)]);
        Assert.Equal(SalesOrderStatus.Approved, so.Status);
        Assert.Equal(0m, so.Lines[0].DispatchedBaseQuantity);
    }

    [Fact]
    public void SalesOrder_Cannot_mutate_or_cancel_when_invalid()
    {
        var so = SalesOrder.CreateDraft(1, 10, 100, "SO-003", Today, Tomorrow, null, "USD", null, null);
        so.ReplaceLines([(1001, 5m, 50, 50, 5m, 10m, null)]);
        so.Approve(FixedUtc);

        // Cannot update draft when approved
        var exDraft = Assert.Throws<SalesRuleException>(() => so.UpdateDraft(100, Today, Tomorrow, null, "USD", null, null));
        Assert.Equal(SalesErrors.NotDraft, exDraft.Code);

        // Cannot replace lines when approved
        var exLines = Assert.Throws<SalesRuleException>(() => so.ReplaceLines([(1001, 5m, 50, 50, 5m, 10m, null)]));
        Assert.Equal(SalesErrors.NotDraft, exLines.Code);

        // Dispatch a portion
        var lineId = so.Lines[0].Id;
        so.ApplyDispatches([(lineId, 2m)]);

        // Cannot cancel when partially dispatched
        var exCancel = Assert.Throws<SalesRuleException>(() => so.Cancel("Mistake", FixedUtc));
        Assert.Equal(SalesErrors.HasDispatchesCannotCancel, exCancel.Code);
    }

    #endregion

    #region ProductionOrder State Transitions

    [Fact]
    public void ProductionOrder_Draft_transitions_through_lifecycle_correctly()
    {
        var po = ProductionOrder.CreateDraft(1, 10, "PO-001", 500, 100m, 5, Today, Tomorrow, 1, "BOM-01", 1, 1, 1m, 5, "Order notes");
        Assert.Equal(ProductionOrderStatus.Draft, po.Status);
        Assert.False(po.CanReserveStock);

        po.UpdateDraftHeader(120m, 5, Today, Tomorrow, "Updated notes");
        Assert.Equal(120m, po.RequestedQuantity);

        po.Release(FixedUtc);
        Assert.Equal(ProductionOrderStatus.Released, po.Status);
        Assert.True(po.CanReserveStock);
        Assert.NotNull(po.ReleasedAtUtc);
        Assert.Contains(po.DomainEvents, e => e is ProductionOrderReleasedEvent);

        po.Start(FixedUtc);
        Assert.Equal(ProductionOrderStatus.InProgress, po.Status);
        Assert.True(po.CanReserveStock);
        Assert.NotNull(po.StartedAtUtc);

        po.RecordActuals(80m, 2m, "Midpoint run");
        Assert.Equal(80m, po.ActualProducedQuantity);
        Assert.Equal(2m, po.ActualScrapQuantity);

        po.Complete(115m, 5m, "Finished batch", FixedUtc);
        Assert.Equal(ProductionOrderStatus.Completed, po.Status);
        Assert.False(po.CanReserveStock);
        Assert.Equal(115m, po.ActualProducedQuantity);
        Assert.Equal(5m, po.ActualScrapQuantity);
        Assert.NotNull(po.CompletedAtUtc);
        Assert.Contains(po.DomainEvents, e => e is ProductionOrderCompletedEvent);
    }

    [Fact]
    public void ProductionOrder_Invalid_transitions_are_rejected()
    {
        var po = ProductionOrder.CreateDraft(1, 10, "PO-002", 500, 50m, 5, Today, Tomorrow, 1, "BOM-01", 1, 1, 1m, 5, null);

        // Cannot start a draft directly
        var exStart = Assert.Throws<ProductionOrderRuleException>(() => po.Start(FixedUtc));
        Assert.Equal(ProductionOrderErrors.TransitionInvalid, exStart.Code);

        // Release order
        po.Release(FixedUtc);

        // Cannot edit draft header once released
        var exEdit = Assert.Throws<ProductionOrderRuleException>(() => po.UpdateDraftHeader(60m, 5, Today, Tomorrow, null));
        Assert.Equal(ProductionOrderErrors.OrderImmutable, exEdit.Code);

        // Cannot cancel with empty reason
        var exCancel = Assert.Throws<ProductionOrderRuleException>(() => po.Cancel("   ", FixedUtc));
        Assert.Equal(ProductionOrderErrors.CancellationReasonInvalid, exCancel.Code);

        // Cancel order
        po.Cancel("Cancelled due to schedule change", FixedUtc);
        Assert.Equal(ProductionOrderStatus.Cancelled, po.Status);
        Assert.False(po.CanReserveStock);

        // Cannot complete cancelled order
        var exComp = Assert.Throws<ProductionOrderRuleException>(() => po.Complete(10m, 0m, null, FixedUtc));
        Assert.Equal(ProductionOrderErrors.TransitionInvalid, exComp.Code);
    }

    #endregion

    #region PurchaseOrder State Transitions

    [Fact]
    public void PurchaseOrder_Draft_transitions_through_lifecycle_correctly()
    {
        var po = PurchaseOrder.CreateDraft(1, 10, 200, "PUR-001", Today, Tomorrow, null, "USD", "PO note");
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);

        po.UpdateDraft(200, Today, Tomorrow, null, "GBP", "Updated note");
        Assert.Equal("GBP", po.CurrencyCode);

        po.ReplaceLines([(1001, 20m, 5, 5, 20m, 15m, null)]);
        Assert.Single(po.Lines);
        Assert.Equal(300m, po.CalculateTotalEstimatedAmount());

        po.Approve(FixedUtc);
        Assert.Equal(PurchaseOrderStatus.Approved, po.Status);
        Assert.Contains(po.DomainEvents, e => e is PurchaseOrderApprovedEvent);

        var lineId = po.Lines[0].Id;
        po.ApplyReceipts([(lineId, 10m)]);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, po.Status);
        Assert.Equal(10m, po.Lines[0].ReceivedBaseQuantity);
        Assert.Equal(10m, po.Lines[0].RemainingBaseQuantity);

        po.CloseRemaining(FixedUtc);
        Assert.Equal(PurchaseOrderStatus.Closed, po.Status);
    }

    [Fact]
    public void PurchaseOrder_Cannot_mutate_draft_when_not_draft()
    {
        var po = PurchaseOrder.CreateDraft(1, 10, 200, "PUR-002", Today, Tomorrow, null, "USD", null);
        po.ReplaceLines([(1001, 10m, 5, 5, 10m, 5m, null)]);
        po.Approve(FixedUtc);

        var exDraft = Assert.Throws<ProcurementRuleException>(() => po.UpdateDraft(200, Today, Tomorrow, null, "USD", null));
        Assert.Equal(ProcurementErrors.NotDraft, exDraft.Code);

        var exLines = Assert.Throws<ProcurementRuleException>(() => po.ReplaceLines([(1001, 10m, 5, 5, 10m, 5m, null)]));
        Assert.Equal(ProcurementErrors.NotDraft, exLines.Code);
    }

    #endregion

    #region QualityInspection State Transitions

    [Fact]
    public void QualityInspection_Lifecycle_and_concession_validation()
    {
        var qi = QualityInspection.Create(1, 10, 100, 500, "LOT-1", "QI-001", QualityInspectionSourceType.Receipt, 1, null, 5, 50m, null);
        Assert.Equal(QualityInspectionStatus.Pending, qi.Status);
        Assert.Equal(50m, qi.RemainingBlockedBaseQuantity);

        qi.ReplaceLines([
            new QualityInspectionLineWriteData("Visual Check", "No scratches", "Passed", true, null, QualityParameterId: 1, IsMandatory: true),
            new QualityInspectionLineWriteData("Hardness", ">= 50 HRC", "48 HRC", false, null, QualityParameterId: 2, IsMandatory: true)
        ]);
        Assert.Equal(QualityInspectionStatus.InReview, qi.Status);
        Assert.Equal(2, qi.Lines.Count);

        // Accepting when a mandatory check failed requires concession notes or defect code
        var exConcession = Assert.Throws<QualityRuleException>(() => qi.RecordDisposition(50m, 0m, null, null, 99, FixedUtc));
        Assert.Equal(QualityErrors.ConcessionReasonRequired, exConcession.Code);

        // Valid disposition with concession reason
        qi.RecordDisposition(45m, 5m, "DEF-01", "Approved under concession waiver #12", 99, FixedUtc);
        Assert.Equal(QualityInspectionStatus.Completed, qi.Status);
        Assert.Equal(45m, qi.AcceptedBaseQuantity);
        Assert.Equal(5m, qi.RejectedBaseQuantity);
        Assert.Equal(0m, qi.RemainingBlockedBaseQuantity);
        Assert.Contains(qi.DomainEvents, e => e is QualityInspectionRecordedEvent);
    }

    [Fact]
    public void QualityInspection_Cancellation_requires_no_prior_dispositions()
    {
        var qi = QualityInspection.Create(1, 10, 100, 500, "LOT-2", "QI-002", QualityInspectionSourceType.ManualHold, null, null, 5, 30m, null);
        qi.RecordDisposition(10m, 0m, null, "Partial accept", 99, FixedUtc);

        var exCancel = Assert.Throws<QualityRuleException>(() => qi.Cancel("Cancel remainder", 99, FixedUtc));
        Assert.Equal(QualityErrors.InspectionHasDispositions, exCancel.Code);
    }

    #endregion

    #region ItemLot Invariant Validation

    [Fact]
    public void ItemLot_Expiration_and_date_invariants()
    {
        var lot = ItemLot.Create(1, 500, "LOT-ALPHA", Today.AddDays(-30), Today.AddDays(-1), "Expired lot");
        Assert.True(lot.IsExpiredOn(Today));
        Assert.False(lot.IsExpiredOn(Today.AddDays(-2)));

        // Updating with manufacture date after expiration date throws
        var exDates = Assert.Throws<QualityRuleException>(() => lot.UpdateMetadata(Tomorrow, Today, null));
        Assert.Equal(QualityErrors.LotDatesInvalid, exDates.Code);

        // Valid update
        var updated = lot.UpdateMetadata(Today, Tomorrow, "Updated notes");
        Assert.True(updated);
        Assert.False(lot.IsExpiredOn(Today));
    }

    #endregion

    #region InventoryBalance & StockReservation

    [Fact]
    public void InventoryBalance_Deltas_and_quantity_reservations()
    {
        var bal = InventoryBalance.CreateZero(1, 10, 100, 500, "LOT-X", 5, FixedUtc);
        Assert.Equal(0m, bal.OnHandBaseQuantity);
        Assert.Equal(0m, bal.AvailableBaseQuantity);

        bal.ApplyDelta(100m, FixedUtc);
        Assert.Equal(100m, bal.OnHandBaseQuantity);
        Assert.Equal(100m, bal.AvailableBaseQuantity);

        bal.ReserveQuantity(30m, FixedUtc);
        Assert.Equal(30m, bal.ReservedBaseQuantity);
        Assert.Equal(70m, bal.AvailableBaseQuantity);

        bal.BlockQuantity(20m, FixedUtc);
        Assert.Equal(20m, bal.BlockedBaseQuantity);
        Assert.Equal(50m, bal.AvailableBaseQuantity);

        // Outbound exceeding available throws
        var exStock = Assert.Throws<InventoryRuleException>(() => bal.ApplyDelta(-60m, FixedUtc, enforceAvailableOnOutbound: true));
        Assert.Equal(InventoryErrors.InsufficientStock, exStock.Code);

        bal.ReleaseReservedQuantity(30m, FixedUtc);
        Assert.Equal(0m, bal.ReservedBaseQuantity);
        Assert.Equal(80m, bal.AvailableBaseQuantity);

        bal.ReleaseBlockedQuantity(20m, FixedUtc);
        Assert.Equal(0m, bal.BlockedBaseQuantity);
        Assert.Equal(100m, bal.AvailableBaseQuantity);
    }

    [Fact]
    public void StockReservation_Consume_and_release_flow()
    {
        var res = StockReservation.Create(1, 10, 100, 50, null, 500, "LOT-X", 5, 40m, "Prod res");
        Assert.Equal(ReservationStatus.Active, res.Status);
        Assert.Equal(40m, res.RemainingReservedBaseQuantity);
        Assert.True(res.IsOpen);

        var consumed = res.Consume(15m);
        Assert.Equal(15m, consumed);
        Assert.Equal(ReservationStatus.PartiallyConsumed, res.Status);
        Assert.Equal(25m, res.RemainingReservedBaseQuantity);

        var released = res.Release(FixedUtc);
        Assert.Equal(25m, released);
        Assert.Equal(ReservationStatus.Released, res.Status);
        Assert.Equal(0m, res.RemainingReservedBaseQuantity);
        Assert.False(res.IsOpen);

        // Cannot consume once released
        var exConsume = Assert.Throws<InventoryRuleException>(() => res.Consume(5m));
        Assert.Equal(InventoryErrors.ReservationInvalidState, exConsume.Code);
    }

    #endregion

    #region Routing & Operation State Transitions

    [Fact]
    public void Routing_Draft_and_activation_lifecycle()
    {
        var routing = Routing.CreateDraft(1, 10, "RT-001", "Machining Process", 500, null, 1, false, "Initial routing");
        Assert.Equal(RoutingStatus.Draft, routing.Status);

        routing.UpdateDraft("Precision Machining", null, true, "Updated notes");
        Assert.Equal("Precision Machining", routing.Name);
        Assert.True(routing.IsDefault);

        routing.ReplaceOperations([
            (10, "OP-10", "Turning", 20, 15m, 2m, 0m, 0m, false, null),
            (20, "OP-20", "Milling", 21, 20m, 5m, 5m, 0m, true, "Inspect surface")
        ]);
        Assert.Equal(2, routing.Operations.Count);

        routing.Activate(FixedUtc);
        Assert.Equal(RoutingStatus.Active, routing.Status);
        Assert.NotNull(routing.ActivatedAtUtc);

        // Cannot update draft once active
        var exDraft = Assert.Throws<RoutingRuleException>(() => routing.UpdateDraft("Illegal update", null, false, null));
        Assert.Equal(RoutingErrors.RoutingNotDraft, exDraft.Code);

        routing.MarkObsolete();
        Assert.Equal(RoutingStatus.Obsolete, routing.Status);
        Assert.False(routing.IsDefault);

        // Cannot set default on obsolete routing
        var exDef = Assert.Throws<RoutingRuleException>(() => routing.SetDefault(true));
        Assert.Equal(RoutingErrors.RoutingInvalidTransition, exDef.Code);
    }

    [Fact]
    public void ProductionOrderOperation_Execution_lifecycle()
    {
        var op = ProductionOrderOperation.Create(1, 10, 50, 10, "OP-10", "Assembly", 25, 10m, 30m, 0m, 0m, true, "Step 1");
        Assert.Equal(ProductionOrderOperationStatus.Pending, op.Status);

        var marked = op.MarkReady();
        Assert.True(marked);
        Assert.Equal(ProductionOrderOperationStatus.Ready, op.Status);

        op.Start(FixedUtc, "Starting assembly run");
        Assert.Equal(ProductionOrderOperationStatus.InProgress, op.Status);
        Assert.NotNull(op.StartedAtUtc);

        op.Pause(10m, 15m, 25m, 1m, "Lunch break pause");
        Assert.Equal(ProductionOrderOperationStatus.Paused, op.Status);
        Assert.Equal(25m, op.CompletedQuantity);

        op.Complete(10m, 32m, 50m, 2m, "Assembly complete", FixedUtc);
        Assert.Equal(ProductionOrderOperationStatus.Completed, op.Status);
        Assert.Equal(50m, op.CompletedQuantity);
        Assert.Equal(2m, op.ScrapQuantity);
        Assert.NotNull(op.CompletedAtUtc);

        // Cannot start a completed operation
        var exStart = Assert.Throws<RoutingRuleException>(() => op.Start(FixedUtc));
        Assert.Equal(RoutingErrors.OperationTransitionInvalid, exStart.Code);
    }

    #endregion

    #region Identity Encapsulation

    [Fact]
    public void ScopedRoleAssignment_and_Memberships_encapsulate_active_state()
    {
        var assignment = new ScopedRoleAssignment { Id = 1, UserId = 10, RoleId = 2, IsActive = true };
        Assert.True(assignment.IsActive);

        assignment.Deactivate();
        Assert.False(assignment.IsActive);

        assignment.Activate();
        Assert.True(assignment.IsActive);

        assignment.SetActive(false);
        Assert.False(assignment.IsActive);

        var orgMembership = new OrganizationMembership { UserId = 10, OrganizationId = 1, IsActive = true };
        orgMembership.Deactivate();
        Assert.False(orgMembership.IsActive);
        orgMembership.SetActive(true);
        Assert.True(orgMembership.IsActive);

        var siteMembership = new SiteMembership { UserId = 10, OrganizationId = 1, SiteId = 20, IsActive = true };
        siteMembership.Deactivate();
        Assert.False(siteMembership.IsActive);
        siteMembership.SetActive(true);
        Assert.True(siteMembership.IsActive);
    }

    #endregion
}
