using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class BomOperationsStore(Func<IDbContextFactory<OrganizationDbContext>> getFactory) : IBomOperationsStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public BomOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public Task<OrganizationPage<BomSummary>> ListBoms(long organizationId, BomListQuery query, CancellationToken ct) => Read(async db =>
    {
        var joined = from b in db.Boms
                     join i in db.Items on new { b.OrganizationId, ItemId = b.PrimaryOutputItemId } equals new { i.OrganizationId, ItemId = i.Id }
                     join u in db.Units on new { i.OrganizationId, UnitId = i.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                     where b.OrganizationId == organizationId && (query.IncludeInactive || b.IsActive)
                     select new { b, i, u };

        if (query.PrimaryOutputItemId is long itemId && itemId > 0)
            joined = joined.Where(x => x.b.PrimaryOutputItemId == itemId);
        if (!string.IsNullOrEmpty(query.Search))
            joined = joined.Where(x => x.b.Code.Contains(query.Search) || x.b.Name.Contains(query.Search) || x.i.Code.Contains(query.Search) || x.i.Name.Contains(query.Search));

        var count = await joined.CountAsync(ct);
        var page = await joined
            .OrderBy(x => x.b.Code)
            .ThenBy(x => x.b.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var bomIds = page.Select(x => x.b.Id).ToArray();
        var versionCounts = bomIds.Length == 0
            ? new Dictionary<long, int>()
            : await db.BomVersions.AsNoTracking()
                .Where(v => v.OrganizationId == organizationId && bomIds.Contains(v.BomId))
                .GroupBy(v => v.BomId)
                .Select(g => new { BomId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.BomId, x => x.Count, ct);

        var items = page.Select(x => new BomSummary(
            x.b.Id,
            x.b.OrganizationId,
            x.b.Code,
            x.b.Name,
            x.b.PrimaryOutputItemId,
            x.i.Code,
            x.i.Name,
            x.u.Symbol,
            x.b.IsActive,
            versionCounts.GetValueOrDefault(x.b.Id),
            Convert.ToBase64String(db.Entry(x.b).Property<byte[]>("RowVersion").CurrentValue!))).ToArray();

        return new OrganizationPage<BomSummary>(items, count, query.Page, query.PageSize);
    }, ct);

    public Task<BomDetails?> GetBom(long organizationId, long id, CancellationToken ct)
        => Read(db => BuildBomDetailsAsync(db, organizationId, id, ct), ct);

    public Task<BomVersionDetails?> GetBomVersion(long organizationId, long bomVersionId, CancellationToken ct)
        => Read(db => BuildBomVersionDetailsAsync(db, organizationId, bomVersionId, ct), ct);

    public Task<OrganizationResult<BomDetails>> CreateBom(CreateBomRequest request, OrganizationWriteAccess access, CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<BomDetails>.Failure(org.Error);

            var primaryItem = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.OrganizationId == request.OrganizationId && i.Id == request.PrimaryOutputItemId, ct);
            if (primaryItem is null || !primaryItem.IsActive)
                return OrganizationResult<BomDetails>.Failure(BomErrors.PrimaryItemInvalid);
            if (!primaryItem.CanBeManufactured)
                return OrganizationResult<BomDetails>.Failure(BomErrors.PrimaryItemNotManufacturable);

            var bom = Bom.Create(request.OrganizationId, request.Code, request.Name, request.PrimaryOutputItemId);
            if (await db.Boms.AnyAsync(b => b.OrganizationId == request.OrganizationId && b.Code == bom.Code, ct))
                return OrganizationResult<BomDetails>.Failure(BomErrors.CodeDuplicate);

            db.Boms.Add(bom);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<BomDetails>.Success((await BuildBomDetailsAsync(db, request.OrganizationId, bom.Id, ct))!);
        }, ct);

    public Task<OrganizationResult<BomDetails>> UpdateBom(
        long organizationId,
        long id,
        byte[] version,
        string? name,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<BomDetails>.Failure(org.Error);

            var bom = await db.Boms.SingleOrDefaultAsync(b => b.OrganizationId == organizationId && b.Id == id, ct);
            if (bom is null) return OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);
            if (!Matches(db, bom, version)) return OrganizationResult<BomDetails>.Failure(BomErrors.ConcurrencyConflict);

            if (bom.Rename(name))
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<BomDetails>.Success((await BuildBomDetailsAsync(db, organizationId, id, ct))!);
        }, ct);

    public Task<OrganizationResult<BomDetails>> SetBomActive(
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
            if (orgRow is null) return OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);
            if (isActive && !orgRow.IsActive) return OrganizationResult<BomDetails>.Failure(BomErrors.ParentOrganizationInactive);

            var bom = await db.Boms.SingleOrDefaultAsync(b => b.OrganizationId == organizationId && b.Id == id, ct);
            if (bom is null) return OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);
            if (!Matches(db, bom, version)) return OrganizationResult<BomDetails>.Failure(BomErrors.ConcurrencyConflict);

            var changed = isActive ? bom.Activate() : bom.Deactivate();
            if (changed)
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<BomDetails>.Success((await BuildBomDetailsAsync(db, organizationId, id, ct))!);
        }, ct);

    public Task<OrganizationResult<BomVersionDetails>> CreateBomVersion(
        CreateBomVersionRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<BomVersionDetails>.Failure(org.Error);

            var bom = await db.Boms.AsNoTracking().SingleOrDefaultAsync(b => b.OrganizationId == request.OrganizationId && b.Id == request.BomId, ct);
            if (bom is null) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.NotFound);
            if (!bom.IsActive) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.BomInactive);

            var unit = await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == request.OrganizationId && u.Id == request.BaseOutputUnitId, ct);
            if (unit is null || !unit.IsActive) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.UnitInvalid);

            if (await db.BomVersions.AnyAsync(v => v.OrganizationId == request.OrganizationId && v.BomId == request.BomId && v.VersionNumber == request.VersionNumber, ct))
                return OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionDuplicate);

            var ver = BomVersion.CreateDraft(
                request.OrganizationId,
                request.BomId,
                request.VersionNumber,
                request.BaseOutputQuantity,
                request.BaseOutputUnitId,
                request.EffectiveFromDate,
                request.EffectiveToDate,
                request.Notes);

            db.BomVersions.Add(ver);
            await db.SaveInOperation(access.Actor, ct);

            // Seed default MainProduct output line pointing to Bom.PrimaryOutputItemId cleanly!
            var mainOutput = new BomOutput(request.OrganizationId, ver.Id, 1, bom.PrimaryOutputItemId, BomOutputType.MainProduct, request.BaseOutputQuantity, request.BaseOutputUnitId);
            db.BomOutputs.Add(mainOutput);
            await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<BomVersionDetails>.Success((await BuildBomVersionDetailsAsync(db, request.OrganizationId, ver.Id, ct))!);
        }, ct);

    public Task<OrganizationResult<BomVersionDetails>> UpdateDraftBomVersion(
        long organizationId,
        long bomVersionId,
        byte[] version,
        UpdateDraftBomVersionRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<BomVersionDetails>.Failure(org.Error);

            var ver = await db.BomVersions.SingleOrDefaultAsync(v => v.OrganizationId == organizationId && v.Id == bomVersionId, ct);
            if (ver is null) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
            if (!Matches(db, ver, version)) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);

            var unit = await db.Units.AsNoTracking().SingleOrDefaultAsync(u => u.OrganizationId == organizationId && u.Id == request.BaseOutputUnitId, ct);
            if (unit is null || !unit.IsActive) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.UnitInvalid);

            if (ver.UpdateDraftHeader(request.BaseOutputQuantity, request.BaseOutputUnitId, request.EffectiveFromDate, request.EffectiveToDate, request.Notes))
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<BomVersionDetails>.Success((await BuildBomVersionDetailsAsync(db, organizationId, ver.Id, ct))!);
        }, ct);

    public Task<OrganizationResult<BomVersionDetails>> SaveDraftBomLines(
        long organizationId,
        long bomVersionId,
        byte[] version,
        IReadOnlyList<BomInputLineInput> inputs,
        IReadOnlyList<BomOutputLineInput> outputs,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<BomVersionDetails>.Failure(org.Error);

            var ver = await db.BomVersions.SingleOrDefaultAsync(v => v.OrganizationId == organizationId && v.Id == bomVersionId, ct);
            if (ver is null) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
            if (!Matches(db, ver, version)) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
            ver.EnsureMutableDraft();

            var bom = await db.Boms.AsNoTracking().SingleAsync(b => b.OrganizationId == organizationId && b.Id == ver.BomId, ct);

            var itemIds = inputs.Select(x => x.ItemId).Concat(outputs.Select(x => x.ItemId)).Distinct().ToArray();
            var unitIds = inputs.Select(x => x.UnitId).Concat(outputs.Select(x => x.UnitId)).Distinct().ToArray();

            var itemsMap = await db.Items.AsNoTracking()
                .Where(i => i.OrganizationId == organizationId && itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);
            var unitsMap = await db.Units.AsNoTracking()
                .Where(u => u.OrganizationId == organizationId && unitIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, ct);

            foreach (var input in inputs)
            {
                if (!itemsMap.TryGetValue(input.ItemId, out var it) || !it.IsActive)
                    return OrganizationResult<BomVersionDetails>.Failure(BomErrors.InputItemInvalid);
                if (input.ItemId == bom.PrimaryOutputItemId)
                    return OrganizationResult<BomVersionDetails>.Failure(BomErrors.DirectSelfCycle);
                if (!unitsMap.TryGetValue(input.UnitId, out var u) || !u.IsActive)
                    return OrganizationResult<BomVersionDetails>.Failure(BomErrors.UnitInvalid);
            }

            foreach (var output in outputs)
            {
                if (!itemsMap.TryGetValue(output.ItemId, out var it) || !it.IsActive)
                    return OrganizationResult<BomVersionDetails>.Failure(BomErrors.OutputItemInvalid);
                if (!unitsMap.TryGetValue(output.UnitId, out var u) || !u.IsActive)
                    return OrganizationResult<BomVersionDetails>.Failure(BomErrors.UnitInvalid);
            }

            var existingInputs = await db.BomInputs.Where(x => x.OrganizationId == organizationId && x.BomVersionId == bomVersionId).ToListAsync(ct);
            var existingOutputs = await db.BomOutputs.Where(x => x.OrganizationId == organizationId && x.BomVersionId == bomVersionId).ToListAsync(ct);

            db.BomInputs.RemoveRange(existingInputs);
            db.BomOutputs.RemoveRange(existingOutputs);

            foreach (var i in inputs)
                db.BomInputs.Add(new BomInput(organizationId, bomVersionId, i.Sequence, i.ItemId, i.Quantity, i.UnitId, i.ScrapPercentage, i.IsOptional, i.Notes));

            foreach (var o in outputs)
                db.BomOutputs.Add(new BomOutput(organizationId, bomVersionId, o.Sequence, o.ItemId, o.OutputType, o.Quantity, o.UnitId));

            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<BomVersionDetails>.Success((await BuildBomVersionDetailsAsync(db, organizationId, bomVersionId, ct))!);
        }, ct);

    public Task<OrganizationResult<BomVersionDetails>> SaveDraftBomApplicability(
        long organizationId,
        long bomVersionId,
        byte[] version,
        SaveDraftBomApplicabilityRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<BomVersionDetails>.Failure(org.Error);

            var ver = await db.BomVersions.SingleOrDefaultAsync(v => v.OrganizationId == organizationId && v.Id == bomVersionId, ct);
            if (ver is null) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
            if (!Matches(db, ver, version)) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
            ver.EnsureMutableDraft();

            var existingApp = await db.BomApplicabilities.SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.BomVersionId == bomVersionId, ct);
            if (!request.Enabled)
            {
                if (existingApp is not null)
                {
                    var oldConds = await db.BomApplicabilityConditions.Where(c => c.OrganizationId == organizationId && c.BomApplicabilityId == existingApp.Id).ToListAsync(ct);
                    db.BomApplicabilityConditions.RemoveRange(oldConds);
                    db.BomApplicabilities.Remove(existingApp);
                    await db.SaveInOperation(access.Actor, ct);
                }
                return OrganizationResult<BomVersionDetails>.Success((await BuildBomVersionDetailsAsync(db, organizationId, bomVersionId, ct))!);
            }

            var dimIds = request.Conditions.Select(c => c.ContextDimensionId).Distinct().ToArray();
            var dims = await db.ContextDimensions.AsNoTracking()
                .Where(d => d.OrganizationId == organizationId && dimIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct);
            if (dims.Count != dimIds.Length || dims.Values.Any(d => !d.IsActive))
                return OrganizationResult<BomVersionDetails>.Failure(BomErrors.ContextDimensionNotFound);

            if (request.Conditions.GroupBy(c => (c.ContextDimensionId, c.Operator)).Any(g => g.Count() > 1))
                return OrganizationResult<BomVersionDetails>.Failure(BomErrors.ApplicabilityInvalid);

            if (existingApp is null)
            {
                existingApp = new BomApplicability(organizationId, bomVersionId, request.Name, request.IsActive);
                db.BomApplicabilities.Add(existingApp);
                await db.SaveInOperation(access.Actor, ct);
            }
            else
            {
                existingApp.Update(request.Name, request.IsActive);
                var oldConds = await db.BomApplicabilityConditions.Where(c => c.OrganizationId == organizationId && c.BomApplicabilityId == existingApp.Id).ToListAsync(ct);
                db.BomApplicabilityConditions.RemoveRange(oldConds);
            }

            foreach (var cond in request.Conditions)
                db.BomApplicabilityConditions.Add(new BomApplicabilityCondition(organizationId, existingApp.Id, cond.ContextDimensionId, cond.Operator, cond.Value));

            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<BomVersionDetails>.Success((await BuildBomVersionDetailsAsync(db, organizationId, bomVersionId, ct))!);
        }, ct);

    public Task<OrganizationResult<BomVersionDetails>> TransitionBomVersionStatus(
        long organizationId,
        long bomVersionId,
        byte[] version,
        BomVersionStatus targetStatus,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<BomVersionDetails>.Failure(org.Error);

            var ver = await db.BomVersions.SingleOrDefaultAsync(v => v.OrganizationId == organizationId && v.Id == bomVersionId, ct);
            if (ver is null) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
            if (!Matches(db, ver, version)) return OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);

            var bom = await db.Boms.AsNoTracking().SingleAsync(b => b.OrganizationId == organizationId && b.Id == ver.BomId, ct);
            if (targetStatus is BomVersionStatus.Approved or BomVersionStatus.Active)
            {
                var inputs = await db.BomInputs.AsNoTracking().Where(i => i.OrganizationId == organizationId && i.BomVersionId == bomVersionId).ToListAsync(ct);
                var outputs = await db.BomOutputs.AsNoTracking().Where(o => o.OrganizationId == organizationId && o.BomVersionId == bomVersionId).ToListAsync(ct);

                if (inputs.Count == 0)
                    return OrganizationResult<BomVersionDetails>.Failure(BomErrors.InputsRequired);
                if (!outputs.Any(o => o.OutputType == BomOutputType.MainProduct && o.ItemId == bom.PrimaryOutputItemId))
                    return OrganizationResult<BomVersionDetails>.Failure(BomErrors.MainOutputRequired);
                if (inputs.Any(i => i.ItemId == bom.PrimaryOutputItemId))
                    return OrganizationResult<BomVersionDetails>.Failure(BomErrors.DirectSelfCycle);
            }

            if (ver.TransitionTo(targetStatus))
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<BomVersionDetails>.Success((await BuildBomVersionDetailsAsync(db, organizationId, bomVersionId, ct))!);
        }, ct);

    public Task<OrganizationPage<ContextDimensionSummary>> ListContextDimensions(long organizationId, ContextDimensionListQuery query, CancellationToken ct) => Read(async db =>
    {
        var rows = db.ContextDimensions.Where(d => d.OrganizationId == organizationId && (query.IncludeInactive || d.IsActive));
        if (!string.IsNullOrEmpty(query.Search))
            rows = rows.Where(d => d.Code.Contains(query.Search) || d.Name.Contains(query.Search));

        var count = await rows.CountAsync(ct);
        var page = await rows.OrderBy(d => d.Code).ThenBy(d => d.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new OrganizationPage<ContextDimensionSummary>(page.Select(d => ToDimSummary(db, d)).ToArray(), count, query.Page, query.PageSize);
    }, ct);

    public Task<IReadOnlyList<ContextDimensionSummary>> ListActiveContextDimensions(long organizationId, CancellationToken ct)
        => Read<IReadOnlyList<ContextDimensionSummary>>(async db =>
        {
            var rows = await db.ContextDimensions.Where(d => d.OrganizationId == organizationId && d.IsActive).OrderBy(d => d.Code).ThenBy(d => d.Id).ToListAsync(ct);
            return rows.Select(d => ToDimSummary(db, d)).ToArray();
        }, ct);

    public Task<ContextDimensionSummary?> GetContextDimension(long organizationId, long id, CancellationToken ct) => Read(async db =>
    {
        var dim = await db.ContextDimensions.SingleOrDefaultAsync(d => d.OrganizationId == organizationId && d.Id == id, ct);
        return dim is null ? null : ToDimSummary(db, dim);
    }, ct);

    public Task<OrganizationResult<ContextDimensionSummary>> CreateContextDimension(
        CreateContextDimensionRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<ContextDimensionSummary>.Failure(org.Error);

            var dim = ContextDimension.Create(request.OrganizationId, request.Code, request.Name);
            if (await db.ContextDimensions.AnyAsync(d => d.OrganizationId == request.OrganizationId && d.Code == dim.Code, ct))
                return OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ContextDimensionCodeDuplicate);

            db.ContextDimensions.Add(dim);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<ContextDimensionSummary>.Success(ToDimSummary(db, dim));
        }, ct);

    public Task<OrganizationResult<ContextDimensionSummary>> UpdateContextDimension(
        long organizationId,
        long id,
        byte[] version,
        string? name,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<ContextDimensionSummary>.Failure(org.Error);

            var dim = await db.ContextDimensions.SingleOrDefaultAsync(d => d.OrganizationId == organizationId && d.Id == id, ct);
            if (dim is null) return OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ContextDimensionNotFound);
            if (!Matches(db, dim, version)) return OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ConcurrencyConflict);

            if (dim.Rename(name))
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<ContextDimensionSummary>.Success(ToDimSummary(db, dim));
        }, ct);

    public Task<OrganizationResult<ContextDimensionSummary>> SetContextDimensionActive(
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
            if (orgRow is null) return OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.NotFound);
            if (isActive && !orgRow.IsActive) return OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ParentOrganizationInactive);

            var dim = await db.ContextDimensions.SingleOrDefaultAsync(d => d.OrganizationId == organizationId && d.Id == id, ct);
            if (dim is null) return OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ContextDimensionNotFound);
            if (!Matches(db, dim, version)) return OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ConcurrencyConflict);

            var changed = isActive ? dim.Activate() : dim.Deactivate();
            if (changed)
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<ContextDimensionSummary>.Success(ToDimSummary(db, dim));
        }, ct);

    public Task<OrganizationResult<SeedContextDimensionsResult>> SeedStandardContextDimensions(
        long organizationId,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<SeedContextDimensionsResult>.Failure(org.Error);

            var existing = await db.ContextDimensions.Where(d => d.OrganizationId == organizationId).Select(d => d.Code).ToHashSetAsync(StringComparer.Ordinal, ct);
            var specs = new (string Code, string Name)[]
            {
                ("PLANT", "Plant / Site"),
                ("PRODUCTION_LINE", "Production Line"),
                ("MARKET", "Target Market"),
                ("CUSTOMER", "Customer"),
                ("MANUFACTURING_METHOD", "Manufacturing Method")
            };

            var created = 0;
            foreach (var (code, name) in specs)
            {
                if (existing.Contains(code)) continue;
                db.ContextDimensions.Add(ContextDimension.Create(organizationId, code, name));
                created++;
            }

            if (created > 0)
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<SeedContextDimensionsResult>.Success(new(created));
        }, ct);

    public Task<BomResolutionDataset?> LoadItemBomResolutionDataset(long organizationId, long itemId, CancellationToken ct)
        => Read(async db =>
        {
            var itemRow = await (from i in db.Items.AsNoTracking()
                                 join u in db.Units.AsNoTracking() on new { i.OrganizationId, UnitId = i.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                                 where i.OrganizationId == organizationId && i.Id == itemId
                                 select new { i, u }).SingleOrDefaultAsync(ct);
            if (itemRow is null)
                return null;

            var boms = await db.Boms.AsNoTracking()
                .Where(b => b.OrganizationId == organizationId && b.PrimaryOutputItemId == itemId && b.IsActive)
                .ToListAsync(ct);

            var globalEdges = await (from c in db.UnitConversions.AsNoTracking()
                                     join f in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                                     join t in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                                     where c.OrganizationId == organizationId && c.IsActive && f.IsActive && t.IsActive
                                     select new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, false))
                .ToListAsync(ct);

            var itemEdges = await ListItemEdgesInternalAsync(db, organizationId, itemId, ct);
            var candidates = new List<BomVersionCandidateRecord>();

            if (boms.Count > 0)
            {
                var bomMap = boms.ToDictionary(b => b.Id);
                var bomIds = bomMap.Keys.ToArray();
                var activeVersions = await db.BomVersions
                    .Where(v => v.OrganizationId == organizationId && bomIds.Contains(v.BomId) && v.Status == BomVersionStatus.Active)
                    .ToListAsync(ct);

                foreach (var ver in activeVersions)
                {
                    var details = await BuildBomVersionDetailsAsync(db, organizationId, ver.Id, ct);
                    if (details is null) continue;
                    var parentBom = bomMap[ver.BomId];
                    candidates.Add(new BomVersionCandidateRecord(
                        parentBom.Id,
                        parentBom.Code,
                        parentBom.Name,
                        parentBom.PrimaryOutputItemId,
                        details,
                        itemEdges));
                }
            }

            return new BomResolutionDataset(
                itemRow.i.Id,
                itemRow.i.Code,
                itemRow.i.Name,
                itemRow.u.Id,
                itemRow.u.Code,
                itemRow.u.Symbol,
                itemRow.i.IsActive,
                candidates,
                globalEdges);
        }, ct);

    public Task<IReadOnlyList<UnitConversionEdge>> ListItemUnitEdges(long organizationId, long itemId, CancellationToken ct)
        => Read<IReadOnlyList<UnitConversionEdge>>(async db => await ListItemEdgesInternalAsync(db, organizationId, itemId, ct), ct);

    private static async Task<List<UnitConversionEdge>> ListItemEdgesInternalAsync(OrganizationDbContext db, long organizationId, long itemId, CancellationToken ct)
        => await (from c in db.ItemUnitConversions.AsNoTracking()
                  join f in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                  join t in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                  where c.OrganizationId == organizationId && c.ItemId == itemId && c.IsActive && f.IsActive && t.IsActive
                  select new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, true))
            .ToListAsync(ct);

    private static async Task<BomDetails?> BuildBomDetailsAsync(OrganizationDbContext db, long organizationId, long id, CancellationToken ct)
    {
        var row = await (from b in db.Boms
                         join i in db.Items on new { b.OrganizationId, ItemId = b.PrimaryOutputItemId } equals new { i.OrganizationId, ItemId = i.Id }
                         join u in db.Units on new { i.OrganizationId, UnitId = i.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                         where b.OrganizationId == organizationId && b.Id == id
                         select new { b, i, u }).SingleOrDefaultAsync(ct);
        if (row is null) return null;

        var versionRows = await (from v in db.BomVersions
                                 join vu in db.Units on new { v.OrganizationId, UnitId = v.BaseOutputUnitId } equals new { vu.OrganizationId, UnitId = vu.Id }
                                 where v.OrganizationId == organizationId && v.BomId == id
                                 orderby v.VersionNumber descending
                                 select new { v, vu }).ToListAsync(ct);

        var verIds = versionRows.Select(x => x.v.Id).ToArray();
        var apps = verIds.Length == 0
            ? new Dictionary<long, (long AppId, bool IsActive)>()
            : await db.BomApplicabilities.AsNoTracking()
                .Where(a => a.OrganizationId == organizationId && verIds.Contains(a.BomVersionId))
                .ToDictionaryAsync(a => a.BomVersionId, a => (AppId: a.Id, a.IsActive), ct);

        var appIds = apps.Values.Select(x => x.AppId).ToArray();
        var condCounts = appIds.Length == 0
            ? new Dictionary<long, int>()
            : await db.BomApplicabilityConditions.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId && appIds.Contains(c.BomApplicabilityId))
                .GroupBy(c => c.BomApplicabilityId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var versionSummaries = versionRows.Select(x =>
        {
            var hasApp = apps.TryGetValue(x.v.Id, out var appInfo);
            var condCount = hasApp ? condCounts.GetValueOrDefault(appInfo.AppId) : 0;
            return new BomVersionSummary(
                x.v.Id,
                x.v.OrganizationId,
                x.v.BomId,
                x.v.VersionNumber,
                x.v.Status,
                x.v.BaseOutputQuantity,
                x.v.BaseOutputUnitId,
                x.vu.Code,
                x.vu.Symbol,
                x.v.EffectiveFromDate,
                x.v.EffectiveToDate,
                x.v.Notes,
                hasApp,
                hasApp && appInfo.IsActive,
                condCount,
                Convert.ToBase64String(db.Entry(x.v).Property<byte[]>("RowVersion").CurrentValue!));
        }).ToArray();

        var summary = new BomSummary(
            row.b.Id,
            row.b.OrganizationId,
            row.b.Code,
            row.b.Name,
            row.b.PrimaryOutputItemId,
            row.i.Code,
            row.i.Name,
            row.u.Symbol,
            row.b.IsActive,
            versionSummaries.Length,
            Convert.ToBase64String(db.Entry(row.b).Property<byte[]>("RowVersion").CurrentValue!));

        return new BomDetails(summary, versionSummaries);
    }

    private static async Task<BomVersionDetails?> BuildBomVersionDetailsAsync(OrganizationDbContext db, long organizationId, long bomVersionId, CancellationToken ct)
    {
        var verRow = await (from v in db.BomVersions
                            join u in db.Units on new { v.OrganizationId, UnitId = v.BaseOutputUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                            where v.OrganizationId == organizationId && v.Id == bomVersionId
                            select new { v, u }).SingleOrDefaultAsync(ct);
        if (verRow is null) return null;

        var inputs = await (from bi in db.BomInputs.AsNoTracking()
                            join it in db.Items.AsNoTracking() on new { bi.OrganizationId, ItemId = bi.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                            join u in db.Units.AsNoTracking() on new { bi.OrganizationId, UnitId = bi.UnitId } equals new { u.OrganizationId, UnitId = u.Id }
                            where bi.OrganizationId == organizationId && bi.BomVersionId == bomVersionId
                            orderby bi.Sequence, bi.Id
                            select new BomInputSummary(bi.Id, bi.Sequence, bi.ItemId, it.Code, it.Name, bi.Quantity, bi.UnitId, u.Code, u.Symbol, bi.ScrapPercentage, bi.IsOptional, bi.Notes))
            .ToListAsync(ct);

        var outputs = await (from bo in db.BomOutputs.AsNoTracking()
                             join it in db.Items.AsNoTracking() on new { bo.OrganizationId, ItemId = bo.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                             join u in db.Units.AsNoTracking() on new { bo.OrganizationId, UnitId = bo.UnitId } equals new { u.OrganizationId, UnitId = u.Id }
                             where bo.OrganizationId == organizationId && bo.BomVersionId == bomVersionId
                             orderby bo.Sequence, bo.Id
                             select new BomOutputSummary(bo.Id, bo.Sequence, bo.ItemId, it.Code, it.Name, bo.OutputType, bo.Quantity, bo.UnitId, u.Code, u.Symbol))
            .ToListAsync(ct);

        var app = await db.BomApplicabilities.AsNoTracking()
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.BomVersionId == bomVersionId, ct);

        BomApplicabilityDetails? appDetails = null;
        if (app is not null)
        {
            var conds = await (from c in db.BomApplicabilityConditions.AsNoTracking()
                               join d in db.ContextDimensions.AsNoTracking() on new { c.OrganizationId, DimId = c.ContextDimensionId } equals new { d.OrganizationId, DimId = d.Id }
                               where c.OrganizationId == organizationId && c.BomApplicabilityId == app.Id
                               orderby d.Code
                               select new BomApplicabilityConditionSummary(d.Id, d.Code, d.Name, d.IsActive, c.Operator, c.Value))
                .ToListAsync(ct);
            appDetails = new BomApplicabilityDetails(app.Name, app.IsActive, conds);
        }

        var summary = new BomVersionSummary(
            verRow.v.Id,
            verRow.v.OrganizationId,
            verRow.v.BomId,
            verRow.v.VersionNumber,
            verRow.v.Status,
            verRow.v.BaseOutputQuantity,
            verRow.v.BaseOutputUnitId,
            verRow.u.Code,
            verRow.u.Symbol,
            verRow.v.EffectiveFromDate,
            verRow.v.EffectiveToDate,
            verRow.v.Notes,
            appDetails is not null,
            appDetails?.IsActive ?? false,
            appDetails?.Conditions.Count ?? 0,
            Convert.ToBase64String(db.Entry(verRow.v).Property<byte[]>("RowVersion").CurrentValue!));

        return new BomVersionDetails(summary, inputs, outputs, appDetails);
    }

    private static ContextDimensionSummary ToDimSummary(OrganizationDbContext db, ContextDimension d) => new(
        d.Id,
        d.OrganizationId,
        d.Code,
        d.Name,
        d.IsActive,
        Convert.ToBase64String(db.Entry(d).Property<byte[]>("RowVersion").CurrentValue!));

    private async Task<T> Read<T>(Func<OrganizationDbContext, Task<T>> query, CancellationToken ct)
    {
        try
        {
            await using var db = await Factory.CreateDbContextAsync(ct);
            return await query(db);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            throw new BomStoreUnavailableException(ex);
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
                    return OrganizationResult<T>.Failure(BomErrors.AccessDenied);

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
        catch (BomRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(BomErrors.ConcurrencyConflict); }
        catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(BomErrors.OutcomeUnknown); }
        catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
        { return OrganizationResult<T>.Failure(BomErrors.AuditUnavailable); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627)
        {
            if (sql.Message.Contains("'UX_Bom_Org_Code'", StringComparison.Ordinal))
                return OrganizationResult<T>.Failure(BomErrors.CodeDuplicate);
            if (sql.Message.Contains("'UX_BomVersion_Org_Bom_Ver'", StringComparison.Ordinal))
                return OrganizationResult<T>.Failure(BomErrors.VersionDuplicate);
            if (sql.Message.Contains("'UX_ContextDimension_Org_Code'", StringComparison.Ordinal))
                return OrganizationResult<T>.Failure(BomErrors.ContextDimensionCodeDuplicate);
            return OrganizationResult<T>.Failure(BomErrors.CodeDuplicate);
        }
        catch (InvalidOperationException ex) when (ex.Message == BomErrors.ParentOrganizationInactive)
        { return OrganizationResult<T>.Failure(BomErrors.ParentOrganizationInactive); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationSiteErrors.OrganizationNotFound)
        { return OrganizationResult<T>.Failure(BomErrors.NotFound); }
        catch (Exception ex) when (Unavailable(ex))
        { return OrganizationResult<T>.Failure(BomErrors.PersistenceUnavailable); }
    }

    private static async Task<(bool Ok, string? Error)> LockAndRequireActiveOrganization(OrganizationDbContext db, long organizationId, CancellationToken ct)
    {
        await LockOrganization(db, organizationId, ct);
        var org = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return (false, BomErrors.NotFound);
        if (!org.IsActive) return (false, BomErrors.ParentOrganizationInactive);
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
