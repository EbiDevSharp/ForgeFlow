using System.Data;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlProcurementOperationsStore(
    Func<IDbContextFactory<OrganizationDbContext>> getFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null)
    : IProcurementOperationsStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public SqlProcurementOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public Task<OrganizationPage<SupplierSummary>> ListSuppliers(
        long organizationId,
        SupplierListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = db.Suppliers.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId);

        if (!query.IncludeInactive)
            baseQuery = baseQuery.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            baseQuery = baseQuery.Where(s =>
                s.Code.Contains(term)
                || s.Name.Contains(term)
                || (s.ContactName != null && s.ContactName.Contains(term))
                || (s.Phone != null && s.Phone.Contains(term))
                || (s.Email != null && s.Email.Contains(term)));
        }

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderBy(s => s.Code).ThenBy(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new
            {
                s,
                RowVersion = EF.Property<byte[]>(s, "RowVersion")
            })
            .ToListAsync(ct);

        var items = rows.Select(r => ToSupplierSummary(r.s, r.RowVersion)).ToArray();
        return new OrganizationPage<SupplierSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<SupplierSummary?> GetSupplier(
        long organizationId,
        long id,
        CancellationToken ct) => Read(async db =>
    {
        var row = await db.Suppliers.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.Id == id)
            .Select(s => new
            {
                s,
                RowVersion = EF.Property<byte[]>(s, "RowVersion")
            })
            .SingleOrDefaultAsync(ct);

        return row is null ? null : ToSupplierSummary(row.s, row.RowVersion);
    }, ct);

    public Task<OrganizationResult<SupplierSummary>> CreateSupplier(
        CreateSupplierRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<SupplierSummary>.Failure(orgCheck.Error!);

        var supplier = Supplier.Create(
            request.OrganizationId,
            request.Code,
            request.Name,
            request.ContactName,
            request.Phone,
            request.Email,
            request.Address,
            request.Notes);

        var duplicateCode = await db.Suppliers.AsNoTracking()
            .AnyAsync(s => s.OrganizationId == request.OrganizationId && s.Code == supplier.Code, ct);
        if (duplicateCode)
            return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierCodeDuplicate);

        db.Suppliers.Add(supplier);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<SupplierSummary>.Success(
            ToSupplierSummary(supplier, (byte[])db.Entry(supplier).Property("RowVersion").CurrentValue!));
    }, ct);

    public Task<OrganizationResult<SupplierSummary>> UpdateSupplier(
        long organizationId,
        long id,
        byte[] version,
        UpdateSupplierRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<SupplierSummary>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[Supplier] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {id}", ct);

        var supplier = await db.Suppliers
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == id, ct);
        if (supplier is null) return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound);
        if (!Matches(db, supplier, version)) return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.ConcurrencyConflict);

        if (supplier.Update(request.Name, request.ContactName, request.Phone, request.Email, request.Address, request.Notes))
            await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<SupplierSummary>.Success(
            ToSupplierSummary(supplier, (byte[])db.Entry(supplier).Property("RowVersion").CurrentValue!));
    }, ct);

    public Task<OrganizationResult<SupplierSummary>> SetSupplierActive(
        long organizationId,
        long id,
        byte[] version,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<SupplierSummary>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[Supplier] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {id}", ct);

        var supplier = await db.Suppliers
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == id, ct);
        if (supplier is null) return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound);
        if (!Matches(db, supplier, version)) return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.ConcurrencyConflict);

        if (supplier.SetActive(isActive))
            await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<SupplierSummary>.Success(
            ToSupplierSummary(supplier, (byte[])db.Entry(supplier).Property("RowVersion").CurrentValue!));
    }, ct);

    public Task<OrganizationPage<PurchaseOrderSummary>> ListPurchaseOrders(
        long organizationId,
        long? forcedSiteId,
        PurchaseOrderListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var effectiveSiteId = forcedSiteId ?? query.SiteId;

        var baseQuery = from po in db.PurchaseOrders.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { po.OrganizationId, SiteId = po.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        join sup in db.Suppliers.AsNoTracking() on new { po.OrganizationId, SupplierId = po.SupplierId } equals new { sup.OrganizationId, SupplierId = sup.Id }
                        join wh in db.Warehouses.AsNoTracking() on new { po.OrganizationId, WhId = po.TargetWarehouseId } equals new { wh.OrganizationId, WhId = (long?)wh.Id } into whJoin
                        from wh in whJoin.DefaultIfEmpty()
                        where po.OrganizationId == organizationId
                        select new
                        {
                            po,
                            s,
                            sup,
                            wh,
                            CreatedAtUtc = EF.Property<DateTime>(po, "CreatedAtUtc"),
                            RowVersion = EF.Property<byte[]>(po, "RowVersion")
                        };

        if (effectiveSiteId is > 0)
            baseQuery = baseQuery.Where(x => x.po.SiteId == effectiveSiteId.Value);
        if (query.SupplierId is > 0)
            baseQuery = baseQuery.Where(x => x.po.SupplierId == query.SupplierId.Value);
        if (query.Status is PurchaseOrderStatus status)
            baseQuery = baseQuery.Where(x => x.po.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            baseQuery = baseQuery.Where(x =>
                x.po.OrderNumber.Contains(term)
                || x.sup.Code.Contains(term)
                || x.sup.Name.Contains(term)
                || x.s.Code.Contains(term)
                || x.s.Name.Contains(term));
        }

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderByDescending(x => x.po.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var orderIds = rows.Select(r => r.po.Id).ToArray();
        var lineStats = orderIds.Length == 0
            ? new Dictionary<long, (decimal OrderedBase, decimal ReceivedBase, decimal? TotalAmount, int Count)>()
            : (await db.PurchaseOrderLines.AsNoTracking()
                .Where(l => l.OrganizationId == organizationId && orderIds.Contains(l.PurchaseOrderId))
                .Select(l => new
                {
                    l.PurchaseOrderId,
                    l.OrderedBaseQuantity,
                    l.ReceivedBaseQuantity,
                    l.OrderedQuantity,
                    l.UnitPrice
                })
                .ToListAsync(ct))
                .GroupBy(l => l.PurchaseOrderId)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        OrderedBase: g.Sum(x => x.OrderedBaseQuantity),
                        ReceivedBase: g.Sum(x => x.ReceivedBaseQuantity),
                        TotalAmount: g.All(x => !x.UnitPrice.HasValue)
                            ? (decimal?)null
                            : decimal.Round(g.Sum(x => x.UnitPrice.HasValue ? x.OrderedQuantity * x.UnitPrice.Value : 0m), 4, MidpointRounding.AwayFromZero),
                        Count: g.Count()));

        var items = rows.Select(r =>
        {
            var stats = lineStats.TryGetValue(r.po.Id, out var st) ? st : (0m, 0m, (decimal?)null, 0);
            return new PurchaseOrderSummary(
                r.po.Id,
                r.po.OrganizationId,
                r.po.SiteId,
                r.s.Code,
                r.s.Name,
                r.po.SupplierId,
                r.sup.Code,
                r.sup.Name,
                r.po.OrderNumber,
                r.po.Status,
                r.po.OrderDate,
                r.po.ExpectedDeliveryDate,
                r.po.TargetWarehouseId,
                r.wh?.Code,
                r.wh?.Name,
                r.po.CurrencyCode,
                stats.Item1,
                stats.Item2,
                stats.Item3,
                stats.Item4,
                r.po.Notes,
                r.po.CancellationReason,
                r.CreatedAtUtc,
                r.po.ApprovedAtUtc,
                r.po.ClosedAtUtc,
                r.po.CancelledAtUtc,
                Convert.ToBase64String(r.RowVersion));
        }).ToArray();

        return new OrganizationPage<PurchaseOrderSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<PurchaseOrderDetails?> GetPurchaseOrder(
        long organizationId,
        long id,
        CancellationToken ct) => Read(db => BuildOrderDetailsAsync(db, organizationId, id, ct), ct);

    public Task<PurchaseOrderSiteLookup?> GetSiteLookup(
        long organizationId,
        long siteId,
        CancellationToken ct) => Read(async db =>
        await (from s in db.Sites.AsNoTracking()
               join o in db.Organizations.AsNoTracking() on s.OrganizationId equals o.Id
               where s.OrganizationId == organizationId && s.Id == siteId
               select new PurchaseOrderSiteLookup(s.Id, s.OrganizationId, s.IsActive, o.IsActive))
            .SingleOrDefaultAsync(ct), ct);

    public Task<PurchaseOrderWarehouseLookup?> GetWarehouseLookup(
        long organizationId,
        long warehouseId,
        CancellationToken ct) => Read(async db =>
        await (from w in db.Warehouses.AsNoTracking()
               join s in db.Sites.AsNoTracking() on new { w.OrganizationId, SiteId = w.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
               join o in db.Organizations.AsNoTracking() on w.OrganizationId equals o.Id
               where w.OrganizationId == organizationId && w.Id == warehouseId
               select new PurchaseOrderWarehouseLookup(w.Id, w.OrganizationId, w.SiteId, w.IsActive, s.IsActive, o.IsActive))
            .SingleOrDefaultAsync(ct), ct);

    public Task<IReadOnlyDictionary<long, ProcurementItemLookup>> LoadItemContexts(
        long organizationId,
        IReadOnlyCollection<long> itemIds,
        CancellationToken ct) => Read<IReadOnlyDictionary<long, ProcurementItemLookup>>(async db =>
    {
        var items = await db.Items.AsNoTracking()
            .Where(i => i.OrganizationId == organizationId && itemIds.Contains(i.Id))
            .Select(i => new
            {
                i.Id,
                i.IsActive,
                i.CanBePurchased,
                i.BaseUnitId,
                i.RequiresLotTracking,
                i.QualityControlMode
            })
            .ToListAsync(ct);

        var globalEdges = await db.UnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.IsActive)
            .Select(c => new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor))
            .ToListAsync(ct);

        var itemEdges = await db.ItemUnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && itemIds.Contains(c.ItemId) && c.IsActive)
            .Select(c => new { c.ItemId, Edge = new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor) })
            .ToListAsync(ct);

        var byItem = itemEdges.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Select(x => x.Edge).ToArray());

        return items.ToDictionary(
            i => i.Id,
            i => new ProcurementItemLookup(
                i.Id,
                i.IsActive,
                i.CanBePurchased,
                i.BaseUnitId,
                i.RequiresLotTracking,
                i.QualityControlMode,
                byItem.TryGetValue(i.Id, out var specific)
                    ? specific.Concat(globalEdges).ToArray()
                    : globalEdges));
    }, ct);

    public Task<IReadOnlySet<long>> LoadActiveUnitIds(
        long organizationId,
        IReadOnlyCollection<long> unitIds,
        CancellationToken ct) => Read<IReadOnlySet<long>>(async db =>
    {
        var active = await db.Units.AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && unitIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);
        return active.ToHashSet();
    }, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CreateDraft(
        CreatePurchaseOrderRequest request,
        IReadOnlyList<ResolvedPurchaseOrderLineWrite> resolvedLines,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<PurchaseOrderDetails>.Failure(orgCheck.Error!);

        var site = await db.Sites.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SiteId, ct);
        if (site is null || !site.IsActive)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SiteInvalid);

        var supplier = await db.Suppliers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SupplierId, ct);
        if (supplier is null)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SupplierNotFound);
        if (!supplier.IsActive)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SupplierInactive);

        if (request.TargetWarehouseId is > 0)
        {
            var wh = await db.Warehouses.AsNoTracking()
                .SingleOrDefaultAsync(w => w.OrganizationId == request.OrganizationId && w.Id == request.TargetWarehouseId.Value, ct);
            if (wh is null || !wh.IsActive || wh.SiteId != request.SiteId)
                return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.WarehouseInvalid);
        }

        var po = PurchaseOrder.CreateDraft(
            request.OrganizationId,
            request.SiteId,
            request.SupplierId,
            request.OrderNumber,
            request.OrderDate,
            request.ExpectedDeliveryDate,
            request.TargetWarehouseId,
            request.CurrencyCode,
            request.Notes);

        var currency = await db.Currencies.AsNoTracking()
            .SingleOrDefaultAsync(c => c.OrganizationId == request.OrganizationId && c.Code == po.CurrencyCode, ct);
        if (currency is null || !currency.IsActive)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.CurrencyInvalid);

        var duplicateNumber = await db.PurchaseOrders.AsNoTracking()
            .AnyAsync(p => p.OrganizationId == request.OrganizationId && p.OrderNumber == po.OrderNumber, ct);
        if (duplicateNumber)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.OrderNumberDuplicate);

        po.ReplaceLines(resolvedLines.Select(l => (l.ItemId, l.OrderedQuantity, l.OrderedUnitId, l.BaseUnitIdSnapshot, l.OrderedBaseQuantity, l.UnitPrice, l.Notes)).ToArray());

        db.PurchaseOrders.Add(po);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<PurchaseOrderDetails>.Success(
            (await BuildOrderDetailsAsync(db, request.OrganizationId, po.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> UpdateDraft(
        long organizationId,
        long id,
        byte[] version,
        UpdatePurchaseOrderRequest request,
        IReadOnlyList<ResolvedPurchaseOrderLineWrite> resolvedLines,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<PurchaseOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[PurchaseOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {id}", ct);

        var po = await db.PurchaseOrders
            .Include(p => p.Lines)
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id, ct);
        if (po is null) return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
        if (!Matches(db, po, version)) return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.ConcurrencyConflict);
        if (po.Status != PurchaseOrderStatus.Draft) return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotDraft);

        var site = await db.Sites.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == po.SiteId, ct);
        if (site is null || !site.IsActive)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SiteInvalid);

        var supplier = await db.Suppliers.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == request.SupplierId, ct);
        if (supplier is null)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SupplierNotFound);
        if (!supplier.IsActive)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SupplierInactive);

        if (request.TargetWarehouseId is > 0)
        {
            var wh = await db.Warehouses.AsNoTracking()
                .SingleOrDefaultAsync(w => w.OrganizationId == organizationId && w.Id == request.TargetWarehouseId.Value, ct);
            if (wh is null || !wh.IsActive || wh.SiteId != po.SiteId)
                return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.WarehouseInvalid);
        }

        po.UpdateDraft(
            request.SupplierId,
            request.OrderDate,
            request.ExpectedDeliveryDate,
            request.TargetWarehouseId,
            request.CurrencyCode,
            request.Notes);

        var currency = await db.Currencies.AsNoTracking()
            .SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Code == po.CurrencyCode, ct);
        if (currency is null || !currency.IsActive)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.CurrencyInvalid);

        var existingLines = po.Lines.ToArray();
        if (existingLines.Length > 0)
            db.PurchaseOrderLines.RemoveRange(existingLines);

        po.ReplaceLines(resolvedLines.Select(l => (l.ItemId, l.OrderedQuantity, l.OrderedUnitId, l.BaseUnitIdSnapshot, l.OrderedBaseQuantity, l.UnitPrice, l.Notes)).ToArray());

        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<PurchaseOrderDetails>.Success(
            (await BuildOrderDetailsAsync(db, organizationId, po.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> ApproveOrder(
        long organizationId,
        long id,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<PurchaseOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[PurchaseOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {id}", ct);

        var po = await db.PurchaseOrders
            .Include(p => p.Lines)
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id, ct);
        if (po is null) return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
        if (!Matches(db, po, version)) return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.ConcurrencyConflict);

        var site = await db.Sites.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == po.SiteId, ct);
        if (site is null || !site.IsActive)
            return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.SiteInvalid);

        po.Approve(utcNow);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<PurchaseOrderDetails>.Success(
            (await BuildOrderDetailsAsync(db, organizationId, po.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CloseOrder(
        long organizationId,
        long id,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<PurchaseOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[PurchaseOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {id}", ct);

        var po = await db.PurchaseOrders
            .Include(p => p.Lines)
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id, ct);
        if (po is null) return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
        if (!Matches(db, po, version)) return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.ConcurrencyConflict);

        po.CloseRemaining(utcNow);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<PurchaseOrderDetails>.Success(
            (await BuildOrderDetailsAsync(db, organizationId, po.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<PurchaseOrderDetails>> CancelOrder(
        long organizationId,
        long id,
        byte[] version,
        string? reason,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<PurchaseOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[PurchaseOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {id}", ct);

        var po = await db.PurchaseOrders
            .Include(p => p.Lines)
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id, ct);
        if (po is null) return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.NotFound);
        if (!Matches(db, po, version)) return OrganizationResult<PurchaseOrderDetails>.Failure(ProcurementErrors.ConcurrencyConflict);

        po.Cancel(reason, utcNow);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<PurchaseOrderDetails>.Success(
            (await BuildOrderDetailsAsync(db, organizationId, po.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<PurchaseOrderReceiptResult>> ReceiveOrder(
        ReceivePurchaseOrderRequest request,
        long siteId,
        byte[] version,
        string documentNumber,
        IReadOnlyList<ResolvedPurchaseOrderReceiptLine> resolvedLines,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<PurchaseOrderReceiptResult>.Failure(orgCheck.Error!);

        var idempKey = InventoryText.OptionalIdempotencyKey(request.IdempotencyKey);
        if (idempKey is not null)
        {
            var existingTxn = await db.InventoryTransactions.AsNoTracking()
                .Where(x => x.OrganizationId == request.OrganizationId && x.IdempotencyKey == idempKey)
                .Select(x => new { x.Id, x.DocumentNumber, x.PurchaseOrderId })
                .SingleOrDefaultAsync(ct);
            if (existingTxn is not null)
            {
                if (existingTxn.PurchaseOrderId != request.PurchaseOrderId)
                    return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.ConcurrencyConflict);

                var idempotentOrder = await BuildOrderDetailsAsync(db, request.OrganizationId, request.PurchaseOrderId, ct);
                return idempotentOrder is null
                    ? OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.NotFound)
                    : OrganizationResult<PurchaseOrderReceiptResult>.Success(
                        new PurchaseOrderReceiptResult(idempotentOrder, existingTxn.Id, existingTxn.DocumentNumber));
            }
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[PurchaseOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {request.OrganizationId} AND [Id] = {request.PurchaseOrderId}", ct);

        var po = await db.PurchaseOrders
            .Include(p => p.Lines)
            .SingleOrDefaultAsync(p => p.OrganizationId == request.OrganizationId && p.Id == request.PurchaseOrderId, ct);
        if (po is null) return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.NotFound);
        if (!Matches(db, po, version)) return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.ConcurrencyConflict);

        var site = await db.Sites.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == po.SiteId, ct);
        if (site is null || !site.IsActive)
            return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.SiteInvalid);

        var wh = await db.Warehouses.AsNoTracking()
            .SingleOrDefaultAsync(w => w.OrganizationId == request.OrganizationId && w.Id == request.WarehouseId, ct);
        if (wh is null || !wh.IsActive || wh.SiteId != po.SiteId)
            return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.WarehouseInvalid);

        var duplicateDocNum = await db.InventoryTransactions.AsNoTracking()
            .AnyAsync(t => t.OrganizationId == request.OrganizationId && t.DocumentNumber == documentNumber, ct);
        if (duplicateDocNum)
            return OrganizationResult<PurchaseOrderReceiptResult>.Failure(ProcurementErrors.ReceiptDocumentInvalid);

        po.ApplyReceipts(resolvedLines.Select(l => (l.PurchaseOrderLineId, l.BaseQuantityDelta)).ToArray());

        var txn = InventoryTransaction.Create(
            request.OrganizationId,
            po.SiteId,
            request.WarehouseId,
            destinationWarehouseId: null,
            documentNumber: documentNumber,
            transactionType: InventoryTransactionType.Receipt,
            productionOrderId: null,
            reversedTransactionId: null,
            referenceNote: request.ReferenceNote,
            idempotencyKey: idempKey,
            postedAtUtc: utcNow,
            qualityInspectionId: null,
            purchaseOrderId: po.Id);

        db.InventoryTransactions.Add(txn);
        await db.SaveInOperation(access.Actor, ct);

        var invLines = resolvedLines.Select(l => new ResolvedInventoryLineWrite(
            po.SiteId,
            request.WarehouseId,
            l.ItemId,
            l.LotCode,
            l.EnteredQuantity,
            l.EnteredUnitId,
            l.BaseQuantityDelta,
            l.BaseUnitId,
            l.Notes,
            l.TriggersQualityControl,
            l.ManufactureDate,
            l.ExpirationDate)).ToArray();

        await SqlInventoryOperationsStore.UpsertInboundLotMetadataAsync(db, request.OrganizationId, invLines, ct);
        await SqlInventoryOperationsStore.ApplyLedgerLinesAndBalancesAsync(db, request.OrganizationId, txn.Id, invLines, utcNow, ct);
        await SqlInventoryOperationsStore.CreateAutoQualityInspectionsAsync(
            db,
            request.OrganizationId,
            InventoryTransactionType.Receipt,
            txn.Id,
            null,
            invLines,
            utcNow,
            ct);

        await db.SaveInOperation(access.Actor, ct);

        var updatedOrder = await BuildOrderDetailsAsync(db, request.OrganizationId, po.Id, ct);
        return OrganizationResult<PurchaseOrderReceiptResult>.Success(
            new PurchaseOrderReceiptResult(updatedOrder!, txn.Id, txn.DocumentNumber));
    }, ct);

    private static async Task<PurchaseOrderDetails?> BuildOrderDetailsAsync(
        OrganizationDbContext db,
        long organizationId,
        long id,
        CancellationToken ct)
    {
        var row = await (from po in db.PurchaseOrders.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { po.OrganizationId, SiteId = po.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         join sup in db.Suppliers.AsNoTracking() on new { po.OrganizationId, SupplierId = po.SupplierId } equals new { sup.OrganizationId, SupplierId = sup.Id }
                         join wh in db.Warehouses.AsNoTracking() on new { po.OrganizationId, WhId = po.TargetWarehouseId } equals new { wh.OrganizationId, WhId = (long?)wh.Id } into whJoin
                         from wh in whJoin.DefaultIfEmpty()
                         where po.OrganizationId == organizationId && po.Id == id
                         select new
                         {
                             po,
                             s,
                             sup,
                             wh,
                             CreatedAtUtc = EF.Property<DateTime>(po, "CreatedAtUtc"),
                             RowVersion = EF.Property<byte[]>(po, "RowVersion")
                         }).SingleOrDefaultAsync(ct);

        if (row is null) return null;

        var lines = await (from l in db.PurchaseOrderLines.AsNoTracking()
                           join it in db.Items.AsNoTracking() on new { l.OrganizationId, ItemId = l.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                           join ou in db.Units.AsNoTracking() on new { l.OrganizationId, UnitId = l.OrderedUnitId } equals new { ou.OrganizationId, UnitId = ou.Id }
                           join bu in db.Units.AsNoTracking() on new { l.OrganizationId, UnitId = l.BaseUnitIdSnapshot } equals new { bu.OrganizationId, UnitId = bu.Id }
                           where l.OrganizationId == organizationId && l.PurchaseOrderId == id
                           orderby l.LineNumber, l.Id
                           select new PurchaseOrderLineDetails(
                               l.Id,
                               l.LineNumber,
                               l.ItemId,
                               it.Code,
                               it.Name,
                               it.RequiresLotTracking,
                               it.QualityControlMode,
                               l.OrderedQuantity,
                               l.OrderedUnitId,
                               ou.Code,
                               ou.Symbol,
                               l.BaseUnitIdSnapshot,
                               bu.Code,
                               bu.Symbol,
                               l.OrderedBaseQuantity,
                               l.ReceivedBaseQuantity,
                               decimal.Round(Math.Max(0m, l.OrderedBaseQuantity - l.ReceivedBaseQuantity), 6, MidpointRounding.AwayFromZero),
                               l.UnitPrice,
                               l.Notes))
            .ToListAsync(ct);

        var receipts = await (from tx in db.InventoryTransactions.AsNoTracking()
                              join wh in db.Warehouses.AsNoTracking() on new { tx.OrganizationId, WarehouseId = tx.WarehouseId } equals new { wh.OrganizationId, WarehouseId = wh.Id }
                              where tx.OrganizationId == organizationId && tx.PurchaseOrderId == id
                              orderby tx.Id descending
                              select new PurchaseOrderReceiptSummary(
                                  tx.Id,
                                  tx.DocumentNumber,
                                  tx.WarehouseId,
                                  wh.Code,
                                  wh.Name,
                                  db.InventoryTransactions.AsNoTracking().Any(r => r.OrganizationId == organizationId && r.ReversedTransactionId == tx.Id),
                                  tx.PostedAtUtc,
                                  tx.ReferenceNote))
            .ToListAsync(ct);

        var totalOrderedBase = lines.Sum(l => l.OrderedBaseQuantity);
        var totalReceivedBase = lines.Sum(l => l.ReceivedBaseQuantity);
        decimal? totalEstimated = lines.All(l => !l.UnitPrice.HasValue)
            ? null
            : decimal.Round(lines.Sum(l => l.UnitPrice.HasValue ? l.OrderedQuantity * l.UnitPrice.Value : 0m), 4, MidpointRounding.AwayFromZero);

        var summary = new PurchaseOrderSummary(
            row.po.Id,
            row.po.OrganizationId,
            row.po.SiteId,
            row.s.Code,
            row.s.Name,
            row.po.SupplierId,
            row.sup.Code,
            row.sup.Name,
            row.po.OrderNumber,
            row.po.Status,
            row.po.OrderDate,
            row.po.ExpectedDeliveryDate,
            row.po.TargetWarehouseId,
            row.wh?.Code,
            row.wh?.Name,
            row.po.CurrencyCode,
            totalOrderedBase,
            totalReceivedBase,
            totalEstimated,
            lines.Count,
            row.po.Notes,
            row.po.CancellationReason,
            row.CreatedAtUtc,
            row.po.ApprovedAtUtc,
            row.po.ClosedAtUtc,
            row.po.CancelledAtUtc,
            Convert.ToBase64String(row.RowVersion));

        return new PurchaseOrderDetails(summary, lines, receipts);
    }

    private static SupplierSummary ToSupplierSummary(Supplier s, byte[] rowVersion) => new(
        s.Id,
        s.OrganizationId,
        s.Code,
        s.Name,
        s.ContactName,
        s.Phone,
        s.Email,
        s.Address,
        s.Notes,
        s.IsActive,
        Convert.ToBase64String(rowVersion));

    private async Task<T> Read<T>(Func<OrganizationDbContext, Task<T>> query, CancellationToken ct)
    {
        try
        {
            await using var db = await Factory.CreateDbContextAsync(ct);
            return await query(db);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlProcurementOperationsStore));
            throw new ProcurementStoreUnavailableException(ex);
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
                    return OrganizationResult<T>.Failure(ProcurementErrors.AccessDenied);

                var commitAttempted = false;
                try
                {
                    await using var db = await Factory.CreateDbContextAsync(ct);
                    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                    var result = await operation(db);
                    if (!result.Succeeded) return result;
                    commitAttempted = true;
                    await transaction.CommitAsync(ct);
                    return result;
                }
                catch (DbUpdateConcurrencyException)
                {
                    return OrganizationResult<T>.Failure(ProcurementErrors.ConcurrencyConflict);
                }
                catch (DbUpdateException ex) when (IsUniqueConflict(ex))
                {
                    return OrganizationResult<T>.Failure(ClassifyUniqueConflict(ex));
                }
                catch (DbUpdateException ex) when (IsCheckViolation(ex))
                {
                    return OrganizationResult<T>.Failure(ClassifyCheckViolation(ex));
                }
                catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
                {
                    return OrganizationResult<T>.Failure(ProcurementErrors.AuditUnavailable);
                }
                catch (Exception ex) when (commitAttempted && ex is not OrganizationCommitOutcomeUnknownException)
                {
                    throw new OrganizationCommitOutcomeUnknownException(ex);
                }
            });
        }
        catch (ProcurementRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(ProcurementErrors.ConcurrencyConflict); }
        catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(ProcurementErrors.OutcomeUnknown); }
        catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
        { return OrganizationResult<T>.Failure(ProcurementErrors.AuditUnavailable); }
        catch (DbUpdateException ex) when (IsUniqueConflict(ex))
        { return OrganizationResult<T>.Failure(ClassifyUniqueConflict(ex)); }
        catch (InvalidOperationException ex) when (ex.Message == ProcurementErrors.ParentOrganizationInactive)
        { return OrganizationResult<T>.Failure(ProcurementErrors.ParentOrganizationInactive); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationSiteErrors.OrganizationNotFound)
        { return OrganizationResult<T>.Failure(ProcurementErrors.NotFound); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationAccessErrors.AccessDenied)
        { return OrganizationResult<T>.Failure(ProcurementErrors.AccessDenied); }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlProcurementOperationsStore), access.Actor.ActorId);
            return OrganizationResult<T>.Failure(ProcurementErrors.PersistenceUnavailable);
        }
    }

    private static async Task<(bool Ok, string? Error)> LockAndRequireActiveOrganization(OrganizationDbContext db, long organizationId, CancellationToken ct)
    {
        var active = await db.Database.SqlQueryRaw<bool>(
            "SELECT [IsActive] AS [Value] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}", organizationId)
            .SingleOrDefaultAsync(ct);
        if (!active)
        {
            var exists = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().AnyAsync(o => o.Id == organizationId, ct);
            return (false, exists ? ProcurementErrors.ParentOrganizationInactive : ProcurementErrors.NotFound);
        }
        return (true, null);
    }

    private static bool Matches(OrganizationDbContext db, object entity, byte[] version)
        => ((byte[])db.Entry(entity).Property("RowVersion").CurrentValue!).SequenceEqual(version);

    private static bool IsUniqueConflict(DbUpdateException ex)
        => ex.InnerException is SqlException sql && sql.Number is 2601 or 2627;

    private static string ClassifyUniqueConflict(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? string.Empty;
        if (msg.Contains("UX_Supplier_Org_Code", StringComparison.Ordinal)) return ProcurementErrors.SupplierCodeDuplicate;
        if (msg.Contains("UX_PurchaseOrder_Org_Number", StringComparison.Ordinal)) return ProcurementErrors.OrderNumberDuplicate;
        if (msg.Contains("UX_InventoryTransaction_Org_DocNum", StringComparison.Ordinal)) return ProcurementErrors.ReceiptDocumentInvalid;
        return ProcurementErrors.ConcurrencyConflict;
    }

    private static bool IsCheckViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql && sql.Number == 547;

    private static string ClassifyCheckViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? string.Empty;
        if (msg.Contains("CK_PurchaseOrderLine_Qty", StringComparison.Ordinal)) return ProcurementErrors.OverReceipt;
        if (msg.Contains("CK_InventoryBalance_NonNegative", StringComparison.Ordinal) || msg.Contains("CK_InventoryBalance_Blocked", StringComparison.Ordinal))
            return InventoryErrors.InsufficientStock;
        return ProcurementErrors.ConcurrencyConflict;
    }

    private static bool Unavailable(Exception exception) => exception switch
    {
        SqlException sql => sql.Number is -2 or 53 or 64 or 233 or 1205 or 4060 or 18456 or 40197 or 40501 or 40613,
        TimeoutException => true,
        DbUpdateException or RetryLimitExceededException when exception.InnerException is not null => Unavailable(exception.InnerException),
        InvalidOperationException when exception.Message == OrganizationAccessErrors.PersistenceUnavailable => true,
        _ => false
    };
}
