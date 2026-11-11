using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Routings;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlProductionOrderOperationsStore(
    Func<IDbContextFactory<OrganizationDbContext>> getFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null,
    IUserNotificationPublisher? notifications = null)
    : IProductionOrderOperationsStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public SqlProductionOrderOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public Task<OrganizationPage<ProductionOrderSummary>> ListOrders(
        long organizationId,
        ProductionOrderListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from po in db.ProductionOrders.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { po.OrganizationId, SiteId = po.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        join it in db.Items.AsNoTracking() on new { po.OrganizationId, ItemId = po.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                        join ru in db.Units.AsNoTracking() on new { po.OrganizationId, UnitId = po.RequestedUnitId } equals new { ru.OrganizationId, UnitId = ru.Id }
                        join bu in db.Units.AsNoTracking() on new { po.OrganizationId, UnitId = po.BaseOutputUnitIdSnapshot } equals new { bu.OrganizationId, UnitId = bu.Id }
                        where po.OrganizationId == organizationId
                        select new
                        {
                            po,
                            s,
                            it,
                            ru,
                            bu,
                            CreatedAtUtc = EF.Property<DateTime>(po, "CreatedAtUtc"),
                            RowVersion = EF.Property<byte[]>(po, "RowVersion")
                        };

        if (query.SiteId is long siteId && siteId > 0)
            baseQuery = baseQuery.Where(x => x.po.SiteId == siteId);
        if (query.Status is ProductionOrderStatus status)
            baseQuery = baseQuery.Where(x => x.po.Status == status);
        if (query.ItemId is long itemId && itemId > 0)
            baseQuery = baseQuery.Where(x => x.po.ItemId == itemId);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.po.OrderNumber.Contains(query.Search)
                || x.po.BomCodeSnapshot.Contains(query.Search)
                || x.it.Code.Contains(query.Search)
                || x.it.Name.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderByDescending(x => x.po.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => ToSummary(r.po, r.s, r.it, r.ru, r.bu, r.CreatedAtUtc, r.RowVersion)).ToArray();
        return new OrganizationPage<ProductionOrderSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<ProductionOrderDetails?> GetOrder(long organizationId, long id, CancellationToken ct) =>
        Read(db => BuildOrderDetailsAsync(db, organizationId, id, ct), ct);

    public Task<ProductionOrderSiteInfo?> GetSiteInfo(long organizationId, long siteId, CancellationToken ct) => Read(async db =>
        await (from s in db.Sites.AsNoTracking()
               join o in db.Organizations.IgnoreAutoIncludes().AsNoTracking() on s.OrganizationId equals o.Id
               where s.OrganizationId == organizationId && s.Id == siteId
               select new ProductionOrderSiteInfo(s.Id, s.OrganizationId, s.Code, s.Name, s.IsActive, o.IsActive))
            .SingleOrDefaultAsync(ct), ct);

    public Task<IReadOnlyList<ProductionOrderSiteInfo>> ListActiveSites(long organizationId, CancellationToken ct) => Read<IReadOnlyList<ProductionOrderSiteInfo>>(async db =>
        await (from s in db.Sites.AsNoTracking()
               join o in db.Organizations.IgnoreAutoIncludes().AsNoTracking() on s.OrganizationId equals o.Id
               where s.OrganizationId == organizationId && s.IsActive
               orderby s.Code, s.Id
               select new ProductionOrderSiteInfo(s.Id, s.OrganizationId, s.Code, s.Name, s.IsActive, o.IsActive))
            .ToListAsync(ct), ct);

    public Task<OrganizationResult<ProductionOrderDetails>> CreateDraft(
        CreateProductionOrderRequest request,
        ResolvedBomSnapshotWrite snapshot,
        OrganizationWriteAccess access,
        CancellationToken ct,
        ResolvedRoutingSnapshotWrite? routingSnapshot = null) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<ProductionOrderDetails>.Failure(orgCheck.Error!);

        var site = await db.Sites.SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SiteId, ct);
        if (site is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.SiteInvalid);
        if (!site.IsActive) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.SiteInactive);

        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.OrganizationId == request.OrganizationId && i.Id == request.ItemId, ct);
        if (item is null || !item.IsActive) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ItemInvalid);
        if (!item.CanBeManufactured) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ItemNotManufacturable);

        var reqUnit = await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == request.OrganizationId && u.Id == snapshot.EffectiveRequestedUnitId, ct);
        if (reqUnit is null || !reqUnit.IsActive) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.UnitInvalid);

        var order = ProductionOrder.CreateDraft(
            request.OrganizationId,
            request.SiteId,
            request.OrderNumber,
            request.ItemId,
            request.RequestedQuantity,
            snapshot.EffectiveRequestedUnitId,
            request.PlannedStartDate,
            request.PlannedDueDate,
            snapshot.BomId,
            snapshot.BomCode,
            snapshot.BomVersionId,
            snapshot.VersionNumber,
            snapshot.BaseOutputQuantity,
            snapshot.BaseOutputUnitId,
            request.Notes);

        if (routingSnapshot is not null)
            order.ApplyRoutingSnapshot(routingSnapshot.RoutingId, routingSnapshot.RoutingCode);

        db.ProductionOrders.Add(order);
        await db.SaveInOperation(access.Actor, ct);

        ReplaceSnapshotChildren(db, request.OrganizationId, order.SiteId, order.Id, order.Status, snapshot, routingSnapshot);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<ProductionOrderDetails>.Success((await BuildOrderDetailsAsync(db, request.OrganizationId, order.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> UpdateDraft(
        long organizationId,
        long id,
        byte[] version,
        UpdateDraftProductionOrderRequest request,
        ResolvedBomSnapshotWrite snapshot,
        OrganizationWriteAccess access,
        CancellationToken ct,
        ResolvedRoutingSnapshotWrite? routingSnapshot = null) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<ProductionOrderDetails>.Failure(orgCheck.Error!);

        var order = await db.ProductionOrders.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (order is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (!Matches(db, order, version)) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        order.UpdateDraftHeader(request.RequestedQuantity, snapshot.EffectiveRequestedUnitId, request.PlannedStartDate, request.PlannedDueDate, request.Notes);
        order.ApplyResolvedBomSnapshot(snapshot.BomId, snapshot.BomCode, snapshot.BomVersionId, snapshot.VersionNumber, snapshot.BaseOutputQuantity, snapshot.BaseOutputUnitId);
        order.ApplyRoutingSnapshot(routingSnapshot?.RoutingId, routingSnapshot?.RoutingCode);

        await ClearSnapshotChildrenAsync(db, organizationId, order.Id, ct);
        ReplaceSnapshotChildren(db, organizationId, order.SiteId, order.Id, order.Status, snapshot, routingSnapshot);

        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<ProductionOrderDetails>.Success((await BuildOrderDetailsAsync(db, organizationId, order.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> RefreshDraftSnapshot(
        long organizationId,
        long id,
        byte[] version,
        ResolvedBomSnapshotWrite snapshot,
        OrganizationWriteAccess access,
        CancellationToken ct,
        ResolvedRoutingSnapshotWrite? routingSnapshot = null) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<ProductionOrderDetails>.Failure(orgCheck.Error!);

        var order = await db.ProductionOrders.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (order is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (!Matches(db, order, version)) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        order.ApplyResolvedBomSnapshot(snapshot.BomId, snapshot.BomCode, snapshot.BomVersionId, snapshot.VersionNumber, snapshot.BaseOutputQuantity, snapshot.BaseOutputUnitId);
        order.ApplyRoutingSnapshot(routingSnapshot?.RoutingId, routingSnapshot?.RoutingCode);
        await ClearSnapshotChildrenAsync(db, organizationId, order.Id, ct);
        ReplaceSnapshotChildren(db, organizationId, order.SiteId, order.Id, order.Status, snapshot, routingSnapshot);

        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<ProductionOrderDetails>.Success((await BuildOrderDetailsAsync(db, organizationId, order.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> ReleaseOrder(
        long organizationId,
        long id,
        byte[] version,
        ResolvedBomSnapshotWrite snapshot,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct,
        ResolvedRoutingSnapshotWrite? routingSnapshot = null)
        => TransitionAndNotifyAsync(organizationId, id, version, NotificationSeverity.Info, access, async (db, order) =>
        {
            var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == order.SiteId, ct);
            if (site is null || !site.IsActive) return OrganizationResult<bool>.Failure(ProductionOrderErrors.SiteInactive);
            order.ApplyResolvedBomSnapshot(snapshot.BomId, snapshot.BomCode, snapshot.BomVersionId, snapshot.VersionNumber, snapshot.BaseOutputQuantity, snapshot.BaseOutputUnitId);
            order.ApplyRoutingSnapshot(routingSnapshot?.RoutingId, routingSnapshot?.RoutingCode);
            order.Release(utcNow);
            await ClearSnapshotChildrenAsync(db, organizationId, order.Id, ct);
            ReplaceSnapshotChildren(db, organizationId, order.SiteId, order.Id, order.Status, snapshot, routingSnapshot);
            return OrganizationResult<bool>.Success(true);
        }, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> StartOrder(
        long organizationId,
        long id,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => TransitionAndNotifyAsync(organizationId, id, version, NotificationSeverity.Info, access, order => order.Start(utcNow), ct);

    public Task<OrganizationResult<ProductionOrderDetails>> RecordActuals(
        long organizationId,
        long id,
        byte[] version,
        decimal producedQuantity,
        decimal scrapQuantity,
        string? notes,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<ProductionOrderDetails>.Failure(orgCheck.Error!);

        var order = await db.ProductionOrders.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (order is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
        if (!Matches(db, order, version)) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

        order.RecordActuals(producedQuantity, scrapQuantity, notes);
        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<ProductionOrderDetails>.Success((await BuildOrderDetailsAsync(db, organizationId, order.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> CompleteOrder(
        long organizationId,
        long id,
        byte[] version,
        decimal producedQuantity,
        decimal scrapQuantity,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => TransitionAndNotifyAsync(organizationId, id, version, NotificationSeverity.Success, access, async (db, order) =>
        {
            order.Complete(producedQuantity, scrapQuantity, notes, utcNow);
            await SqlStockReservationStore.ReleaseAllForOrderInDbAsync(db, organizationId, order.Id, utcNow, ct);
            return OrganizationResult<bool>.Success(true);
        }, ct);

    public Task<OrganizationResult<ProductionOrderDetails>> CancelOrder(
        long organizationId,
        long id,
        byte[] version,
        string? reason,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => TransitionAndNotifyAsync(organizationId, id, version, NotificationSeverity.Warning, access, async (db, order) =>
        {
            order.Cancel(reason, utcNow);
            await SqlStockReservationStore.ReleaseAllForOrderInDbAsync(db, organizationId, order.Id, utcNow, ct);
            return OrganizationResult<bool>.Success(true);
        }, ct);

    private Task<OrganizationResult<ProductionOrderDetails>> TransitionAndNotifyAsync(
        long organizationId,
        long id,
        byte[] version,
        NotificationSeverity severity,
        OrganizationWriteAccess access,
        Action<ProductionOrder> apply,
        CancellationToken ct)
        => TransitionAndNotifyAsync(organizationId, id, version, severity, access, (_, order) =>
        {
            apply(order);
            return Task.FromResult(OrganizationResult<bool>.Success(true));
        }, ct);

    private async Task<OrganizationResult<ProductionOrderDetails>> TransitionAndNotifyAsync(
        long organizationId,
        long id,
        byte[] version,
        NotificationSeverity severity,
        OrganizationWriteAccess access,
        Func<OrganizationDbContext, ProductionOrder, Task<OrganizationResult<bool>>> applyAsync,
        CancellationToken ct)
    {
        long creatorId = 0;
        string orderNumber = string.Empty;
        ProductionOrderStatus previousStatus = default;
        ProductionOrderStatus nextStatus = default;
        var result = await Write(access, async db =>
        {
            var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (!orgCheck.Ok) return OrganizationResult<ProductionOrderDetails>.Failure(orgCheck.Error!);

            var order = await db.ProductionOrders.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
            if (order is null) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.NotFound);
            if (!Matches(db, order, version)) return OrganizationResult<ProductionOrderDetails>.Failure(ProductionOrderErrors.ConcurrencyConflict);

            creatorId = db.Entry(order).Property<long>("CreatedByActorId").CurrentValue;
            orderNumber = order.OrderNumber;
            previousStatus = order.Status;

            var check = await applyAsync(db, order);
            if (!check.Succeeded) return OrganizationResult<ProductionOrderDetails>.Failure(check.Error!.Code);

            nextStatus = order.Status;
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<ProductionOrderDetails>.Success((await BuildOrderDetailsAsync(db, organizationId, order.Id, ct))!);
        }, ct);

        if (result.Succeeded && notifications is not null && creatorId > 0)
        {
            await notifications.PublishAsync(
                creatorId,
                NotificationEventCatalog.MyProductionOrders,
                NotificationCategory.Orders,
                severity,
                NotificationTemplateCatalog.MyProductionOrderStatusChanged,
                new
                {
                    orderNumber,
                    fromStatus = previousStatus.ToString(),
                    toStatus = nextStatus.ToString()
                },
                "/production-orders",
                ct);
        }

        return result;
    }

    private static async Task ClearSnapshotChildrenAsync(OrganizationDbContext db, long organizationId, long productionOrderId, CancellationToken ct)
    {
        var oldInputs = await db.ProductionOrderInputs.Where(x => x.OrganizationId == organizationId && x.ProductionOrderId == productionOrderId).ToListAsync(ct);
        var oldOutputs = await db.ProductionOrderOutputs.Where(x => x.OrganizationId == organizationId && x.ProductionOrderId == productionOrderId).ToListAsync(ct);
        var oldContexts = await db.ProductionOrderContexts.Where(x => x.OrganizationId == organizationId && x.ProductionOrderId == productionOrderId).ToListAsync(ct);
        var oldOperations = await db.ProductionOrderOperations.Where(x => x.OrganizationId == organizationId && x.ProductionOrderId == productionOrderId).ToListAsync(ct);
        db.ProductionOrderInputs.RemoveRange(oldInputs);
        db.ProductionOrderOutputs.RemoveRange(oldOutputs);
        db.ProductionOrderContexts.RemoveRange(oldContexts);
        db.ProductionOrderOperations.RemoveRange(oldOperations);
    }

    private static void ReplaceSnapshotChildren(
        OrganizationDbContext db,
        long organizationId,
        long siteId,
        long productionOrderId,
        ProductionOrderStatus orderStatus,
        ResolvedBomSnapshotWrite snapshot,
        ResolvedRoutingSnapshotWrite? routingSnapshot = null)
    {
        for (var i = 0; i < snapshot.ConsumedInputs.Count; i++)
        {
            var inp = snapshot.ConsumedInputs[i];
            db.ProductionOrderInputs.Add(new ProductionOrderInput(
                organizationId,
                productionOrderId,
                inp.Level,
                i + 1,
                inp.ParentItemId,
                inp.ItemId,
                inp.NetQuantity,
                inp.ScrapPercentage,
                inp.GrossQuantity,
                inp.UnitId,
                inp.IsOptional,
                inp.IsSubAssembly,
                inp.ResolvedBomVersionId));
        }

        for (var i = 0; i < snapshot.ProducedOutputs.Count; i++)
        {
            var outLine = snapshot.ProducedOutputs[i];
            db.ProductionOrderOutputs.Add(new ProductionOrderOutput(
                organizationId,
                productionOrderId,
                outLine.Level,
                i + 1,
                outLine.ItemId,
                outLine.OutputType,
                outLine.ProducedQuantity,
                outLine.UnitId));
        }

        foreach (var kv in snapshot.EffectiveContext.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            db.ProductionOrderContexts.Add(new ProductionOrderContext(
                organizationId,
                productionOrderId,
                kv.Key,
                kv.Value));
        }

        if (routingSnapshot is not null)
        {
            foreach (var rOp in routingSnapshot.Operations.OrderBy(o => o.Sequence))
            {
                var entity = ProductionOrderOperation.Create(
                    organizationId,
                    siteId,
                    productionOrderId,
                    rOp.Sequence,
                    rOp.OperationCode,
                    rOp.OperationName,
                    rOp.WorkCenterId,
                    rOp.PlannedSetupMinutes,
                    rOp.PlannedRunMinutes,
                    rOp.PlannedQueueMinutes,
                    rOp.PlannedWaitMinutes,
                    rOp.RequiresQualityCheck,
                    rOp.Instructions);
                if (orderStatus == ProductionOrderStatus.Released)
                    entity.MarkReady();
                db.ProductionOrderOperations.Add(entity);
            }
        }
    }

    private static async Task<ProductionOrderDetails?> BuildOrderDetailsAsync(OrganizationDbContext db, long organizationId, long id, CancellationToken ct)
    {
        var row = await (from po in db.ProductionOrders.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { po.OrganizationId, SiteId = po.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         join it in db.Items.AsNoTracking() on new { po.OrganizationId, ItemId = po.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                         join ru in db.Units.AsNoTracking() on new { po.OrganizationId, UnitId = po.RequestedUnitId } equals new { ru.OrganizationId, UnitId = ru.Id }
                         join bu in db.Units.AsNoTracking() on new { po.OrganizationId, UnitId = po.BaseOutputUnitIdSnapshot } equals new { bu.OrganizationId, UnitId = bu.Id }
                         where po.OrganizationId == organizationId && po.Id == id
                         select new
                         {
                             po,
                             s,
                             it,
                             ru,
                             bu,
                             CreatedAtUtc = EF.Property<DateTime>(po, "CreatedAtUtc"),
                             RowVersion = EF.Property<byte[]>(po, "RowVersion")
                         }).SingleOrDefaultAsync(ct);
        if (row is null) return null;

        var inputs = await (from pi in db.ProductionOrderInputs.AsNoTracking()
                            join pit in db.Items.AsNoTracking() on new { pi.OrganizationId, ItemId = pi.ParentItemId } equals new { pit.OrganizationId, ItemId = pit.Id }
                            join it in db.Items.AsNoTracking() on new { pi.OrganizationId, ItemId = pi.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                            join u in db.Units.AsNoTracking() on new { pi.OrganizationId, UnitId = pi.UnitId } equals new { u.OrganizationId, UnitId = u.Id }
                            where pi.OrganizationId == organizationId && pi.ProductionOrderId == id
                            orderby pi.Level, pi.Sequence, pi.Id
                            select new ProductionOrderInputSummary(
                                pi.Id, pi.Level, pi.Sequence, pi.ParentItemId, pit.Code,
                                pi.ItemId, it.Code, it.Name, pi.NetQuantity, pi.ScrapPercentage,
                                pi.GrossQuantity, pi.UnitId, u.Code, u.Symbol, pi.IsOptional, pi.IsSubAssembly, pi.ResolvedSubBomVersionId))
            .ToListAsync(ct);

        var outputs = await (from poOut in db.ProductionOrderOutputs.AsNoTracking()
                             join it in db.Items.AsNoTracking() on new { poOut.OrganizationId, ItemId = poOut.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                             join u in db.Units.AsNoTracking() on new { poOut.OrganizationId, UnitId = poOut.UnitId } equals new { u.OrganizationId, UnitId = u.Id }
                             where poOut.OrganizationId == organizationId && poOut.ProductionOrderId == id
                             orderby poOut.Level, poOut.Sequence, poOut.Id
                             select new ProductionOrderOutputSummary(
                                 poOut.Id, poOut.Level, poOut.Sequence, poOut.ItemId, it.Code, it.Name,
                                 poOut.OutputType, poOut.PlannedQuantity, poOut.UnitId, u.Code, u.Symbol))
            .ToListAsync(ct);

        var contexts = await db.ProductionOrderContexts.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.ProductionOrderId == id)
            .OrderBy(c => c.DimensionCode)
            .Select(c => new ProductionOrderContextSummary(c.DimensionCode, c.DimensionValue))
            .ToListAsync(ct);

        var operations = await SqlRoutingOperationsStore.ListOperationsForOrderInDb(db, organizationId, id, ct);

        return new ProductionOrderDetails(
            ToSummary(row.po, row.s, row.it, row.ru, row.bu, row.CreatedAtUtc, row.RowVersion),
            inputs,
            outputs,
            contexts,
            operations);
    }

    private static ProductionOrderSummary ToSummary(
        ProductionOrder po,
        Site s,
        Domain.Items.Item it,
        Domain.Units.Unit ru,
        Domain.Units.Unit bu,
        DateTime createdAtUtc,
        byte[] rowVersion) => new(
            po.Id,
            po.OrganizationId,
            po.SiteId,
            s.Code,
            s.Name,
            po.OrderNumber,
            po.Status,
            po.ItemId,
            it.Code,
            it.Name,
            po.RequestedQuantity,
            po.RequestedUnitId,
            ru.Code,
            ru.Symbol,
            po.PlannedStartDate,
            po.PlannedDueDate,
            po.BomId,
            po.BomCodeSnapshot,
            po.BomVersionId,
            po.BomVersionNumberSnapshot,
            po.BaseOutputQuantitySnapshot,
            po.BaseOutputUnitIdSnapshot,
            bu.Symbol,
            po.ActualProducedQuantity,
            po.ActualScrapQuantity,
            po.Notes,
            po.CancellationReason,
            createdAtUtc,
            po.ReleasedAtUtc,
            po.StartedAtUtc,
            po.CompletedAtUtc,
            po.CancelledAtUtc,
            Convert.ToBase64String(rowVersion),
            po.RoutingId,
            po.RoutingCodeSnapshot);

    private async Task<T> Read<T>(Func<OrganizationDbContext, Task<T>> query, CancellationToken ct)
    {
        try
        {
            await using var db = await Factory.CreateDbContextAsync(ct);
            return await query(db);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlProductionOrderOperationsStore));
            throw new ProductionOrderStoreUnavailableException(ex);
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
                    return OrganizationResult<T>.Failure(ProductionOrderErrors.AccessDenied);

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
        catch (ProductionOrderRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(ProductionOrderErrors.ConcurrencyConflict); }
        catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(ProductionOrderErrors.OutcomeUnknown); }
        catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
        { return OrganizationResult<T>.Failure(ProductionOrderErrors.AuditUnavailable); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627)
        { return OrganizationResult<T>.Failure(ProductionOrderErrors.OrderNumberDuplicate); }
        catch (InvalidOperationException ex) when (ex.Message == ProductionOrderErrors.ParentOrganizationInactive)
        { return OrganizationResult<T>.Failure(ProductionOrderErrors.ParentOrganizationInactive); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationSiteErrors.OrganizationNotFound)
        { return OrganizationResult<T>.Failure(ProductionOrderErrors.NotFound); }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlProductionOrderOperationsStore), access.Actor.ActorId);
            return OrganizationResult<T>.Failure(ProductionOrderErrors.PersistenceUnavailable);
        }
    }

    private static async Task<(bool Ok, string? Error)> LockAndRequireActiveOrganization(OrganizationDbContext db, long organizationId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {organizationId}", ct);
        var org = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return (false, ProductionOrderErrors.NotFound);
        if (!org.IsActive) return (false, ProductionOrderErrors.ParentOrganizationInactive);
        return (true, null);
    }

    private static bool Matches(OrganizationDbContext db, object entity, byte[] version)
        => ((byte[])db.Entry(entity).Property("RowVersion").CurrentValue!).SequenceEqual(version);

    private static bool Unavailable(Exception exception) => exception switch
    {
        SqlException sql => sql.Number is -2 or 53 or 64 or 233 or 1205 or 4060 or 18456 or 40197 or 40501 or 40613,
        TimeoutException => true,
        DbUpdateException or RetryLimitExceededException when exception.InnerException is not null => Unavailable(exception.InnerException),
        InvalidOperationException when exception.Message == OrganizationAccessErrors.PersistenceUnavailable => true,
        _ => false
    };
}
