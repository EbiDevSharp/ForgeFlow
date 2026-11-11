using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlInventoryOperationsStore(
    Func<IDbContextFactory<OrganizationDbContext>> getFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null)
    : IInventoryOperationsStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public SqlInventoryOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public Task<OrganizationPage<WarehouseSummary>> ListWarehouses(
        long organizationId,
        WarehouseListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from wh in db.Warehouses.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { wh.OrganizationId, SiteId = wh.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        where wh.OrganizationId == organizationId
                        select new
                        {
                            wh,
                            s,
                            RowVersion = EF.Property<byte[]>(wh, "RowVersion")
                        };

        if (query.SiteId is long siteId && siteId > 0)
            baseQuery = baseQuery.Where(x => x.wh.SiteId == siteId);
        if (!query.IncludeInactive)
            baseQuery = baseQuery.Where(x => x.wh.IsActive);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x => x.wh.Code.Contains(query.Search) || x.wh.Name.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderBy(x => x.wh.SiteId)
            .ThenBy(x => x.wh.Code)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new WarehouseSummary(
            r.wh.Id, r.wh.OrganizationId, r.wh.SiteId, r.s.Code, r.s.Name,
            r.wh.Code, r.wh.Name, r.wh.IsDefaultForSite, r.wh.IsActive,
            Convert.ToBase64String(r.RowVersion))).ToArray();

        return new OrganizationPage<WarehouseSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<WarehouseSummary?> GetWarehouse(long organizationId, long id, CancellationToken ct) => Read(async db =>
    {
        var row = await (from wh in db.Warehouses.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { wh.OrganizationId, SiteId = wh.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         where wh.OrganizationId == organizationId && wh.Id == id
                         select new { wh, s, RowVersion = EF.Property<byte[]>(wh, "RowVersion") })
            .SingleOrDefaultAsync(ct);
        if (row is null) return null;
        return new WarehouseSummary(
            row.wh.Id, row.wh.OrganizationId, row.wh.SiteId, row.s.Code, row.s.Name,
            row.wh.Code, row.wh.Name, row.wh.IsDefaultForSite, row.wh.IsActive,
            Convert.ToBase64String(row.RowVersion));
    }, ct);

    public Task<OrganizationResult<WarehouseSummary>> CreateWarehouse(
        CreateWarehouseRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<WarehouseSummary>.Failure(orgCheck.Error!);

        var site = await db.Sites.SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SiteId, ct);
        if (site is null) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.SiteInvalid);
        if (!site.IsActive) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.SiteInactive);

        if (request.IsDefaultForSite)
            await ClearExistingSiteDefaultsAsync(db, request.OrganizationId, request.SiteId, null, ct);

        var wh = Warehouse.Create(request.OrganizationId, request.SiteId, request.Code, request.Name, request.IsDefaultForSite);
        db.Warehouses.Add(wh);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<WarehouseSummary>.Success(
            (await BuildWarehouseSummaryAsync(db, request.OrganizationId, wh.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<WarehouseSummary>> UpdateWarehouse(
        long organizationId,
        long id,
        byte[] version,
        UpdateWarehouseRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<WarehouseSummary>.Failure(orgCheck.Error!);

        var wh = await db.Warehouses.SingleOrDefaultAsync(w => w.OrganizationId == organizationId && w.Id == id, ct);
        if (wh is null) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);
        if (!Matches(db, wh, version)) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.ConcurrencyConflict);
        if (!wh.IsActive) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseInactive);

        if (request.IsDefaultForSite && !wh.IsDefaultForSite)
            await ClearExistingSiteDefaultsAsync(db, organizationId, wh.SiteId, wh.Id, ct);

        wh.UpdateDetails(request.Name, request.IsDefaultForSite);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<WarehouseSummary>.Success(
            (await BuildWarehouseSummaryAsync(db, organizationId, wh.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<WarehouseSummary>> SetWarehouseActive(
        long organizationId,
        long id,
        byte[] version,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<WarehouseSummary>.Failure(orgCheck.Error!);

        var wh = await db.Warehouses.SingleOrDefaultAsync(w => w.OrganizationId == organizationId && w.Id == id, ct);
        if (wh is null) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.WarehouseNotFound);
        if (!Matches(db, wh, version)) return OrganizationResult<WarehouseSummary>.Failure(InventoryErrors.ConcurrencyConflict);

        if (wh.SetActive(isActive))
            await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<WarehouseSummary>.Success(
            (await BuildWarehouseSummaryAsync(db, organizationId, wh.Id, ct))!);
    }, ct);

    public Task<OrganizationPage<InventoryBalanceSummary>> ListBalances(
        long organizationId,
        InventoryBalanceListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var soonCutoff = today.AddDays(30);

        var baseQuery = from bal in db.InventoryBalances.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { bal.OrganizationId, SiteId = bal.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        join w in db.Warehouses.AsNoTracking() on new { bal.OrganizationId, WarehouseId = bal.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                        join it in db.Items.AsNoTracking() on new { bal.OrganizationId, ItemId = bal.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                        join u in db.Units.AsNoTracking() on new { bal.OrganizationId, UnitId = bal.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                        join lot in db.ItemLots.AsNoTracking() on new { bal.OrganizationId, bal.ItemId, bal.LotCode } equals new { lot.OrganizationId, lot.ItemId, lot.LotCode } into lotJoin
                        from lot in lotJoin.DefaultIfEmpty()
                        where bal.OrganizationId == organizationId
                        select new
                        {
                            bal,
                            s,
                            w,
                            it,
                            u,
                            ManufactureDate = lot != null ? lot.ManufactureDate : null,
                            ExpirationDate = lot != null ? lot.ExpirationDate : null,
                            RowVersion = EF.Property<byte[]>(bal, "RowVersion")
                        };

        if (query.SiteId is long siteId && siteId > 0)
            baseQuery = baseQuery.Where(x => x.bal.SiteId == siteId);
        if (query.WarehouseId is long whId && whId > 0)
            baseQuery = baseQuery.Where(x => x.bal.WarehouseId == whId);
        if (query.ItemId is long itemId && itemId > 0)
            baseQuery = baseQuery.Where(x => x.bal.ItemId == itemId);
        if (!query.IncludeZeroBalances)
            baseQuery = baseQuery.Where(x => x.bal.OnHandBaseQuantity > 0m);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.it.Code.Contains(query.Search)
                || x.it.Name.Contains(query.Search)
                || x.w.Code.Contains(query.Search)
                || x.bal.LotCode.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderBy(x => x.w.Code)
            .ThenBy(x => x.it.Code)
            .ThenBy(x => x.bal.LotCode)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new InventoryBalanceSummary(
            r.bal.Id, r.bal.OrganizationId, r.bal.SiteId, r.s.Code, r.s.Name,
            r.bal.WarehouseId, r.w.Code, r.w.Name,
            r.bal.ItemId, r.it.Code, r.it.Name, r.bal.LotCode,
            r.bal.BaseUnitId, r.u.Code, r.u.Symbol,
            r.bal.OnHandBaseQuantity, r.bal.LastMovedAtUtc,
            Convert.ToBase64String(r.RowVersion),
            r.bal.BlockedBaseQuantity,
            decimal.Round(r.bal.OnHandBaseQuantity - r.bal.BlockedBaseQuantity, 6, MidpointRounding.AwayFromZero),
            r.ManufactureDate,
            r.ExpirationDate,
            r.ExpirationDate.HasValue && r.ExpirationDate.Value < today,
            r.ExpirationDate.HasValue && r.ExpirationDate.Value >= today && r.ExpirationDate.Value <= soonCutoff)).ToArray();

        return new OrganizationPage<InventoryBalanceSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<OrganizationPage<InventoryTransactionSummary>> ListTransactions(
        long organizationId,
        InventoryTransactionListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from t in db.InventoryTransactions.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { t.OrganizationId, SiteId = t.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        join w in db.Warehouses.AsNoTracking() on new { t.OrganizationId, WarehouseId = t.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                        join dw in db.Warehouses.AsNoTracking() on new { t.OrganizationId, WarehouseId = t.DestinationWarehouseId } equals new { dw.OrganizationId, WarehouseId = (long?)dw.Id } into destJoin
                        from dw in destJoin.DefaultIfEmpty()
                        join po in db.ProductionOrders.AsNoTracking() on new { t.OrganizationId, PoId = t.ProductionOrderId } equals new { po.OrganizationId, PoId = (long?)po.Id } into poJoin
                        from po in poJoin.DefaultIfEmpty()
                        join qi in db.QualityInspections.AsNoTracking() on new { t.OrganizationId, QiId = t.QualityInspectionId } equals new { qi.OrganizationId, QiId = (long?)qi.Id } into qiJoin
                        from qi in qiJoin.DefaultIfEmpty()
                        join pur in db.PurchaseOrders.AsNoTracking() on new { t.OrganizationId, PurId = t.PurchaseOrderId } equals new { pur.OrganizationId, PurId = (long?)pur.Id } into purJoin
                        from pur in purJoin.DefaultIfEmpty()
                        where t.OrganizationId == organizationId
                        select new
                        {
                            t,
                            s,
                            w,
                            DestCode = dw != null ? dw.Code : null,
                            PoNumber = po != null ? po.OrderNumber : null,
                            QiNumber = qi != null ? qi.InspectionNumber : null,
                            PurNumber = pur != null ? pur.OrderNumber : null,
                            IsReversed = db.InventoryTransactions.Any(r => r.OrganizationId == organizationId && r.ReversedTransactionId == t.Id),
                            LineCount = db.InventoryLedgerEntries.Count(l => l.OrganizationId == organizationId && l.InventoryTransactionId == t.Id)
                        };

        if (query.SiteId is long siteId && siteId > 0)
            baseQuery = baseQuery.Where(x => x.t.SiteId == siteId);
        if (query.WarehouseId is long whId && whId > 0)
            baseQuery = baseQuery.Where(x => x.t.WarehouseId == whId || x.t.DestinationWarehouseId == whId);
        if (query.TransactionType is InventoryTransactionType tt)
            baseQuery = baseQuery.Where(x => x.t.TransactionType == tt);
        if (query.ProductionOrderId is long poId && poId > 0)
            baseQuery = baseQuery.Where(x => x.t.ProductionOrderId == poId);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.t.DocumentNumber.Contains(query.Search)
                || (x.PoNumber != null && x.PoNumber.Contains(query.Search))
                || x.w.Code.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderByDescending(x => x.t.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new InventoryTransactionSummary(
            r.t.Id, r.t.OrganizationId, r.t.SiteId, r.s.Code, r.s.Name,
            r.t.WarehouseId, r.w.Code, r.w.Name,
            r.t.DestinationWarehouseId, r.DestCode,
            r.t.DocumentNumber, r.t.TransactionType,
            r.t.ProductionOrderId, r.PoNumber,
            r.t.ReversedTransactionId, r.IsReversed,
            r.t.ReferenceNote, r.t.PostedAtUtc, r.LineCount,
            r.t.QualityInspectionId, r.QiNumber,
            r.t.PurchaseOrderId, r.PurNumber)).ToArray();

        return new OrganizationPage<InventoryTransactionSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<InventoryTransactionDetails?> GetTransaction(long organizationId, long id, CancellationToken ct) =>
        Read(db => BuildTransactionDetailsAsync(db, organizationId, id, ct), ct);

    public Task<InventoryWarehouseLookup?> GetWarehouseLookup(long organizationId, long warehouseId, CancellationToken ct) => Read(async db =>
        await (from w in db.Warehouses.AsNoTracking()
               join s in db.Sites.AsNoTracking() on new { w.OrganizationId, SiteId = w.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
               join o in db.Organizations.IgnoreAutoIncludes().AsNoTracking() on w.OrganizationId equals o.Id
               where w.OrganizationId == organizationId && w.Id == warehouseId
               select new InventoryWarehouseLookup(w.Id, w.OrganizationId, w.SiteId, s.Code, w.IsActive, s.IsActive, o.IsActive))
            .SingleOrDefaultAsync(ct), ct);

    public Task<IReadOnlyDictionary<long, InventoryItemConversionContext>> LoadItemUnitContexts(
        long organizationId,
        IReadOnlyCollection<long> itemIds,
        CancellationToken ct) => Read<IReadOnlyDictionary<long, InventoryItemConversionContext>>(async db =>
    {
        var items = await db.Items.AsNoTracking()
            .Where(i => i.OrganizationId == organizationId && itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.IsActive, i.BaseUnitId, i.RequiresLotTracking, i.QualityControlMode })
            .ToListAsync(ct);

        var globalEdges = await db.UnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.IsActive)
            .Select(c => new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor))
            .ToListAsync(ct);

        var itemEdgesList = await db.ItemUnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && itemIds.Contains(c.ItemId) && c.IsActive)
            .Select(c => new { c.ItemId, Edge = new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor) })
            .ToListAsync(ct);

        var edgesByItem = itemEdgesList.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Select(x => x.Edge).ToArray());

        return items.ToDictionary(
            i => i.Id,
            i => new InventoryItemConversionContext(
                i.Id,
                i.IsActive,
                i.BaseUnitId,
                edgesByItem.TryGetValue(i.Id, out var specific)
                    ? specific.Concat(globalEdges).ToArray()
                    : globalEdges,
                i.RequiresLotTracking,
                i.QualityControlMode));
    }, ct);

    public Task<IReadOnlyHashSetLike> LoadActiveUnitIds(
        long organizationId,
        IReadOnlyCollection<long> unitIds,
        CancellationToken ct) => Read(async db =>
    {
        var active = await db.Units.AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && unitIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);
        return new IReadOnlyHashSetLike(active);
    }, ct);

    public Task<InventoryProductionOrderLookup?> GetProductionOrderLookup(
        long organizationId,
        long productionOrderId,
        CancellationToken ct) => Read(async db =>
        await db.ProductionOrders.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.Id == productionOrderId)
            .Select(p => new InventoryProductionOrderLookup(p.Id, p.OrganizationId, p.SiteId, p.Status, p.OrderNumber))
            .SingleOrDefaultAsync(ct), ct);

    public Task<OrganizationResult<InventoryTransactionDetails>> PostTransaction(
        PostInventoryTransactionRequest request,
        long siteId,
        IReadOnlyList<ResolvedInventoryLineWrite> resolvedLines,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<InventoryTransactionDetails>.Failure(orgCheck.Error!);

        var idempKey = InventoryText.OptionalIdempotencyKey(request.IdempotencyKey);
        if (idempKey is not null)
        {
            var existingId = await db.InventoryTransactions.AsNoTracking()
                .Where(x => x.OrganizationId == request.OrganizationId && x.IdempotencyKey == idempKey)
                .Select(x => (long?)x.Id)
                .SingleOrDefaultAsync(ct);
            if (existingId.HasValue)
                return OrganizationResult<InventoryTransactionDetails>.Success(
                    (await BuildTransactionDetailsAsync(db, request.OrganizationId, existingId.Value, ct))!);
        }

        var today = DateOnly.FromDateTime(utcNow);
        var outboundLotKeys = resolvedLines
            .Where(l => l.BaseQuantityDelta < 0m && !string.IsNullOrEmpty(l.LotCode))
            .Select(l => (l.ItemId, l.LotCode))
            .Distinct()
            .ToArray();
        if (outboundLotKeys.Length > 0)
        {
            var outItemIds = outboundLotKeys.Select(k => k.ItemId).Distinct().ToArray();
            var candidateLots = await db.ItemLots.AsNoTracking()
                .Where(l => l.OrganizationId == request.OrganizationId && outItemIds.Contains(l.ItemId) && l.ExpirationDate != null)
                .Select(l => new { l.ItemId, l.LotCode, l.ExpirationDate })
                .ToListAsync(ct);
            foreach (var key in outboundLotKeys)
            {
                var lotMeta = candidateLots.FirstOrDefault(l => l.ItemId == key.ItemId && l.LotCode == key.LotCode);
                if (lotMeta is not null && lotMeta.ExpirationDate.HasValue && lotMeta.ExpirationDate.Value < today)
                {
                    return OrganizationResult<InventoryTransactionDetails>.Failure(
                        InventoryErrors.LotExpired,
                        new Dictionary<string, object> { ["lotCode"] = key.LotCode });
                }
            }
        }

        var txn = InventoryTransaction.Create(
            request.OrganizationId,
            siteId,
            request.WarehouseId,
            request.DestinationWarehouseId,
            request.DocumentNumber,
            request.TransactionType,
            request.ProductionOrderId,
            null,
            request.ReferenceNote,
            idempKey,
            utcNow);

        db.InventoryTransactions.Add(txn);
        await db.SaveInOperation(access.Actor, ct);

        await UpsertInboundLotMetadataAsync(db, request.OrganizationId, resolvedLines, ct);
        await ApplyLedgerLinesAndBalancesAsync(db, request.OrganizationId, txn.Id, resolvedLines, utcNow, ct);
        await CreateAutoQualityInspectionsAsync(db, request.OrganizationId, request.TransactionType, txn.Id, request.ProductionOrderId, resolvedLines, utcNow, ct);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<InventoryTransactionDetails>.Success(
            (await BuildTransactionDetailsAsync(db, request.OrganizationId, txn.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<InventoryTransactionDetails>> ReverseTransaction(
        long organizationId,
        long transactionId,
        string reversalDocumentNumber,
        string? referenceNote,
        string? idempotencyKey,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<InventoryTransactionDetails>.Failure(orgCheck.Error!);

        if (idempotencyKey is not null)
        {
            var existingIdemp = await db.InventoryTransactions.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.IdempotencyKey == idempotencyKey)
                .Select(x => (long?)x.Id)
                .SingleOrDefaultAsync(ct);
            if (existingIdemp.HasValue)
                return OrganizationResult<InventoryTransactionDetails>.Success(
                    (await BuildTransactionDetailsAsync(db, organizationId, existingIdemp.Value, ct))!);
        }

        var orig = await db.InventoryTransactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.OrganizationId == organizationId && t.Id == transactionId, ct);
        if (orig is null) return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.TransactionNotFound);
        if (orig.TransactionType is InventoryTransactionType.Reversal or InventoryTransactionType.QualityScrap)
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.CannotReverseReversal);
        if (await db.InventoryTransactions.AsNoTracking().AnyAsync(r => r.OrganizationId == organizationId && r.ReversedTransactionId == transactionId, ct))
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.AlreadyReversed);

        var linkedInspections = await db.QualityInspections
            .Where(q => q.OrganizationId == organizationId && q.SourceTransactionId == transactionId && q.Status != QualityInspectionStatus.Cancelled)
            .OrderBy(q => q.Id)
            .ToListAsync(ct);
        if (linkedInspections.Any(q => q.AcceptedBaseQuantity > 0m || q.RejectedBaseQuantity > 0m))
            return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.InsufficientStock);

        var origLines = await db.InventoryLedgerEntries.AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && l.InventoryTransactionId == transactionId)
            .OrderBy(l => l.LineNumber)
            .ToListAsync(ct);

        var reversalTxn = InventoryTransaction.Create(
            organizationId,
            orig.SiteId,
            orig.WarehouseId,
            orig.DestinationWarehouseId,
            reversalDocumentNumber,
            InventoryTransactionType.Reversal,
            orig.ProductionOrderId,
            orig.Id,
            referenceNote,
            idempotencyKey,
            utcNow,
            purchaseOrderId: orig.PurchaseOrderId);

        db.InventoryTransactions.Add(reversalTxn);
        await db.SaveInOperation(access.Actor, ct);

        if (orig.TransactionType == InventoryTransactionType.Receipt && orig.PurchaseOrderId is long poId)
        {
            var po = await db.PurchaseOrders
                .FromSqlInterpolated($"SELECT * FROM [organization].[PurchaseOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {poId}")
                .SingleOrDefaultAsync(ct);
            if (po is not null)
            {
                await db.PurchaseOrderLines
                    .Where(l => l.OrganizationId == organizationId && l.PurchaseOrderId == poId)
                    .LoadAsync(ct);
                var revertedByItem = origLines
                    .Where(l => l.BaseQuantityDelta > 0m)
                    .GroupBy(l => l.ItemId)
                    .Select(g => (g.Key, g.Sum(x => x.BaseQuantityDelta)))
                    .ToArray();
                if (revertedByItem.Length > 0)
                    po.RevertReceiptsByItem(revertedByItem);
            }
        }

        foreach (var qi in linkedInspections)
        {
            if (qi.RemainingBlockedBaseQuantity > 0m)
            {
                var bal = await db.InventoryBalances
                    .FromSqlInterpolated(
                        $"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [WarehouseId] = {qi.WarehouseId} AND [ItemId] = {qi.ItemId} AND [LotCode] = {qi.LotCode}")
                    .SingleOrDefaultAsync(ct);
                if (bal is null || bal.BlockedBaseQuantity < qi.RemainingBlockedBaseQuantity)
                    return OrganizationResult<InventoryTransactionDetails>.Failure(InventoryErrors.InsufficientStock);
                bal.ReleaseBlockedQuantity(qi.RemainingBlockedBaseQuantity, utcNow);
            }
            qi.Cancel(referenceNote, access.Actor.ActorId, utcNow);
        }

        var negated = origLines.Select(l => new ResolvedInventoryLineWrite(
            l.SiteId,
            l.WarehouseId,
            l.ItemId,
            l.LotCode,
            -l.EnteredQuantity,
            l.EnteredUnitId,
            -l.BaseQuantityDelta,
            l.BaseUnitId,
            l.Notes)).ToArray();

        await ApplyLedgerLinesAndBalancesAsync(db, organizationId, reversalTxn.Id, negated, utcNow, ct);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<InventoryTransactionDetails>.Success(
            (await BuildTransactionDetailsAsync(db, organizationId, reversalTxn.Id, ct))!);
    }, ct);

    internal static async Task ApplyLedgerLinesAndBalancesAsync(
        OrganizationDbContext db,
        long organizationId,
        long transactionId,
        IReadOnlyList<ResolvedInventoryLineWrite> lines,
        DateTime utcNow,
        CancellationToken ct)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            db.InventoryLedgerEntries.Add(InventoryLedgerEntry.Create(
                organizationId,
                transactionId,
                l.SiteId,
                l.WarehouseId,
                i + 1,
                l.ItemId,
                l.LotCode,
                l.EnteredQuantity,
                l.EnteredUnitId,
                l.BaseQuantityDelta,
                l.BaseUnitId,
                utcNow,
                l.Notes));
        }

        // Deterministic sort order prevents deadlocks across concurrent multi-line or transfer postings.
        var grouped = lines
            .GroupBy(l => (l.SiteId, l.WarehouseId, l.ItemId, l.LotCode, l.BaseUnitId))
            .OrderBy(g => g.Key.WarehouseId)
            .ThenBy(g => g.Key.ItemId)
            .ThenBy(g => g.Key.LotCode, StringComparer.Ordinal)
            .ToArray();

        foreach (var grp in grouped)
        {
            var (siteId, warehouseId, itemId, lotCode, baseUnitId) = grp.Key;
            var totalDelta = grp.Sum(x => x.BaseQuantityDelta);
            if (totalDelta == 0m) continue;

            var bal = await db.InventoryBalances
                .FromSqlInterpolated(
                    $"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [WarehouseId] = {warehouseId} AND [ItemId] = {itemId} AND [LotCode] = {lotCode}")
                .SingleOrDefaultAsync(ct);

            if (bal is null)
            {
                bal = InventoryBalance.CreateZero(organizationId, siteId, warehouseId, itemId, lotCode, baseUnitId, utcNow);
                bal.ApplyDelta(totalDelta, utcNow);
                db.InventoryBalances.Add(bal);
            }
            else
            {
                bal.ApplyDelta(totalDelta, utcNow);
            }
        }
    }

    internal static async Task UpsertInboundLotMetadataAsync(
        OrganizationDbContext db,
        long organizationId,
        IReadOnlyList<ResolvedInventoryLineWrite> lines,
        CancellationToken ct)
    {
        var candidates = lines
            .Where(l => l.BaseQuantityDelta > 0m && !string.IsNullOrEmpty(l.LotCode) && (l.ManufactureDate.HasValue || l.ExpirationDate.HasValue))
            .GroupBy(l => (l.ItemId, l.LotCode))
            .Select(g => g.First())
            .ToArray();

        foreach (var line in candidates)
        {
            var existing = await db.ItemLots
                .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.ItemId == line.ItemId && x.LotCode == line.LotCode, ct);
            if (existing is null)
            {
                db.ItemLots.Add(ItemLot.Create(organizationId, line.ItemId, line.LotCode, line.ManufactureDate, line.ExpirationDate));
            }
            else
            {
                existing.UpdateMetadata(
                    line.ManufactureDate ?? existing.ManufactureDate,
                    line.ExpirationDate ?? existing.ExpirationDate,
                    existing.Notes);
            }
        }
    }

    internal static async Task CreateAutoQualityInspectionsAsync(
        OrganizationDbContext db,
        long organizationId,
        InventoryTransactionType transactionType,
        long transactionId,
        long? productionOrderId,
        IReadOnlyList<ResolvedInventoryLineWrite> lines,
        DateTime utcNow,
        CancellationToken ct)
    {
        var sourceType = transactionType == InventoryTransactionType.ProductionReceipt
            ? QualityInspectionSourceType.ProductionReceipt
            : QualityInspectionSourceType.Receipt;

        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (!l.TriggersQualityControl || l.BaseQuantityDelta <= 0m)
                continue;

            var bal = db.InventoryBalances.Local
                .FirstOrDefault(b => b.OrganizationId == organizationId && b.WarehouseId == l.WarehouseId && b.ItemId == l.ItemId && b.LotCode == l.LotCode);
            if (bal is null)
            {
                bal = await db.InventoryBalances
                    .FromSqlInterpolated(
                        $"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [WarehouseId] = {l.WarehouseId} AND [ItemId] = {l.ItemId} AND [LotCode] = {l.LotCode}")
                    .SingleAsync(ct);
            }
            bal.BlockQuantity(l.BaseQuantityDelta, utcNow);

            var inspNum = $"QC-{transactionId}-{i + 1}";
            db.QualityInspections.Add(QualityInspection.Create(
                organizationId,
                l.SiteId,
                l.WarehouseId,
                l.ItemId,
                l.LotCode,
                inspNum,
                sourceType,
                transactionId,
                productionOrderId,
                l.BaseUnitId,
                l.BaseQuantityDelta,
                l.Notes));
        }
    }

    private static async Task ClearExistingSiteDefaultsAsync(
        OrganizationDbContext db,
        long organizationId,
        long siteId,
        long? excludeWarehouseId,
        CancellationToken ct)
    {
        var existing = await db.Warehouses
            .Where(w => w.OrganizationId == organizationId && w.SiteId == siteId && w.IsDefaultForSite && w.IsActive
                        && (!excludeWarehouseId.HasValue || w.Id != excludeWarehouseId.Value))
            .ToListAsync(ct);
        foreach (var w in existing)
            w.ClearDefault();
    }

    private static async Task<WarehouseSummary?> BuildWarehouseSummaryAsync(OrganizationDbContext db, long organizationId, long id, CancellationToken ct)
    {
        var row = await (from wh in db.Warehouses.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { wh.OrganizationId, SiteId = wh.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         where wh.OrganizationId == organizationId && wh.Id == id
                         select new { wh, s, RowVersion = EF.Property<byte[]>(wh, "RowVersion") })
            .SingleOrDefaultAsync(ct);
        if (row is null) return null;
        return new WarehouseSummary(
            row.wh.Id, row.wh.OrganizationId, row.wh.SiteId, row.s.Code, row.s.Name,
            row.wh.Code, row.wh.Name, row.wh.IsDefaultForSite, row.wh.IsActive,
            Convert.ToBase64String(row.RowVersion));
    }

    private static async Task<InventoryTransactionDetails?> BuildTransactionDetailsAsync(OrganizationDbContext db, long organizationId, long id, CancellationToken ct)
    {
        var row = await (from t in db.InventoryTransactions.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { t.OrganizationId, SiteId = t.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         join w in db.Warehouses.AsNoTracking() on new { t.OrganizationId, WarehouseId = t.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                         join dw in db.Warehouses.AsNoTracking() on new { t.OrganizationId, WarehouseId = t.DestinationWarehouseId } equals new { dw.OrganizationId, WarehouseId = (long?)dw.Id } into destJoin
                         from dw in destJoin.DefaultIfEmpty()
                         join po in db.ProductionOrders.AsNoTracking() on new { t.OrganizationId, PoId = t.ProductionOrderId } equals new { po.OrganizationId, PoId = (long?)po.Id } into poJoin
                         from po in poJoin.DefaultIfEmpty()
                         join qi in db.QualityInspections.AsNoTracking() on new { t.OrganizationId, QiId = t.QualityInspectionId } equals new { qi.OrganizationId, QiId = (long?)qi.Id } into qiJoin
                         from qi in qiJoin.DefaultIfEmpty()
                         join pur in db.PurchaseOrders.AsNoTracking() on new { t.OrganizationId, PurId = t.PurchaseOrderId } equals new { pur.OrganizationId, PurId = (long?)pur.Id } into purJoin
                         from pur in purJoin.DefaultIfEmpty()
                         where t.OrganizationId == organizationId && t.Id == id
                         select new
                         {
                             t,
                             s,
                             w,
                             DestCode = dw != null ? dw.Code : null,
                             PoNumber = po != null ? po.OrderNumber : null,
                             QiNumber = qi != null ? qi.InspectionNumber : null,
                             PurNumber = pur != null ? pur.OrderNumber : null,
                             IsReversed = db.InventoryTransactions.Any(r => r.OrganizationId == organizationId && r.ReversedTransactionId == t.Id)
                         }).SingleOrDefaultAsync(ct);
        if (row is null) return null;

        var entries = await (from l in db.InventoryLedgerEntries.AsNoTracking()
                             join w in db.Warehouses.AsNoTracking() on new { l.OrganizationId, WarehouseId = l.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                             join it in db.Items.AsNoTracking() on new { l.OrganizationId, ItemId = l.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                             join eu in db.Units.AsNoTracking() on new { l.OrganizationId, UnitId = l.EnteredUnitId } equals new { eu.OrganizationId, UnitId = eu.Id }
                             join bu in db.Units.AsNoTracking() on new { l.OrganizationId, UnitId = l.BaseUnitId } equals new { bu.OrganizationId, UnitId = bu.Id }
                             where l.OrganizationId == organizationId && l.InventoryTransactionId == id
                             orderby l.LineNumber
                             select new InventoryLedgerEntrySummary(
                                 l.Id, l.LineNumber, l.SiteId, l.WarehouseId, w.Code, w.Name,
                                 l.ItemId, it.Code, it.Name, l.LotCode,
                                 l.EnteredQuantity, l.EnteredUnitId, eu.Symbol,
                                 l.BaseQuantityDelta, l.BaseUnitId, bu.Symbol,
                                 l.OccurredAtUtc, l.Notes))
            .ToListAsync(ct);

        var summary = new InventoryTransactionSummary(
            row.t.Id, row.t.OrganizationId, row.t.SiteId, row.s.Code, row.s.Name,
            row.t.WarehouseId, row.w.Code, row.w.Name,
            row.t.DestinationWarehouseId, row.DestCode,
            row.t.DocumentNumber, row.t.TransactionType,
            row.t.ProductionOrderId, row.PoNumber,
            row.t.ReversedTransactionId, row.IsReversed,
            row.t.ReferenceNote, row.t.PostedAtUtc, entries.Count,
            row.t.QualityInspectionId, row.QiNumber,
            row.t.PurchaseOrderId, row.PurNumber);

        return new InventoryTransactionDetails(summary, entries);
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
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlInventoryOperationsStore));
            throw new InventoryStoreUnavailableException(ex);
        }
    }

    private async Task<OrganizationResult<T>> Write<T>(
        OrganizationWriteAccess access,
        Func<OrganizationDbContext, Task<OrganizationResult<T>>> operation,
        CancellationToken ct)
    {
        try
        {
            await using var strategyContext = await Factory.CreateDbContextAsync(ct);
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                if (!await access.Revalidate(ct))
                    return OrganizationResult<T>.Failure(InventoryErrors.AccessDenied);

                var commitAttempted = false;
                try
                {
                    await using var db = await Factory.CreateDbContextAsync(ct);
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    var result = await operation(db);
                    if (!result.Succeeded)
                    {
                        await transaction.RollbackAsync(ct);
                        return result;
                    }
                    commitAttempted = true;
                    await transaction.CommitAsync(ct);
                    return result;
                }
                catch (DbUpdateConcurrencyException)
                {
                    return OrganizationResult<T>.Failure(InventoryErrors.ConcurrencyConflict);
                }
                catch (DbUpdateException ex) when (IsUniqueConflict(ex))
                {
                    return OrganizationResult<T>.Failure(ClassifyUniqueConflict(ex));
                }
                catch (DbUpdateException ex) when (IsCheckViolation(ex))
                {
                    return OrganizationResult<T>.Failure(InventoryErrors.InsufficientStock);
                }
                catch (OrganizationAuditWriteException)
                {
                    return OrganizationResult<T>.Failure(InventoryErrors.AuditUnavailable);
                }
                catch (OrganizationCommitOutcomeUnknownException)
                {
                    return OrganizationResult<T>.Failure(InventoryErrors.OutcomeUnknown);
                }
                catch (InvalidOperationException ex) when (ex.Message == InventoryErrors.ParentOrganizationInactive)
                {
                    return OrganizationResult<T>.Failure(InventoryErrors.ParentOrganizationInactive);
                }
                catch (InvalidOperationException ex) when (ex.Message == OrganizationAccessErrors.AccessDenied)
                {
                    return OrganizationResult<T>.Failure(InventoryErrors.AccessDenied);
                }
                catch (Exception ex) when (commitAttempted && Unavailable(ex))
                {
                    return OrganizationResult<T>.Failure(InventoryErrors.OutcomeUnknown);
                }
            });
        }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlInventoryOperationsStore), access.Actor.ActorId);
            return OrganizationResult<T>.Failure(InventoryErrors.PersistenceUnavailable);
        }
    }

    private static async Task<(bool Ok, string? Error)> LockAndRequireActiveOrganization(OrganizationDbContext db, long orgId, CancellationToken ct)
    {
        var active = await db.Database.SqlQueryRaw<bool>(
            "SELECT [IsActive] AS [Value] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}", orgId)
            .SingleOrDefaultAsync(ct);
        if (!active)
        {
            var exists = await db.Organizations.AsNoTracking().AnyAsync(o => o.Id == orgId, ct);
            return (false, exists ? InventoryErrors.ParentOrganizationInactive : InventoryErrors.AccessDenied);
        }
        return (true, null);
    }

    private static bool Matches(OrganizationDbContext db, object entity, byte[] version)
        => ((byte[])db.Entry(entity).Property("RowVersion").CurrentValue!).SequenceEqual(version);

    private static bool IsUniqueConflict(DbUpdateException ex)
        => ex.InnerException is SqlException sql && sql.Number is 2601 or 2627;

    private static bool IsCheckViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql && sql.Number == 547 && (sql.Message.Contains("CK_InventoryBalance_NonNegative", StringComparison.Ordinal) || sql.Message.Contains("CK_InventoryBalance_Blocked", StringComparison.Ordinal));

    private static string ClassifyUniqueConflict(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? string.Empty;
        if (msg.Contains("UX_Warehouse_Org_Code", StringComparison.Ordinal)) return InventoryErrors.CodeDuplicate;
        if (msg.Contains("UX_InventoryTransaction_Org_DocNum", StringComparison.Ordinal)) return InventoryErrors.DocumentNumberDuplicate;
        if (msg.Contains("UX_InventoryTransaction_Org_Reversal", StringComparison.Ordinal)) return InventoryErrors.AlreadyReversed;
        return InventoryErrors.ConcurrencyConflict;
    }

    private static bool Unavailable(Exception ex)
        => ex is SqlException or TimeoutException or DbUpdateException or InvalidOperationException { InnerException: SqlException };
}
