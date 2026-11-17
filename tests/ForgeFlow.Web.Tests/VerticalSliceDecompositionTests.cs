using ForgeFlow.Application.BusinessPartners;
using ForgeFlow.Application.BusinessPartners.Slices;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Inventory.Slices;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Inventory;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class VerticalSliceDecompositionTests
{
    private sealed class StubActorAccessor(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken cancellationToken) => ValueTask.FromResult(actor);
    }

    private sealed class StubPermissionEvaluator(bool allowed) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor, OrganizationScope scope, string permission, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PermissionDecision(allowed));
    }

    private sealed class StubPartnerStore : IBusinessPartnerStore
    {
        public Task<OrganizationPage<BusinessPartnerSummary>> List(long organizationId, BusinessPartnerListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<BusinessPartnerSummary>([], 0, 1, 20));

        public Task<BusinessPartnerDetail?> Get(long organizationId, long id, CancellationToken ct) => Task.FromResult<BusinessPartnerDetail?>(null);
        public Task<OrganizationPage<PartnerLookupItem>> SearchLookup(long organizationId, PartnerLookupQuery query, CancellationToken ct) => Task.FromResult(new OrganizationPage<PartnerLookupItem>([], 0, 1, 20));
        public Task<PartnerLookupItem?> GetLookupItem(long organizationId, PartnerLookupRole role, long targetId, CancellationToken ct) => Task.FromResult<PartnerLookupItem?>(null);
        public Task<IReadOnlyList<BusinessPartnerRoleDto>> ListRoleDefinitions(CancellationToken ct) => Task.FromResult<IReadOnlyList<BusinessPartnerRoleDto>>([]);
        public Task<IReadOnlyList<IranProvinceDto>> ListProvinces(CancellationToken ct) => Task.FromResult<IReadOnlyList<IranProvinceDto>>([]);
        public Task<IReadOnlyList<IranCityDto>> ListCities(int? provinceId, CancellationToken ct) => Task.FromResult<IReadOnlyList<IranCityDto>>([]);
        public Task<OrganizationResult<BusinessPartnerDetail>> Create(CreateBusinessPartnerRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BusinessPartnerDetail>> Update(long organizationId, long id, byte[] rowVersion, UpdateBusinessPartnerRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BusinessPartnerSummary>> SetActive(long organizationId, long id, byte[] rowVersion, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubReservationStore : IStockReservationStore
    {
        public Task<OrganizationPage<StockReservationSummary>> ListReservations(long organizationId, ReservationListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<StockReservationSummary>([], 0, 1, 20));

        public Task<IReadOnlyList<StockReservationSummary>> ListOrderReservations(long organizationId, long productionOrderId, CancellationToken ct) => Task.FromResult<IReadOnlyList<StockReservationSummary>>([]);
        public Task<IReadOnlyList<StockReservationSummary>> ListSalesOrderReservations(long organizationId, long salesOrderId, CancellationToken ct) => Task.FromResult<IReadOnlyList<StockReservationSummary>>([]);
        public Task<StockReservationSummary?> GetReservation(long organizationId, long reservationId, CancellationToken ct) => Task.FromResult<StockReservationSummary?>(null);
        public Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveStock(ReserveStockRequest request, decimal baseQuantity, long baseUnitId, DateTime now, OrganizationWriteAccess access, CancellationToken ct) => Task.FromResult(OrganizationResult<IReadOnlyList<StockReservationSummary>>.Success([]));
        public Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveSalesOrderStock(ReserveSalesOrderStockRequest request, decimal baseQuantity, long baseUnitId, DateTime now, OrganizationWriteAccess access, CancellationToken ct) => Task.FromResult(OrganizationResult<IReadOnlyList<StockReservationSummary>>.Success([]));
        public Task<OrganizationResult<StockReservationSummary>> ReleaseReservation(long organizationId, long reservationId, byte[] expectedRowVersion, DateTime now, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubInventoryStore : IInventoryOperationsStore
    {
        public Task<InventoryProductionOrderLookup?> GetProductionOrderLookup(long organizationId, long productionOrderId, CancellationToken ct)
            => Task.FromResult<InventoryProductionOrderLookup?>(null);

        public Task<IReadOnlyDictionary<long, InventoryItemConversionContext>> LoadItemUnitContexts(long organizationId, IReadOnlyCollection<long> itemIds, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<long, InventoryItemConversionContext>>(new Dictionary<long, InventoryItemConversionContext>());

        public Task<IReadOnlyHashSetLike> LoadActiveUnitIds(long organizationId, IReadOnlyCollection<long> unitIds, CancellationToken ct)
            => Task.FromResult(new IReadOnlyHashSetLike([]));

        public Task<OrganizationPage<WarehouseSummary>> ListWarehouses(long organizationId, WarehouseListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<WarehouseSummary>> ListActiveWarehouses(long organizationId, long? siteId, CancellationToken ct) => throw new NotImplementedException();
        public Task<WarehouseSummary?> GetWarehouse(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<InventoryWarehouseLookup?> GetWarehouseLookup(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> CreateWarehouse(CreateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> UpdateWarehouse(long organizationId, long id, byte[] expectedRowVersion, UpdateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> SetWarehouseActive(long organizationId, long id, byte[] expectedRowVersion, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<InventoryBalanceSummary>> ListBalances(long organizationId, InventoryBalanceListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<InventoryTransactionSummary>> ListTransactions(long organizationId, InventoryTransactionListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<InventoryTransactionDetails?> GetTransaction(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<InventoryLedgerEntrySummary>> ListLedger(long organizationId, InventoryBalanceListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<InventoryTransactionDetails>> PostTransaction(PostInventoryTransactionRequest request, long transactionNumber, IReadOnlyList<ResolvedInventoryLineWrite> resolvedLines, DateTime now, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<InventoryTransactionDetails>> ReverseTransaction(long organizationId, long originalTransactionId, string originalRowVersionBase64, string? reason, string? reference, DateTime now, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    [Fact]
    public async Task BusinessPartnerCatalogSlices_Enforces_CrossTenant_Isolation()
    {
        var actor = new TrustedActor(10, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var actors = new StubActorAccessor(actor);
        var permissions = new StubPermissionEvaluator(true);
        var support = new BusinessPartnerSliceSupport(actors, permissions);
        var slices = new BusinessPartnerCatalogSlices(new StubPartnerStore(), support);

        var scope = new OrganizationScope.Organization(100);
        var result = await slices.ListAsync(scope, 200, new BusinessPartnerListQuery(null, null, false, 1, 20, null));

        Assert.False(result.Succeeded);
        Assert.Equal(BusinessPartnerErrors.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task BusinessPartnerCatalogSlices_Enforces_Organization_Only_Scope_On_Create()
    {
        var actor = new TrustedActor(10, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var actors = new StubActorAccessor(actor);
        var permissions = new StubPermissionEvaluator(true);
        var support = new BusinessPartnerSliceSupport(actors, permissions);
        var slices = new BusinessPartnerCatalogSlices(new StubPartnerStore(), support);

        var siteScope = new OrganizationScope.Site(100, 50);
        var request = new CreateBusinessPartnerRequest(
            100, "BP-01", PersonType.Legal, "Acme", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, []);
        var result = await slices.CreateAsync(siteScope, request);

        Assert.False(result.Succeeded);
        Assert.Equal(BusinessPartnerErrors.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task StockReservationSlices_Enforces_Scope_And_Tenant_Validation()
    {
        var actor = new TrustedActor(10, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var actors = new StubActorAccessor(actor);
        var permissions = new StubPermissionEvaluator(true);
        var support = new InventorySliceSupport(actors, permissions);
        var slices = new StockReservationSlices(new StubReservationStore(), new StubInventoryStore(), support);

        var scope = new OrganizationScope.Organization(100);
        var result = await slices.ListReservationsAsync(scope, 200, new ReservationListQuery(Page: 1, PageSize: 20));

        Assert.False(result.Succeeded);
        Assert.Equal(InventoryErrors.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task StockReservationSlices_Validates_ProductionOrder_Presence()
    {
        var actor = new TrustedActor(10, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var actors = new StubActorAccessor(actor);
        var permissions = new StubPermissionEvaluator(true);
        var support = new InventorySliceSupport(actors, permissions);
        var slices = new StockReservationSlices(new StubReservationStore(), new StubInventoryStore(), support);

        var scope = new OrganizationScope.Organization(100);
        var request = new ReserveStockRequest(OrganizationId: 100, ProductionOrderId: 0, ProductionOrderInputId: null, ItemId: 1, Quantity: 50);
        var result = await slices.ReserveStockAsync(scope, request);

        Assert.False(result.Succeeded);
        Assert.Equal(InventoryErrors.ProductionOrderInvalid, result.Error!.Code);
    }
}
