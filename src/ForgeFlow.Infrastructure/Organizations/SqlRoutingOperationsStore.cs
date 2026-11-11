using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Routings;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlRoutingOperationsStore(
    Func<IDbContextFactory<OrganizationDbContext>> getFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null)
    : IRoutingOperationsStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public SqlRoutingOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public Task<OrganizationPage<WorkCenterSummary>> ListWorkCenters(
        long organizationId,
        WorkCenterListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from wc in db.WorkCenters.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { wc.OrganizationId, SiteId = wc.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        where wc.OrganizationId == organizationId
                        select new { wc, s, RowVersion = EF.Property<byte[]>(wc, "RowVersion") };

        if (query.SiteId is long siteId && siteId > 0)
            baseQuery = baseQuery.Where(x => x.wc.SiteId == siteId);
        if (query.WorkCenterType is WorkCenterType type)
            baseQuery = baseQuery.Where(x => x.wc.WorkCenterType == type);
        if (query.IsActive is bool active)
            baseQuery = baseQuery.Where(x => x.wc.IsActive == active);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x => x.wc.Code.Contains(query.Search) || x.wc.Name.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderBy(x => x.s.Code).ThenBy(x => x.wc.Code).ThenBy(x => x.wc.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => ToWorkCenterSummary(r.wc, r.s, r.RowVersion)).ToArray();
        return new OrganizationPage<WorkCenterSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<IReadOnlyList<WorkCenterSummary>> ListActiveWorkCenters(long organizationId, long? siteId, CancellationToken ct) =>
        Read<IReadOnlyList<WorkCenterSummary>>(async db =>
        {
            var baseQuery = from wc in db.WorkCenters.AsNoTracking()
                            join s in db.Sites.AsNoTracking() on new { wc.OrganizationId, SiteId = wc.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                            where wc.OrganizationId == organizationId && wc.IsActive
                            select new { wc, s, RowVersion = EF.Property<byte[]>(wc, "RowVersion") };
            if (siteId is long sid && sid > 0)
                baseQuery = baseQuery.Where(x => x.wc.SiteId == sid);

            var rows = await baseQuery.OrderBy(x => x.s.Code).ThenBy(x => x.wc.Code).ToListAsync(ct);
            return rows.Select(r => ToWorkCenterSummary(r.wc, r.s, r.RowVersion)).ToArray();
        }, ct);

    public Task<WorkCenterSummary?> GetWorkCenter(long organizationId, long id, CancellationToken ct) => Read(async db =>
    {
        var row = await (from wc in db.WorkCenters.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { wc.OrganizationId, SiteId = wc.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         where wc.OrganizationId == organizationId && wc.Id == id
                         select new { wc, s, RowVersion = EF.Property<byte[]>(wc, "RowVersion") })
            .SingleOrDefaultAsync(ct);
        return row is null ? null : ToWorkCenterSummary(row.wc, row.s, row.RowVersion);
    }, ct);

    public Task<OrganizationResult<WorkCenterSummary>> CreateWorkCenter(
        CreateWorkCenterRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<WorkCenterSummary>.Failure(orgCheck.Error!);

        var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SiteId, ct);
        if (site is null || !site.IsActive) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.SiteInvalid);

        var entity = WorkCenter.Create(
            request.OrganizationId,
            request.SiteId,
            request.Code,
            request.Name,
            request.WorkCenterType,
            request.CapacityUnitsPerHour,
            request.DailyAvailableHours,
            request.HourlyCostRate,
            request.Notes);

        db.WorkCenters.Add(entity);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<WorkCenterSummary>.Success((await GetWorkCenterInDb(db, request.OrganizationId, entity.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<WorkCenterSummary>> UpdateWorkCenter(
        long organizationId,
        long id,
        byte[] version,
        UpdateWorkCenterRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<WorkCenterSummary>.Failure(orgCheck.Error!);

        var entity = await db.WorkCenters.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (entity is null) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
        if (!Matches(db, entity, version)) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.ConcurrencyConflict);

        if (entity.Update(request.Name, request.WorkCenterType, request.CapacityUnitsPerHour, request.DailyAvailableHours, request.HourlyCostRate, request.Notes))
            await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<WorkCenterSummary>.Success((await GetWorkCenterInDb(db, organizationId, entity.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<WorkCenterSummary>> SetWorkCenterActive(
        long organizationId,
        long id,
        byte[] version,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<WorkCenterSummary>.Failure(orgCheck.Error!);

        var entity = await db.WorkCenters.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (entity is null) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.WorkCenterNotFound);
        if (!Matches(db, entity, version)) return OrganizationResult<WorkCenterSummary>.Failure(RoutingErrors.ConcurrencyConflict);

        if (entity.SetActive(isActive))
            await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<WorkCenterSummary>.Success((await GetWorkCenterInDb(db, organizationId, entity.Id, ct))!);
    }, ct);

    public Task<OrganizationPage<RoutingSummary>> ListRoutings(
        long organizationId,
        RoutingListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from r in db.Routings.AsNoTracking()
                        join s in db.Sites.AsNoTracking() on new { r.OrganizationId, SiteId = r.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                        join it in db.Items.AsNoTracking() on new { r.OrganizationId, ItemId = r.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                        where r.OrganizationId == organizationId
                        select new { r, s, it };

        if (query.SiteId is long siteId && siteId > 0)
            baseQuery = baseQuery.Where(x => x.r.SiteId == siteId);
        if (query.ItemId is long itemId && itemId > 0)
            baseQuery = baseQuery.Where(x => x.r.ItemId == itemId);
        if (query.Status is RoutingStatus status)
            baseQuery = baseQuery.Where(x => x.r.Status == status);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.r.Code.Contains(query.Search) ||
                x.r.Name.Contains(query.Search) ||
                x.it.Code.Contains(query.Search) ||
                x.it.Name.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await (from x in baseQuery
                          join bv in db.BomVersions.AsNoTracking() on new { x.r.OrganizationId, BomVerId = x.r.BomVersionId } equals new { bv.OrganizationId, BomVerId = (long?)bv.Id } into bvj
                          from bv in bvj.DefaultIfEmpty()
                          join b in db.Boms.AsNoTracking() on new { x.r.OrganizationId, BomId = bv != null ? (long?)bv.BomId : null } equals new { b.OrganizationId, BomId = (long?)b.Id } into bj
                          from b in bj.DefaultIfEmpty()
                          orderby x.r.Id descending
                          select new
                          {
                              x.r,
                              x.s,
                              x.it,
                              BomCode = b != null ? b.Code : null,
                              BomVersionNumber = bv != null ? (int?)bv.VersionNumber : null,
                              RowVersion = EF.Property<byte[]>(x.r, "RowVersion"),
                              OpCount = db.RoutingOperations.Count(o => o.OrganizationId == organizationId && o.RoutingId == x.r.Id),
                              SetupSum = db.RoutingOperations.Where(o => o.OrganizationId == organizationId && o.RoutingId == x.r.Id).Select(o => (decimal?)o.SetupMinutes).Sum() ?? 0m,
                              RunSum = db.RoutingOperations.Where(o => o.OrganizationId == organizationId && o.RoutingId == x.r.Id).Select(o => (decimal?)o.RunMinutesPerUnit).Sum() ?? 0m
                          })
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => ToRoutingSummary(
            r.r, r.s, r.it, r.BomCode, r.BomVersionNumber, r.OpCount, r.SetupSum, r.RunSum, r.RowVersion)).ToArray();

        return new OrganizationPage<RoutingSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<RoutingDetails?> GetRouting(long organizationId, long id, CancellationToken ct) =>
        Read(db => BuildRoutingDetailsAsync(db, organizationId, id, ct), ct);

    public Task<IReadOnlyList<RoutingSummary>> ListActiveRoutingsForItem(
        long organizationId,
        long siteId,
        long itemId,
        CancellationToken ct) => Read<IReadOnlyList<RoutingSummary>>(async db =>
    {
        var rows = await (from r in db.Routings.AsNoTracking()
                          join s in db.Sites.AsNoTracking() on new { r.OrganizationId, SiteId = r.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                          join it in db.Items.AsNoTracking() on new { r.OrganizationId, ItemId = r.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                          join bv in db.BomVersions.AsNoTracking() on new { r.OrganizationId, BomVerId = r.BomVersionId } equals new { bv.OrganizationId, BomVerId = (long?)bv.Id } into bvj
                          from bv in bvj.DefaultIfEmpty()
                          join b in db.Boms.AsNoTracking() on new { r.OrganizationId, BomId = bv != null ? (long?)bv.BomId : null } equals new { b.OrganizationId, BomId = (long?)b.Id } into bj
                          from b in bj.DefaultIfEmpty()
                          where r.OrganizationId == organizationId
                              && r.SiteId == siteId
                              && r.ItemId == itemId
                              && r.Status == RoutingStatus.Active
                          orderby r.IsDefault descending, r.VersionNumber descending, r.Id descending
                          select new
                          {
                              r,
                              s,
                              it,
                              BomCode = b != null ? b.Code : null,
                              BomVersionNumber = bv != null ? (int?)bv.VersionNumber : null,
                              RowVersion = EF.Property<byte[]>(r, "RowVersion"),
                              OpCount = db.RoutingOperations.Count(o => o.OrganizationId == organizationId && o.RoutingId == r.Id),
                              SetupSum = db.RoutingOperations.Where(o => o.OrganizationId == organizationId && o.RoutingId == r.Id).Select(o => (decimal?)o.SetupMinutes).Sum() ?? 0m,
                              RunSum = db.RoutingOperations.Where(o => o.OrganizationId == organizationId && o.RoutingId == r.Id).Select(o => (decimal?)o.RunMinutesPerUnit).Sum() ?? 0m
                          })
            .ToListAsync(ct);

        return rows.Select(r => ToRoutingSummary(
            r.r, r.s, r.it, r.BomCode, r.BomVersionNumber, r.OpCount, r.SetupSum, r.RunSum, r.RowVersion)).ToArray();
    }, ct);

    public Task<RoutingDetails?> ResolveRoutingForProductionOrder(
        long organizationId,
        long siteId,
        long itemId,
        long? requestedRoutingId,
        CancellationToken ct) => Read(async db =>
    {
        long? targetId;
        if (requestedRoutingId.HasValue)
        {
            targetId = await db.Routings.AsNoTracking()
                .Where(r => r.OrganizationId == organizationId
                    && r.SiteId == siteId
                    && r.ItemId == itemId
                    && r.Id == requestedRoutingId.Value
                    && r.Status == RoutingStatus.Active)
                .Select(r => (long?)r.Id)
                .SingleOrDefaultAsync(ct);
        }
        else
        {
            targetId = await db.Routings.AsNoTracking()
                .Where(r => r.OrganizationId == organizationId
                    && r.SiteId == siteId
                    && r.ItemId == itemId
                    && r.Status == RoutingStatus.Active
                    && r.IsDefault)
                .OrderByDescending(r => r.VersionNumber)
                .Select(r => (long?)r.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (!targetId.HasValue) return null;
        return await BuildRoutingDetailsAsync(db, organizationId, targetId.Value, ct);
    }, ct);

    public Task<OrganizationResult<RoutingDetails>> CreateDraftRouting(
        CreateRoutingRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<RoutingDetails>.Failure(orgCheck.Error!);

        var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SiteId, ct);
        if (site is null || !site.IsActive) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.SiteInvalid);

        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.OrganizationId == request.OrganizationId && i.Id == request.ItemId, ct);
        if (item is null || !item.IsActive) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ItemInvalid);
        if (!item.CanBeManufactured) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ItemNotManufacturable);

        if (request.BomVersionId is > 0)
        {
            var bvValid = await (from bv in db.BomVersions.AsNoTracking()
                                 join b in db.Boms.AsNoTracking() on new { bv.OrganizationId, bv.BomId } equals new { b.OrganizationId, BomId = b.Id }
                                 where bv.OrganizationId == request.OrganizationId && bv.Id == request.BomVersionId.Value && b.PrimaryOutputItemId == request.ItemId
                                 select bv.Id).AnyAsync(ct);
            if (!bvValid) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.BomVersionInvalid);
        }

        var routing = Routing.CreateDraft(
            request.OrganizationId,
            request.SiteId,
            request.Code,
            request.Name,
            request.ItemId,
            request.BomVersionId,
            request.VersionNumber,
            request.IsDefault,
            request.Notes);

        db.Routings.Add(routing);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<RoutingDetails>.Success((await BuildRoutingDetailsAsync(db, request.OrganizationId, routing.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<RoutingDetails>> UpdateDraftRouting(
        long organizationId,
        long id,
        byte[] version,
        UpdateDraftRoutingRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<RoutingDetails>.Failure(orgCheck.Error!);

        var routing = await db.Routings.SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == id, ct);
        if (routing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        if (!Matches(db, routing, version)) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        if (routing.Status != RoutingStatus.Draft) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotDraft);

        if (request.BomVersionId is > 0)
        {
            var bvValid = await (from bv in db.BomVersions.AsNoTracking()
                                 join b in db.Boms.AsNoTracking() on new { bv.OrganizationId, bv.BomId } equals new { b.OrganizationId, BomId = b.Id }
                                 where bv.OrganizationId == organizationId && bv.Id == request.BomVersionId.Value && b.PrimaryOutputItemId == routing.ItemId
                                 select bv.Id).AnyAsync(ct);
            if (!bvValid) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.BomVersionInvalid);
        }

        routing.UpdateDraft(request.Name, request.BomVersionId, request.IsDefault, request.Notes);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<RoutingDetails>.Success((await BuildRoutingDetailsAsync(db, organizationId, routing.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<RoutingDetails>> SaveRoutingOperations(
        long organizationId,
        long routingId,
        byte[] version,
        IReadOnlyList<RoutingOperationInput> operations,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<RoutingDetails>.Failure(orgCheck.Error!);

        var routing = await db.Routings
            .Include(r => r.Operations)
            .SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == routingId, ct);
        if (routing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        if (!Matches(db, routing, version)) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);
        if (routing.Status != RoutingStatus.Draft) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotDraft);

        var wcIds = operations.Select(o => o.WorkCenterId).Distinct().ToArray();
        var validWorkCenters = await db.WorkCenters.AsNoTracking()
            .Where(w => w.OrganizationId == organizationId && w.SiteId == routing.SiteId && wcIds.Contains(w.Id))
            .Select(w => new { w.Id, w.IsActive })
            .ToListAsync(ct);

        if (validWorkCenters.Count != wcIds.Length)
            return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.WorkCenterNotFound);
        if (validWorkCenters.Any(w => !w.IsActive))
            return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.WorkCenterInactive);

        var existingOps = await db.RoutingOperations
            .Where(o => o.OrganizationId == organizationId && o.RoutingId == routingId)
            .ToListAsync(ct);
        db.RoutingOperations.RemoveRange(existingOps);

        routing.ReplaceOperations(operations.Select(o => (
            o.Sequence,
            o.OperationCode,
            o.OperationName,
            o.WorkCenterId,
            o.SetupMinutes,
            o.RunMinutesPerUnit,
            o.QueueMinutes,
            o.WaitMinutes,
            o.RequiresQualityCheck,
            o.Instructions)).ToArray());

        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<RoutingDetails>.Success((await BuildRoutingDetailsAsync(db, organizationId, routing.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<RoutingDetails>> ActivateRouting(
        long organizationId,
        long id,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<RoutingDetails>.Failure(orgCheck.Error!);

        var routing = await db.Routings
            .Include(r => r.Operations)
            .SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == id, ct);
        if (routing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        if (!Matches(db, routing, version)) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);

        if (routing.Operations.Count == 0)
            return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.EmptyOperations);

        var wcIds = routing.Operations.Select(o => o.WorkCenterId).Distinct().ToArray();
        var activeCount = await db.WorkCenters.AsNoTracking()
            .CountAsync(w => w.OrganizationId == organizationId && w.SiteId == routing.SiteId && wcIds.Contains(w.Id) && w.IsActive, ct);
        if (activeCount != wcIds.Length)
            return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.WorkCenterInactive);

        if (routing.IsDefault)
        {
            var previousDefaults = await db.Routings
                .Where(r => r.OrganizationId == organizationId
                    && r.SiteId == routing.SiteId
                    && r.ItemId == routing.ItemId
                    && r.Id != routing.Id
                    && r.Status == RoutingStatus.Active
                    && r.IsDefault)
                .ToListAsync(ct);
            foreach (var prev in previousDefaults)
                prev.SetDefault(false);
        }

        routing.Activate(utcNow);
        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<RoutingDetails>.Success((await BuildRoutingDetailsAsync(db, organizationId, routing.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<RoutingDetails>> MarkRoutingObsolete(
        long organizationId,
        long id,
        byte[] version,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<RoutingDetails>.Failure(orgCheck.Error!);

        var routing = await db.Routings.SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == id, ct);
        if (routing is null) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.RoutingNotFound);
        if (!Matches(db, routing, version)) return OrganizationResult<RoutingDetails>.Failure(RoutingErrors.ConcurrencyConflict);

        routing.MarkObsolete();
        await db.SaveInOperation(access.Actor, ct);
        return OrganizationResult<RoutingDetails>.Success((await BuildRoutingDetailsAsync(db, organizationId, routing.Id, ct))!);
    }, ct);

    public Task<ProductionOrderOperationSummary?> GetProductionOrderOperation(long organizationId, long operationId, CancellationToken ct) =>
        Read(db => BuildOperationSummaryAsync(db, organizationId, operationId, ct), ct);

    public Task<IReadOnlyList<ProductionOrderOperationSummary>> ListOperationsForOrder(long organizationId, long productionOrderId, CancellationToken ct) =>
        Read<IReadOnlyList<ProductionOrderOperationSummary>>(db => ListOperationsForOrderInDb(db, organizationId, productionOrderId, ct), ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> StartOperation(
        long organizationId,
        long operationId,
        byte[] version,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => MutateOperationAsync(organizationId, operationId, version, access, op => op.Start(utcNow, notes), ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> PauseOperation(
        long organizationId,
        long operationId,
        byte[] version,
        decimal actualSetupMinutes,
        decimal actualRunMinutes,
        decimal completedQuantity,
        decimal scrapQuantity,
        string? notes,
        OrganizationWriteAccess access,
        CancellationToken ct) => MutateOperationAsync(organizationId, operationId, version, access,
            op => op.Pause(actualSetupMinutes, actualRunMinutes, completedQuantity, scrapQuantity, notes), ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> CompleteOperation(
        long organizationId,
        long operationId,
        byte[] version,
        decimal actualSetupMinutes,
        decimal actualRunMinutes,
        decimal completedQuantity,
        decimal scrapQuantity,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => MutateOperationAsync(organizationId, operationId, version, access,
            op => op.Complete(actualSetupMinutes, actualRunMinutes, completedQuantity, scrapQuantity, notes, utcNow), ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> SkipOperation(
        long organizationId,
        long operationId,
        byte[] version,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct) => MutateOperationAsync(organizationId, operationId, version, access,
            op => op.Skip(notes, utcNow), ct);

    public Task<IReadOnlyList<WorkCenterLoadSummary>> GetWorkCenterLoads(
        long organizationId,
        long? siteId,
        CancellationToken ct) => Read<IReadOnlyList<WorkCenterLoadSummary>>(async db =>
    {
        var wcQuery = db.WorkCenters.AsNoTracking().Where(w => w.OrganizationId == organizationId && w.IsActive);
        if (siteId is long sid && sid > 0)
            wcQuery = wcQuery.Where(w => w.SiteId == sid);

        var workCenters = await wcQuery.OrderBy(w => w.Code).ToListAsync(ct);

        var activeOpsQuery = from op in db.ProductionOrderOperations.AsNoTracking()
                             join po in db.ProductionOrders.AsNoTracking() on new { op.OrganizationId, OrderId = op.ProductionOrderId } equals new { po.OrganizationId, OrderId = po.Id }
                             where op.OrganizationId == organizationId
                                 && po.Status != ProductionOrderStatus.Completed
                                 && po.Status != ProductionOrderStatus.Cancelled
                                 && (op.Status == ProductionOrderOperationStatus.Pending
                                     || op.Status == ProductionOrderOperationStatus.Ready
                                     || op.Status == ProductionOrderOperationStatus.InProgress
                                     || op.Status == ProductionOrderOperationStatus.Paused)
                             select op;

        if (siteId is long filterSite && filterSite > 0)
            activeOpsQuery = activeOpsQuery.Where(o => o.SiteId == filterSite);

        var grouped = await activeOpsQuery
            .GroupBy(o => o.WorkCenterId)
            .Select(g => new
            {
                WorkCenterId = g.Key,
                Count = g.Count(),
                Minutes = g.Sum(x => x.PlannedSetupMinutes + x.PlannedRunMinutes + x.PlannedQueueMinutes + x.PlannedWaitMinutes)
            })
            .ToDictionaryAsync(x => x.WorkCenterId, ct);

        return workCenters.Select(wc =>
        {
            grouped.TryGetValue(wc.Id, out var load);
            var count = load?.Count ?? 0;
            var totalMin = load?.Minutes ?? 0m;
            var availableMin = wc.DailyAvailableHours * 60m;
            var util = availableMin > 0m ? decimal.Round((totalMin / availableMin) * 100m, 1, MidpointRounding.AwayFromZero) : 0m;
            return new WorkCenterLoadSummary(
                wc.Id,
                wc.SiteId,
                wc.Code,
                wc.Name,
                wc.DailyAvailableHours,
                count,
                decimal.Round(totalMin, 2, MidpointRounding.AwayFromZero),
                util);
        }).ToArray();
    }, ct);

    private Task<OrganizationResult<ProductionOrderOperationSummary>> MutateOperationAsync(
        long organizationId,
        long operationId,
        byte[] version,
        OrganizationWriteAccess access,
        Action<ProductionOrderOperation> apply,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<ProductionOrderOperationSummary>.Failure(orgCheck.Error!);

        var op = await db.ProductionOrderOperations.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == operationId, ct);
        if (op is null) return OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.OperationNotFound);
        if (!Matches(db, op, version)) return OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.ConcurrencyConflict);

        var po = await db.ProductionOrders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == op.ProductionOrderId, ct);
        if (po is null || po.Status is ProductionOrderStatus.Draft or ProductionOrderStatus.Cancelled or ProductionOrderStatus.Completed)
            return OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.OperationTransitionInvalid);

        apply(op);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<ProductionOrderOperationSummary>.Success((await BuildOperationSummaryAsync(db, organizationId, op.Id, ct))!);
    }, ct);

    private static async Task<WorkCenterSummary?> GetWorkCenterInDb(OrganizationDbContext db, long organizationId, long id, CancellationToken ct)
    {
        var row = await (from wc in db.WorkCenters.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { wc.OrganizationId, SiteId = wc.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         where wc.OrganizationId == organizationId && wc.Id == id
                         select new { wc, s, RowVersion = EF.Property<byte[]>(wc, "RowVersion") })
            .SingleOrDefaultAsync(ct);
        return row is null ? null : ToWorkCenterSummary(row.wc, row.s, row.RowVersion);
    }

    private static async Task<RoutingDetails?> BuildRoutingDetailsAsync(OrganizationDbContext db, long organizationId, long id, CancellationToken ct)
    {
        var row = await (from r in db.Routings.AsNoTracking()
                         join s in db.Sites.AsNoTracking() on new { r.OrganizationId, SiteId = r.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                         join it in db.Items.AsNoTracking() on new { r.OrganizationId, ItemId = r.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                         join bv in db.BomVersions.AsNoTracking() on new { r.OrganizationId, BomVerId = r.BomVersionId } equals new { bv.OrganizationId, BomVerId = (long?)bv.Id } into bvj
                         from bv in bvj.DefaultIfEmpty()
                         join b in db.Boms.AsNoTracking() on new { r.OrganizationId, BomId = bv != null ? (long?)bv.BomId : null } equals new { b.OrganizationId, BomId = (long?)b.Id } into bj
                         from b in bj.DefaultIfEmpty()
                         where r.OrganizationId == organizationId && r.Id == id
                         select new
                         {
                             r,
                             s,
                             it,
                             BomCode = b != null ? b.Code : null,
                             BomVersionNumber = bv != null ? (int?)bv.VersionNumber : null,
                             RowVersion = EF.Property<byte[]>(r, "RowVersion")
                         }).SingleOrDefaultAsync(ct);
        if (row is null) return null;

        var ops = await (from op in db.RoutingOperations.AsNoTracking()
                         join wc in db.WorkCenters.AsNoTracking() on new { op.OrganizationId, WorkCenterId = op.WorkCenterId } equals new { wc.OrganizationId, WorkCenterId = wc.Id }
                         where op.OrganizationId == organizationId && op.RoutingId == id
                         orderby op.Sequence, op.Id
                         select new RoutingOperationSummary(
                             op.Id,
                             op.Sequence,
                             op.OperationCode,
                             op.OperationName,
                             op.WorkCenterId,
                             wc.Code,
                             wc.Name,
                             op.SetupMinutes,
                             op.RunMinutesPerUnit,
                             op.QueueMinutes,
                             op.WaitMinutes,
                             op.RequiresQualityCheck,
                             op.Instructions)).ToListAsync(ct);

        var summary = ToRoutingSummary(
            row.r,
            row.s,
            row.it,
            row.BomCode,
            row.BomVersionNumber,
            ops.Count,
            ops.Sum(o => o.SetupMinutes),
            ops.Sum(o => o.RunMinutesPerUnit),
            row.RowVersion);

        return new RoutingDetails(summary, ops);
    }

    private static async Task<ProductionOrderOperationSummary?> BuildOperationSummaryAsync(OrganizationDbContext db, long organizationId, long operationId, CancellationToken ct)
    {
        var row = await (from op in db.ProductionOrderOperations.AsNoTracking()
                         join wc in db.WorkCenters.AsNoTracking() on new { op.OrganizationId, WorkCenterId = op.WorkCenterId } equals new { wc.OrganizationId, WorkCenterId = wc.Id }
                         join po in db.ProductionOrders.AsNoTracking() on new { op.OrganizationId, OrderId = op.ProductionOrderId } equals new { po.OrganizationId, OrderId = po.Id }
                         where op.OrganizationId == organizationId && op.Id == operationId
                         select new { op, wc, po.OrderNumber, RowVersion = EF.Property<byte[]>(op, "RowVersion") })
            .SingleOrDefaultAsync(ct);
        return row is null ? null : ToOperationSummary(row.op, row.wc, row.OrderNumber, row.RowVersion);
    }

    internal static async Task<IReadOnlyList<ProductionOrderOperationSummary>> ListOperationsForOrderInDb(
        OrganizationDbContext db,
        long organizationId,
        long productionOrderId,
        CancellationToken ct)
    {
        var rows = await (from op in db.ProductionOrderOperations.AsNoTracking()
                          join wc in db.WorkCenters.AsNoTracking() on new { op.OrganizationId, WorkCenterId = op.WorkCenterId } equals new { wc.OrganizationId, WorkCenterId = wc.Id }
                          join po in db.ProductionOrders.AsNoTracking() on new { op.OrganizationId, OrderId = op.ProductionOrderId } equals new { po.OrganizationId, OrderId = po.Id }
                          where op.OrganizationId == organizationId && op.ProductionOrderId == productionOrderId
                          orderby op.Sequence, op.Id
                          select new { op, wc, po.OrderNumber, RowVersion = EF.Property<byte[]>(op, "RowVersion") })
            .ToListAsync(ct);

        return rows.Select(r => ToOperationSummary(r.op, r.wc, r.OrderNumber, r.RowVersion)).ToArray();
    }

    private static WorkCenterSummary ToWorkCenterSummary(WorkCenter wc, Site s, byte[] rowVersion) => new(
        wc.Id,
        wc.OrganizationId,
        wc.SiteId,
        s.Code,
        s.Name,
        wc.Code,
        wc.Name,
        wc.WorkCenterType,
        wc.CapacityUnitsPerHour,
        wc.DailyAvailableHours,
        wc.HourlyCostRate,
        wc.Notes,
        wc.IsActive,
        Convert.ToBase64String(rowVersion));

    private static RoutingSummary ToRoutingSummary(
        Routing r,
        Site s,
        Domain.Items.Item it,
        string? bomCode,
        int? bomVersionNumber,
        int opCount,
        decimal totalSetup,
        decimal totalRunPerUnit,
        byte[] rowVersion) => new(
        r.Id,
        r.OrganizationId,
        r.SiteId,
        s.Code,
        s.Name,
        r.Code,
        r.Name,
        r.ItemId,
        it.Code,
        it.Name,
        r.BomVersionId,
        bomCode,
        bomVersionNumber,
        r.VersionNumber,
        r.Status,
        r.IsDefault,
        opCount,
        totalSetup,
        totalRunPerUnit,
        r.Notes,
        r.ActivatedAtUtc,
        Convert.ToBase64String(rowVersion));

    private static ProductionOrderOperationSummary ToOperationSummary(
        ProductionOrderOperation op,
        WorkCenter wc,
        string? orderNumber,
        byte[] rowVersion) => new(
        op.Id,
        op.OrganizationId,
        op.SiteId,
        op.ProductionOrderId,
        orderNumber,
        op.Sequence,
        op.OperationCode,
        op.OperationName,
        op.WorkCenterId,
        wc.Code,
        wc.Name,
        op.PlannedSetupMinutes,
        op.PlannedRunMinutes,
        op.PlannedQueueMinutes,
        op.PlannedWaitMinutes,
        op.PlannedTotalMinutes,
        op.ActualSetupMinutes,
        op.ActualRunMinutes,
        op.CompletedQuantity,
        op.ScrapQuantity,
        op.RequiresQualityCheck,
        op.Instructions,
        op.OperatorNotes,
        op.Status,
        op.StartedAtUtc,
        op.CompletedAtUtc,
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
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlRoutingOperationsStore));
            throw new RoutingStoreUnavailableException(ex);
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
                    return OrganizationResult<T>.Failure(RoutingErrors.AccessDenied);

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
        catch (RoutingRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(RoutingErrors.ConcurrencyConflict); }
        catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(RoutingErrors.OutcomeUnknown); }
        catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
        { return OrganizationResult<T>.Failure(RoutingErrors.AuditUnavailable); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627)
        {
            if (sql.Message.Contains("UX_WorkCenter", StringComparison.OrdinalIgnoreCase))
                return OrganizationResult<T>.Failure(RoutingErrors.WorkCenterCodeDuplicate);
            if (sql.Message.Contains("UX_Routing_Org_Site_Code_Version", StringComparison.OrdinalIgnoreCase))
                return OrganizationResult<T>.Failure(RoutingErrors.VersionDuplicate);
            return OrganizationResult<T>.Failure(RoutingErrors.RoutingCodeDuplicate);
        }
        catch (InvalidOperationException ex) when (ex.Message == RoutingErrors.ParentOrganizationInactive)
        { return OrganizationResult<T>.Failure(RoutingErrors.ParentOrganizationInactive); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationSiteErrors.OrganizationNotFound)
        { return OrganizationResult<T>.Failure(RoutingErrors.NotFound); }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlRoutingOperationsStore), access.Actor.ActorId);
            return OrganizationResult<T>.Failure(RoutingErrors.PersistenceUnavailable);
        }
    }

    private static async Task<(bool Ok, string? Error)> LockAndRequireActiveOrganization(OrganizationDbContext db, long organizationId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {organizationId}", ct);
        var org = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return (false, RoutingErrors.NotFound);
        if (!org.IsActive) return (false, RoutingErrors.ParentOrganizationInactive);
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
