using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlReportingQueryStore(Func<IDbContextFactory<OrganizationDbContext>> getFactory)
    : IReportingQueryStore
{
    private const int MaxVarianceOrders = 2000;
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public SqlReportingQueryStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public Task<IReadOnlyList<ReportSiteInfo>> ListAuthorizedSites(
        long organizationId,
        long? forcedSiteId,
        CancellationToken ct) => Read<IReadOnlyList<ReportSiteInfo>>(async db =>
    {
        var query = db.Sites.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId);
        if (forcedSiteId is long sid && sid > 0)
            query = query.Where(s => s.Id == sid);

        return await query
            .OrderBy(s => s.Code)
            .Select(s => new ReportSiteInfo(s.Id, s.Code, s.Name))
            .ToListAsync(ct);
    }, ct);

    public Task<ShortageDataset> GetShortageDataset(
        long organizationId,
        long? siteId,
        DateOnly? dueFrom,
        DateOnly? dueTo,
        CancellationToken ct) => Read(async db =>
    {
        var orderQuery = from po in db.ProductionOrders.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { po.OrganizationId, SiteId = po.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         where po.OrganizationId == organizationId
                               && (po.Status == ProductionOrderStatus.Released || po.Status == ProductionOrderStatus.InProgress)
                         select new { po, SiteCode = s.Code };

        if (siteId is long sid && sid > 0)
            orderQuery = orderQuery.Where(x => x.po.SiteId == sid);
        if (dueFrom is DateOnly df)
            orderQuery = orderQuery.Where(x => x.po.PlannedDueDate >= df);
        if (dueTo is DateOnly dt)
            orderQuery = orderQuery.Where(x => x.po.PlannedDueDate <= dt);

        var openOrders = await orderQuery
            .OrderBy(x => x.po.PlannedDueDate)
            .ThenBy(x => x.po.OrderNumber)
            .Select(x => new ReportOpenOrderRow(
                x.po.Id,
                x.po.OrderNumber,
                x.po.SiteId,
                x.SiteCode,
                x.po.PlannedDueDate,
                x.po.Status))
            .ToListAsync(ct);

        var orderIds = openOrders.Select(o => o.ProductionOrderId).ToArray();
        var inputs = orderIds.Length == 0
            ? new List<ReportOpenOrderInputRow>()
            : await db.ProductionOrderInputs.AsNoTracking()
                .Where(i => i.OrganizationId == organizationId && !i.IsSubAssembly && orderIds.Contains(i.ProductionOrderId))
                .Select(i => new ReportOpenOrderInputRow(i.ProductionOrderId, i.ItemId, i.GrossQuantity, i.UnitId))
                .ToListAsync(ct);

        var balanceQuery = db.InventoryBalances.AsNoTracking()
            .Where(b => b.OrganizationId == organizationId);
        if (siteId is long bSid && bSid > 0)
            balanceQuery = balanceQuery.Where(b => b.SiteId == bSid);

        var balances = await balanceQuery
            .GroupBy(b => new { b.SiteId, b.ItemId })
            .Select(g => new ReportStockBalanceRow(
                g.Key.SiteId,
                g.Key.ItemId,
                g.Sum(x => x.OnHandBaseQuantity)))
            .ToListAsync(ct);

        var itemIds = inputs.Select(i => i.ItemId)
            .Concat(balances.Select(b => b.ItemId))
            .Distinct()
            .ToArray();

        var items = await LoadItemRefsAsync(db, organizationId, itemIds, ct);
        var conversions = await LoadConversionsAsync(db, organizationId, itemIds, ct);

        return new ShortageDataset(openOrders, inputs, balances, items, conversions);
    }, ct);

    public Task<VarianceDataset> GetVarianceDataset(
        long organizationId,
        long? siteId,
        long? productionOrderId,
        ProductionOrderStatus? status,
        DateOnly? dueFrom,
        DateOnly? dueTo,
        string? search,
        CancellationToken ct) => Read(async db =>
    {
        var orderQuery = from po in db.ProductionOrders.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { po.OrganizationId, SiteId = po.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         join fg in db.Items.AsNoTracking() on new { po.OrganizationId, ItemId = po.ItemId } equals new { fg.OrganizationId, ItemId = fg.Id }
                         where po.OrganizationId == organizationId
                         select new { po, s, fg };

        if (siteId is long sid && sid > 0)
            orderQuery = orderQuery.Where(x => x.po.SiteId == sid);
        if (productionOrderId is long poId && poId > 0)
            orderQuery = orderQuery.Where(x => x.po.Id == poId);
        if (status is ProductionOrderStatus st)
            orderQuery = orderQuery.Where(x => x.po.Status == st);
        if (dueFrom is DateOnly df)
            orderQuery = orderQuery.Where(x => x.po.PlannedDueDate >= df);
        if (dueTo is DateOnly dt)
            orderQuery = orderQuery.Where(x => x.po.PlannedDueDate <= dt);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            orderQuery = orderQuery.Where(x =>
                x.po.OrderNumber.Contains(term)
                || x.fg.Code.Contains(term)
                || x.fg.Name.Contains(term));
        }

        var orders = await orderQuery
            .OrderByDescending(x => x.po.Id)
            .Take(MaxVarianceOrders)
            .Select(x => new ReportVarianceOrderRow(
                x.po.Id,
                x.po.OrganizationId,
                x.po.SiteId,
                x.s.Code,
                x.s.Name,
                x.po.OrderNumber,
                x.po.Status,
                x.po.ItemId,
                x.po.RequestedQuantity,
                x.po.RequestedUnitId,
                x.po.PlannedStartDate,
                x.po.PlannedDueDate,
                x.po.ActualProducedQuantity,
                x.po.ActualScrapQuantity))
            .ToListAsync(ct);

        if (orders.Count == 0)
            return new VarianceDataset([], [], [], [], [], []);

        var orderIds = orders.Select(o => o.ProductionOrderId).ToArray();

        var inputs = await db.ProductionOrderInputs.AsNoTracking()
            .Where(i => i.OrganizationId == organizationId && orderIds.Contains(i.ProductionOrderId))
            .Select(i => new ReportVarianceInputRow(i.ProductionOrderId, i.ItemId, i.GrossQuantity, i.UnitId, i.IsSubAssembly))
            .ToListAsync(ct);

        var outputs = await db.ProductionOrderOutputs.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId && orderIds.Contains(o.ProductionOrderId))
            .Select(o => new ReportVarianceOutputRow(o.ProductionOrderId, o.ItemId, o.OutputType, o.PlannedQuantity, o.UnitId))
            .ToListAsync(ct);

        var ledgerQuery =
            from l in db.InventoryLedgerEntries.AsNoTracking()
            join t in db.InventoryTransactions.AsNoTracking() on new { l.OrganizationId, TxId = l.InventoryTransactionId } equals new { t.OrganizationId, TxId = t.Id }
            join orig in db.InventoryTransactions.AsNoTracking() on new { t.OrganizationId, OrigId = t.ReversedTransactionId } equals new { orig.OrganizationId, OrigId = (long?)orig.Id } into origJoin
            from orig in origJoin.DefaultIfEmpty()
            where l.OrganizationId == organizationId
                  && t.ProductionOrderId.HasValue
                  && orderIds.Contains(t.ProductionOrderId.Value)
            select new
            {
                ProductionOrderId = t.ProductionOrderId!.Value,
                RootType = t.TransactionType == InventoryTransactionType.Reversal && orig != null
                    ? orig.TransactionType
                    : t.TransactionType,
                l.ItemId,
                l.BaseQuantityDelta
            };

        var ledgerRows = await ledgerQuery
            .Where(x => x.RootType == InventoryTransactionType.ProductionIssue
                        || x.RootType == InventoryTransactionType.ProductionReceipt)
            .GroupBy(x => new { x.ProductionOrderId, x.RootType, x.ItemId })
            .Select(g => new ReportOrderLedgerRow(
                g.Key.ProductionOrderId,
                g.Key.RootType,
                g.Key.ItemId,
                g.Sum(y => y.BaseQuantityDelta)))
            .ToListAsync(ct);

        var itemIds = orders.Select(o => o.ItemId)
            .Concat(inputs.Select(i => i.ItemId))
            .Concat(outputs.Select(o => o.ItemId))
            .Concat(ledgerRows.Select(l => l.ItemId))
            .Distinct()
            .ToArray();

        var items = await LoadItemRefsAsync(db, organizationId, itemIds, ct);
        var conversions = await LoadConversionsAsync(db, organizationId, itemIds, ct);

        return new VarianceDataset(orders, inputs, outputs, ledgerRows, items, conversions);
    }, ct);

    private static async Task<List<ReportItemRef>> LoadItemRefsAsync(
        OrganizationDbContext db,
        long organizationId,
        long[] itemIds,
        CancellationToken ct)
    {
        if (itemIds.Length == 0)
            return [];

        return await (from it in db.Items.AsNoTracking()
                      join u in db.Units.AsNoTracking() on new { it.OrganizationId, UnitId = it.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                      where it.OrganizationId == organizationId && itemIds.Contains(it.Id)
                      select new ReportItemRef(it.Id, it.Code, it.Name, it.BaseUnitId, u.Code, u.Symbol))
            .ToListAsync(ct);
    }

    private static async Task<List<ReportConversionEdgeRow>> LoadConversionsAsync(
        OrganizationDbContext db,
        long organizationId,
        long[] itemIds,
        CancellationToken ct)
    {
        var globals = await db.UnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .Select(c => new ReportConversionEdgeRow(null, c.FromUnitId, c.ToUnitId, c.Factor))
            .ToListAsync(ct);

        if (itemIds.Length == 0)
            return globals;

        var specifics = await db.ItemUnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && itemIds.Contains(c.ItemId))
            .Select(c => new ReportConversionEdgeRow(c.ItemId, c.FromUnitId, c.ToUnitId, c.Factor))
            .ToListAsync(ct);

        return [.. specifics, .. globals];
    }

    private async Task<T> Read<T>(Func<OrganizationDbContext, Task<T>> query, CancellationToken ct)
    {
        try
        {
            await using var db = await Factory.CreateDbContextAsync(ct);
            return await query(db);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            throw new ReportingStoreUnavailableException(ex);
        }
    }

    private static bool Unavailable(Exception ex) =>
        ex is SqlException or TimeoutException or InvalidOperationException { InnerException: SqlException or TimeoutException };
}
