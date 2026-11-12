using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Sales;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlSalesOperationsStore(
    Func<IDbContextFactory<OrganizationDbContext>> dbFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null) : ISalesOperationsStore
{
    public SqlSalesOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }
    public Task<IReadOnlyDictionary<long, SalesItemContext>> LoadItemContexts(
        long organizationId,
        IReadOnlyList<long> itemIds,
        CancellationToken ct) => Read<IReadOnlyDictionary<long, SalesItemContext>>(async db =>
    {
        if (itemIds.Count == 0)
            return new Dictionary<long, SalesItemContext>();

        var items = await db.Items.AsNoTracking()
            .Where(i => i.OrganizationId == organizationId && itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.IsActive, i.CanBeSold, i.BaseUnitId })
            .ToListAsync(ct);

        var orgEdges = await db.UnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.IsActive)
            .Select(c => new { c.FromUnitId, c.ToUnitId, c.Factor })
            .ToListAsync(ct);

        var itemEdges = await db.ItemUnitConversions.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.IsActive && itemIds.Contains(c.ItemId))
            .Select(c => new { c.ItemId, c.FromUnitId, c.ToUnitId, c.Factor })
            .ToListAsync(ct);

        var result = new Dictionary<long, SalesItemContext>(items.Count);
        foreach (var item in items)
        {
            var edges = new List<UnitConversionEdge>(orgEdges.Count + 4);
            foreach (var e in itemEdges.Where(x => x.ItemId == item.Id))
                edges.Add(new UnitConversionEdge(e.FromUnitId, e.ToUnitId, e.Factor, true));
            foreach (var e in orgEdges)
                edges.Add(new UnitConversionEdge(e.FromUnitId, e.ToUnitId, e.Factor, false));

            result[item.Id] = new SalesItemContext(item.Id, item.IsActive, item.CanBeSold, item.BaseUnitId, edges);
        }

        return result;
    }, ct);

    public Task<IReadOnlySet<long>> LoadActiveUnitIds(
        long organizationId,
        IReadOnlyList<long> unitIds,
        CancellationToken ct) => Read<IReadOnlySet<long>>(async db =>
    {
        if (unitIds.Count == 0)
            return new HashSet<long>();

        var ids = await db.Units.AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && u.IsActive && unitIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct);

        return ids.ToHashSet();
    }, ct);

    public Task<OrganizationPage<CustomerSummary>> ListCustomers(
        long organizationId,
        CustomerListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = db.Customers.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId);

        if (!query.IncludeInactive)
            baseQuery = baseQuery.Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
            baseQuery = baseQuery.Where(c =>
                c.Code.Contains(query.Search)
                || c.Name.Contains(query.Search)
                || (c.ContactName != null && c.ContactName.Contains(query.Search))
                || (c.Phone != null && c.Phone.Contains(query.Search)));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderBy(c => c.Code).ThenBy(c => c.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new { c, RowVersion = EF.Property<byte[]>(c, "RowVersion") })
            .ToListAsync(ct);

        var items = rows.Select(r => ToCustomerSummary(r.c, r.RowVersion)).ToArray();
        return new OrganizationPage<CustomerSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<OrganizationResult<CustomerSummary>> CreateCustomer(
        CreateCustomerRequest request,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<CustomerSummary>.Failure(orgCheck.Error!);

        var customer = Customer.Create(
            request.OrganizationId,
            request.Code,
            request.Name,
            request.ContactName,
            request.Phone,
            request.Email,
            request.ShippingAddress,
            request.TaxCode,
            request.Notes);

        var duplicateCode = await db.Customers.AsNoTracking()
            .AnyAsync(c => c.OrganizationId == request.OrganizationId && c.Code == customer.Code, ct);
        if (duplicateCode)
            return OrganizationResult<CustomerSummary>.Failure(SalesErrors.CustomerCodeDuplicate);

        db.Customers.Add(customer);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<CustomerSummary>.Success(
            ToCustomerSummary(customer, (byte[])db.Entry(customer).Property("RowVersion").CurrentValue!));
    }, ct);

    public Task<OrganizationResult<CustomerSummary>> UpdateCustomer(
        long organizationId,
        long customerId,
        UpdateCustomerRequest request,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<CustomerSummary>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[Customer] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {customerId}", ct);

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == customerId, ct);
        if (customer is null) return OrganizationResult<CustomerSummary>.Failure(SalesErrors.CustomerNotFound);
        if (!Matches(db, customer, version)) return OrganizationResult<CustomerSummary>.Failure(SalesErrors.ConcurrencyConflict);

        if (customer.Update(request.Name, request.ContactName, request.Phone, request.Email, request.ShippingAddress, request.TaxCode, request.Notes))
            await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<CustomerSummary>.Success(
            ToCustomerSummary(customer, (byte[])db.Entry(customer).Property("RowVersion").CurrentValue!));
    }, ct);

    public Task<OrganizationResult<CustomerSummary>> SetCustomerActive(
        long organizationId,
        long customerId,
        bool isActive,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<CustomerSummary>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[Customer] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {customerId}", ct);

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == customerId, ct);
        if (customer is null) return OrganizationResult<CustomerSummary>.Failure(SalesErrors.CustomerNotFound);
        if (!Matches(db, customer, version)) return OrganizationResult<CustomerSummary>.Failure(SalesErrors.ConcurrencyConflict);

        if (customer.SetActive(isActive))
            await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<CustomerSummary>.Success(
            ToCustomerSummary(customer, (byte[])db.Entry(customer).Property("RowVersion").CurrentValue!));
    }, ct);

    public Task<OrganizationPage<SalesOrderSummary>> ListSalesOrders(
        long organizationId,
        SalesOrderListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from so in db.SalesOrders.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { so.OrganizationId, SiteId = so.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        join c in db.Customers.AsNoTracking() on new { so.OrganizationId, CustomerId = so.CustomerId } equals new { c.OrganizationId, CustomerId = c.Id }
                        join wh in db.Warehouses.AsNoTracking() on new { so.OrganizationId, WhId = so.SourceWarehouseId } equals new { wh.OrganizationId, WhId = (long?)wh.Id } into whJoin
                        from wh in whJoin.DefaultIfEmpty()
                        where so.OrganizationId == organizationId
                        select new
                        {
                            so,
                            s.Code,
                            SiteName = s.Name,
                            CustomerCode = c.Code,
                            CustomerName = c.Name,
                            WhCode = wh != null ? wh.Code : null,
                            WhName = wh != null ? wh.Name : null,
                            CreatedAtUtc = EF.Property<DateTime>(so, "CreatedAtUtc"),
                            RowVersion = EF.Property<byte[]>(so, "RowVersion")
                        };

        if (query.SiteId.HasValue)
            baseQuery = baseQuery.Where(x => x.so.SiteId == query.SiteId.Value);
        if (query.CustomerId.HasValue)
            baseQuery = baseQuery.Where(x => x.so.CustomerId == query.CustomerId.Value);
        if (query.Status.HasValue)
            baseQuery = baseQuery.Where(x => x.so.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.so.OrderNumber.Contains(query.Search)
                || x.CustomerCode.Contains(query.Search)
                || x.CustomerName.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderByDescending(x => x.so.OrderDate).ThenByDescending(x => x.so.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var orderIds = rows.Select(r => r.so.Id).ToArray();
        var lineStats = orderIds.Length == 0
            ? new Dictionary<long, (decimal Ordered, decimal Dispatched, decimal? Amount, int Count)>()
            : (await db.SalesOrderLines.AsNoTracking()
                .Where(l => l.OrganizationId == organizationId && orderIds.Contains(l.SalesOrderId))
                .GroupBy(l => l.SalesOrderId)
                .Select(g => new
                {
                    OrderId = g.Key,
                    Ordered = g.Sum(l => l.OrderedBaseQuantity),
                    Dispatched = g.Sum(l => l.DispatchedBaseQuantity),
                    Amount = g.Any(l => l.UnitPrice.HasValue) ? g.Sum(l => (l.UnitPrice ?? 0m) * l.OrderedQuantity) : (decimal?)null,
                    Count = g.Count()
                })
                .ToListAsync(ct))
                .ToDictionary(x => x.OrderId, x => (x.Ordered, x.Dispatched, x.Amount, x.Count));

        var items = rows.Select(r =>
        {
            var stats = lineStats.GetValueOrDefault(r.so.Id);
            return new SalesOrderSummary(
                r.so.Id,
                r.so.OrganizationId,
                r.so.SiteId,
                r.Code,
                r.SiteName,
                r.so.CustomerId,
                r.CustomerCode,
                r.CustomerName,
                r.so.OrderNumber,
                r.so.Status,
                r.so.OrderDate,
                r.so.RequestedDeliveryDate,
                r.so.SourceWarehouseId,
                r.WhCode,
                r.WhName,
                r.so.CurrencyCode,
                r.so.ShippingAddressSnapshot,
                stats.Ordered,
                stats.Dispatched,
                stats.Amount,
                stats.Count,
                r.so.Notes,
                r.so.CancellationReason,
                r.CreatedAtUtc,
                r.so.ApprovedAtUtc,
                r.so.ClosedAtUtc,
                r.so.CancelledAtUtc,
                Convert.ToBase64String(r.RowVersion));
        }).ToArray();

        return new OrganizationPage<SalesOrderSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<SalesOrderDetails?> GetSalesOrder(long organizationId, long salesOrderId, CancellationToken ct) =>
        Read(db => BuildOrderDetailsAsync(db, organizationId, salesOrderId, ct), ct);

    public Task<OrganizationResult<SalesOrderDetails>> CreateSalesOrder(
        CreateSalesOrderRequest request,
        IReadOnlyList<ResolvedSalesOrderLine> resolvedLines,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<SalesOrderDetails>.Failure(orgCheck.Error!);

        var site = await db.Sites.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SiteId, ct);
        if (site is null || !site.IsActive)
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.SiteInvalid);

        var customer = await db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(c => c.OrganizationId == request.OrganizationId && c.Id == request.CustomerId, ct);
        if (customer is null)
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.CustomerNotFound);
        if (!customer.IsActive)
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.CustomerInactive);

        if (request.SourceWarehouseId is > 0)
        {
            var wh = await db.Warehouses.AsNoTracking()
                .SingleOrDefaultAsync(w => w.OrganizationId == request.OrganizationId && w.Id == request.SourceWarehouseId.Value, ct);
            if (wh is null || !wh.IsActive || wh.SiteId != request.SiteId)
                return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.WarehouseInvalid);
        }

        var currencyCode = SalesText.Currency(request.CurrencyCode);
        var currencyExists = await db.Currencies.AsNoTracking()
            .AnyAsync(c => c.OrganizationId == request.OrganizationId && c.Code == currencyCode && c.IsActive, ct);
        if (!currencyExists)
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.CurrencyInvalid);

        var shippingAddress = string.IsNullOrWhiteSpace(request.ShippingAddressSnapshot)
            ? customer.ShippingAddress
            : request.ShippingAddressSnapshot;

        var orderNumber = await SqlDocumentSequenceStore.AllocateAsync(
            db, request.OrganizationId, DocumentSequenceType.SalesOrder, request.OrderNumber, utcNow, ct);

        var order = SalesOrder.CreateDraft(
            request.OrganizationId,
            request.SiteId,
            request.CustomerId,
            orderNumber,
            request.OrderDate,
            request.RequestedDeliveryDate,
            request.SourceWarehouseId,
            currencyCode,
            shippingAddress,
            request.Notes);

        var dupNum = await db.SalesOrders.AsNoTracking()
            .AnyAsync(o => o.OrganizationId == request.OrganizationId && o.OrderNumber == order.OrderNumber, ct);
        if (dupNum)
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.OrderNumberDuplicate);

        order.ReplaceLines(resolvedLines.Select(l =>
            (l.ItemId, l.OrderedQuantity, l.OrderedUnitId, l.BaseUnitIdSnapshot, l.OrderedBaseQuantity, l.UnitPrice, l.Notes)).ToArray());

        db.SalesOrders.Add(order);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<SalesOrderDetails>.Success((await BuildOrderDetailsAsync(db, request.OrganizationId, order.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<SalesOrderDetails>> UpdateSalesOrder(
        long organizationId,
        long salesOrderId,
        UpdateSalesOrderRequest request,
        IReadOnlyList<ResolvedSalesOrderLine> resolvedLines,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<SalesOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[SalesOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {salesOrderId}", ct);

        var order = await db.SalesOrders.Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.OrganizationId == organizationId && o.Id == salesOrderId, ct);
        if (order is null) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
        if (!Matches(db, order, version)) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);

        var customer = await db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == request.CustomerId, ct);
        if (customer is null) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.CustomerNotFound);
        if (!customer.IsActive) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.CustomerInactive);

        if (request.SourceWarehouseId is > 0)
        {
            var wh = await db.Warehouses.AsNoTracking()
                .SingleOrDefaultAsync(w => w.OrganizationId == organizationId && w.Id == request.SourceWarehouseId.Value, ct);
            if (wh is null || !wh.IsActive || wh.SiteId != order.SiteId)
                return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.WarehouseInvalid);
        }

        var currencyCode = SalesText.Currency(request.CurrencyCode);
        var currencyExists = await db.Currencies.AsNoTracking()
            .AnyAsync(c => c.OrganizationId == organizationId && c.Code == currencyCode && c.IsActive, ct);
        if (!currencyExists)
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.CurrencyInvalid);

        order.UpdateDraft(
            request.CustomerId,
            request.OrderDate,
            request.RequestedDeliveryDate,
            request.SourceWarehouseId,
            currencyCode,
            request.ShippingAddressSnapshot,
            request.Notes);

        order.ReplaceLines(resolvedLines.Select(l =>
            (l.ItemId, l.OrderedQuantity, l.OrderedUnitId, l.BaseUnitIdSnapshot, l.OrderedBaseQuantity, l.UnitPrice, l.Notes)).ToArray());

        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<SalesOrderDetails>.Success((await BuildOrderDetailsAsync(db, organizationId, order.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<SalesOrderDetails>> ApproveSalesOrder(
        long organizationId,
        long salesOrderId,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<SalesOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[SalesOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {salesOrderId}", ct);

        var order = await db.SalesOrders.Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.OrganizationId == organizationId && o.Id == salesOrderId, ct);
        if (order is null) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
        if (!Matches(db, order, version)) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);

        order.Approve(utcNow);
        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<SalesOrderDetails>.Success((await BuildOrderDetailsAsync(db, organizationId, order.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<SalesOrderDetails>> CloseSalesOrder(
        long organizationId,
        long salesOrderId,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<SalesOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[SalesOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {salesOrderId}", ct);

        var order = await db.SalesOrders.Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.OrganizationId == organizationId && o.Id == salesOrderId, ct);
        if (order is null) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
        if (!Matches(db, order, version)) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);

        order.Close(utcNow);
        await ReleaseOpenSalesReservationsAsync(db, organizationId, salesOrderId, utcNow, ct);
        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<SalesOrderDetails>.Success((await BuildOrderDetailsAsync(db, organizationId, order.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<SalesOrderDetails>> CancelSalesOrder(
        long organizationId,
        long salesOrderId,
        string reason,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<SalesOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[SalesOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {salesOrderId}", ct);

        var order = await db.SalesOrders.Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.OrganizationId == organizationId && o.Id == salesOrderId, ct);
        if (order is null) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
        if (!Matches(db, order, version)) return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);

        order.Cancel(reason, utcNow);
        await ReleaseOpenSalesReservationsAsync(db, organizationId, salesOrderId, utcNow, ct);
        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<SalesOrderDetails>.Success((await BuildOrderDetailsAsync(db, organizationId, order.Id, ct))!);
    }, ct);

    public Task<OrganizationPage<DeliveryOrderSummary>> ListDeliveryOrders(
        long organizationId,
        DeliveryOrderListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from d in db.DeliveryOrders.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { d.OrganizationId, SiteId = d.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        join so in db.SalesOrders.AsNoTracking() on new { d.OrganizationId, d.SalesOrderId } equals new { so.OrganizationId, SalesOrderId = so.Id }
                        join c in db.Customers.AsNoTracking() on new { so.OrganizationId, so.CustomerId } equals new { c.OrganizationId, CustomerId = c.Id }
                        join wh in db.Warehouses.AsNoTracking() on new { d.OrganizationId, d.WarehouseId } equals new { wh.OrganizationId, WarehouseId = wh.Id }
                        where d.OrganizationId == organizationId
                        select new
                        {
                            d,
                            SiteCode = s.Code,
                            SiteName = s.Name,
                            so.OrderNumber,
                            so.CustomerId,
                            CustomerCode = c.Code,
                            CustomerName = c.Name,
                            WhCode = wh.Code,
                            WhName = wh.Name,
                            RowVersion = EF.Property<byte[]>(d, "RowVersion")
                        };

        if (query.SiteId.HasValue)
            baseQuery = baseQuery.Where(x => x.d.SiteId == query.SiteId.Value);
        if (query.SalesOrderId.HasValue)
            baseQuery = baseQuery.Where(x => x.d.SalesOrderId == query.SalesOrderId.Value);
        if (query.WarehouseId.HasValue)
            baseQuery = baseQuery.Where(x => x.d.WarehouseId == query.WarehouseId.Value);
        if (query.Status.HasValue)
            baseQuery = baseQuery.Where(x => x.d.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.d.DispatchNumber.Contains(query.Search)
                || x.OrderNumber.Contains(query.Search)
                || x.CustomerCode.Contains(query.Search)
                || x.CustomerName.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderByDescending(x => x.d.DispatchDate).ThenByDescending(x => x.d.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var ids = rows.Select(r => r.d.Id).ToArray();
        var lineStats = ids.Length == 0
            ? new Dictionary<long, (decimal Qty, int Count)>()
            : (await db.DeliveryOrderLines.AsNoTracking()
                .Where(l => l.OrganizationId == organizationId && ids.Contains(l.DeliveryOrderId))
                .GroupBy(l => l.DeliveryOrderId)
                .Select(g => new { Id = g.Key, Qty = g.Sum(l => l.DispatchedBaseQuantity), Count = g.Count() })
                .ToListAsync(ct))
                .ToDictionary(x => x.Id, x => (x.Qty, x.Count));

        var items = rows.Select(r =>
        {
            var stats = lineStats.GetValueOrDefault(r.d.Id);
            return new DeliveryOrderSummary(
                r.d.Id,
                r.d.OrganizationId,
                r.d.SiteId,
                r.SiteCode,
                r.SiteName,
                r.d.SalesOrderId,
                r.OrderNumber,
                r.CustomerId,
                r.CustomerCode,
                r.CustomerName,
                r.d.WarehouseId,
                r.WhCode,
                r.WhName,
                r.d.DispatchNumber,
                r.d.Status,
                r.d.DispatchDate,
                r.d.CarrierName,
                r.d.TrackingNumber,
                stats.Qty,
                stats.Count,
                r.d.Notes,
                r.d.PostedAtUtc,
                r.d.ReversedAtUtc,
                r.d.ReversalReason,
                Convert.ToBase64String(r.RowVersion));
        }).ToArray();

        return new OrganizationPage<DeliveryOrderSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<DeliveryOrderDetails?> GetDeliveryOrder(long organizationId, long deliveryOrderId, CancellationToken ct) =>
        Read(db => BuildDeliveryDetailsAsync(db, organizationId, deliveryOrderId, ct), ct);

    public Task<IReadOnlyList<DispatchableLotSummary>> ListDispatchableLots(
        long organizationId,
        long salesOrderId,
        long warehouseId,
        DateOnly asOfDate,
        CancellationToken ct) => Read<IReadOnlyList<DispatchableLotSummary>>(async db =>
    {
        var orderItemIds = await db.SalesOrderLines.AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && l.SalesOrderId == salesOrderId)
            .Select(l => l.ItemId)
            .Distinct()
            .ToArrayAsync(ct);

        if (orderItemIds.Length == 0)
            return [];

        var balances = await (from b in db.InventoryBalances.AsNoTracking()
                              join w in db.Warehouses.AsNoTracking() on new { b.OrganizationId, b.WarehouseId } equals new { w.OrganizationId, WarehouseId = w.Id }
                              where b.OrganizationId == organizationId
                                    && b.WarehouseId == warehouseId
                                    && orderItemIds.Contains(b.ItemId)
                                    && b.OnHandBaseQuantity > 0m
                              select new { b, w.Code, w.Name })
            .ToListAsync(ct);

        if (balances.Count == 0)
            return [];

        var lotMeta = await db.ItemLots.AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && orderItemIds.Contains(l.ItemId))
            .ToDictionaryAsync(l => (l.ItemId, l.LotCode), ct);

        var openOrderRes = await db.StockReservations.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId
                        && r.WarehouseId == warehouseId
                        && r.SourceKind == ReservationSourceKind.SalesOrder
                        && r.SalesOrderId == salesOrderId
                        && (r.Status == ReservationStatus.Active || r.Status == ReservationStatus.PartiallyConsumed))
            .GroupBy(r => new { r.ItemId, r.LotCode })
            .Select(g => new { g.Key.ItemId, g.Key.LotCode, Qty = g.Sum(r => r.ReservedBaseQuantity - r.ConsumedBaseQuantity) })
            .ToDictionaryAsync(x => (x.ItemId, x.LotCode), x => x.Qty, ct);

        var latestInspections = await db.QualityInspections.AsNoTracking()
            .Where(q => q.OrganizationId == organizationId
                        && orderItemIds.Contains(q.ItemId)
                        && q.Status == QualityInspectionStatus.Completed)
            .OrderByDescending(q => q.Id)
            .Select(q => new { q.Id, q.ItemId, q.LotCode, q.CertificateNumber })
            .ToListAsync(ct);

        var coaByLot = new Dictionary<(long ItemId, string LotCode), (long Id, string? Cert)>();
        foreach (var q in latestInspections)
        {
            coaByLot.TryAdd((q.ItemId, q.LotCode), (q.Id, q.CertificateNumber));
        }

        return balances.Select(x =>
        {
            var key = (x.b.ItemId, x.b.LotCode);
            var orderResQty = openOrderRes.GetValueOrDefault(key);
            var available = x.b.AvailableBaseQuantity + orderResQty;
            var meta = lotMeta.GetValueOrDefault(key);
            var isExpired = meta is not null && meta.IsExpiredOn(asOfDate);
            var coa = coaByLot.GetValueOrDefault(key);

            return new DispatchableLotSummary(
                x.b.WarehouseId,
                x.Code,
                x.Name,
                x.b.ItemId,
                x.b.LotCode,
                x.b.OnHandBaseQuantity,
                x.b.BlockedBaseQuantity,
                x.b.ReservedBaseQuantity,
                orderResQty,
                available,
                meta?.ExpirationDate,
                isExpired,
                coa.Id > 0 ? coa.Id : null,
                coa.Cert);
        })
        .OrderBy(x => x.ItemId).ThenBy(x => x.LotCode)
        .ToArray();
    }, ct);

    public Task<OrganizationResult<DeliveryOrderDetails>> PostDeliveryOrder(
        long organizationId,
        long salesOrderId,
        PostDeliveryOrderRequest request,
        IReadOnlyList<ResolvedDeliveryOrderLine> resolvedLines,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<DeliveryOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[SalesOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {salesOrderId}", ct);

        var order = await db.SalesOrders.Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.OrganizationId == organizationId && o.Id == salesOrderId, ct);
        if (order is null) return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.NotFound);

        var site = await db.Sites.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == order.SiteId, ct);
        if (site is null || !site.IsActive)
            return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.SiteInvalid);

        var wh = await db.Warehouses.AsNoTracking()
            .SingleOrDefaultAsync(w => w.OrganizationId == organizationId && w.Id == request.WarehouseId, ct);
        if (wh is null || !wh.IsActive || wh.SiteId != order.SiteId)
            return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.WarehouseInvalid);

        var dispatchNum = await SqlDocumentSequenceStore.AllocateAsync(
            db, organizationId, DocumentSequenceType.DeliveryOrder, request.DispatchNumber, utcNow, ct);
        var dupDispatch = await db.DeliveryOrders.AsNoTracking()
            .AnyAsync(d => d.OrganizationId == organizationId && d.DispatchNumber == dispatchNum, ct);
        if (dupDispatch)
            return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNumberDuplicate);

        var itemIds = resolvedLines.Select(l => l.ItemId).Distinct().ToArray();
        var lotMeta = await db.ItemLots.AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && itemIds.Contains(l.ItemId))
            .ToDictionaryAsync(l => (l.ItemId, l.LotCode), ct);

        var latestInspections = await db.QualityInspections.AsNoTracking()
            .Where(q => q.OrganizationId == organizationId
                        && itemIds.Contains(q.ItemId)
                        && q.Status == QualityInspectionStatus.Completed)
            .OrderByDescending(q => q.Id)
            .Select(q => new { q.Id, q.ItemId, q.LotCode, q.CertificateNumber })
            .ToListAsync(ct);

        var coaByLot = new Dictionary<(long ItemId, string LotCode), (long Id, string? Cert)>();
        foreach (var q in latestInspections)
            coaByLot.TryAdd((q.ItemId, q.LotCode), (q.Id, q.CertificateNumber));

        // Lock matching inventory balances & open SalesOrder reservations
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[InventoryBalance] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [WarehouseId] = {request.WarehouseId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[StockReservation] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [SalesOrderId] = {salesOrderId}", ct);

        var balances = await db.InventoryBalances
            .Where(b => b.OrganizationId == organizationId && b.WarehouseId == request.WarehouseId && itemIds.Contains(b.ItemId))
            .ToDictionaryAsync(b => (b.ItemId, b.LotCode), ct);

        var openReservations = await db.StockReservations
            .Where(r => r.OrganizationId == organizationId
                        && r.WarehouseId == request.WarehouseId
                        && r.SourceKind == ReservationSourceKind.SalesOrder
                        && r.SalesOrderId == salesOrderId
                        && (r.Status == ReservationStatus.Active || r.Status == ReservationStatus.PartiallyConsumed))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var deliveryLinePayloads = new List<(long SalesOrderLineId, long ItemId, string LotCode, decimal DispatchedBaseQuantity, long BaseUnitIdSnapshot, long? QualityInspectionId, string? CertificateNumberSnapshot, string? Notes)>(resolvedLines.Count);
        var orderDeltas = new Dictionary<long, decimal>();

        foreach (var line in resolvedLines)
        {
            var key = (line.ItemId, line.LotCode);
            if (lotMeta.TryGetValue(key, out var meta) && meta.IsExpiredOn(request.DispatchDate))
                return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.LotExpired);

            if (!balances.TryGetValue(key, out var balance))
                return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.LotQuarantinedOrInsufficient);

            // Consume matching SalesOrder reservations first
            var remainingToCover = line.DispatchedBaseQuantity;
            var matchingRes = openReservations.Where(r =>
                r.ItemId == line.ItemId
                && r.LotCode == line.LotCode
                && r.IsOpen
                && (!r.SalesOrderLineId.HasValue || r.SalesOrderLineId.Value == line.SalesOrderLineId));

            decimal consumedFromRes = 0m;
            foreach (var res in matchingRes)
            {
                if (remainingToCover <= 0m) break;
                var took = res.Consume(remainingToCover);
                if (took > 0m)
                {
                    consumedFromRes += took;
                    remainingToCover = decimal.Round(remainingToCover - took, 6, MidpointRounding.AwayFromZero);
                }
            }

            if (consumedFromRes > 0m)
                balance.ReleaseReservedQuantity(consumedFromRes, utcNow);

            if (line.DispatchedBaseQuantity > balance.AvailableBaseQuantity)
                return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.LotQuarantinedOrInsufficient);

            balance.ApplyDelta(-line.DispatchedBaseQuantity, utcNow, enforceAvailableOnOutbound: true);

            coaByLot.TryGetValue(key, out var coa);
            deliveryLinePayloads.Add((
                line.SalesOrderLineId,
                line.ItemId,
                line.LotCode,
                line.DispatchedBaseQuantity,
                line.BaseUnitIdSnapshot,
                coa.Id > 0 ? coa.Id : null,
                coa.Cert,
                line.Notes));

            orderDeltas[line.SalesOrderLineId] = orderDeltas.GetValueOrDefault(line.SalesOrderLineId) + line.DispatchedBaseQuantity;
        }

        order.ApplyDispatches(orderDeltas.Select(kv => (kv.Key, kv.Value)).ToArray());

        var deliveryOrder = DeliveryOrder.CreatePosted(
            organizationId,
            order.SiteId,
            order.Id,
            request.WarehouseId,
            dispatchNum,
            request.DispatchDate,
            request.CarrierName,
            request.TrackingNumber,
            request.Notes,
            utcNow,
            deliveryLinePayloads);

        db.DeliveryOrders.Add(deliveryOrder);
        await db.SaveInOperation(access.Actor, ct);

        // Record InventoryTransaction + InventoryLedgerEntry linked to DeliveryOrder.Id
        var txnDocNum = dispatchNum.Length <= 28 ? $"DSP-{dispatchNum}" : dispatchNum;
        var txn = InventoryTransaction.Create(
            organizationId,
            order.SiteId,
            request.WarehouseId,
            null,
            txnDocNum,
            InventoryTransactionType.SalesDispatch,
            null,
            null,
            $"Sales Order {order.OrderNumber} / Dispatch {deliveryOrder.DispatchNumber}",
            null,
            utcNow,
            null,
            null,
            deliveryOrder.Id);

        db.InventoryTransactions.Add(txn);
        await db.SaveInOperation(access.Actor, ct);

        for (var i = 0; i < deliveryOrder.Lines.Count; i++)
        {
            var dl = deliveryOrder.Lines[i];
            var ledger = InventoryLedgerEntry.Create(
                organizationId,
                txn.Id,
                order.SiteId,
                request.WarehouseId,
                i + 1,
                dl.ItemId,
                dl.LotCode,
                dl.DispatchedBaseQuantity,
                dl.BaseUnitIdSnapshot,
                -dl.DispatchedBaseQuantity,
                dl.BaseUnitIdSnapshot,
                utcNow,
                dl.Notes);
            db.InventoryLedgerEntries.Add(ledger);
        }

        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<DeliveryOrderDetails>.Success((await BuildDeliveryDetailsAsync(db, organizationId, deliveryOrder.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<DeliveryOrderDetails>> ReverseDeliveryOrder(
        long organizationId,
        long deliveryOrderId,
        string reason,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<DeliveryOrderDetails>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[DeliveryOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {deliveryOrderId}", ct);

        var delivery = await db.DeliveryOrders.Include(d => d.Lines)
            .SingleOrDefaultAsync(d => d.OrganizationId == organizationId && d.Id == deliveryOrderId, ct);
        if (delivery is null) return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNotFound);
        if (!Matches(db, delivery, version)) return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[SalesOrder] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {delivery.SalesOrderId}", ct);

        var order = await db.SalesOrders.Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.OrganizationId == organizationId && o.Id == delivery.SalesOrderId, ct);
        if (order is null) return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.NotFound);
        if (order.Status is SalesOrderStatus.Closed or SalesOrderStatus.Cancelled)
            return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.InvalidTransition);

        delivery.Reverse(reason, utcNow);

        var revertDeltas = delivery.Lines
            .GroupBy(l => l.SalesOrderLineId)
            .Select(g => (g.Key, g.Sum(x => x.DispatchedBaseQuantity)))
            .ToArray();
        order.RevertDispatches(revertDeltas);

        var itemIds = delivery.Lines.Select(l => l.ItemId).Distinct().ToArray();
        var balances = await db.InventoryBalances
            .Where(b => b.OrganizationId == organizationId && b.WarehouseId == delivery.WarehouseId && itemIds.Contains(b.ItemId))
            .ToDictionaryAsync(b => (b.ItemId, b.LotCode), ct);

        foreach (var line in delivery.Lines)
        {
            if (!balances.TryGetValue((line.ItemId, line.LotCode), out var bal))
            {
                bal = InventoryBalance.CreateZero(organizationId, delivery.SiteId, delivery.WarehouseId, line.ItemId, line.LotCode, line.BaseUnitIdSnapshot, utcNow);
                db.InventoryBalances.Add(bal);
                balances[(line.ItemId, line.LotCode)] = bal;
            }
            bal.ApplyDelta(line.DispatchedBaseQuantity, utcNow, enforceAvailableOnOutbound: false);
        }

        var origTxn = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.OrganizationId == organizationId && t.DeliveryOrderId == delivery.Id && t.TransactionType == InventoryTransactionType.SalesDispatch)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);

        var revDocNum = delivery.DispatchNumber.Length <= 28 ? $"REV-{delivery.DispatchNumber}" : $"R{delivery.DispatchNumber[..31]}";
        var revTxn = InventoryTransaction.Create(
            organizationId,
            delivery.SiteId,
            delivery.WarehouseId,
            null,
            revDocNum,
            InventoryTransactionType.Reversal,
            null,
            origTxn?.Id,
            $"Reversal of Dispatch {delivery.DispatchNumber}: {reason}",
            null,
            utcNow,
            null,
            null,
            delivery.Id);

        db.InventoryTransactions.Add(revTxn);
        await db.SaveInOperation(access.Actor, ct);

        for (var i = 0; i < delivery.Lines.Count; i++)
        {
            var dl = delivery.Lines[i];
            db.InventoryLedgerEntries.Add(InventoryLedgerEntry.Create(
                organizationId,
                revTxn.Id,
                delivery.SiteId,
                delivery.WarehouseId,
                i + 1,
                dl.ItemId,
                dl.LotCode,
                dl.DispatchedBaseQuantity,
                dl.BaseUnitIdSnapshot,
                dl.DispatchedBaseQuantity,
                dl.BaseUnitIdSnapshot,
                utcNow,
                reason));
        }

        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<DeliveryOrderDetails>.Success((await BuildDeliveryDetailsAsync(db, organizationId, delivery.Id, ct))!);
    }, ct);

    private static async Task ReleaseOpenSalesReservationsAsync(
        OrganizationDbContext db,
        long organizationId,
        long salesOrderId,
        DateTime utcNow,
        CancellationToken ct)
    {
        var openRes = await db.StockReservations
            .Where(r => r.OrganizationId == organizationId
                        && r.SourceKind == ReservationSourceKind.SalesOrder
                        && r.SalesOrderId == salesOrderId
                        && (r.Status == ReservationStatus.Active || r.Status == ReservationStatus.PartiallyConsumed))
            .ToListAsync(ct);

        if (openRes.Count == 0) return;

        var whIds = openRes.Select(r => r.WarehouseId).Distinct().ToArray();
        var itemIds = openRes.Select(r => r.ItemId).Distinct().ToArray();
        var balances = await db.InventoryBalances
            .Where(b => b.OrganizationId == organizationId && whIds.Contains(b.WarehouseId) && itemIds.Contains(b.ItemId))
            .ToDictionaryAsync(b => (b.WarehouseId, b.ItemId, b.LotCode), ct);

        foreach (var res in openRes)
        {
            var releasedQty = res.Release(utcNow);
            if (releasedQty > 0m && balances.TryGetValue((res.WarehouseId, res.ItemId, res.LotCode), out var bal))
                bal.ReleaseReservedQuantity(releasedQty, utcNow);
        }
    }

    private static async Task<SalesOrderDetails?> BuildOrderDetailsAsync(OrganizationDbContext db, long organizationId, long salesOrderId, CancellationToken ct)
    {
        var row = await (from so in db.SalesOrders.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { so.OrganizationId, so.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         join c in db.Customers.AsNoTracking() on new { so.OrganizationId, so.CustomerId } equals new { c.OrganizationId, CustomerId = c.Id }
                         join wh in db.Warehouses.AsNoTracking() on new { so.OrganizationId, WhId = so.SourceWarehouseId } equals new { wh.OrganizationId, WhId = (long?)wh.Id } into whJoin
                         from wh in whJoin.DefaultIfEmpty()
                         where so.OrganizationId == organizationId && so.Id == salesOrderId
                         select new
                         {
                             so,
                             SiteCode = s.Code,
                             SiteName = s.Name,
                             CustomerCode = c.Code,
                             CustomerName = c.Name,
                             WhCode = wh != null ? wh.Code : null,
                             WhName = wh != null ? wh.Name : null,
                             CreatedAtUtc = EF.Property<DateTime>(so, "CreatedAtUtc"),
                             RowVersion = EF.Property<byte[]>(so, "RowVersion")
                         }).SingleOrDefaultAsync(ct);

        if (row is null) return null;

        var lines = await (from l in db.SalesOrderLines.AsNoTracking()
                           join i in db.Items.AsNoTracking() on new { l.OrganizationId, l.ItemId } equals new { i.OrganizationId, ItemId = i.Id }
                           join ou in db.Units.AsNoTracking() on new { l.OrganizationId, UnitId = l.OrderedUnitId } equals new { ou.OrganizationId, UnitId = ou.Id }
                           join bu in db.Units.AsNoTracking() on new { l.OrganizationId, UnitId = l.BaseUnitIdSnapshot } equals new { bu.OrganizationId, UnitId = bu.Id }
                           where l.OrganizationId == organizationId && l.SalesOrderId == salesOrderId
                           orderby l.LineNumber
                           select new SalesOrderLineDetails(
                               l.Id,
                               l.LineNumber,
                               l.ItemId,
                               i.Code,
                               i.Name,
                               i.RequiresLotTracking,
                               i.QualityControlMode,
                               l.OrderedQuantity,
                               l.OrderedUnitId,
                               ou.Code,
                               ou.Symbol,
                               l.BaseUnitIdSnapshot,
                               bu.Code,
                               bu.Symbol,
                               l.OrderedBaseQuantity,
                               l.DispatchedBaseQuantity,
                               l.RemainingBaseQuantity,
                               l.UnitPrice,
                               l.Notes)).ToListAsync(ct);

        var dispatches = await (from d in db.DeliveryOrders.AsNoTracking()
                                join wh in db.Warehouses.AsNoTracking() on new { d.OrganizationId, d.WarehouseId } equals new { wh.OrganizationId, WarehouseId = wh.Id }
                                where d.OrganizationId == organizationId && d.SalesOrderId == salesOrderId
                                orderby d.Id descending
                                select new
                                {
                                    d,
                                    WhCode = wh.Code,
                                    WhName = wh.Name,
                                    RowVersion = EF.Property<byte[]>(d, "RowVersion")
                                }).ToListAsync(ct);

        var dispIds = dispatches.Select(x => x.d.Id).ToArray();
        var dispStats = dispIds.Length == 0
            ? new Dictionary<long, (decimal Qty, int Count)>()
            : (await db.DeliveryOrderLines.AsNoTracking()
                .Where(l => l.OrganizationId == organizationId && dispIds.Contains(l.DeliveryOrderId))
                .GroupBy(l => l.DeliveryOrderId)
                .Select(g => new { Id = g.Key, Qty = g.Sum(x => x.DispatchedBaseQuantity), Count = g.Count() })
                .ToListAsync(ct))
                .ToDictionary(x => x.Id, x => (x.Qty, x.Count));

        var dispatchSummaries = dispatches.Select(x =>
        {
            var st = dispStats.GetValueOrDefault(x.d.Id);
            return new DeliveryOrderSummary(
                x.d.Id,
                x.d.OrganizationId,
                x.d.SiteId,
                row.SiteCode,
                row.SiteName,
                x.d.SalesOrderId,
                row.so.OrderNumber,
                row.so.CustomerId,
                row.CustomerCode,
                row.CustomerName,
                x.d.WarehouseId,
                x.WhCode,
                x.WhName,
                x.d.DispatchNumber,
                x.d.Status,
                x.d.DispatchDate,
                x.d.CarrierName,
                x.d.TrackingNumber,
                st.Qty,
                st.Count,
                x.d.Notes,
                x.d.PostedAtUtc,
                x.d.ReversedAtUtc,
                x.d.ReversalReason,
                Convert.ToBase64String(x.RowVersion));
        }).ToArray();

        decimal? totalEst = lines.Any(l => l.UnitPrice.HasValue)
            ? lines.Sum(l => (l.UnitPrice ?? 0m) * l.OrderedQuantity)
            : null;

        var summary = new SalesOrderSummary(
            row.so.Id,
            row.so.OrganizationId,
            row.so.SiteId,
            row.SiteCode,
            row.SiteName,
            row.so.CustomerId,
            row.CustomerCode,
            row.CustomerName,
            row.so.OrderNumber,
            row.so.Status,
            row.so.OrderDate,
            row.so.RequestedDeliveryDate,
            row.so.SourceWarehouseId,
            row.WhCode,
            row.WhName,
            row.so.CurrencyCode,
            row.so.ShippingAddressSnapshot,
            lines.Sum(l => l.OrderedBaseQuantity),
            lines.Sum(l => l.DispatchedBaseQuantity),
            totalEst,
            lines.Count,
            row.so.Notes,
            row.so.CancellationReason,
            row.CreatedAtUtc,
            row.so.ApprovedAtUtc,
            row.so.ClosedAtUtc,
            row.so.CancelledAtUtc,
            Convert.ToBase64String(row.RowVersion));

        return new SalesOrderDetails(summary, lines, dispatchSummaries);
    }

    private static async Task<DeliveryOrderDetails?> BuildDeliveryDetailsAsync(OrganizationDbContext db, long organizationId, long deliveryOrderId, CancellationToken ct)
    {
        var row = await (from d in db.DeliveryOrders.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { d.OrganizationId, d.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         join so in db.SalesOrders.AsNoTracking() on new { d.OrganizationId, d.SalesOrderId } equals new { so.OrganizationId, SalesOrderId = so.Id }
                         join c in db.Customers.AsNoTracking() on new { so.OrganizationId, so.CustomerId } equals new { c.OrganizationId, CustomerId = c.Id }
                         join wh in db.Warehouses.AsNoTracking() on new { d.OrganizationId, d.WarehouseId } equals new { wh.OrganizationId, WarehouseId = wh.Id }
                         where d.OrganizationId == organizationId && d.Id == deliveryOrderId
                         select new
                         {
                             d,
                             SiteCode = s.Code,
                             SiteName = s.Name,
                             so.OrderNumber,
                             so.CustomerId,
                             CustomerCode = c.Code,
                             CustomerName = c.Name,
                             WhCode = wh.Code,
                             WhName = wh.Name,
                             RowVersion = EF.Property<byte[]>(d, "RowVersion")
                         }).SingleOrDefaultAsync(ct);

        if (row is null) return null;

        var lines = await (from l in db.DeliveryOrderLines.AsNoTracking()
                           join i in db.Items.AsNoTracking() on new { l.OrganizationId, l.ItemId } equals new { i.OrganizationId, ItemId = i.Id }
                           join bu in db.Units.AsNoTracking() on new { l.OrganizationId, UnitId = l.BaseUnitIdSnapshot } equals new { bu.OrganizationId, UnitId = bu.Id }
                           where l.OrganizationId == organizationId && l.DeliveryOrderId == deliveryOrderId
                           orderby l.LineNumber
                           select new DeliveryOrderLineDetails(
                               l.Id,
                               l.LineNumber,
                               l.SalesOrderLineId,
                               l.ItemId,
                               i.Code,
                               i.Name,
                               l.LotCode,
                               l.DispatchedBaseQuantity,
                               l.BaseUnitIdSnapshot,
                               bu.Code,
                               bu.Symbol,
                               l.QualityInspectionId,
                               l.CertificateNumberSnapshot,
                               l.Notes)).ToListAsync(ct);

        var summary = new DeliveryOrderSummary(
            row.d.Id,
            row.d.OrganizationId,
            row.d.SiteId,
            row.SiteCode,
            row.SiteName,
            row.d.SalesOrderId,
            row.OrderNumber,
            row.CustomerId,
            row.CustomerCode,
            row.CustomerName,
            row.d.WarehouseId,
            row.WhCode,
            row.WhName,
            row.d.DispatchNumber,
            row.d.Status,
            row.d.DispatchDate,
            row.d.CarrierName,
            row.d.TrackingNumber,
            lines.Sum(l => l.DispatchedBaseQuantity),
            lines.Count,
            row.d.Notes,
            row.d.PostedAtUtc,
            row.d.ReversedAtUtc,
            row.d.ReversalReason,
            Convert.ToBase64String(row.RowVersion));

        return new DeliveryOrderDetails(summary, lines);
    }

    private static CustomerSummary ToCustomerSummary(Customer c, byte[] rowVersion) => new(
        c.Id,
        c.OrganizationId,
        c.Code,
        c.Name,
        c.ContactName,
        c.Phone,
        c.Email,
        c.ShippingAddress,
        c.TaxCode,
        c.Notes,
        c.IsActive,
        Convert.ToBase64String(rowVersion));

    private async Task<T> Read<T>(Func<OrganizationDbContext, Task<T>> action, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory().CreateDbContextAsync(ct);
            return await action(db);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlSalesOperationsStore));
            throw new SalesStoreUnavailableException(SalesErrors.PersistenceUnavailable, ex);
        }
    }

    private async Task<OrganizationResult<T>> Write<T>(
        OrganizationWriteAccess access,
        Func<OrganizationDbContext, Task<OrganizationResult<T>>> action,
        CancellationToken ct)
    {
        if (!await access.Revalidate(ct))
            return OrganizationResult<T>.Failure(SalesErrors.AccessDenied);

        try
        {
            await using var db = await dbFactory().CreateDbContextAsync(ct);
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var commitAttempted = false;
                try
                {
                    var result = await action(db);
                    if (!result.Succeeded) return result;
                    commitAttempted = true;
                    await transaction.CommitAsync(ct);
                    return result;
                }
                catch (DbUpdateConcurrencyException)
                {
                    return OrganizationResult<T>.Failure(SalesErrors.ConcurrencyConflict);
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
                    return OrganizationResult<T>.Failure(SalesErrors.AuditUnavailable);
                }
                catch (Exception ex) when (commitAttempted && ex is not OrganizationCommitOutcomeUnknownException)
                {
                    throw new OrganizationCommitOutcomeUnknownException(ex);
                }
            });
        }
        catch (SalesRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(SalesErrors.ConcurrencyConflict); }
        catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(SalesErrors.OutcomeUnknown); }
        catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
        { return OrganizationResult<T>.Failure(SalesErrors.AuditUnavailable); }
        catch (DbUpdateException ex) when (IsUniqueConflict(ex))
        { return OrganizationResult<T>.Failure(ClassifyUniqueConflict(ex)); }
        catch (InvalidOperationException ex) when (ex.Message == SalesErrors.ParentOrganizationInactive)
        { return OrganizationResult<T>.Failure(SalesErrors.ParentOrganizationInactive); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationSiteErrors.OrganizationNotFound)
        { return OrganizationResult<T>.Failure(SalesErrors.NotFound); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationAccessErrors.AccessDenied)
        { return OrganizationResult<T>.Failure(SalesErrors.AccessDenied); }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlSalesOperationsStore), access.Actor.ActorId);
            return OrganizationResult<T>.Failure(SalesErrors.PersistenceUnavailable);
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
            return (false, exists ? SalesErrors.ParentOrganizationInactive : SalesErrors.NotFound);
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
        if (msg.Contains("UX_Customer_Org_Code", StringComparison.Ordinal)) return SalesErrors.CustomerCodeDuplicate;
        if (msg.Contains("UX_SalesOrder_Org_Number", StringComparison.Ordinal)) return SalesErrors.OrderNumberDuplicate;
        if (msg.Contains("UX_DeliveryOrder_Org_Number", StringComparison.Ordinal)) return SalesErrors.DispatchNumberDuplicate;
        return SalesErrors.ConcurrencyConflict;
    }

    private static bool IsCheckViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql && sql.Number == 547;

    private static string ClassifyCheckViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? string.Empty;
        if (msg.Contains("CK_SalesOrderLine_Qty", StringComparison.Ordinal)) return SalesErrors.OverDispatch;
        if (msg.Contains("CK_InventoryBalance_NonNegative", StringComparison.Ordinal) || msg.Contains("CK_InventoryBalance_Blocked", StringComparison.Ordinal))
            return SalesErrors.LotQuarantinedOrInsufficient;
        return SalesErrors.ConcurrencyConflict;
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
