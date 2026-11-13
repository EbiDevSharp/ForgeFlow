using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class ItemOperationsStore(Func<IDbContextFactory<OrganizationDbContext>> getFactory) : IItemOperationsStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public ItemOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public Task<OrganizationPage<ItemSummary>> ListItems(long organizationId, ItemListQuery query, CancellationToken ct) => Read(async db =>
    {
        var joined = from i in db.Items.AsNoTracking()
                     join u in db.Units.AsNoTracking() on new { i.OrganizationId, UnitId = i.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                     where i.OrganizationId == organizationId && (query.IncludeInactive || i.IsActive)
                     select new { i, u };

        if (query.ClassificationId is long clsId && clsId > 0)
        {
            var matchingIds = db.ItemClassifications.AsNoTracking()
                .Where(ic => ic.OrganizationId == organizationId && ic.ClassificationId == clsId)
                .Select(ic => ic.ItemId);
            joined = joined.Where(x => matchingIds.Contains(x.i.Id));
        }

        if (query.CanBePurchased.HasValue) joined = joined.Where(x => x.i.CanBePurchased == query.CanBePurchased.Value);
        if (query.CanBeManufactured.HasValue) joined = joined.Where(x => x.i.CanBeManufactured == query.CanBeManufactured.Value);
        if (query.CanBeSold.HasValue) joined = joined.Where(x => x.i.CanBeSold == query.CanBeSold.Value);
        if (query.CanBeConsumed.HasValue) joined = joined.Where(x => x.i.CanBeConsumed == query.CanBeConsumed.Value);

        if (!string.IsNullOrEmpty(query.Search))
            joined = joined.Where(x => x.i.Code.Contains(query.Search) || x.i.Name.Contains(query.Search));

        var count = await joined.CountAsync(ct);
        var page = await joined
            .OrderBy(x => x.i.Code)
            .ThenBy(x => x.i.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new
            {
                x.i.Id,
                x.i.OrganizationId,
                x.i.Code,
                x.i.Name,
                x.i.Description,
                x.i.BaseUnitId,
                BaseUnitCode = x.u.Code,
                BaseUnitName = x.u.Name,
                BaseUnitSymbol = x.u.Symbol,
                x.i.CanBePurchased,
                x.i.CanBeManufactured,
                x.i.CanBeSold,
                x.i.CanBeConsumed,
                x.i.IsActive,
                RowVersion = EF.Property<byte[]>(x.i, "RowVersion"),
                x.i.RequiresLotTracking,
                x.i.QualityControlMode
            })
            .ToListAsync(ct);

        var items = page.Select(x => new ItemSummary(
            x.Id, x.OrganizationId, x.Code, x.Name, x.Description,
            x.BaseUnitId, x.BaseUnitCode, x.BaseUnitName, x.BaseUnitSymbol,
            x.CanBePurchased, x.CanBeManufactured, x.CanBeSold, x.CanBeConsumed,
            x.IsActive, Convert.ToBase64String(x.RowVersion), x.RequiresLotTracking, x.QualityControlMode)).ToArray();
        return new OrganizationPage<ItemSummary>(items, count, query.Page, query.PageSize);
    }, ct);

    public Task<IReadOnlyList<ItemSummary>> ListActiveItems(long organizationId, CancellationToken ct) => Read<IReadOnlyList<ItemSummary>>(async db =>
    {
        var rows = await (from i in db.Items.AsNoTracking()
                          join u in db.Units.AsNoTracking() on new { i.OrganizationId, UnitId = i.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                          where i.OrganizationId == organizationId && i.IsActive
                          orderby i.Code, i.Id
                          select new
                          {
                              i.Id,
                              i.OrganizationId,
                              i.Code,
                              i.Name,
                              i.Description,
                              i.BaseUnitId,
                              BaseUnitCode = u.Code,
                              BaseUnitName = u.Name,
                              BaseUnitSymbol = u.Symbol,
                              i.CanBePurchased,
                              i.CanBeManufactured,
                              i.CanBeSold,
                              i.CanBeConsumed,
                              i.IsActive,
                              RowVersion = EF.Property<byte[]>(i, "RowVersion"),
                              i.RequiresLotTracking,
                              i.QualityControlMode
                          })
            .ToListAsync(ct);

        return rows.Select(x => new ItemSummary(
            x.Id, x.OrganizationId, x.Code, x.Name, x.Description,
            x.BaseUnitId, x.BaseUnitCode, x.BaseUnitName, x.BaseUnitSymbol,
            x.CanBePurchased, x.CanBeManufactured, x.CanBeSold, x.CanBeConsumed,
            x.IsActive, Convert.ToBase64String(x.RowVersion), x.RequiresLotTracking, x.QualityControlMode)).ToArray();
    }, ct);

    public Task<ItemDetails?> GetItem(long organizationId, long id, CancellationToken ct)
        => Read(db => BuildItemDetailsAsync(db, organizationId, id, ct), ct);

    public Task<OrganizationResult<ItemDetails>> CreateItem(CreateItemRequest request, OrganizationWriteAccess access, CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<ItemDetails>.Failure(org.Error);

            var unit = await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == request.OrganizationId && u.Id == request.BaseUnitId, ct);
            if (unit is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.BaseUnitNotFound);
            if (!unit.IsActive) return OrganizationResult<ItemDetails>.Failure(ItemErrors.BaseUnitInactive);

            var item = Item.Create(
                request.OrganizationId,
                request.Code,
                request.Name,
                request.Description,
                request.BaseUnitId,
                request.CanBePurchased,
                request.CanBeManufactured,
                request.CanBeSold,
                request.CanBeConsumed,
                request.RequiresLotTracking,
                request.QualityControlMode);

            if (await db.Items.AnyAsync(i => i.OrganizationId == request.OrganizationId && i.Code == item.Code, ct))
                return OrganizationResult<ItemDetails>.Failure(ItemErrors.CodeDuplicate);

            db.Items.Add(item);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<ItemDetails>.Success((await BuildItemDetailsAsync(db, request.OrganizationId, item.Id, ct))!);
        }, ct);

    public Task<OrganizationResult<ItemDetails>> UpdateItem(
        long organizationId,
        long id,
        byte[] version,
        UpdateItemRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<ItemDetails>.Failure(org.Error);

            var item = await db.Items.SingleOrDefaultAsync(i => i.OrganizationId == organizationId && i.Id == id, ct);
            if (item is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
            if (!Matches(db, item, version)) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

            var unit = await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == organizationId && u.Id == request.BaseUnitId, ct);
            if (unit is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.BaseUnitNotFound);
            if (!unit.IsActive) return OrganizationResult<ItemDetails>.Failure(ItemErrors.BaseUnitInactive);

            if (item.Update(
                request.Name,
                request.Description,
                request.BaseUnitId,
                request.CanBePurchased,
                request.CanBeManufactured,
                request.CanBeSold,
                request.CanBeConsumed,
                request.RequiresLotTracking,
                request.QualityControlMode))
            {
                await db.SaveInOperation(access.Actor, ct);
            }

            return OrganizationResult<ItemDetails>.Success((await BuildItemDetailsAsync(db, organizationId, item.Id, ct))!);
        }, ct);

    public Task<OrganizationResult<ItemDetails>> SetItemActive(
        long organizationId,
        long id,
        byte[] version,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            await LockOrganization(db, organizationId, ct);
            var orgRow = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
            if (orgRow is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
            if (isActive && !orgRow.IsActive) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ParentOrganizationInactive);

            var item = await db.Items.SingleOrDefaultAsync(i => i.OrganizationId == organizationId && i.Id == id, ct);
            if (item is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
            if (!Matches(db, item, version)) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

            var changed = isActive ? item.Activate() : item.Deactivate();
            if (changed)
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<ItemDetails>.Success((await BuildItemDetailsAsync(db, organizationId, item.Id, ct))!);
        }, ct);

    public Task<OrganizationResult<ItemDetails>> SaveItemClassifications(
        long organizationId,
        long itemId,
        byte[] version,
        IReadOnlyList<long> classificationIds,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<ItemDetails>.Failure(org.Error);

            var item = await db.Items.SingleOrDefaultAsync(i => i.OrganizationId == organizationId && i.Id == itemId, ct);
            if (item is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
            if (!item.IsActive) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemInactive);
            if (!Matches(db, item, version)) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

            var requestedIds = classificationIds.Distinct().ToArray();
            if (requestedIds.Length > 0)
            {
                var targets = await (from c in db.Classifications.AsNoTracking()
                                     join s in db.ClassificationSets.AsNoTracking() on new { c.OrganizationId, SetId = c.ClassificationSetId } equals new { s.OrganizationId, SetId = s.Id }
                                     where c.OrganizationId == organizationId && requestedIds.Contains(c.Id)
                                     select new { c.Id, c.IsActive, SetId = s.Id, s.AllowMultiple, SetActive = s.IsActive })
                    .ToListAsync(ct);

                if (targets.Count != requestedIds.Length)
                    return OrganizationResult<ItemDetails>.Failure(ItemErrors.ClassificationNotFound);
                if (targets.Any(x => !x.IsActive || !x.SetActive))
                    return OrganizationResult<ItemDetails>.Failure(ItemErrors.ClassificationInactive);
                if (targets.GroupBy(x => x.SetId).Any(g => !g.First().AllowMultiple && g.Count() > 1))
                    return OrganizationResult<ItemDetails>.Failure(ItemErrors.ClassificationParentInvalid);
            }

            var existing = await db.ItemClassifications
                .Where(ic => ic.OrganizationId == organizationId && ic.ItemId == itemId)
                .ToListAsync(ct);

            var existingSet = existing.Select(x => x.ClassificationId).ToHashSet();
            var requestedSet = requestedIds.ToHashSet();

            foreach (var remove in existing.Where(x => !requestedSet.Contains(x.ClassificationId)))
                db.ItemClassifications.Remove(remove);

            foreach (var addId in requestedIds.Where(id => !existingSet.Contains(id)))
                db.ItemClassifications.Add(new ItemClassification(organizationId, itemId, addId));

            if (db.ChangeTracker.HasChanges())
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<ItemDetails>.Success((await BuildItemDetailsAsync(db, organizationId, itemId, ct))!);
        }, ct);

    public Task<IReadOnlyList<UnitConversionEdge>> ListItemActiveConversionEdges(long organizationId, long itemId, CancellationToken ct)
        => Read<IReadOnlyList<UnitConversionEdge>>(async db =>
        {
            return await (from c in db.ItemUnitConversions.AsNoTracking()
                          join f in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                          join t in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                          where c.OrganizationId == organizationId && c.ItemId == itemId && c.IsActive && f.IsActive && t.IsActive
                          select new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, true))
                .ToListAsync(ct);
        }, ct);

    public Task<OrganizationResult<ItemDetails>> CreateItemConversion(
        CreateItemUnitConversionRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<ItemDetails>.Failure(org.Error);

            var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.OrganizationId == request.OrganizationId && i.Id == request.ItemId, ct);
            if (item is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
            if (!item.IsActive) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemInactive);

            var fromUnit = await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == request.OrganizationId && u.Id == request.FromUnitId, ct);
            var toUnit = await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == request.OrganizationId && u.Id == request.ToUnitId, ct);
            if (fromUnit is null || toUnit is null) return OrganizationResult<ItemDetails>.Failure(UnitErrors.NotFound);
            if (!fromUnit.IsActive || !toUnit.IsActive) return OrganizationResult<ItemDetails>.Failure(UnitErrors.UnitInactive);

            var conversion = ItemUnitConversion.Create(request.OrganizationId, request.ItemId, request.FromUnitId, request.ToUnitId, request.Factor);
            if (await db.ItemUnitConversions.AnyAsync(c =>
                c.OrganizationId == request.OrganizationId
                && c.ItemId == request.ItemId
                && c.FromUnitId == request.FromUnitId
                && c.ToUnitId == request.ToUnitId, ct))
            {
                return OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemUnitConversionDuplicate);
            }

            await ValidateItemGraphConsistencyAsync(db, request.OrganizationId, request.ItemId, conversion, excludeId: null, ct);

            db.ItemUnitConversions.Add(conversion);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<ItemDetails>.Success((await BuildItemDetailsAsync(db, request.OrganizationId, request.ItemId, ct))!);
        }, ct);

    public Task<OrganizationResult<ItemDetails>> UpdateItemConversionFactor(
        long organizationId,
        long itemId,
        long conversionId,
        byte[] version,
        decimal factor,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<ItemDetails>.Failure(org.Error);

            var conv = await db.ItemUnitConversions.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.ItemId == itemId && c.Id == conversionId, ct);
            if (conv is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemUnitConversionNotFound);
            if (!Matches(db, conv, version)) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

            if (conv.UpdateFactor(factor))
            {
                await ValidateItemGraphConsistencyAsync(db, organizationId, itemId, conv, conv.Id, ct);
                await db.SaveInOperation(access.Actor, ct);
            }

            return OrganizationResult<ItemDetails>.Success((await BuildItemDetailsAsync(db, organizationId, itemId, ct))!);
        }, ct);

    public Task<OrganizationResult<ItemDetails>> SetItemConversionActive(
        long organizationId,
        long itemId,
        long conversionId,
        byte[] version,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            await LockOrganization(db, organizationId, ct);
            var orgRow = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
            if (orgRow is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
            if (isActive && !orgRow.IsActive) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ParentOrganizationInactive);

            var conv = await db.ItemUnitConversions.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.ItemId == itemId && c.Id == conversionId, ct);
            if (conv is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemUnitConversionNotFound);
            if (!Matches(db, conv, version)) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

            var changed = isActive ? conv.Activate() : conv.Deactivate();
            if (changed)
            {
                if (isActive)
                    await ValidateItemGraphConsistencyAsync(db, organizationId, itemId, conv, conv.Id, ct);
                await db.SaveInOperation(access.Actor, ct);
            }

            return OrganizationResult<ItemDetails>.Success((await BuildItemDetailsAsync(db, organizationId, itemId, ct))!);
        }, ct);

    public Task<IReadOnlyList<ClassificationSetSummary>> ListClassificationSets(long organizationId, bool includeInactive, CancellationToken ct)
        => Read<IReadOnlyList<ClassificationSetSummary>>(async db =>
        {
            var rows = await db.ClassificationSets
                .Where(s => s.OrganizationId == organizationId && (includeInactive || s.IsActive))
                .OrderBy(s => s.Code)
                .ThenBy(s => s.Id)
                .ToListAsync(ct);

            return rows.Select(s => ToSetSummary(db, s)).ToArray();
        }, ct);

    public Task<OrganizationResult<ClassificationSetSummary>> CreateClassificationSet(
        CreateClassificationSetRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<ClassificationSetSummary>.Failure(org.Error);

            var set = ClassificationSet.Create(request.OrganizationId, request.Code, request.Name, request.IsMandatory, request.AllowMultiple);
            if (await db.ClassificationSets.AnyAsync(s => s.OrganizationId == request.OrganizationId && s.Code == set.Code, ct))
                return OrganizationResult<ClassificationSetSummary>.Failure(ItemErrors.ClassificationSetCodeDuplicate);

            db.ClassificationSets.Add(set);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<ClassificationSetSummary>.Success(ToSetSummary(db, set));
        }, ct);

    public Task<OrganizationPage<ClassificationSummary>> ListClassifications(long organizationId, ClassificationListQuery query, CancellationToken ct) => Read(async db =>
    {
        var joined = from c in db.Classifications
                     join s in db.ClassificationSets on new { c.OrganizationId, SetId = c.ClassificationSetId } equals new { s.OrganizationId, SetId = s.Id }
                     join p in db.Classifications on new { c.OrganizationId, ParentId = c.ParentClassificationId } equals new { p.OrganizationId, ParentId = (long?)p.Id } into parents
                     from p in parents.DefaultIfEmpty()
                     where c.OrganizationId == organizationId && (query.IncludeInactive || c.IsActive)
                     select new { c, s, p };

        if (query.ClassificationSetId is long setId && setId > 0)
            joined = joined.Where(x => x.c.ClassificationSetId == setId);
        if (!string.IsNullOrEmpty(query.Search))
            joined = joined.Where(x => x.c.Code.Contains(query.Search) || x.c.Name.Contains(query.Search));

        var count = await joined.CountAsync(ct);
        var page = await joined
            .OrderBy(x => x.s.Code)
            .ThenBy(x => x.c.Code)
            .ThenBy(x => x.c.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new OrganizationPage<ClassificationSummary>(
            page.Select(x => ToClassificationSummary(db, x.c, x.s, x.p)).ToArray(),
            count,
            query.Page,
            query.PageSize);
    }, ct);

    public Task<IReadOnlyList<ClassificationSummary>> ListActiveClassifications(long organizationId, CancellationToken ct) => Read<IReadOnlyList<ClassificationSummary>>(async db =>
    {
        var rows = await (from c in db.Classifications
                          join s in db.ClassificationSets on new { c.OrganizationId, SetId = c.ClassificationSetId } equals new { s.OrganizationId, SetId = s.Id }
                          join p in db.Classifications on new { c.OrganizationId, ParentId = c.ParentClassificationId } equals new { p.OrganizationId, ParentId = (long?)p.Id } into parents
                          from p in parents.DefaultIfEmpty()
                          where c.OrganizationId == organizationId && c.IsActive && s.IsActive
                          orderby s.Code, c.Code, c.Id
                          select new { c, s, p })
            .ToListAsync(ct);

        return rows.Select(x => ToClassificationSummary(db, x.c, x.s, x.p)).ToArray();
    }, ct);

    public Task<ClassificationSummary?> GetClassification(long organizationId, long id, CancellationToken ct) => Read(async db =>
    {
        var row = await (from c in db.Classifications
                         join s in db.ClassificationSets on new { c.OrganizationId, SetId = c.ClassificationSetId } equals new { s.OrganizationId, SetId = s.Id }
                         join p in db.Classifications on new { c.OrganizationId, ParentId = c.ParentClassificationId } equals new { p.OrganizationId, ParentId = (long?)p.Id } into parents
                         from p in parents.DefaultIfEmpty()
                         where c.OrganizationId == organizationId && c.Id == id
                         select new { c, s, p })
            .SingleOrDefaultAsync(ct);

        return row is null ? null : ToClassificationSummary(db, row.c, row.s, row.p);
    }, ct);

    public Task<OrganizationResult<ClassificationSummary>> CreateClassification(
        CreateClassificationRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<ClassificationSummary>.Failure(org.Error);

            var set = await db.ClassificationSets.AsNoTracking()
                .SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.ClassificationSetId, ct);
            if (set is null) return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationSetNotFound);
            if (!set.IsActive) return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationInactive);

            Classification? parent = null;
            if (request.ParentClassificationId is long parentId)
            {
                parent = await db.Classifications.AsNoTracking()
                    .SingleOrDefaultAsync(c => c.OrganizationId == request.OrganizationId && c.Id == parentId, ct);
                if (parent is null || parent.ClassificationSetId != request.ClassificationSetId)
                    return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationParentInvalid);
            }

            var node = Classification.Create(request.OrganizationId, request.ClassificationSetId, request.ParentClassificationId, request.Code, request.Name);
            if (await db.Classifications.AnyAsync(c =>
                c.OrganizationId == request.OrganizationId
                && c.ClassificationSetId == request.ClassificationSetId
                && c.Code == node.Code, ct))
            {
                return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationCodeDuplicate);
            }

            db.Classifications.Add(node);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<ClassificationSummary>.Success(ToClassificationSummary(db, node, set, parent));
        }, ct);

    public Task<OrganizationResult<ClassificationSummary>> UpdateClassification(
        long organizationId,
        long id,
        byte[] version,
        string? name,
        long? parentClassificationId,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<ClassificationSummary>.Failure(org.Error);

            var node = await db.Classifications.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, ct);
            if (node is null) return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound);
            if (!Matches(db, node, version)) return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ConcurrencyConflict);

            var set = await db.ClassificationSets.AsNoTracking().SingleAsync(s => s.OrganizationId == organizationId && s.Id == node.ClassificationSetId, ct);
            Classification? parent = null;
            if (parentClassificationId is long parentId)
            {
                if (parentId == id)
                    return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationCircularParent);

                var allInSet = await db.Classifications.AsNoTracking()
                    .Where(c => c.OrganizationId == organizationId && c.ClassificationSetId == node.ClassificationSetId)
                    .ToDictionaryAsync(c => c.Id, ct);

                if (!allInSet.TryGetValue(parentId, out parent))
                    return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationParentInvalid);

                var cursor = (long?)parentId;
                while (cursor is long cid)
                {
                    if (cid == id)
                        return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationCircularParent);
                    cursor = allInSet.TryGetValue(cid, out var p) ? p.ParentClassificationId : null;
                }
            }

            if (node.Update(name, parentClassificationId))
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<ClassificationSummary>.Success(ToClassificationSummary(db, node, set, parent));
        }, ct);

    public Task<OrganizationResult<ClassificationSummary>> SetClassificationActive(
        long organizationId,
        long id,
        byte[] version,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            await LockOrganization(db, organizationId, ct);
            var orgRow = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
            if (orgRow is null) return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.NotFound);
            if (isActive && !orgRow.IsActive) return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ParentOrganizationInactive);

            var node = await db.Classifications.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, ct);
            if (node is null) return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ClassificationNotFound);
            if (!Matches(db, node, version)) return OrganizationResult<ClassificationSummary>.Failure(ItemErrors.ConcurrencyConflict);

            var changed = isActive ? node.Activate() : node.Deactivate();
            if (changed)
                await db.SaveInOperation(access.Actor, ct);

            var set = await db.ClassificationSets.AsNoTracking().SingleAsync(s => s.OrganizationId == organizationId && s.Id == node.ClassificationSetId, ct);
            var parent = node.ParentClassificationId is long pid
                ? await db.Classifications.AsNoTracking().SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == pid, ct)
                : null;
            return OrganizationResult<ClassificationSummary>.Success(ToClassificationSummary(db, node, set, parent));
        }, ct);

    public Task<OrganizationPage<AttributeDefinitionSummary>> ListAttributeDefinitions(long organizationId, AttributeDefinitionListQuery query, CancellationToken ct) => Read(async db =>
    {
        var joined = from a in db.AttributeDefinitions
                     join u in db.Units on new { a.OrganizationId, UnitId = a.UnitId } equals new { u.OrganizationId, UnitId = (long?)u.Id } into units
                     from u in units.DefaultIfEmpty()
                     where a.OrganizationId == organizationId && (query.IncludeInactive || a.IsActive)
                     select new { a, u };

        if (query.DataType.HasValue)
            joined = joined.Where(x => x.a.DataType == query.DataType.Value);
        if (!string.IsNullOrEmpty(query.Search))
            joined = joined.Where(x => x.a.Code.Contains(query.Search) || x.a.Name.Contains(query.Search));

        var count = await joined.CountAsync(ct);
        var page = await joined
            .OrderBy(x => x.a.Code)
            .ThenBy(x => x.a.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new OrganizationPage<AttributeDefinitionSummary>(
            page.Select(x => ToAttributeSummary(db, x.a, x.u)).ToArray(),
            count,
            query.Page,
            query.PageSize);
    }, ct);

    public Task<IReadOnlyList<AttributeDefinitionSummary>> ListActiveAttributeDefinitions(long organizationId, CancellationToken ct)
        => Read<IReadOnlyList<AttributeDefinitionSummary>>(async db =>
        {
            var rows = await (from a in db.AttributeDefinitions
                              join u in db.Units on new { a.OrganizationId, UnitId = a.UnitId } equals new { u.OrganizationId, UnitId = (long?)u.Id } into units
                              from u in units.DefaultIfEmpty()
                              where a.OrganizationId == organizationId && a.IsActive
                              orderby a.Code, a.Id
                              select new { a, u })
                .ToListAsync(ct);

            return rows.Select(x => ToAttributeSummary(db, x.a, x.u)).ToArray();
        }, ct);

    public Task<AttributeDefinitionSummary?> GetAttributeDefinition(long organizationId, long id, CancellationToken ct) => Read(async db =>
    {
        var row = await (from a in db.AttributeDefinitions
                         join u in db.Units on new { a.OrganizationId, UnitId = a.UnitId } equals new { u.OrganizationId, UnitId = (long?)u.Id } into units
                         from u in units.DefaultIfEmpty()
                         where a.OrganizationId == organizationId && a.Id == id
                         select new { a, u })
            .SingleOrDefaultAsync(ct);

        return row is null ? null : ToAttributeSummary(db, row.a, row.u);
    }, ct);

    public Task<OrganizationResult<AttributeDefinitionSummary>> CreateAttributeDefinition(
        CreateAttributeDefinitionRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<AttributeDefinitionSummary>.Failure(org.Error);

            Unit? unit = null;
            if (request.DataType == AttributeDataType.Decimal && request.UnitId is long uid)
            {
                unit = await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == request.OrganizationId && u.Id == uid, ct);
                if (unit is null) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeUnitNotFound);
                if (!unit.IsActive) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeUnitInactive);
            }

            var attr = AttributeDefinition.Create(request.OrganizationId, request.Code, request.Name, request.DataType, request.UnitId, request.IsRequired);
            if (await db.AttributeDefinitions.AnyAsync(a => a.OrganizationId == request.OrganizationId && a.Code == attr.Code, ct))
                return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeCodeDuplicate);

            db.AttributeDefinitions.Add(attr);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<AttributeDefinitionSummary>.Success(ToAttributeSummary(db, attr, unit));
        }, ct);

    public Task<OrganizationResult<AttributeDefinitionSummary>> UpdateAttributeDefinition(
        long organizationId,
        long id,
        byte[] version,
        string? name,
        long? unitId,
        bool isRequired,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<AttributeDefinitionSummary>.Failure(org.Error);

            var attr = await db.AttributeDefinitions.SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id, ct);
            if (attr is null) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound);
            if (!Matches(db, attr, version)) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.ConcurrencyConflict);

            Unit? unit = null;
            if (attr.DataType == AttributeDataType.Decimal && unitId is long uid)
            {
                unit = await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == organizationId && u.Id == uid, ct);
                if (unit is null) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeUnitNotFound);
                if (!unit.IsActive) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeUnitInactive);
            }

            if (attr.Update(name, unitId, isRequired))
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<AttributeDefinitionSummary>.Success(ToAttributeSummary(db, attr, unit));
        }, ct);

    public Task<OrganizationResult<AttributeDefinitionSummary>> SetAttributeDefinitionActive(
        long organizationId,
        long id,
        byte[] version,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            await LockOrganization(db, organizationId, ct);
            var orgRow = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
            if (orgRow is null) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.NotFound);
            if (isActive && !orgRow.IsActive) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.ParentOrganizationInactive);

            var attr = await db.AttributeDefinitions.SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id, ct);
            if (attr is null) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.AttributeDefinitionNotFound);
            if (!Matches(db, attr, version)) return OrganizationResult<AttributeDefinitionSummary>.Failure(ItemErrors.ConcurrencyConflict);

            var changed = isActive ? attr.Activate() : attr.Deactivate();
            if (changed)
                await db.SaveInOperation(access.Actor, ct);

            var unit = attr.UnitId is long uid
                ? await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == organizationId && u.Id == uid, ct)
                : null;
            return OrganizationResult<AttributeDefinitionSummary>.Success(ToAttributeSummary(db, attr, unit));
        }, ct);

    public Task<OrganizationResult<ItemDetails>> SaveItemAttributeValues(
        long organizationId,
        long itemId,
        byte[] version,
        IReadOnlyList<ItemAttributeValueInput> values,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<ItemDetails>.Failure(org.Error);

            var item = await db.Items.SingleOrDefaultAsync(i => i.OrganizationId == organizationId && i.Id == itemId, ct);
            if (item is null) return OrganizationResult<ItemDetails>.Failure(ItemErrors.NotFound);
            if (!item.IsActive) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ItemInactive);
            if (!Matches(db, item, version)) return OrganizationResult<ItemDetails>.Failure(ItemErrors.ConcurrencyConflict);

            var defs = await db.AttributeDefinitions.AsNoTracking()
                .Where(d => d.OrganizationId == organizationId && d.IsActive)
                .ToDictionaryAsync(d => d.Id, ct);

            var existing = await db.ItemAttributeValues
                .Where(v => v.OrganizationId == organizationId && v.ItemId == itemId)
                .ToDictionaryAsync(v => v.AttributeDefinitionId, ct);

            var providedMap = new Dictionary<long, ItemAttributeValueInput>();
            foreach (var input in values)
            {
                if (!defs.ContainsKey(input.AttributeDefinitionId))
                    return OrganizationResult<ItemDetails>.Failure(ItemErrors.AttributeDefinitionNotFound);
                providedMap[input.AttributeDefinitionId] = input;
            }

            foreach (var def in defs.Values)
            {
                providedMap.TryGetValue(def.Id, out var input);
                var hasValue = input is not null && def.DataType switch
                {
                    AttributeDataType.Text => !string.IsNullOrWhiteSpace(input.TextValue),
                    AttributeDataType.Decimal => input.DecimalValue.HasValue,
                    AttributeDataType.Boolean => input.BooleanValue.HasValue,
                    _ => false
                };

                if (!hasValue)
                {
                    if (def.IsRequired)
                        return OrganizationResult<ItemDetails>.Failure(ItemErrors.AttributeRequired, new Dictionary<string, object> { ["code"] = def.Code });

                    if (existing.TryGetValue(def.Id, out var toDelete))
                        db.ItemAttributeValues.Remove(toDelete);
                    continue;
                }

                if (existing.TryGetValue(def.Id, out var existingVal))
                {
                    existingVal.UpdateValidated(def, input!.TextValue, input.DecimalValue, input.BooleanValue);
                }
                else
                {
                    db.ItemAttributeValues.Add(ItemAttributeValue.CreateValidated(
                        def,
                        itemId,
                        input!.TextValue,
                        input.DecimalValue,
                        input.BooleanValue));
                }
            }

            if (db.ChangeTracker.HasChanges())
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<ItemDetails>.Success((await BuildItemDetailsAsync(db, organizationId, itemId, ct))!);
        }, ct);

    private static async Task ValidateItemGraphConsistencyAsync(
        OrganizationDbContext db,
        long organizationId,
        long itemId,
        ItemUnitConversion candidate,
        long? excludeId,
        CancellationToken ct)
    {
        var globalEdges = await (from c in db.UnitConversions.AsNoTracking()
                                 join f in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                                 join t in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                                 where c.OrganizationId == organizationId && c.IsActive && f.IsActive && t.IsActive
                                 select new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, false))
            .ToListAsync(ct);

        var itemEdges = await (from c in db.ItemUnitConversions.AsNoTracking()
                               join f in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                               join t in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                               where c.OrganizationId == organizationId
                                     && c.ItemId == itemId
                                     && c.IsActive
                                     && f.IsActive
                                     && t.IsActive
                                     && (!excludeId.HasValue || c.Id != excludeId.Value)
                               select new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, true))
            .ToListAsync(ct);

        itemEdges.Add(new UnitConversionEdge(candidate.FromUnitId, candidate.ToUnitId, candidate.Factor, true));
        _ = UnitConversionGraph.ResolveFactor(candidate.FromUnitId, candidate.ToUnitId, itemEdges.Concat(globalEdges));
    }

    private static async Task<ItemDetails?> BuildItemDetailsAsync(OrganizationDbContext db, long organizationId, long id, CancellationToken ct)
    {
        var row = await (from i in db.Items.AsNoTracking()
                         join u in db.Units.AsNoTracking() on new { i.OrganizationId, UnitId = i.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                         where i.OrganizationId == organizationId && i.Id == id
                         select new
                         {
                             i.Id,
                             i.OrganizationId,
                             i.Code,
                             i.Name,
                             i.Description,
                             i.BaseUnitId,
                             BaseUnitCode = u.Code,
                             BaseUnitName = u.Name,
                             BaseUnitSymbol = u.Symbol,
                             i.CanBePurchased,
                             i.CanBeManufactured,
                             i.CanBeSold,
                             i.CanBeConsumed,
                             i.IsActive,
                             RowVersion = EF.Property<byte[]>(i, "RowVersion"),
                             i.RequiresLotTracking,
                             i.QualityControlMode
                         })
            .SingleOrDefaultAsync(ct);
        if (row is null) return null;

        var classifications = await (from ic in db.ItemClassifications.AsNoTracking()
                                     join c in db.Classifications.AsNoTracking() on new { ic.OrganizationId, ClassId = ic.ClassificationId } equals new { c.OrganizationId, ClassId = c.Id }
                                     join s in db.ClassificationSets.AsNoTracking() on new { c.OrganizationId, SetId = c.ClassificationSetId } equals new { s.OrganizationId, SetId = s.Id }
                                     where ic.OrganizationId == organizationId && ic.ItemId == id
                                     orderby s.Code, c.Code
                                     select new ItemClassificationSummary(c.Id, s.Id, s.Code, s.Name, c.Code, c.Name))
            .ToListAsync(ct);

        var conversions = await (from c in db.ItemUnitConversions.AsNoTracking()
                                 join f in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                                 join t in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                                 where c.OrganizationId == organizationId && c.ItemId == id
                                 orderby f.Code, t.Code, c.Id
                                 select new
                                 {
                                     c.Id,
                                     c.OrganizationId,
                                     c.ItemId,
                                     c.FromUnitId,
                                     FromCode = f.Code,
                                     FromName = f.Name,
                                     FromSymbol = f.Symbol,
                                     c.ToUnitId,
                                     ToCode = t.Code,
                                     ToName = t.Name,
                                     ToSymbol = t.Symbol,
                                     c.Factor,
                                     c.IsActive,
                                     RowVersion = EF.Property<byte[]>(c, "RowVersion")
                                 })
            .ToListAsync(ct);

        var attrs = await (from ad in db.AttributeDefinitions.AsNoTracking()
                           join u in db.Units.AsNoTracking() on new { ad.OrganizationId, UnitId = ad.UnitId } equals new { u.OrganizationId, UnitId = (long?)u.Id } into units
                           from u in units.DefaultIfEmpty()
                           join v in db.ItemAttributeValues.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.ItemId == id)
                               on ad.Id equals v.AttributeDefinitionId into vals
                           from v in vals.DefaultIfEmpty()
                           where ad.OrganizationId == organizationId && (ad.IsActive || v != null)
                           orderby ad.Code
                           select new ItemAttributeValueSummary(
                               ad.Id,
                               ad.Code,
                               ad.Name,
                               ad.DataType,
                               ad.UnitId,
                               u != null ? u.Symbol : null,
                               ad.IsRequired,
                               v != null ? v.TextValue : null,
                               v != null ? v.DecimalValue : null,
                               v != null ? v.BooleanValue : null))
            .ToListAsync(ct);

        return new ItemDetails(
            new ItemSummary(
                row.Id, row.OrganizationId, row.Code, row.Name, row.Description,
                row.BaseUnitId, row.BaseUnitCode, row.BaseUnitName, row.BaseUnitSymbol,
                row.CanBePurchased, row.CanBeManufactured, row.CanBeSold, row.CanBeConsumed,
                row.IsActive, Convert.ToBase64String(row.RowVersion), row.RequiresLotTracking, row.QualityControlMode),
            classifications,
            conversions.Select(x => new ItemUnitConversionSummary(
                x.Id,
                x.OrganizationId,
                x.ItemId,
                x.FromUnitId,
                x.FromCode,
                x.FromName,
                x.FromSymbol,
                x.ToUnitId,
                x.ToCode,
                x.ToName,
                x.ToSymbol,
                x.Factor,
                x.IsActive,
                Convert.ToBase64String(x.RowVersion))).ToArray(),
            attrs);
    }

    private static ClassificationSetSummary ToSetSummary(OrganizationDbContext db, ClassificationSet s) => new(
        s.Id,
        s.OrganizationId,
        s.Code,
        s.Name,
        s.IsMandatory,
        s.AllowMultiple,
        s.IsActive,
        Convert.ToBase64String(db.Entry(s).Property<byte[]>("RowVersion").CurrentValue!));

    private static ClassificationSummary ToClassificationSummary(
        OrganizationDbContext db,
        Classification c,
        ClassificationSet s,
        Classification? parent) => new(
        c.Id,
        c.OrganizationId,
        c.ClassificationSetId,
        s.Code,
        s.Name,
        c.ParentClassificationId,
        parent?.Code,
        parent?.Name,
        c.Code,
        c.Name,
        c.IsActive,
        Convert.ToBase64String(db.Entry(c).Property<byte[]>("RowVersion").CurrentValue!));

    private static AttributeDefinitionSummary ToAttributeSummary(OrganizationDbContext db, AttributeDefinition a, Unit? unit) => new(
        a.Id,
        a.OrganizationId,
        a.Code,
        a.Name,
        a.DataType,
        a.UnitId,
        unit?.Code,
        unit?.Symbol,
        a.IsRequired,
        a.IsActive,
        Convert.ToBase64String(db.Entry(a).Property<byte[]>("RowVersion").CurrentValue!));

    private async Task<T> Read<T>(Func<OrganizationDbContext, Task<T>> query, CancellationToken ct)
    {
        try
        {
            await using var db = await Factory.CreateDbContextAsync(ct);
            return await query(db);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            throw new ItemStoreUnavailableException(ex);
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
                    return OrganizationResult<T>.Failure(ItemErrors.AccessDenied);

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
        catch (ItemRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(ItemErrors.ConcurrencyConflict); }
        catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(ItemErrors.OutcomeUnknown); }
        catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
        { return OrganizationResult<T>.Failure(ItemErrors.AuditUnavailable); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627)
        {
            if (sql.Message.Contains("'UX_Item_Organization_Code'", StringComparison.Ordinal))
                return OrganizationResult<T>.Failure(ItemErrors.CodeDuplicate);
            if (sql.Message.Contains("'UX_ClassificationSet_Org_Code'", StringComparison.Ordinal))
                return OrganizationResult<T>.Failure(ItemErrors.ClassificationSetCodeDuplicate);
            if (sql.Message.Contains("'UX_Classification_Org_Set_Code'", StringComparison.Ordinal))
                return OrganizationResult<T>.Failure(ItemErrors.ClassificationCodeDuplicate);
            if (sql.Message.Contains("'UX_AttributeDefinition_Org_Code'", StringComparison.Ordinal))
                return OrganizationResult<T>.Failure(ItemErrors.AttributeCodeDuplicate);
            if (sql.Message.Contains("'UX_ItemUnitConversion_Org_Item_From_To'", StringComparison.Ordinal))
                return OrganizationResult<T>.Failure(ItemErrors.ItemUnitConversionDuplicate);
            return OrganizationResult<T>.Failure(ItemErrors.CodeDuplicate);
        }
        catch (InvalidOperationException ex) when (ex.Message == ItemErrors.ParentOrganizationInactive)
        { return OrganizationResult<T>.Failure(ItemErrors.ParentOrganizationInactive); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationSiteErrors.OrganizationNotFound)
        { return OrganizationResult<T>.Failure(ItemErrors.NotFound); }
        catch (Exception ex) when (Unavailable(ex))
        { return OrganizationResult<T>.Failure(ItemErrors.PersistenceUnavailable); }
    }

    private static async Task<(bool Ok, string? Error)> LockAndRequireActiveOrganization(OrganizationDbContext db, long organizationId, CancellationToken ct)
    {
        await LockOrganization(db, organizationId, ct);
        var org = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return (false, ItemErrors.NotFound);
        if (!org.IsActive) return (false, ItemErrors.ParentOrganizationInactive);
        return (true, null);
    }

    private static Task LockOrganization(OrganizationDbContext db, long id, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}", ct);

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
