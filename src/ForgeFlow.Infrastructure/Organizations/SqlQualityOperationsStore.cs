using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Quality;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Diagnostics;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlQualityOperationsStore(
    Func<IDbContextFactory<OrganizationDbContext>> getFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null,
    IServiceScopeFactory? scopeFactory = null)
    : IQualityOperationsStore
{
    private const int SoonDays = 30;
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public SqlQualityOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public Task<OrganizationPage<QualityInspectionSummary>> ListInspections(
        long organizationId,
        QualityInspectionListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from qi in db.QualityInspections.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { qi.OrganizationId, SiteId = qi.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        join w in db.Warehouses.AsNoTracking() on new { qi.OrganizationId, WarehouseId = qi.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                        join it in db.Items.AsNoTracking() on new { qi.OrganizationId, ItemId = qi.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                        join u in db.Units.AsNoTracking() on new { qi.OrganizationId, UnitId = qi.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                        join tx in db.InventoryTransactions.AsNoTracking() on new { qi.OrganizationId, TxId = qi.SourceTransactionId } equals new { tx.OrganizationId, TxId = (long?)tx.Id } into txJoin
                        from tx in txJoin.DefaultIfEmpty()
                        join po in db.ProductionOrders.AsNoTracking() on new { qi.OrganizationId, PoId = qi.ProductionOrderId } equals new { po.OrganizationId, PoId = (long?)po.Id } into poJoin
                        from po in poJoin.DefaultIfEmpty()
                        where qi.OrganizationId == organizationId
                        select new
                        {
                            qi,
                            s,
                            w,
                            it,
                            u,
                            SourceDoc = tx != null ? tx.DocumentNumber : null,
                            PoNumber = po != null ? po.OrderNumber : null,
                            RowVersion = EF.Property<byte[]>(qi, "RowVersion")
                        };

        if (query.SiteId is long siteId && siteId > 0)
            baseQuery = baseQuery.Where(x => x.qi.SiteId == siteId);
        if (query.WarehouseId is long whId && whId > 0)
            baseQuery = baseQuery.Where(x => x.qi.WarehouseId == whId);
        if (query.ItemId is long itemId && itemId > 0)
            baseQuery = baseQuery.Where(x => x.qi.ItemId == itemId);
        if (query.Status is QualityInspectionStatus st)
            baseQuery = baseQuery.Where(x => x.qi.Status == st);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.qi.InspectionNumber.Contains(query.Search)
                || x.qi.LotCode.Contains(query.Search)
                || x.it.Code.Contains(query.Search)
                || x.it.Name.Contains(query.Search)
                || (x.PoNumber != null && x.PoNumber.Contains(query.Search)));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderByDescending(x => x.qi.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var actorNames = await ResolveUserNamesAsync(rows.Select(r => r.qi.InspectedByActorId), ct);

        var items = rows.Select(r => new QualityInspectionSummary(
            r.qi.Id, r.qi.OrganizationId, r.qi.SiteId, r.s.Code, r.s.Name,
            r.qi.WarehouseId, r.w.Code, r.w.Name,
            r.qi.ItemId, r.it.Code, r.it.Name, r.qi.LotCode,
            r.qi.InspectionNumber, r.qi.SourceType, r.qi.SourceTransactionId, r.SourceDoc,
            r.qi.ProductionOrderId, r.PoNumber,
            r.qi.BaseUnitId, r.u.Code, r.u.Symbol,
            r.qi.InspectedBaseQuantity, r.qi.AcceptedBaseQuantity, r.qi.RejectedBaseQuantity, r.qi.RemainingBlockedBaseQuantity,
            r.qi.Status, r.qi.DefectReasonCode, r.qi.Notes,
            r.qi.InspectedAtUtc, r.qi.InspectedByActorId,
            r.qi.InspectedByActorId.HasValue && actorNames.TryGetValue(r.qi.InspectedByActorId.Value, out var name) ? name : null,
            Convert.ToBase64String(r.RowVersion),
            r.qi.CertificateNumber)).ToArray();

        return new OrganizationPage<QualityInspectionSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<QualityInspectionDetails?> GetInspection(long organizationId, long id, CancellationToken ct) =>
        Read(db => BuildInspectionDetailsAsync(db, organizationId, id, ct), ct);

    public Task<QualityInspectionBalanceTarget?> GetHoldTargetLookup(
        long organizationId,
        long warehouseId,
        long itemId,
        string lotCode,
        CancellationToken ct) => Read(async db =>
    {
        var bal = await db.InventoryBalances.AsNoTracking()
            .Where(b => b.OrganizationId == organizationId && b.WarehouseId == warehouseId && b.ItemId == itemId && b.LotCode == lotCode)
            .Select(b => new
            {
                b.OrganizationId,
                b.SiteId,
                b.WarehouseId,
                b.ItemId,
                b.LotCode,
                b.BaseUnitId,
                Available = b.OnHandBaseQuantity - b.BlockedBaseQuantity
            })
            .SingleOrDefaultAsync(ct);
        if (bal is null) return null;

        var edges = await LoadItemUnitEdgesInternalAsync(db, organizationId, itemId, ct);
        return new QualityInspectionBalanceTarget(
            bal.OrganizationId,
            bal.SiteId,
            bal.WarehouseId,
            bal.ItemId,
            bal.LotCode,
            bal.BaseUnitId,
            bal.Available,
            edges);
    }, ct);

    public Task<IReadOnlyList<UnitConversionEdge>> LoadItemUnitEdges(long organizationId, long itemId, CancellationToken ct) =>
        Read(db => LoadItemUnitEdgesInternalAsync(db, organizationId, itemId, ct), ct);

    public Task<OrganizationResult<QualityInspectionDetails>> CreateManualHold(
        CreateManualQualityHoldRequest request,
        long siteId,
        string inspectionNumber,
        string normalizedLotCode,
        long baseUnitId,
        decimal holdBaseQuantity,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualityInspectionDetails>.Failure(orgCheck.Error!);

        var wh = await db.Warehouses.AsNoTracking()
            .SingleOrDefaultAsync(w => w.OrganizationId == request.OrganizationId && w.Id == request.WarehouseId, ct);
        if (wh is null || !wh.IsActive) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.WarehouseInvalid);

        var siteActive = await db.Sites.AsNoTracking()
            .Where(s => s.OrganizationId == request.OrganizationId && s.Id == wh.SiteId)
            .Select(s => s.IsActive)
            .SingleOrDefaultAsync(ct);
        if (!siteActive) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.SiteInactive);

        var bal = await db.InventoryBalances
            .FromSqlInterpolated(
                $"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {request.OrganizationId} AND [WarehouseId] = {request.WarehouseId} AND [ItemId] = {request.ItemId} AND [LotCode] = {normalizedLotCode}")
            .SingleOrDefaultAsync(ct);

        if (bal is null || bal.AvailableBaseQuantity < holdBaseQuantity)
        {
            return OrganizationResult<QualityInspectionDetails>.Failure(
                QualityErrors.InsufficientAvailableStock,
                new Dictionary<string, object>
                {
                    ["available"] = bal?.AvailableBaseQuantity ?? 0m,
                    ["requested"] = holdBaseQuantity
                });
        }

        bal.BlockQuantity(holdBaseQuantity, utcNow);

        var inspNumber = await SqlDocumentSequenceStore.AllocateAsync(
            db, request.OrganizationId, DocumentSequenceType.QualityInspection, inspectionNumber, utcNow, ct);

        var qi = QualityInspection.Create(
            request.OrganizationId,
            wh.SiteId,
            request.WarehouseId,
            request.ItemId,
            normalizedLotCode,
            inspNumber,
            QualityInspectionSourceType.ManualHold,
            null,
            null,
            baseUnitId,
            holdBaseQuantity,
            request.Notes);

        if (!string.IsNullOrWhiteSpace(request.DefectReasonCode))
            qi.UpdateHeaderNotes(request.DefectReasonCode, request.Notes);

        await SqlQualitySpecStore.PopulateInspectionFromSpecAsync(db, qi, QualitySpecCheckpoint.Incoming, ct);

        db.QualityInspections.Add(qi);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<QualityInspectionDetails>.Success(
            (await BuildInspectionDetailsAsync(db, request.OrganizationId, qi.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> UpdateMeasurements(
        long organizationId,
        long inspectionId,
        byte[] version,
        string? defectReasonCode,
        string? notes,
        IReadOnlyList<QualityInspectionLineInput> lines,
        OrganizationWriteAccess access,
        CancellationToken ct,
        string? certificateNumber = null) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualityInspectionDetails>.Failure(orgCheck.Error!);

        var qi = await db.QualityInspections
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == inspectionId, ct);
        if (qi is null) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        if (!Matches(db, qi, version)) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);

        if (qi.Lines.Count > 0)
            db.QualityInspectionLines.RemoveRange(qi.Lines);

        qi.UpdateHeaderNotes(defectReasonCode, notes, certificateNumber);
        qi.ReplaceLines(lines.Select(l => new QualityInspectionLineWriteData(
            l.ParameterName,
            l.ExpectedSpec,
            l.MeasuredValue,
            l.IsPassed,
            l.Notes,
            l.QualityParameterId,
            l.ParameterCode,
            l.Symbol,
            l.UnitSymbol,
            l.TestMethod,
            l.DataType,
            l.MinValue,
            l.MaxValue,
            l.TargetValue,
            l.MeasuredNumericValue,
            l.IsMandatory)));
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<QualityInspectionDetails>.Success(
            (await BuildInspectionDetailsAsync(db, organizationId, qi.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> RecordDisposition(
        long organizationId,
        long inspectionId,
        byte[] version,
        decimal acceptBaseQuantity,
        decimal rejectBaseQuantity,
        string? defectReasonCode,
        string? notes,
        string? scrapDocumentNumber,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualityInspectionDetails>.Failure(orgCheck.Error!);

        var qi = await db.QualityInspections
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == inspectionId, ct);
        if (qi is null) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        if (!Matches(db, qi, version)) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);

        var bal = await db.InventoryBalances
            .FromSqlInterpolated(
                $"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [WarehouseId] = {qi.WarehouseId} AND [ItemId] = {qi.ItemId} AND [LotCode] = {qi.LotCode}")
            .SingleOrDefaultAsync(ct);

        var totalAct = decimal.Round(acceptBaseQuantity + rejectBaseQuantity, 6, MidpointRounding.AwayFromZero);
        if (bal is null || bal.BlockedBaseQuantity < totalAct)
            return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InsufficientAvailableStock);

        qi.RecordDisposition(acceptBaseQuantity, rejectBaseQuantity, defectReasonCode, notes, access.Actor.ActorId, utcNow);

        if (acceptBaseQuantity > 0m)
            bal.ReleaseBlockedQuantity(acceptBaseQuantity, utcNow);

        if (rejectBaseQuantity > 0m)
        {
            bal.ScrapBlockedQuantity(rejectBaseQuantity, utcNow);

            var docNum = await SqlDocumentSequenceStore.AllocateAsync(
                db, organizationId, DocumentSequenceType.QualityScrap, scrapDocumentNumber, utcNow, ct);

            var scrapTxn = InventoryTransaction.Create(
                organizationId,
                qi.SiteId,
                qi.WarehouseId,
                null,
                docNum,
                InventoryTransactionType.QualityScrap,
                qi.ProductionOrderId,
                null,
                notes ?? qi.DefectReasonCode,
                null,
                utcNow,
                qi.Id);

            db.InventoryTransactions.Add(scrapTxn);
            await db.SaveInOperation(access.Actor, ct);

            db.InventoryLedgerEntries.Add(InventoryLedgerEntry.Create(
                organizationId,
                scrapTxn.Id,
                qi.SiteId,
                qi.WarehouseId,
                1,
                qi.ItemId,
                qi.LotCode,
                -rejectBaseQuantity,
                qi.BaseUnitId,
                -rejectBaseQuantity,
                qi.BaseUnitId,
                utcNow,
                notes ?? qi.DefectReasonCode));
        }

        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<QualityInspectionDetails>.Success(
            (await BuildInspectionDetailsAsync(db, organizationId, qi.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> CancelHold(
        long organizationId,
        long inspectionId,
        byte[] version,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualityInspectionDetails>.Failure(orgCheck.Error!);

        var qi = await db.QualityInspections
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == inspectionId, ct);
        if (qi is null) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        if (!Matches(db, qi, version)) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);

        if (qi.RemainingBlockedBaseQuantity > 0m)
        {
            var bal = await db.InventoryBalances
                .FromSqlInterpolated(
                    $"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [WarehouseId] = {qi.WarehouseId} AND [ItemId] = {qi.ItemId} AND [LotCode] = {qi.LotCode}")
                .SingleOrDefaultAsync(ct);
            if (bal is null || bal.BlockedBaseQuantity < qi.RemainingBlockedBaseQuantity)
                return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InsufficientAvailableStock);
            bal.ReleaseBlockedQuantity(qi.RemainingBlockedBaseQuantity, utcNow);
        }

        qi.Cancel(notes, access.Actor.ActorId, utcNow);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<QualityInspectionDetails>.Success(
            (await BuildInspectionDetailsAsync(db, organizationId, qi.Id, ct))!);
    }, ct);

    public Task<OrganizationPage<ItemLotSummary>> ListItemLots(
        long organizationId,
        ItemLotListQuery query,
        DateOnly today,
        CancellationToken ct) => Read(async db =>
    {
        var soonCutoff = today.AddDays(SoonDays);
        var baseQuery = from lot in db.ItemLots.AsNoTracking()
                        join it in db.Items.AsNoTracking() on new { lot.OrganizationId, ItemId = lot.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                        where lot.OrganizationId == organizationId
                        select new { lot, it, RowVersion = EF.Property<byte[]>(lot, "RowVersion") };

        if (query.ItemId is long itemId && itemId > 0)
            baseQuery = baseQuery.Where(x => x.lot.ItemId == itemId);
        if (query.OnlyExpiredOrExpiring)
            baseQuery = baseQuery.Where(x => x.lot.ExpirationDate != null && x.lot.ExpirationDate <= soonCutoff);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.lot.LotCode.Contains(query.Search)
                || x.it.Code.Contains(query.Search)
                || x.it.Name.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderBy(x => x.lot.ExpirationDate == null ? 1 : 0)
            .ThenBy(x => x.lot.ExpirationDate)
            .ThenBy(x => x.it.Code)
            .ThenBy(x => x.lot.LotCode)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new ItemLotSummary(
            r.lot.Id,
            r.lot.OrganizationId,
            r.lot.ItemId,
            r.it.Code,
            r.it.Name,
            r.lot.LotCode,
            r.lot.ManufactureDate,
            r.lot.ExpirationDate,
            r.lot.ExpirationDate.HasValue && r.lot.ExpirationDate.Value < today,
            r.lot.ExpirationDate.HasValue && r.lot.ExpirationDate.Value >= today && r.lot.ExpirationDate.Value <= soonCutoff,
            r.lot.Notes,
            Convert.ToBase64String(r.RowVersion))).ToArray();

        return new OrganizationPage<ItemLotSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<OrganizationResult<ItemLotSummary>> UpsertItemLotMetadata(
        UpsertItemLotMetadataRequest request,
        DateOnly today,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<ItemLotSummary>.Failure(orgCheck.Error!);

        var item = await db.Items.AsNoTracking()
            .SingleOrDefaultAsync(i => i.OrganizationId == request.OrganizationId && i.Id == request.ItemId, ct);
        if (item is null) return OrganizationResult<ItemLotSummary>.Failure(QualityErrors.ItemInvalid);

        var lot = await db.ItemLots
            .SingleOrDefaultAsync(l => l.OrganizationId == request.OrganizationId && l.ItemId == request.ItemId && l.LotCode == request.LotCode, ct);

        if (lot is null)
        {
            lot = ItemLot.Create(request.OrganizationId, request.ItemId, request.LotCode, request.ManufactureDate, request.ExpirationDate, request.Notes);
            db.ItemLots.Add(lot);
            await db.SaveInOperation(access.Actor, ct);
        }
        else
        {
            if (!string.IsNullOrEmpty(request.ExpectedRowVersion))
            {
                byte[] version;
                try { version = Convert.FromBase64String(request.ExpectedRowVersion); }
                catch (FormatException) { return OrganizationResult<ItemLotSummary>.Failure(QualityErrors.ConcurrencyConflict); }
                if (version.Length != 8 || !Matches(db, lot, version))
                    return OrganizationResult<ItemLotSummary>.Failure(QualityErrors.ConcurrencyConflict);
            }

            if (lot.UpdateMetadata(request.ManufactureDate, request.ExpirationDate, request.Notes))
                await db.SaveInOperation(access.Actor, ct);
        }

        var rowVersion = db.Entry(lot).Property<byte[]>("RowVersion").CurrentValue!;
        var soonCutoff = today.AddDays(SoonDays);
        return OrganizationResult<ItemLotSummary>.Success(new ItemLotSummary(
            lot.Id,
            lot.OrganizationId,
            lot.ItemId,
            item.Code,
            item.Name,
            lot.LotCode,
            lot.ManufactureDate,
            lot.ExpirationDate,
            lot.ExpirationDate.HasValue && lot.ExpirationDate.Value < today,
            lot.ExpirationDate.HasValue && lot.ExpirationDate.Value >= today && lot.ExpirationDate.Value <= soonCutoff,
            lot.Notes,
            Convert.ToBase64String(rowVersion)));
    }, ct);

    public Task<LotGenealogyDataset> GetGenealogyDataset(
        long organizationId,
        long? forcedSiteId,
        DateOnly today,
        CancellationToken ct) => Read(async db =>
    {
        var soonCutoff = today.AddDays(SoonDays);

        var orderQuery = from po in db.ProductionOrders.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { po.OrganizationId, SiteId = po.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         where po.OrganizationId == organizationId
                         select new { po, SiteCode = s.Code };
        if (forcedSiteId is long sid && sid > 0)
            orderQuery = orderQuery.Where(x => x.po.SiteId == sid);

        var orders = await orderQuery
            .Select(x => new LotGenealogyOrderRow(x.po.Id, x.po.OrderNumber, x.po.SiteId, x.SiteCode, x.po.Status))
            .ToListAsync(ct);

        var moveQuery = from l in db.InventoryLedgerEntries.AsNoTracking()
                        join tx in db.InventoryTransactions.AsNoTracking() on new { l.OrganizationId, TxId = l.InventoryTransactionId } equals new { tx.OrganizationId, TxId = tx.Id }
                        join s in db.Sites.AsNoTracking() on new { l.OrganizationId, SiteId = l.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        join w in db.Warehouses.AsNoTracking() on new { l.OrganizationId, WarehouseId = l.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                        join it in db.Items.AsNoTracking() on new { l.OrganizationId, ItemId = l.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                        join bu in db.Units.AsNoTracking() on new { l.OrganizationId, UnitId = l.BaseUnitId } equals new { bu.OrganizationId, UnitId = bu.Id }
                        join po in db.ProductionOrders.AsNoTracking() on new { tx.OrganizationId, PoId = tx.ProductionOrderId } equals new { po.OrganizationId, PoId = (long?)po.Id } into poJoin
                        from po in poJoin.DefaultIfEmpty()
                        where l.OrganizationId == organizationId && l.LotCode != ""
                        select new { l, tx, s, w, it, bu, PoNumber = po != null ? po.OrderNumber : null };
        if (forcedSiteId is long mSid && mSid > 0)
            moveQuery = moveQuery.Where(x => x.l.SiteId == mSid);

        var movements = await moveQuery
            .Select(x => new LotGenealogyMovementRow(
                x.tx.Id,
                x.tx.TransactionType,
                x.tx.ReversedTransactionId,
                x.tx.QualityInspectionId,
                x.tx.ProductionOrderId,
                x.PoNumber,
                x.l.SiteId,
                x.s.Code,
                x.l.WarehouseId,
                x.w.Code,
                x.tx.DocumentNumber,
                x.l.ItemId,
                x.it.Code,
                x.it.Name,
                x.l.LotCode,
                x.l.BaseQuantityDelta,
                x.bu.Symbol,
                x.l.OccurredAtUtc,
                x.l.Notes))
            .ToListAsync(ct);

        var lots = await (from lot in db.ItemLots.AsNoTracking()
                          join it in db.Items.AsNoTracking() on new { lot.OrganizationId, ItemId = lot.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                          where lot.OrganizationId == organizationId
                          select new { lot, it, RowVersion = EF.Property<byte[]>(lot, "RowVersion") })
            .ToListAsync(ct);
        var lotSummaries = lots.Select(r => new ItemLotSummary(
            r.lot.Id, r.lot.OrganizationId, r.lot.ItemId, r.it.Code, r.it.Name, r.lot.LotCode,
            r.lot.ManufactureDate, r.lot.ExpirationDate,
            r.lot.ExpirationDate.HasValue && r.lot.ExpirationDate.Value < today,
            r.lot.ExpirationDate.HasValue && r.lot.ExpirationDate.Value >= today && r.lot.ExpirationDate.Value <= soonCutoff,
            r.lot.Notes, Convert.ToBase64String(r.RowVersion))).ToArray();

        var balQuery = from bal in db.InventoryBalances.AsNoTracking()
                       join s in db.Sites.AsNoTracking() on new { bal.OrganizationId, SiteId = bal.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                       join w in db.Warehouses.AsNoTracking() on new { bal.OrganizationId, WarehouseId = bal.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                       join it in db.Items.AsNoTracking() on new { bal.OrganizationId, ItemId = bal.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                       join u in db.Units.AsNoTracking() on new { bal.OrganizationId, UnitId = bal.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                       where bal.OrganizationId == organizationId && bal.LotCode != ""
                       select new { bal, s, w, it, u, RowVersion = EF.Property<byte[]>(bal, "RowVersion") };
        if (forcedSiteId is long bSid && bSid > 0)
            balQuery = balQuery.Where(x => x.bal.SiteId == bSid);

        var balances = (await balQuery.ToListAsync(ct)).Select(r => new InventoryBalanceSummary(
            r.bal.Id, r.bal.OrganizationId, r.bal.SiteId, r.s.Code, r.s.Name,
            r.bal.WarehouseId, r.w.Code, r.w.Name,
            r.bal.ItemId, r.it.Code, r.it.Name, r.bal.LotCode,
            r.bal.BaseUnitId, r.u.Code, r.u.Symbol,
            r.bal.OnHandBaseQuantity, r.bal.LastMovedAtUtc,
            Convert.ToBase64String(r.RowVersion),
            r.bal.BlockedBaseQuantity,
            decimal.Round(r.bal.OnHandBaseQuantity - r.bal.BlockedBaseQuantity, 6, MidpointRounding.AwayFromZero))).ToArray();

        var qiQuery = from qi in db.QualityInspections.AsNoTracking()
                      join s in db.Sites.AsNoTracking() on new { qi.OrganizationId, SiteId = qi.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                      join w in db.Warehouses.AsNoTracking() on new { qi.OrganizationId, WarehouseId = qi.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                      join it in db.Items.AsNoTracking() on new { qi.OrganizationId, ItemId = qi.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                      join u in db.Units.AsNoTracking() on new { qi.OrganizationId, UnitId = qi.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                      join tx in db.InventoryTransactions.AsNoTracking() on new { qi.OrganizationId, TxId = qi.SourceTransactionId } equals new { tx.OrganizationId, TxId = (long?)tx.Id } into txJoin
                      from tx in txJoin.DefaultIfEmpty()
                      join po in db.ProductionOrders.AsNoTracking() on new { qi.OrganizationId, PoId = qi.ProductionOrderId } equals new { po.OrganizationId, PoId = (long?)po.Id } into poJoin
                      from po in poJoin.DefaultIfEmpty()
                      where qi.OrganizationId == organizationId && qi.LotCode != ""
                      select new
                      {
                          qi, s, w, it, u,
                          SourceDoc = tx != null ? tx.DocumentNumber : null,
                          PoNumber = po != null ? po.OrderNumber : null,
                          RowVersion = EF.Property<byte[]>(qi, "RowVersion")
                      };
        if (forcedSiteId is long qSid && qSid > 0)
            qiQuery = qiQuery.Where(x => x.qi.SiteId == qSid);

        var inspections = (await qiQuery.ToListAsync(ct)).Select(r => new QualityInspectionSummary(
            r.qi.Id, r.qi.OrganizationId, r.qi.SiteId, r.s.Code, r.s.Name,
            r.qi.WarehouseId, r.w.Code, r.w.Name,
            r.qi.ItemId, r.it.Code, r.it.Name, r.qi.LotCode,
            r.qi.InspectionNumber, r.qi.SourceType, r.qi.SourceTransactionId, r.SourceDoc,
            r.qi.ProductionOrderId, r.PoNumber,
            r.qi.BaseUnitId, r.u.Code, r.u.Symbol,
            r.qi.InspectedBaseQuantity, r.qi.AcceptedBaseQuantity, r.qi.RejectedBaseQuantity, r.qi.RemainingBlockedBaseQuantity,
            r.qi.Status, r.qi.DefectReasonCode, r.qi.Notes,
            r.qi.InspectedAtUtc, r.qi.InspectedByActorId, null,
            Convert.ToBase64String(r.RowVersion),
            r.qi.CertificateNumber)).ToArray();

        return new LotGenealogyDataset(orders, movements, lotSummaries, balances, inspections);
    }, ct);

    private static async Task<IReadOnlyList<UnitConversionEdge>> LoadItemUnitEdgesInternalAsync(
        OrganizationDbContext db,
        long organizationId,
        long itemId,
        CancellationToken ct)
    {
        var globalEdges = await db.UnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.IsActive)
            .Select(c => new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor))
            .ToListAsync(ct);

        var itemEdges = await db.ItemUnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.ItemId == itemId && c.IsActive)
            .Select(c => new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor))
            .ToListAsync(ct);

        return itemEdges.Concat(globalEdges).ToArray();
    }

    private Task<QualityInspectionDetails?> BuildInspectionDetailsAsync(
        OrganizationDbContext db,
        long organizationId,
        long id,
        CancellationToken ct) =>
        BuildInspectionDetailsStaticAsync(db, organizationId, id, scopeFactory, ct);

    internal static async Task<QualityInspectionDetails?> BuildInspectionDetailsStaticAsync(
        OrganizationDbContext db,
        long organizationId,
        long id,
        IServiceScopeFactory? scopeFactory,
        CancellationToken ct)
    {
        var row = await (from qi in db.QualityInspections.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { qi.OrganizationId, SiteId = qi.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         join w in db.Warehouses.AsNoTracking() on new { qi.OrganizationId, WarehouseId = qi.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                         join it in db.Items.AsNoTracking() on new { qi.OrganizationId, ItemId = qi.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                         join u in db.Units.AsNoTracking() on new { qi.OrganizationId, UnitId = qi.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                         join tx in db.InventoryTransactions.AsNoTracking() on new { qi.OrganizationId, TxId = qi.SourceTransactionId } equals new { tx.OrganizationId, TxId = (long?)tx.Id } into txJoin
                         from tx in txJoin.DefaultIfEmpty()
                         join po in db.ProductionOrders.AsNoTracking() on new { qi.OrganizationId, PoId = qi.ProductionOrderId } equals new { po.OrganizationId, PoId = (long?)po.Id } into poJoin
                         from po in poJoin.DefaultIfEmpty()
                         where qi.OrganizationId == organizationId && qi.Id == id
                         select new
                         {
                             qi,
                             s,
                             w,
                             it,
                             u,
                             SourceDoc = tx != null ? tx.DocumentNumber : null,
                             PoNumber = po != null ? po.OrderNumber : null,
                             RowVersion = EF.Property<byte[]>(qi, "RowVersion")
                         }).SingleOrDefaultAsync(ct);
        if (row is null) return null;

        var lines = await db.QualityInspectionLines.AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && l.QualityInspectionId == id)
            .OrderBy(l => l.LineNumber)
            .Select(l => new QualityInspectionLineSummary(
                l.Id,
                l.LineNumber,
                l.ParameterName,
                l.ExpectedSpec,
                l.MeasuredValue,
                l.IsPassed,
                l.Notes,
                l.QualityParameterId,
                l.ParameterCodeSnapshot,
                l.SymbolSnapshot,
                l.UnitSymbolSnapshot,
                l.TestMethodSnapshot,
                l.DataType,
                l.MinValueSnapshot,
                l.MaxValueSnapshot,
                l.TargetValueSnapshot,
                l.MeasuredNumericValue,
                l.IsMandatory))
            .ToListAsync(ct);

        var actorNames = await ResolveUserNamesStaticAsync(scopeFactory, [row.qi.InspectedByActorId], ct);
        var summary = new QualityInspectionSummary(
            row.qi.Id, row.qi.OrganizationId, row.qi.SiteId, row.s.Code, row.s.Name,
            row.qi.WarehouseId, row.w.Code, row.w.Name,
            row.qi.ItemId, row.it.Code, row.it.Name, row.qi.LotCode,
            row.qi.InspectionNumber, row.qi.SourceType, row.qi.SourceTransactionId, row.SourceDoc,
            row.qi.ProductionOrderId, row.PoNumber,
            row.qi.BaseUnitId, row.u.Code, row.u.Symbol,
            row.qi.InspectedBaseQuantity, row.qi.AcceptedBaseQuantity, row.qi.RejectedBaseQuantity, row.qi.RemainingBlockedBaseQuantity,
            row.qi.Status, row.qi.DefectReasonCode, row.qi.Notes,
            row.qi.InspectedAtUtc, row.qi.InspectedByActorId,
            row.qi.InspectedByActorId.HasValue && actorNames.TryGetValue(row.qi.InspectedByActorId.Value, out var name) ? name : null,
            Convert.ToBase64String(row.RowVersion),
            row.qi.CertificateNumber);

        return new QualityInspectionDetails(summary, lines);
    }

    private Task<Dictionary<long, string>> ResolveUserNamesAsync(IEnumerable<long?> userIds, CancellationToken ct) =>
        ResolveUserNamesStaticAsync(scopeFactory, userIds, ct);

    internal static async Task<Dictionary<long, string>> ResolveUserNamesStaticAsync(
        IServiceScopeFactory? scopeFactory,
        IEnumerable<long?> userIds,
        CancellationToken ct)
    {
        if (scopeFactory is null) return [];
        var ids = userIds.Where(x => x is > 0).Select(x => x!.Value).Distinct().ToArray();
        if (ids.Length == 0) return [];

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var authDb = scope.ServiceProvider.GetService<AuthDbContext>();
            if (authDb is null) return [];

            return await authDb.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.UserName ?? string.Empty })
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        }
        catch
        {
            return [];
        }
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
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlQualityOperationsStore));
            throw new QualityStoreUnavailableException(ex);
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
                    return OrganizationResult<T>.Failure(QualityErrors.AccessDenied);

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
                    return OrganizationResult<T>.Failure(QualityErrors.ConcurrencyConflict);
                }
                catch (DbUpdateException ex) when (IsUniqueConflict(ex))
                {
                    return OrganizationResult<T>.Failure(ClassifyUniqueConflict(ex));
                }
                catch (DbUpdateException ex) when (IsCheckViolation(ex))
                {
                    return OrganizationResult<T>.Failure(QualityErrors.InsufficientAvailableStock);
                }
                catch (OrganizationAuditWriteException)
                {
                    return OrganizationResult<T>.Failure(QualityErrors.AuditUnavailable);
                }
                catch (OrganizationCommitOutcomeUnknownException)
                {
                    return OrganizationResult<T>.Failure(QualityErrors.OutcomeUnknown);
                }
                catch (InvalidOperationException ex) when (ex.Message == QualityErrors.ParentOrganizationInactive)
                {
                    return OrganizationResult<T>.Failure(QualityErrors.ParentOrganizationInactive);
                }
                catch (InvalidOperationException ex) when (ex.Message == OrganizationAccessErrors.AccessDenied)
                {
                    return OrganizationResult<T>.Failure(QualityErrors.AccessDenied);
                }
                catch (Exception ex) when (commitAttempted && Unavailable(ex))
                {
                    return OrganizationResult<T>.Failure(QualityErrors.OutcomeUnknown);
                }
            });
        }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(QualityErrors.InsufficientAvailableStock, ex.Parameters); }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlQualityOperationsStore), access.Actor.ActorId);
            return OrganizationResult<T>.Failure(QualityErrors.PersistenceUnavailable);
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
            return (false, exists ? QualityErrors.ParentOrganizationInactive : QualityErrors.AccessDenied);
        }
        return (true, null);
    }

    private static bool Matches(OrganizationDbContext db, object entity, byte[] version)
        => ((byte[])db.Entry(entity).Property("RowVersion").CurrentValue!).SequenceEqual(version);

    private static bool IsUniqueConflict(DbUpdateException ex)
        => ex.InnerException is SqlException sql && sql.Number is 2601 or 2627;

    private static bool IsCheckViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql && sql.Number == 547 && (sql.Message.Contains("CK_InventoryBalance_Blocked", StringComparison.Ordinal) || sql.Message.Contains("CK_InventoryBalance_NonNegative", StringComparison.Ordinal));

    private static string ClassifyUniqueConflict(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? string.Empty;
        if (msg.Contains("UX_QualityInspection_Org_Number", StringComparison.Ordinal)) return QualityErrors.InspectionNumberDuplicate;
        if (msg.Contains("UX_InventoryTransaction_Org_DocNum", StringComparison.Ordinal)) return QualityErrors.InspectionNumberDuplicate;
        return QualityErrors.ConcurrencyConflict;
    }

    private static bool Unavailable(Exception ex)
        => ex is SqlException or TimeoutException or DbUpdateException or InvalidOperationException { InnerException: SqlException };
}
