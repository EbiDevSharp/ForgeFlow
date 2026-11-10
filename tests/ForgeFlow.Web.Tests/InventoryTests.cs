using System.Reflection;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Units;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Roles;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class InventoryTests
{
    private sealed class ActorStub(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(actor);
    }

    private sealed class PermissionStub(Func<OrganizationScope, string, bool> predicate) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct)
            => ValueTask.FromResult(new PermissionDecision(predicate(scope, permission)));
    }

    private sealed class InventoryStoreStub : IInventoryOperationsStore
    {
        private long nextTxnId = 1000;
        private long nextLineId = 5000;
        public Dictionary<long, InventoryWarehouseLookup> Warehouses { get; } = [];
        public Dictionary<long, InventoryItemConversionContext> ItemContexts { get; } = [];
        public HashSet<long> ActiveUnits { get; } = [];
        public Dictionary<long, InventoryProductionOrderLookup> Orders { get; } = [];
        public Dictionary<(long WarehouseId, long ItemId, string LotCode), decimal> Balances { get; } = [];
        public Dictionary<long, InventoryTransactionDetails> Transactions { get; } = [];
        public Dictionary<string, long> IdempotencyIndex { get; } = new(StringComparer.Ordinal);
        public HashSet<long> ReversedIds { get; } = [];

        public Task<OrganizationPage<WarehouseSummary>> ListWarehouses(long organizationId, WarehouseListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<WarehouseSummary>([], 0, query.Page, query.PageSize));
        public Task<WarehouseSummary?> GetWarehouse(long organizationId, long id, CancellationToken ct) => Task.FromResult<WarehouseSummary?>(null);
        public Task<OrganizationResult<WarehouseSummary>> CreateWarehouse(CreateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> UpdateWarehouse(long organizationId, long id, byte[] version, UpdateWarehouseRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<WarehouseSummary>> SetWarehouseActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<InventoryBalanceSummary>> ListBalances(long organizationId, InventoryBalanceListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<InventoryBalanceSummary>([], 0, query.Page, query.PageSize));
        public Task<OrganizationPage<InventoryTransactionSummary>> ListTransactions(long organizationId, InventoryTransactionListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<InventoryTransactionSummary>([], 0, query.Page, query.PageSize));

        public Task<InventoryTransactionDetails?> GetTransaction(long organizationId, long id, CancellationToken ct)
            => Task.FromResult(Transactions.TryGetValue(id, out var d) && d.Summary.OrganizationId == organizationId
                ? d with { Summary = d.Summary with { IsReversed = ReversedIds.Contains(id) } }
                : null);

        public Task<InventoryWarehouseLookup?> GetWarehouseLookup(long organizationId, long warehouseId, CancellationToken ct)
            => Task.FromResult(Warehouses.TryGetValue(warehouseId, out var w) && w.OrganizationId == organizationId ? w : null);

        public Task<IReadOnlyDictionary<long, InventoryItemConversionContext>> LoadItemUnitContexts(long organizationId, IReadOnlyCollection<long> itemIds, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<long, InventoryItemConversionContext>>(
                itemIds.Where(ItemContexts.ContainsKey).ToDictionary(id => id, id => ItemContexts[id]));

        public Task<IReadOnlyHashSetLike> LoadActiveUnitIds(long organizationId, IReadOnlyCollection<long> unitIds, CancellationToken ct)
            => Task.FromResult(new IReadOnlyHashSetLike(unitIds.Where(ActiveUnits.Contains)));

        public Task<InventoryProductionOrderLookup?> GetProductionOrderLookup(long organizationId, long productionOrderId, CancellationToken ct)
            => Task.FromResult(Orders.TryGetValue(productionOrderId, out var o) && o.OrganizationId == organizationId ? o : null);

        public Task<OrganizationResult<InventoryTransactionDetails>> PostTransaction(
            PostInventoryTransactionRequest request,
            long siteId,
            IReadOnlyList<ResolvedInventoryLineWrite> resolvedLines,
            DateTime utcNow,
            OrganizationWriteAccess access,
            CancellationToken ct)
        {
            if (request.IdempotencyKey is not null && IdempotencyIndex.TryGetValue(request.IdempotencyKey, out var existingId))
                return Task.FromResult(OrganizationResult<InventoryTransactionDetails>.Success(Transactions[existingId]));

            var docNum = InventoryText.Code(request.DocumentNumber, InventoryErrors.DocumentNumberInvalid);
            if (Transactions.Values.Any(t => t.Summary.DocumentNumber == docNum))
                return Task.FromResult(OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.DocumentNumberDuplicate));

            foreach (var grp in resolvedLines.GroupBy(l => (l.WarehouseId, l.ItemId, l.LotCode)))
            {
                var current = Balances.TryGetValue(grp.Key, out var existing) ? existing : 0m;
                if (current + grp.Sum(x => x.BaseQuantityDelta) < 0m)
                    return Task.FromResult(OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.InsufficientStock));
            }
            foreach (var l in resolvedLines)
            {
                var key = (l.WarehouseId, l.ItemId, l.LotCode);
                Balances[key] = (Balances.TryGetValue(key, out var existing) ? existing : 0m) + l.BaseQuantityDelta;
            }

            var id = nextTxnId++;
            var entries = resolvedLines.Select((l, i) => new InventoryLedgerEntrySummary(
                nextLineId++, i + 1, l.SiteId, l.WarehouseId, $"WH-{l.WarehouseId}", "WH",
                l.ItemId, $"IT-{l.ItemId}", "Item", l.LotCode,
                l.EnteredQuantity, l.EnteredUnitId, "u", l.BaseQuantityDelta, l.BaseUnitId, "u", utcNow, l.Notes)).ToArray();

            var details = new InventoryTransactionDetails(
                new InventoryTransactionSummary(
                    id, request.OrganizationId, siteId, $"S-{siteId}", "Site",
                    request.WarehouseId, $"WH-{request.WarehouseId}", "WH",
                    request.DestinationWarehouseId, request.DestinationWarehouseId is long dw ? $"WH-{dw}" : null,
                    docNum, request.TransactionType, request.ProductionOrderId, null, null, false,
                    request.ReferenceNote, utcNow, entries.Length),
                entries);

            Transactions[id] = details;
            if (request.IdempotencyKey is not null) IdempotencyIndex[request.IdempotencyKey] = id;
            return Task.FromResult(OrganizationResult<InventoryTransactionDetails>.Success(details));
        }

        public Task<OrganizationResult<InventoryTransactionDetails>> ReverseTransaction(
            long organizationId,
            long transactionId,
            string reversalDocumentNumber,
            string? referenceNote,
            string? idempotencyKey,
            DateTime utcNow,
            OrganizationWriteAccess access,
            CancellationToken ct)
        {
            var orig = Transactions[transactionId];
            if (ReversedIds.Contains(transactionId))
                return Task.FromResult(OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AlreadyReversed));
            var negated = orig.Entries.Select(e => new ResolvedInventoryLineWrite(
                e.SiteId, e.WarehouseId, e.ItemId, e.LotCode, -e.EnteredQuantity, e.EnteredUnitId, -e.BaseQuantityDelta, e.BaseUnitId, e.Notes)).ToArray();

            foreach (var l in negated)
            {
                var key = (l.WarehouseId, l.ItemId, l.LotCode);
                var current = Balances.TryGetValue(key, out var existing) ? existing : 0m;
                if (current + l.BaseQuantityDelta < 0m)
                    return Task.FromResult(OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.InsufficientStock));
            }
            foreach (var l in negated)
            {
                var key = (l.WarehouseId, l.ItemId, l.LotCode);
                Balances[key] = (Balances.TryGetValue(key, out var existing) ? existing : 0m) + l.BaseQuantityDelta;
            }

            ReversedIds.Add(transactionId);
            var id = nextTxnId++;
            var revEntries = negated.Select((l, i) => new InventoryLedgerEntrySummary(
                nextLineId++, i + 1, l.SiteId, l.WarehouseId, $"WH-{l.WarehouseId}", "WH",
                l.ItemId, $"IT-{l.ItemId}", "Item", l.LotCode,
                l.EnteredQuantity, l.EnteredUnitId, "u", l.BaseQuantityDelta, l.BaseUnitId, "u", utcNow, l.Notes)).ToArray();
            var details = new InventoryTransactionDetails(
                new InventoryTransactionSummary(
                    id, organizationId, orig.Summary.SiteId, orig.Summary.SiteCode, orig.Summary.SiteName,
                    orig.Summary.WarehouseId, orig.Summary.WarehouseCode, orig.Summary.WarehouseName,
                    orig.Summary.DestinationWarehouseId, orig.Summary.DestinationWarehouseCode,
                    reversalDocumentNumber, InventoryTransactionType.Reversal, orig.Summary.ProductionOrderId, orig.Summary.ProductionOrderNumber,
                    transactionId, false, referenceNote, utcNow, revEntries.Length),
                revEntries);
            Transactions[id] = details;
            return Task.FromResult(OrganizationResult<InventoryTransactionDetails>.Success(details));
        }
    }

    [Fact]
    public void I01_domain_balance_forbids_negative_and_validates_lot_code()
    {
        var bal = InventoryBalance.CreateZero(1, 10, 100, 200, "lot-a", 1, DateTime.UtcNow);
        Assert.Equal("LOT-A", bal.LotCode);
        bal.ApplyDelta(15m, DateTime.UtcNow);
        Assert.Equal(15m, bal.OnHandBaseQuantity);

        var ex = Assert.Throws<InventoryRuleException>(() => bal.ApplyDelta(-16m, DateTime.UtcNow));
        Assert.Equal(InventoryErrors.InsufficientStock, ex.Code);

        Assert.Equal(InventoryErrors.LotCodeInvalid,
            Assert.Throws<InventoryRuleException>(() => InventoryText.LotCode("!BAD")).Code);
    }

    [Fact]
    public async Task I02_I06_uom_conversion_transfer_negative_guard_idempotency_production_and_reversal()
    {
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var store = new InventoryStoreStub();
        store.Warehouses[10] = new InventoryWarehouseLookup(10, 1, 100, "S1", true, true, true);
        store.Warehouses[20] = new InventoryWarehouseLookup(20, 1, 100, "S1", true, true, true);
        store.Warehouses[30] = new InventoryWarehouseLookup(30, 1, 200, "S2", true, true, true);

        // Item 500: BaseUnitId = 1 (PCS), Item conversion 2 (BOX) -> 1 (PCS) factor 12
        store.ActiveUnits.UnionWith([1L, 2L]);
        store.ItemContexts[500] = new InventoryItemConversionContext(500, true, 1, [new UnitConversionEdge(2, 1, 12m)]);
        store.Orders[900] = new InventoryProductionOrderLookup(900, 1, 100, ProductionOrderStatus.Released, "PO-1");
        store.Orders[901] = new InventoryProductionOrderLookup(901, 1, 100, ProductionOrderStatus.Draft, "PO-DRAFT");

        var site1Scope = new OrganizationScope.Site(1, 100);
        var useCases = new InventoryUseCases(store, new ActorStub(actor), new PermissionStub((s, _) => s is OrganizationScope.Site st && st.SiteId == 100));

        // I03 + I04: Receive 2 BOX = 24 PCS + idempotency deduplication
        var req = new PostInventoryTransactionRequest(1, 10, null, "RCV-1", InventoryTransactionType.Receipt, null, "Opening", "IDEMP-1",
            [new InventoryLineInput(500, "B1", 2m, 2L)]);
        var r1 = await useCases.PostTransaction(site1Scope, req);
        var r2 = await useCases.PostTransaction(site1Scope, req);
        Assert.True(r1.Succeeded && r2.Succeeded);
        Assert.Equal(r1.Value!.Summary.Id, r2.Value!.Summary.Id);
        Assert.Equal(24m, store.Balances[(10, 500, "B1")]);

        // I04: Over-issue fails cleanly with InsufficientStock
        var over = await useCases.PostTransaction(site1Scope, new PostInventoryTransactionRequest(
            1, 10, null, "ISS-FAIL", InventoryTransactionType.Issue, null, null, null,
            [new InventoryLineInput(500, "B1", 25m, 1L)]));
        Assert.False(over.Succeeded);
        Assert.Equal(InventoryErrors.InsufficientStock, over.Error!.Code);

        // I02: Atomic Transfer WH-10 -> WH-20 for 10 PCS
        var trf = await useCases.PostTransaction(site1Scope, new PostInventoryTransactionRequest(
            1, 10, 20, "TRF-1", InventoryTransactionType.TransferOut, null, null, null,
            [new InventoryLineInput(500, "B1", 10m, 1L)]));
        Assert.True(trf.Succeeded);
        Assert.Equal(14m, store.Balances[(10, 500, "B1")]);
        Assert.Equal(10m, store.Balances[(20, 500, "B1")]);

        // I05: ProductionIssue fails on Draft order, succeeds on Released order
        var badPo = await useCases.PostTransaction(site1Scope, new PostInventoryTransactionRequest(
            1, 10, null, "PI-BAD", InventoryTransactionType.ProductionIssue, 901, null, null,
            [new InventoryLineInput(500, "B1", 2m, 1L)]));
        Assert.False(badPo.Succeeded);
        Assert.Equal(InventoryErrors.ProductionOrderStateInvalid, badPo.Error!.Code);

        var okPo = await useCases.PostTransaction(site1Scope, new PostInventoryTransactionRequest(
            1, 10, null, "PI-1", InventoryTransactionType.ProductionIssue, 900, null, null,
            [new InventoryLineInput(500, "B1", 4m, 1L)]));
        Assert.True(okPo.Succeeded);
        Assert.Equal(10m, store.Balances[(10, 500, "B1")]);

        // I06: Reversal restores balance and rejects double-reversal
        var rev = await useCases.ReverseTransaction(site1Scope, new ReverseInventoryTransactionRequest(1, okPo.Value!.Summary.Id, "PI-1-REV"));
        Assert.True(rev.Succeeded);
        Assert.Equal(14m, store.Balances[(10, 500, "B1")]);

        var revAgain = await useCases.ReverseTransaction(site1Scope, new ReverseInventoryTransactionRequest(1, okPo.Value.Summary.Id, "PI-1-REV2"));
        Assert.False(revAgain.Succeeded);
        Assert.Equal(InventoryErrors.AlreadyReversed, revAgain.Error!.Code);

        // I07: Cross-site warehouse access denied
        var crossSite = await useCases.PostTransaction(site1Scope, new PostInventoryTransactionRequest(
            1, 30, null, "RCV-S2", InventoryTransactionType.Receipt, null, null, null,
            [new InventoryLineInput(500, null, 1m, 1L)]));
        Assert.False(crossSite.Succeeded);
        Assert.Equal(InventoryErrors.AccessDenied, crossSite.Error!.Code);
    }

    [Fact]
    public void I07_permissions_and_routes_hold()
    {
        Assert.True(PermissionCatalog.TryGet(InventoryPermissions.InventoryRead, out var r));
        Assert.Equal(PermissionScopeApplicability.OrganizationOrSite, r.Applicability);
        Assert.True(PermissionCatalog.TryGet(InventoryPermissions.InventoryManage, out var m));
        Assert.Equal(PermissionScopeApplicability.OrganizationOrSite, m.Applicability);
        Assert.Contains(PermissionModule.Inventory, RolePermissionLocalization.TenantModules);

        var routes = typeof(Inventory).GetCustomAttributes<RouteAttribute>().Select(a => a.Template).ToArray();
        Assert.Contains("/inventory", routes);
        Assert.Contains("/organizations/{OrganizationId:long}/inventory", routes);
    }
}
