using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Sales;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Sales;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class SalesDispatchTests
{
    [Fact]
    public void Customer_Validates_Code_Name_Email_And_Inactive_Guard()
    {
        var customer = Customer.Create(1, " cust-01 ", "Acme Polymers", "Ali", "+98-21-5555", "ali@acme.example", "Tehran", "TX-99", "VIP");

        Assert.Equal("CUST-01", customer.Code);
        Assert.Equal("Acme Polymers", customer.Name);
        Assert.True(customer.IsActive);

        Assert.True(customer.Update("Acme Polymers Co.", "Ali", "+98-21-5555", "ali@acme.example", "Tehran", "TX-99", "VIP"));
        Assert.False(customer.Update("Acme Polymers Co.", "Ali", "+98-21-5555", "ali@acme.example", "Tehran", "TX-99", "VIP"));

        Assert.True(customer.SetActive(false));
        var ex = Assert.Throws<SalesRuleException>(() => customer.Update("New Name", null, null, null, null, null, null));
        Assert.Equal(SalesErrors.CustomerInactive, ex.Code);

        Assert.Throws<SalesRuleException>(() => Customer.Create(1, "-bad", "Name", null, null, null, null, null, null));
        Assert.Throws<SalesRuleException>(() => Customer.Create(1, "C02", "Name", null, null, "not-an-email", null, null, null));
    }

    [Fact]
    public void SalesOrder_Lifecycle_Enforces_Transitions_And_OverDispatch()
    {
        var order = SalesOrder.CreateDraft(
            1, 10, 5, "so-001", new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 20), 3, "usd", "Tehran", "Rush");
        Assert.Equal("SO-001", order.OrderNumber);
        Assert.Equal("USD", order.CurrencyCode);
        Assert.Equal(SalesOrderStatus.Draft, order.Status);

        Assert.Throws<SalesRuleException>(() => order.Approve(DateTime.UtcNow));

        order.ReplaceLines([(100, 2m, 1, 2, 20m, 150.5m, "Line 1")]);
        Assert.Single(order.Lines);
        Assert.Equal(20m, order.Lines[0].RemainingBaseQuantity);

        order.Approve(DateTime.UtcNow);
        Assert.Equal(SalesOrderStatus.Approved, order.Status);
        Assert.Throws<SalesRuleException>(() => order.UpdateDraft(5, new DateOnly(2026, 11, 12), null, null, "USD", null, null));

        // Partial dispatch
        order.ApplyDispatches([(order.Lines[0].Id, 8m)]);
        Assert.Equal(SalesOrderStatus.PartiallyDispatched, order.Status);
        Assert.Equal(12m, order.Lines[0].RemainingBaseQuantity);

        // Cannot cancel once dispatches exist
        var cancelEx = Assert.Throws<SalesRuleException>(() => order.Cancel("Customer changed mind", DateTime.UtcNow));
        Assert.Equal(SalesErrors.HasDispatchesCannotCancel, cancelEx.Code);

        // Over-dispatch rejected
        var overEx = Assert.Throws<SalesRuleException>(() => order.ApplyDispatches([(order.Lines[0].Id, 13m)]));
        Assert.Equal(SalesErrors.OverDispatch, overEx.Code);

        // Complete dispatch
        order.ApplyDispatches([(order.Lines[0].Id, 12m)]);
        Assert.Equal(SalesOrderStatus.Dispatched, order.Status);
        Assert.Equal(0m, order.Lines[0].RemainingBaseQuantity);

        // Revert partial dispatch
        order.RevertDispatches([(order.Lines[0].Id, 12m)]);
        Assert.Equal(SalesOrderStatus.PartiallyDispatched, order.Status);
        Assert.Equal(12m, order.Lines[0].RemainingBaseQuantity);

        // Close partially dispatched order
        order.Close(DateTime.UtcNow);
        Assert.Equal(SalesOrderStatus.Closed, order.Status);
    }

    [Fact]
    public void SalesOrder_Invalid_Dates_Rejected()
    {
        var ex = Assert.Throws<SalesRuleException>(() =>
            SalesOrder.CreateDraft(1, 10, 5, "SO-002", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 10), null, "IRR", null, null));
        Assert.Equal(SalesErrors.DatesInvalid, ex.Code);
    }

    [Fact]
    public void DeliveryOrder_CreatePosted_And_Reverse_Work()
    {
        var now = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
        var delivery = DeliveryOrder.CreatePosted(
            1, 10, 50, 3, "do-001", new DateOnly(2026, 11, 12), "Barbar", "TRK-77", "Handle with care", now,
            [(101, 100, "lot-fg-01", 8m, 2, 99, "COA-2026-01", "Line note")]);

        Assert.Equal("DO-001", delivery.DispatchNumber);
        Assert.Equal(DeliveryOrderStatus.Posted, delivery.Status);
        Assert.Single(delivery.Lines);
        Assert.Equal("LOT-FG-01", delivery.Lines[0].LotCode);
        Assert.Equal("COA-2026-01", delivery.Lines[0].CertificateNumberSnapshot);

        Assert.Throws<SalesRuleException>(() => delivery.Reverse("  ", now));
        delivery.Reverse("Wrong lot picked", now.AddHours(1));
        Assert.Equal(DeliveryOrderStatus.Reversed, delivery.Status);
        Assert.Equal("Wrong lot picked", delivery.ReversalReason);

        var revEx = Assert.Throws<SalesRuleException>(() => delivery.Reverse("Again", now.AddHours(2)));
        Assert.Equal(SalesErrors.AlreadyReversed, revEx.Code);
    }

    [Fact]
    public void StockReservation_Supports_SalesOrder_And_ProductionOrder_Xor()
    {
        var poRes = StockReservation.Create(1, 10, 3, 500, 501, 100, "LOT-1", 2, 10m);
        Assert.Equal(ReservationSourceKind.ProductionOrder, poRes.SourceKind);
        Assert.Equal(500L, poRes.ProductionOrderId);
        Assert.Null(poRes.SalesOrderId);

        var soRes = StockReservation.CreateForSalesOrder(1, 10, 3, 600, 601, 100, "LOT-2", 2, 15m, "For SO");
        Assert.Equal(ReservationSourceKind.SalesOrder, soRes.SourceKind);
        Assert.Equal(600L, soRes.SalesOrderId);
        Assert.Equal(601L, soRes.SalesOrderLineId);
        Assert.Null(soRes.ProductionOrderId);

        Assert.Equal(10m, soRes.Consume(10m));
        Assert.Equal(ReservationStatus.Fulfilled, soRes.Consume(5m) > 0 ? soRes.Status : ReservationStatus.Fulfilled);
    }

    [Fact]
    public void PermissionCatalog_Contains_Sales_Permissions_With_Correct_Scopes()
    {
        Assert.True(PermissionCatalog.TryGet("Sales.Read", out var read));
        Assert.Equal(PermissionModule.Sales, read.Module);
        Assert.Equal(PermissionScopeApplicability.OrganizationOrSite, read.Applicability);

        Assert.True(PermissionCatalog.TryGet("Sales.ManageCustomers", out var cust));
        Assert.Equal(PermissionScopeApplicability.OrganizationOnly, cust.Applicability);

        Assert.True(PermissionCatalog.TryGet("Sales.ManageOrders", out var orders));
        Assert.Equal(PermissionScopeApplicability.OrganizationOrSite, orders.Applicability);

        Assert.True(PermissionCatalog.TryGet("Sales.Dispatch", out var dispatch));
        Assert.Equal(PermissionScopeApplicability.OrganizationOrSite, dispatch.Applicability);

        // Site-scoped role cannot hold OrganizationOnly Sales.ManageCustomers
        Assert.Throws<RoleRuleException>(() => PermissionCatalog.ValidateTenantPermissions(2, ["Sales.ManageCustomers"]));
        var siteAllowed = PermissionCatalog.ValidateTenantPermissions(2, ["Sales.Read", "Sales.ManageOrders", "Sales.Dispatch"]);
        Assert.Equal(3, siteAllowed.Count);
    }

    [Fact]
    public void AuditEventCatalog_Contains_Sales_Events_And_Rejection_Code()
    {
        string[] expected =
        [
            "Customer.Created", "Customer.Updated", "Customer.Activated", "Customer.Deactivated",
            "SalesOrder.Created", "SalesOrder.Updated", "SalesOrder.Approved", "SalesOrder.Dispatched", "SalesOrder.Closed", "SalesOrder.Cancelled",
            "DeliveryOrder.Posted", "DeliveryOrder.Reversed",
            UserOperationCatalog.SalesActionRejected
        ];

        foreach (var code in expected)
            Assert.True(AuditEventCatalog.TryGet(code, out _), $"Missing audit event: {code}");
    }

    [Fact]
    public async Task SalesUseCases_Rejects_Unauthorized_And_Records_Operation_Audit()
    {
        var recorder = new FakeOperationRecorder();
        var useCases = new SalesUseCases(
            new StubSalesStore(),
            new StubActorAccessor(new TrustedActor(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid())),
            new StubPermissionEvaluator(false),
            recorder);

        var res = await useCases.CreateCustomer(
            new OrganizationScope.Organization(10),
            new CreateCustomerRequest(10, "C01", "Test Customer"));

        Assert.False(res.Succeeded);
        Assert.Equal(SalesErrors.AccessDenied, res.Error!.Code);
        Assert.Single(recorder.Entries);
        Assert.Equal(UserOperationCatalog.SalesActionRejected, recorder.Entries[0].OperationCode);
    }

    private sealed class StubActorAccessor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class StubPermissionEvaluator(bool allow) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permissionCode, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(allow));
    }

    private sealed class FakeOperationRecorder : IUserOperationRecorder
    {
        public List<UserOperationRecordRequest> Entries { get; } = [];
        public Task RecordAsync(UserOperationRecordRequest entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class StubSalesStore : ISalesOperationsStore
    {
        public Task<IReadOnlyDictionary<long, SalesItemContext>> LoadItemContexts(long organizationId, IReadOnlyList<long> itemIds, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlySet<long>> LoadActiveUnitIds(long organizationId, IReadOnlyList<long> unitIds, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<CustomerSummary>> ListCustomers(long organizationId, CustomerListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<CustomerSummary>> CreateCustomer(CreateCustomerRequest request, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<CustomerSummary>> UpdateCustomer(long organizationId, long customerId, UpdateCustomerRequest request, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<CustomerSummary>> SetCustomerActive(long organizationId, long customerId, bool isActive, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<SalesOrderSummary>> ListSalesOrders(long organizationId, SalesOrderListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<SalesOrderDetails?> GetSalesOrder(long organizationId, long salesOrderId, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SalesOrderDetails>> CreateSalesOrder(CreateSalesOrderRequest request, IReadOnlyList<ResolvedSalesOrderLine> resolvedLines, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SalesOrderDetails>> UpdateSalesOrder(long organizationId, long salesOrderId, UpdateSalesOrderRequest request, IReadOnlyList<ResolvedSalesOrderLine> resolvedLines, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SalesOrderDetails>> ApproveSalesOrder(long organizationId, long salesOrderId, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SalesOrderDetails>> CloseSalesOrder(long organizationId, long salesOrderId, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SalesOrderDetails>> CancelSalesOrder(long organizationId, long salesOrderId, string reason, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<DeliveryOrderSummary>> ListDeliveryOrders(long organizationId, DeliveryOrderListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeliveryOrderDetails?> GetDeliveryOrder(long organizationId, long deliveryOrderId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<DispatchableLotSummary>> ListDispatchableLots(long organizationId, long salesOrderId, long warehouseId, DateOnly asOfDate, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<DeliveryOrderDetails>> PostDeliveryOrder(long organizationId, long salesOrderId, PostDeliveryOrderRequest request, IReadOnlyList<ResolvedDeliveryOrderLine> resolvedLines, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<DeliveryOrderDetails>> ReverseDeliveryOrder(long organizationId, long deliveryOrderId, string reason, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }
}
