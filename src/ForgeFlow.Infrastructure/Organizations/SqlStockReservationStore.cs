using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlStockReservationStore(
    Func<IDbContextFactory<OrganizationDbContext>> getFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null)
    : IStockReservationStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public SqlStockReservationStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public Task<OrganizationPage<StockReservationSummary>> ListReservations(
        long organizationId,
        ReservationListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = BaseReservationQuery(db, organizationId);

        if (query.SiteId is long siteId && siteId > 0)
            baseQuery = baseQuery.Where(x => x.sr.SiteId == siteId);
        if (query.SourceKind is ReservationSourceKind sk)
            baseQuery = baseQuery.Where(x => x.sr.SourceKind == sk);
        if (query.ProductionOrderId is long poId && poId > 0)
            baseQuery = baseQuery.Where(x => x.sr.ProductionOrderId == poId);
        if (query.SalesOrderId is long soId && soId > 0)
            baseQuery = baseQuery.Where(x => x.sr.SalesOrderId == soId);
        if (query.WarehouseId is long whId && whId > 0)
            baseQuery = baseQuery.Where(x => x.sr.WarehouseId == whId);
        if (query.ItemId is long itemId && itemId > 0)
            baseQuery = baseQuery.Where(x => x.sr.ItemId == itemId);
        if (query.Status is ReservationStatus st)
            baseQuery = baseQuery.Where(x => x.sr.Status == st);
        else if (query.OpenOnly)
            baseQuery = baseQuery.Where(x => x.sr.Status == ReservationStatus.Active || x.sr.Status == ReservationStatus.PartiallyConsumed);

        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.OrderNumber.Contains(query.Search)
                || x.it.Code.Contains(query.Search)
                || x.it.Name.Contains(query.Search)
                || x.w.Code.Contains(query.Search)
                || x.sr.LotCode.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderByDescending(x => x.sr.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => ToSummary(r.sr, r.s, r.w, r.OrderNumber, r.it, r.u, r.CreatedAtUtc, r.RowVersion)).ToArray();
        return new OrganizationPage<StockReservationSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<IReadOnlyList<StockReservationSummary>> ListOrderReservations(
        long organizationId,
        long productionOrderId,
        CancellationToken ct) => Read<IReadOnlyList<StockReservationSummary>>(async db =>
    {
        var rows = await BaseReservationQuery(db, organizationId)
            .Where(x => x.sr.ProductionOrderId == productionOrderId)
            .OrderBy(x => x.sr.Status)
            .ThenBy(x => x.sr.Id)
            .ToListAsync(ct);

        return rows.Select(r => ToSummary(r.sr, r.s, r.w, r.OrderNumber, r.it, r.u, r.CreatedAtUtc, r.RowVersion)).ToArray();
    }, ct);

    public Task<IReadOnlyList<StockReservationSummary>> ListSalesOrderReservations(
        long organizationId,
        long salesOrderId,
        CancellationToken ct) => Read<IReadOnlyList<StockReservationSummary>>(async db =>
    {
        var rows = await BaseReservationQuery(db, organizationId)
            .Where(x => x.sr.SourceKind == ReservationSourceKind.SalesOrder && x.sr.SalesOrderId == salesOrderId)
            .OrderBy(x => x.sr.Status)
            .ThenBy(x => x.sr.Id)
            .ToListAsync(ct);

        return rows.Select(r => ToSummary(r.sr, r.s, r.w, r.OrderNumber, r.it, r.u, r.CreatedAtUtc, r.RowVersion)).ToArray();
    }, ct);

    public Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveSalesOrderStock(
        ReserveSalesOrderStockRequest request,
        decimal requestedBaseQuantity,
        long baseUnitId,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[SalesOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {request.OrganizationId} AND [Id] = {request.SalesOrderId}", ct);

        var so = await db.SalesOrders.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.Id == request.SalesOrderId, ct);
        if (so is null) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);
        if (so.Status is not (Domain.Sales.SalesOrderStatus.Approved or Domain.Sales.SalesOrderStatus.PartiallyDispatched))
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ReservationNotAllowedForOrderState);

        if (request.SalesOrderLineId is > 0 && !so.Lines.Any(l => l.Id == request.SalesOrderLineId.Value && l.ItemId == request.ItemId))
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ItemInvalid);

        if (request.WarehouseId is long whFilter && whFilter > 0)
        {
            var wh = await db.Warehouses.AsNoTracking()
                .SingleOrDefaultAsync(w => w.OrganizationId == request.OrganizationId && w.Id == whFilter && w.IsActive, ct);
            if (wh is null || wh.SiteId != so.SiteId)
                return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.WarehouseNotFound);
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {request.OrganizationId} AND [SiteId] = {so.SiteId} AND [ItemId] = {request.ItemId}", ct);

        var balQuery = db.InventoryBalances
            .Where(b => b.OrganizationId == request.OrganizationId
                        && b.SiteId == so.SiteId
                        && b.ItemId == request.ItemId
                        && (b.OnHandBaseQuantity - b.BlockedBaseQuantity - b.ReservedBaseQuantity) > 0m);

        if (request.WarehouseId is > 0)
            balQuery = balQuery.Where(b => b.WarehouseId == request.WarehouseId.Value);
        if (!string.IsNullOrEmpty(request.LotCode))
            balQuery = balQuery.Where(b => b.LotCode == request.LotCode);

        var candidates = await balQuery
            .OrderBy(b => b.WarehouseId).ThenBy(b => b.LotCode)
            .ToListAsync(ct);

        var totalAvailable = candidates.Sum(b => b.AvailableBaseQuantity);
        if (totalAvailable < requestedBaseQuantity)
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(
                InventoryErrors.ReservationExceedsAvailable,
                new Dictionary<string, object>
                {
                    ["available"] = totalAvailable,
                    ["requested"] = requestedBaseQuantity
                });

        var remainingToReserve = requestedBaseQuantity;
        var created = new List<StockReservation>();
        foreach (var bal in candidates)
        {
            if (remainingToReserve <= 0m) break;
            var take = Math.Min(remainingToReserve, bal.AvailableBaseQuantity);
            if (take <= 0m) continue;

            bal.ReserveQuantity(take, utcNow);
            var sr = StockReservation.CreateForSalesOrder(
                request.OrganizationId,
                so.SiteId,
                bal.WarehouseId,
                so.Id,
                request.SalesOrderLineId,
                request.ItemId,
                bal.LotCode,
                baseUnitId,
                take,
                request.Notes);
            db.StockReservations.Add(sr);
            created.Add(sr);
            remainingToReserve = decimal.Round(remainingToReserve - take, 6, MidpointRounding.AwayFromZero);
        }

        await db.SaveInOperation(access.Actor, ct);

        var createdIds = created.Select(x => x.Id).ToArray();
        var summaries = await BaseReservationQuery(db, request.OrganizationId)
            .Where(x => createdIds.Contains(x.sr.Id))
            .OrderBy(x => x.sr.Id)
            .ToListAsync(ct);

        return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Success(
            summaries.Select(r => ToSummary(r.sr, r.s, r.w, r.OrderNumber, r.it, r.u, r.CreatedAtUtc, r.RowVersion)).ToArray());
    }, ct);

    public Task<StockReservationSummary?> GetReservation(long organizationId, long reservationId, CancellationToken ct) =>
        Read(db => GetReservationInDbAsync(db, organizationId, reservationId, ct), ct);

    public Task<OrganizationResult<IReadOnlyList<StockReservationSummary>>> ReserveStock(
        ReserveStockRequest request,
        decimal requestedBaseQuantity,
        long baseUnitId,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(orgCheck.Error!);

        var po = await db.ProductionOrders
            .FromSqlInterpolated($"SELECT * FROM [organization].[ProductionOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {request.OrganizationId} AND [Id] = {request.ProductionOrderId}")
            .SingleOrDefaultAsync(ct);
        if (po is null) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ProductionOrderInvalid);
        if (po.Status is not (ProductionOrderStatus.Released or ProductionOrderStatus.InProgress))
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.ReservationNotAllowedForOrderState);

        var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == po.SiteId, ct);
        if (site is null || !site.IsActive) return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.SiteInactive);

        if (request.WarehouseId is > 0)
        {
            var wh = await db.Warehouses.AsNoTracking().SingleOrDefaultAsync(
                w => w.OrganizationId == request.OrganizationId && w.Id == request.WarehouseId.Value, ct);
            if (wh is null || wh.SiteId != po.SiteId)
                return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.WarehouseNotFound);
            if (!wh.IsActive)
                return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(InventoryErrors.WarehouseInactive);
        }

        // Lock all candidate positive balances on this (Organization, Site, Item) deterministically to prevent allocation races.
        var balancesQuery = db.InventoryBalances
            .FromSqlInterpolated($"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {request.OrganizationId} AND [SiteId] = {po.SiteId} AND [ItemId] = {request.ItemId} AND [OnHandBaseQuantity] > ([BlockedBaseQuantity] + [ReservedBaseQuantity])");

        var allLockedBalances = await balancesQuery.OrderBy(b => b.WarehouseId).ThenBy(b => b.Id).ToListAsync(ct);

        var activeWarehouseIds = (await db.Warehouses.AsNoTracking()
            .Where(w => w.OrganizationId == request.OrganizationId && w.SiteId == po.SiteId && w.IsActive)
            .Select(w => w.Id)
            .ToListAsync(ct)).ToHashSet();

        var today = DateOnly.FromDateTime(utcNow);
        var lotExp = await db.ItemLots.AsNoTracking()
            .Where(l => l.OrganizationId == request.OrganizationId && l.ItemId == request.ItemId)
            .Select(l => new { l.LotCode, l.ExpirationDate })
            .ToDictionaryAsync(l => l.LotCode, l => l.ExpirationDate, StringComparer.Ordinal, ct);

        var candidates = allLockedBalances
            .Where(b => activeWarehouseIds.Contains(b.WarehouseId))
            .Where(b => !request.WarehouseId.HasValue || request.WarehouseId.Value <= 0 || b.WarehouseId == request.WarehouseId.Value)
            .Where(b => request.LotCode is null || string.Equals(b.LotCode, request.LotCode, StringComparison.Ordinal))
            .Where(b => string.IsNullOrEmpty(b.LotCode) || !lotExp.TryGetValue(b.LotCode, out var exp) || !exp.HasValue || exp.Value >= today)
            .OrderBy(b => lotExp.TryGetValue(b.LotCode, out var exp) && exp.HasValue ? exp.Value : DateOnly.MaxValue)
            .ThenBy(b => b.LastMovedAtUtc)
            .ThenBy(b => b.Id)
            .ToList();

        var totalAvailable = candidates.Sum(b => b.AvailableBaseQuantity);
        if (totalAvailable < requestedBaseQuantity)
            return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Failure(
                InventoryErrors.ReservationExceedsAvailable,
                new Dictionary<string, object> { ["available"] = totalAvailable, ["requested"] = requestedBaseQuantity });

        var remaining = requestedBaseQuantity;
        var createdEntities = new List<StockReservation>();
        foreach (var bal in candidates)
        {
            if (remaining <= 0m) break;
            var free = bal.AvailableBaseQuantity;
            if (free <= 0m) continue;

            var allocate = Math.Min(remaining, free);
            bal.ReserveQuantity(allocate, utcNow);
            var sr = StockReservation.Create(
                request.OrganizationId,
                po.SiteId,
                bal.WarehouseId,
                po.Id,
                request.ProductionOrderInputId,
                request.ItemId,
                bal.LotCode,
                baseUnitId,
                allocate,
                request.Notes);
            db.StockReservations.Add(sr);
            createdEntities.Add(sr);
            remaining = decimal.Round(remaining - allocate, 6, MidpointRounding.AwayFromZero);
        }

        await db.SaveInOperation(access.Actor, ct);

        var createdIds = createdEntities.Select(e => e.Id).ToHashSet();
        var orderRows = await BaseReservationQuery(db, request.OrganizationId)
            .Where(x => x.sr.ProductionOrderId == po.Id && createdIds.Contains(x.sr.Id))
            .OrderBy(x => x.sr.Id)
            .ToListAsync(ct);

        var result = orderRows
            .Select(r => ToSummary(r.sr, r.s, r.w, r.OrderNumber, r.it, r.u, r.CreatedAtUtc, r.RowVersion))
            .ToArray();
        return OrganizationResult<IReadOnlyList<StockReservationSummary>>.Success(result);
    }, ct);

    public Task<OrganizationResult<StockReservationSummary>> ReleaseReservation(
        long organizationId,
        long reservationId,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<StockReservationSummary>.Failure(orgCheck.Error!);

        var sr = await db.StockReservations
            .FromSqlInterpolated($"SELECT * FROM [organization].[StockReservation] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {reservationId}")
            .SingleOrDefaultAsync(ct);
        if (sr is null) return OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ReservationNotFound);
        if (!Matches(db, sr, version)) return OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ConcurrencyConflict);
        if (!sr.IsOpen) return OrganizationResult<StockReservationSummary>.Failure(InventoryErrors.ReservationInvalidState);

        var releasedQty = sr.Release(utcNow);
        if (releasedQty > 0m)
        {
            var bal = await db.InventoryBalances
                .FromSqlInterpolated($"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [WarehouseId] = {sr.WarehouseId} AND [ItemId] = {sr.ItemId} AND [LotCode] = {sr.LotCode}")
                .SingleOrDefaultAsync(ct);
            bal?.ReleaseReservedQuantity(releasedQty, utcNow);
        }

        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<StockReservationSummary>.Success((await GetReservationInDbAsync(db, organizationId, sr.Id, ct))!);
    }, ct);

    internal static async Task ConsumeForProductionIssueInDbAsync(
        OrganizationDbContext db,
        long organizationId,
        long productionOrderId,
        InventoryBalance balance,
        decimal issueBaseQuantity,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (balance.ReservedBaseQuantity <= 0m || issueBaseQuantity <= 0m) return;

        var openReservations = await db.StockReservations
            .FromSqlInterpolated($"SELECT * FROM [organization].[StockReservation] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [ProductionOrderId] = {productionOrderId} AND [WarehouseId] = {balance.WarehouseId} AND [ItemId] = {balance.ItemId} AND [LotCode] = {balance.LotCode} AND [Status] IN (1, 2)")
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var remaining = issueBaseQuantity;
        foreach (var sr in openReservations)
        {
            if (remaining <= 0m) break;
            var consumed = sr.Consume(remaining);
            if (consumed > 0m)
            {
                balance.ReleaseReservedQuantity(consumed, utcNow);
                remaining = decimal.Round(remaining - consumed, 6, MidpointRounding.AwayFromZero);
            }
        }
    }

    internal static async Task ReleaseAllForOrderInDbAsync(
        OrganizationDbContext db,
        long organizationId,
        long productionOrderId,
        DateTime utcNow,
        CancellationToken ct)
    {
        var openReservations = await db.StockReservations
            .FromSqlInterpolated($"SELECT * FROM [organization].[StockReservation] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [ProductionOrderId] = {productionOrderId} AND [Status] IN (1, 2)")
            .OrderBy(r => r.WarehouseId).ThenBy(r => r.ItemId).ThenBy(r => r.LotCode).ThenBy(r => r.Id)
            .ToListAsync(ct);

        foreach (var sr in openReservations)
        {
            var releasedQty = sr.Release(utcNow);
            if (releasedQty <= 0m) continue;

            var bal = await db.InventoryBalances
                .FromSqlInterpolated($"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [WarehouseId] = {sr.WarehouseId} AND [ItemId] = {sr.ItemId} AND [LotCode] = {sr.LotCode}")
                .SingleOrDefaultAsync(ct);
            bal?.ReleaseReservedQuantity(releasedQty, utcNow);
        }
    }

    private sealed class ReservationJoinRow
    {
        public StockReservation sr { get; init; } = null!;
        public Site s { get; init; } = null!;
        public Warehouse w { get; init; } = null!;
        public string OrderNumber { get; init; } = string.Empty;
        public Domain.Items.Item it { get; init; } = null!;
        public Domain.Units.Unit u { get; init; } = null!;
        public DateTime CreatedAtUtc { get; init; }
        public byte[] RowVersion { get; init; } = [];
    }

    private static IQueryable<ReservationJoinRow> BaseReservationQuery(OrganizationDbContext db, long organizationId) =>
        from sr in db.StockReservations.AsNoTracking()
        join s in db.Sites.AsNoTracking() on new { sr.OrganizationId, SiteId = sr.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
        join w in db.Warehouses.AsNoTracking() on new { sr.OrganizationId, WarehouseId = sr.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
        join po in db.ProductionOrders.AsNoTracking() on new { sr.OrganizationId, PoId = sr.ProductionOrderId } equals new { po.OrganizationId, PoId = (long?)po.Id } into poJoin
        from po in poJoin.DefaultIfEmpty()
        join so in db.SalesOrders.AsNoTracking() on new { sr.OrganizationId, SoId = sr.SalesOrderId } equals new { so.OrganizationId, SoId = (long?)so.Id } into soJoin
        from so in soJoin.DefaultIfEmpty()
        join it in db.Items.AsNoTracking() on new { sr.OrganizationId, ItemId = sr.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
        join u in db.Units.AsNoTracking() on new { sr.OrganizationId, UnitId = sr.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
        where sr.OrganizationId == organizationId
        select new ReservationJoinRow
        {
            sr = sr,
            s = s,
            w = w,
            OrderNumber = po != null ? po.OrderNumber : (so != null ? so.OrderNumber : string.Empty),
            it = it,
            u = u,
            CreatedAtUtc = EF.Property<DateTime>(sr, "CreatedAtUtc"),
            RowVersion = EF.Property<byte[]>(sr, "RowVersion")
        };

    private static async Task<StockReservationSummary?> GetReservationInDbAsync(
        OrganizationDbContext db,
        long organizationId,
        long reservationId,
        CancellationToken ct)
    {
        var r = await BaseReservationQuery(db, organizationId).SingleOrDefaultAsync(x => x.sr.Id == reservationId, ct);
        return r is null ? null : ToSummary(r.sr, r.s, r.w, r.OrderNumber, r.it, r.u, r.CreatedAtUtc, r.RowVersion);
    }

    private static StockReservationSummary ToSummary(
        StockReservation sr,
        Site s,
        Warehouse w,
        string orderNumber,
        Domain.Items.Item it,
        Domain.Units.Unit u,
        DateTime createdAtUtc,
        byte[] rowVersion) => new(
        sr.Id,
        sr.OrganizationId,
        sr.SiteId,
        s.Code,
        s.Name,
        sr.WarehouseId,
        w.Code,
        w.Name,
        sr.ProductionOrderId ?? 0L,
        orderNumber,
        sr.ProductionOrderInputId,
        sr.ItemId,
        it.Code,
        it.Name,
        sr.LotCode,
        sr.BaseUnitId,
        u.Code,
        u.Symbol,
        sr.ReservedBaseQuantity,
        sr.ConsumedBaseQuantity,
        sr.RemainingReservedBaseQuantity,
        sr.Status,
        sr.Notes,
        createdAtUtc,
        sr.ReleasedAtUtc,
        Convert.ToBase64String(rowVersion),
        sr.SourceKind,
        sr.SalesOrderId,
        sr.SalesOrderLineId);

    private async Task<T> Read<T>(Func<OrganizationDbContext, Task<T>> query, CancellationToken ct)
    {
        try
        {
            await using var db = await Factory.CreateDbContextAsync(ct);
            return await query(db);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlStockReservationStore));
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
                    if (!result.Succeeded) return result;
                    commitAttempted = true;
                    await transaction.CommitAsync(ct);
                    return result;
                }
                catch (Exception ex) when (commitAttempted && ex is not OrganizationCommitOutcomeUnknownException)
                {
                    throw new OrganizationCommitOutcomeUnknownException(ex);
                }
            });
        }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(InventoryErrors.ConcurrencyConflict); }
        catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(InventoryErrors.OutcomeUnknown); }
        catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
        { return OrganizationResult<T>.Failure(InventoryErrors.AuditUnavailable); }
        catch (InvalidOperationException ex) when (ex.Message == InventoryErrors.ParentOrganizationInactive)
        { return OrganizationResult<T>.Failure(InventoryErrors.ParentOrganizationInactive); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationSiteErrors.OrganizationNotFound)
        { return OrganizationResult<T>.Failure(InventoryErrors.ParentOrganizationInactive); }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlStockReservationStore), access.Actor.ActorId);
            return OrganizationResult<T>.Failure(InventoryErrors.PersistenceUnavailable);
        }
    }

    private static async Task<(bool Ok, string? Error)> LockAndRequireActiveOrganization(OrganizationDbContext db, long organizationId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {organizationId}", ct);
        var org = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null || !org.IsActive) return (false, InventoryErrors.ParentOrganizationInactive);
        return (true, null);
    }

    private static bool Matches(OrganizationDbContext db, object entity, byte[] version)
        => entity is ForgeFlow.Domain.Common.IHasRowVersion hasVersion && hasVersion.RowVersion.Length > 0
            ? hasVersion.RowVersion.SequenceEqual(version)
            : ((byte[])db.Entry(entity).Property("RowVersion").CurrentValue!).SequenceEqual(version);

    private static bool Unavailable(Exception exception) => exception switch
    {
        SqlException sql => sql.Number is -2 or 53 or 64 or 233 or 1205 or 4060 or 18456 or 40197 or 40501 or 40613,
        TimeoutException => true,
        DbUpdateException or RetryLimitExceededException when exception.InnerException is not null => Unavailable(exception.InnerException),
        InvalidOperationException when exception.Message == OrganizationAccessErrors.PersistenceUnavailable => true,
        _ => false
    };
}
