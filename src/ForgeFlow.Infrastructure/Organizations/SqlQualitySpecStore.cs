using System.Globalization;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Quality;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Infrastructure.Diagnostics;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlQualitySpecStore(
    Func<IDbContextFactory<OrganizationDbContext>> getFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null,
    IServiceScopeFactory? scopeFactory = null) : IQualitySpecOperationsStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();

    public Task<OrganizationPage<QualityParameterSummary>> ListParameters(
        long organizationId, QualityParameterListQuery query, CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from p in db.QualityParameters.AsNoTracking()
                        where p.OrganizationId == organizationId
                        select new { p, RowVersion = EF.Property<byte[]>(p, "RowVersion") };

        if (!query.IncludeInactive)
            baseQuery = baseQuery.Where(x => x.p.IsActive);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.p.Code.Contains(query.Search)
                || x.p.Name.Contains(query.Search)
                || x.p.Symbol.Contains(query.Search));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderBy(x => x.p.Code)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => ToParameterSummary(r.p, r.RowVersion)).ToArray();
        return new OrganizationPage<QualityParameterSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<QualityParameterSummary?> GetParameter(long organizationId, long id, CancellationToken ct) => Read(async db =>
    {
        var row = await db.QualityParameters.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Id == id)
            .Select(x => new { p = x, RowVersion = EF.Property<byte[]>(x, "RowVersion") })
            .SingleOrDefaultAsync(ct);
        return row is null ? null : ToParameterSummary(row.p, row.RowVersion);
    }, ct);

    public Task<OrganizationResult<QualityParameterSummary>> CreateParameter(
        CreateQualityParameterRequest request, OrganizationWriteAccess access, CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualityParameterSummary>.Failure(orgCheck.Error!);

        var entity = QualityParameter.Create(
            request.OrganizationId, request.Code, request.Name, request.Symbol,
            request.UnitSymbol, request.DataType, request.DecimalPlaces, request.DefaultTestMethod);

        db.QualityParameters.Add(entity);
        await db.SaveInOperation(access.Actor, ct);

        var rv = (byte[])db.Entry(entity).Property("RowVersion").CurrentValue!;
        return OrganizationResult<QualityParameterSummary>.Success(ToParameterSummary(entity, rv));
    }, ct);

    public Task<OrganizationResult<QualityParameterSummary>> UpdateParameter(
        long organizationId, long id, byte[] version, UpdateQualityParameterRequest request, OrganizationWriteAccess access, CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualityParameterSummary>.Failure(orgCheck.Error!);

        var entity = await db.QualityParameters.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (entity is null) return OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ParameterNotFound);
        if (!Matches(db, entity, version)) return OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ConcurrencyConflict);

        entity.Update(request.Name, request.Symbol, request.UnitSymbol, request.DecimalPlaces, request.DefaultTestMethod);
        await db.SaveInOperation(access.Actor, ct);

        var rv = (byte[])db.Entry(entity).Property("RowVersion").CurrentValue!;
        return OrganizationResult<QualityParameterSummary>.Success(ToParameterSummary(entity, rv));
    }, ct);

    public Task<OrganizationResult<QualityParameterSummary>> SetParameterActive(
        long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualityParameterSummary>.Failure(orgCheck.Error!);

        var entity = await db.QualityParameters.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (entity is null) return OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ParameterNotFound);
        if (!Matches(db, entity, version)) return OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ConcurrencyConflict);

        _ = isActive ? entity.Activate() : entity.Deactivate();
        await db.SaveInOperation(access.Actor, ct);

        var rv = (byte[])db.Entry(entity).Property("RowVersion").CurrentValue!;
        return OrganizationResult<QualityParameterSummary>.Success(ToParameterSummary(entity, rv));
    }, ct);

    public Task<OrganizationPage<QualitySpecSummary>> ListSpecs(
        long organizationId, QualitySpecListQuery query, CancellationToken ct) => Read(async db =>
    {
        var baseQuery = from s in db.QualitySpecs.AsNoTracking()
                        join it in db.Items.AsNoTracking() on new { s.OrganizationId, ItemId = s.ItemId } equals new { it.OrganizationId, ItemId = (long?)it.Id } into itJoin
                        from it in itJoin.DefaultIfEmpty()
                        join cl in db.Classifications.AsNoTracking() on new { s.OrganizationId, ClassId = s.ClassificationId } equals new { cl.OrganizationId, ClassId = (long?)cl.Id } into clJoin
                        from cl in clJoin.DefaultIfEmpty()
                        where s.OrganizationId == organizationId
                        select new
                        {
                            s,
                            ItemCode = it != null ? it.Code : null,
                            ItemName = it != null ? it.Name : null,
                            ClassCode = cl != null ? cl.Code : null,
                            ClassName = cl != null ? cl.Name : null,
                            LineCount = db.QualitySpecLines.Count(l => l.OrganizationId == organizationId && l.QualitySpecId == s.Id),
                            RowVersion = EF.Property<byte[]>(s, "RowVersion")
                        };

        if (!query.IncludeInactive)
            baseQuery = baseQuery.Where(x => x.s.IsActive);
        if (query.TargetKind is QualitySpecTargetKind tk)
            baseQuery = baseQuery.Where(x => x.s.TargetKind == tk);
        if (query.Checkpoint is QualitySpecCheckpoint cp)
            baseQuery = baseQuery.Where(x => x.s.Checkpoint == cp);
        if (!string.IsNullOrEmpty(query.Search))
            baseQuery = baseQuery.Where(x =>
                x.s.Code.Contains(query.Search)
                || x.s.Name.Contains(query.Search)
                || (x.ItemCode != null && x.ItemCode.Contains(query.Search))
                || (x.ItemName != null && x.ItemName.Contains(query.Search))
                || (x.ClassCode != null && x.ClassCode.Contains(query.Search))
                || (x.ClassName != null && x.ClassName.Contains(query.Search)));

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderByDescending(x => x.s.IsActive)
            .ThenBy(x => x.s.Code)
            .ThenByDescending(x => x.s.Version)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new QualitySpecSummary(
            r.s.Id, r.s.OrganizationId, r.s.Code, r.s.Name, r.s.TargetKind,
            r.s.ItemId, r.ItemCode, r.ItemName,
            r.s.ClassificationId, r.ClassCode, r.ClassName,
            r.s.Checkpoint, r.s.Version, r.s.IsActive, r.LineCount, r.s.Notes,
            Convert.ToBase64String(r.RowVersion))).ToArray();

        return new OrganizationPage<QualitySpecSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<QualitySpecDetails?> GetSpec(long organizationId, long id, CancellationToken ct) =>
        Read(db => BuildSpecDetailsAsync(db, organizationId, id, ct), ct);

    public Task<QualitySpecDetails?> ResolveApplicableSpec(
        long organizationId, long itemId, QualitySpecCheckpoint checkpoint, CancellationToken ct) => Read(async db =>
    {
        var specId = await ResolveApplicableSpecIdAsync(db, organizationId, itemId, checkpoint, ct);
        return specId is long sid ? await BuildSpecDetailsAsync(db, organizationId, sid, ct) : null;
    }, ct);

    public Task<OrganizationResult<QualitySpecDetails>> CreateSpec(
        CreateQualitySpecRequest request, IReadOnlyList<QualitySpecLineInputData> resolvedLines, OrganizationWriteAccess access, CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualitySpecDetails>.Failure(orgCheck.Error!);

        if (request.TargetKind == QualitySpecTargetKind.Item)
        {
            var itemOk = await db.Items.AsNoTracking().AnyAsync(i => i.OrganizationId == request.OrganizationId && i.Id == request.ItemId && i.IsActive, ct);
            if (!itemOk) return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ItemInvalid);
        }
        else
        {
            var clsOk = await db.Classifications.AsNoTracking().AnyAsync(c => c.OrganizationId == request.OrganizationId && c.Id == request.ClassificationId && c.IsActive, ct);
            if (!clsOk) return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecTargetInvalid);
        }

        var paramIds = resolvedLines.Select(l => l.QualityParameterId).Distinct().ToArray();
        var activeParams = await db.QualityParameters.AsNoTracking()
            .Where(p => p.OrganizationId == request.OrganizationId && paramIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.IsActive, ct);
        foreach (var pid in paramIds)
        {
            if (!activeParams.TryGetValue(pid, out var isAct))
                return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ParameterNotFound);
            if (!isAct)
                return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ParameterInactive);
        }

        var spec = QualitySpec.Create(
            request.OrganizationId, request.Code, request.Name, request.TargetKind,
            request.ItemId, request.ClassificationId, request.Checkpoint, request.Version, request.Notes, resolvedLines);

        db.QualitySpecs.Add(spec);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<QualitySpecDetails>.Success((await BuildSpecDetailsAsync(db, request.OrganizationId, spec.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<QualitySpecDetails>> UpdateSpec(
        long organizationId, long id, byte[] version, string name, QualitySpecCheckpoint checkpoint, string? notes, IReadOnlyList<QualitySpecLineInputData> resolvedLines, OrganizationWriteAccess access, CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualitySpecDetails>.Failure(orgCheck.Error!);

        var spec = await db.QualitySpecs.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (spec is null) return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound);
        if (!Matches(db, spec, version)) return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ConcurrencyConflict);

        var paramIds = resolvedLines.Select(l => l.QualityParameterId).Distinct().ToArray();
        var activeParams = await db.QualityParameters.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && paramIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.IsActive, ct);
        foreach (var pid in paramIds)
        {
            if (!activeParams.TryGetValue(pid, out var isAct))
                return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ParameterNotFound);
            if (!isAct)
                return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ParameterInactive);
        }

        if (spec.Lines.Count > 0)
            db.QualitySpecLines.RemoveRange(spec.Lines);

        spec.UpdateHeader(name, checkpoint, notes);
        spec.ReplaceLines(resolvedLines);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<QualitySpecDetails>.Success((await BuildSpecDetailsAsync(db, organizationId, spec.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<QualitySpecDetails>> SetSpecActive(
        long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualitySpecDetails>.Failure(orgCheck.Error!);

        var spec = await db.QualitySpecs.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (spec is null) return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound);
        if (!Matches(db, spec, version)) return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ConcurrencyConflict);

        _ = isActive ? spec.Activate() : spec.Deactivate();
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<QualitySpecDetails>.Success((await BuildSpecDetailsAsync(db, organizationId, spec.Id, ct))!);
    }, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> ApplySpecToInspection(
        long organizationId, long inspectionId, byte[] version, OrganizationWriteAccess access, CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<QualityInspectionDetails>.Failure(orgCheck.Error!);

        var qi = await db.QualityInspections.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == inspectionId, ct);
        if (qi is null) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        if (!Matches(db, qi, version)) return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);
        if (qi.Status is QualityInspectionStatus.Completed or QualityInspectionStatus.Cancelled)
            return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionAlreadyTerminal);

        var checkpoint = qi.SourceType == QualityInspectionSourceType.ProductionReceipt
            ? QualitySpecCheckpoint.Production
            : QualitySpecCheckpoint.Incoming;

        var lines = await BuildTemplateLinesForItemAsync(db, organizationId, qi.ItemId, checkpoint, ct);
        if (lines.Count == 0)
            return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.SpecNotFound);

        if (qi.Lines.Count > 0)
            db.QualityInspectionLines.RemoveRange(qi.Lines);

        qi.ReplaceLines(lines, advanceStatus: false);
        await db.SaveInOperation(access.Actor, ct);

        return OrganizationResult<QualityInspectionDetails>.Success(
            (await SqlQualityOperationsStore.BuildInspectionDetailsStaticAsync(db, organizationId, qi.Id, scopeFactory, ct))!);
    }, ct);

    public Task<LotCertificateSummary?> GetLotCertificate(
        long organizationId, long itemId, string lotCode, CancellationToken ct) => Read(async db =>
    {
        var item = await db.Items.AsNoTracking()
            .SingleOrDefaultAsync(i => i.OrganizationId == organizationId && i.Id == itemId, ct);
        if (item is null) return null;

        var lotMeta = await db.ItemLots.AsNoTracking()
            .SingleOrDefaultAsync(l => l.OrganizationId == organizationId && l.ItemId == itemId && l.LotCode == lotCode, ct);

        var activeSpecId = await ResolveApplicableSpecIdAsync(db, organizationId, itemId, QualitySpecCheckpoint.Incoming, ct);
        var activeSpec = activeSpecId is long sid ? await BuildSpecDetailsAsync(db, organizationId, sid, ct) : null;

        var qiRows = await db.QualityInspections.AsNoTracking()
            .Include(q => q.Lines)
            .Where(q => q.OrganizationId == organizationId
                && q.ItemId == itemId
                && q.LotCode == lotCode
                && q.Status != QualityInspectionStatus.Cancelled)
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        var actorNames = await ResolveUserNamesAsync(qiRows.Select(q => q.InspectedByActorId), ct);

        var inspSummaries = qiRows.Select(q => new LotCertificateInspectionSummary(
            q.Id,
            q.InspectionNumber,
            q.Status,
            q.SourceType,
            q.CertificateNumber,
            q.DefectReasonCode,
            q.Notes,
            q.InspectedAtUtc,
            q.InspectedByActorId.HasValue && actorNames.TryGetValue(q.InspectedByActorId.Value, out var name) ? name : null,
            q.Lines.OrderBy(l => l.LineNumber).Select(l => new LotCertificateMeasurementSummary(
                l.Id, l.LineNumber, l.ParameterName, l.ParameterCodeSnapshot, l.SymbolSnapshot,
                l.UnitSymbolSnapshot, l.TestMethodSnapshot, l.DataType,
                l.MinValueSnapshot, l.MaxValueSnapshot, l.TargetValueSnapshot, l.ExpectedSpec,
                l.MeasuredNumericValue, l.MeasuredValue, l.IsPassed, l.IsMandatory, l.Notes)).ToArray())).ToArray();

        return new LotCertificateSummary(
            organizationId, item.Id, item.Code, item.Name, lotCode,
            lotMeta?.ManufactureDate, lotMeta?.ExpirationDate,
            activeSpec, inspSummaries);
    }, ct);

    internal static async Task PopulateInspectionFromSpecAsync(
        OrganizationDbContext db,
        QualityInspection qi,
        QualitySpecCheckpoint checkpoint,
        CancellationToken ct)
    {
        var lines = await BuildTemplateLinesForItemAsync(db, qi.OrganizationId, qi.ItemId, checkpoint, ct);
        if (lines.Count > 0)
            qi.ReplaceLines(lines, advanceStatus: false);
    }

    private static async Task<IReadOnlyList<QualityInspectionLineWriteData>> BuildTemplateLinesForItemAsync(
        OrganizationDbContext db,
        long organizationId,
        long itemId,
        QualitySpecCheckpoint checkpoint,
        CancellationToken ct)
    {
        var specId = await ResolveApplicableSpecIdAsync(db, organizationId, itemId, checkpoint, ct);
        if (specId is null) return [];

        var rows = await (from l in db.QualitySpecLines.AsNoTracking()
                          join p in db.QualityParameters.AsNoTracking() on new { l.OrganizationId, ParamId = l.QualityParameterId } equals new { p.OrganizationId, ParamId = p.Id }
                          where l.OrganizationId == organizationId && l.QualitySpecId == specId.Value
                          orderby l.Sequence
                          select new { l, p }).ToListAsync(ct);

        return rows.Select(x =>
        {
            var expected = x.l.ExpectedTextSpec ?? FormatRangeSpec(x.l.MinValue, x.l.MaxValue, x.l.TargetValue, x.p.UnitSymbol);
            return new QualityInspectionLineWriteData(
                x.p.Name,
                expected,
                null,
                false,
                x.l.Notes,
                x.p.Id,
                x.p.Code,
                x.p.Symbol,
                x.p.UnitSymbol,
                x.p.DefaultTestMethod,
                x.p.DataType,
                x.l.MinValue,
                x.l.MaxValue,
                x.l.TargetValue,
                null,
                x.l.IsMandatory);
        }).ToArray();
    }

    private static async Task<long?> ResolveApplicableSpecIdAsync(
        OrganizationDbContext db,
        long organizationId,
        long itemId,
        QualitySpecCheckpoint checkpoint,
        CancellationToken ct)
    {
        var itemSpecs = await db.QualitySpecs.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId
                && s.IsActive
                && s.TargetKind == QualitySpecTargetKind.Item
                && s.ItemId == itemId
                && (s.Checkpoint == checkpoint || s.Checkpoint == QualitySpecCheckpoint.All))
            .Select(s => new { s.Id, s.Checkpoint, s.Version })
            .ToListAsync(ct);

        if (itemSpecs.Count > 0)
        {
            return itemSpecs
                .OrderBy(s => s.Checkpoint == checkpoint ? 0 : 1)
                .ThenByDescending(s => s.Version)
                .First().Id;
        }

        var directClassIds = await db.ItemClassifications.AsNoTracking()
            .Where(ic => ic.OrganizationId == organizationId && ic.ItemId == itemId)
            .Select(ic => ic.ClassificationId)
            .ToListAsync(ct);

        if (directClassIds.Count == 0)
            return null;

        var allClassifications = await db.Classifications.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.IsActive)
            .Select(c => new { c.Id, c.ParentClassificationId })
            .ToDictionaryAsync(c => c.Id, c => c.ParentClassificationId, ct);

        var candidateOrder = new List<long>();
        var seen = new HashSet<long>();
        var queue = new Queue<long>(directClassIds);
        while (queue.Count > 0)
        {
            var cid = queue.Dequeue();
            if (!seen.Add(cid)) continue;
            candidateOrder.Add(cid);
            if (allClassifications.TryGetValue(cid, out var parent) && parent is long pid && !seen.Contains(pid))
                queue.Enqueue(pid);
        }

        var classSpecs = await db.QualitySpecs.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId
                && s.IsActive
                && s.TargetKind == QualitySpecTargetKind.Classification
                && s.ClassificationId != null
                && candidateOrder.Contains(s.ClassificationId.Value)
                && (s.Checkpoint == checkpoint || s.Checkpoint == QualitySpecCheckpoint.All))
            .Select(s => new { s.Id, ClassId = s.ClassificationId!.Value, s.Checkpoint, s.Version })
            .ToListAsync(ct);

        if (classSpecs.Count == 0) return null;

        foreach (var cid in candidateOrder)
        {
            var match = classSpecs
                .Where(s => s.ClassId == cid)
                .OrderBy(s => s.Checkpoint == checkpoint ? 0 : 1)
                .ThenByDescending(s => s.Version)
                .FirstOrDefault();
            if (match is not null)
                return match.Id;
        }
        return null;
    }

    private static async Task<QualitySpecDetails?> BuildSpecDetailsAsync(
        OrganizationDbContext db,
        long organizationId,
        long id,
        CancellationToken ct)
    {
        var row = await (from s in db.QualitySpecs.AsNoTracking()
                         join it in db.Items.AsNoTracking() on new { s.OrganizationId, ItemId = s.ItemId } equals new { it.OrganizationId, ItemId = (long?)it.Id } into itJoin
                         from it in itJoin.DefaultIfEmpty()
                         join cl in db.Classifications.AsNoTracking() on new { s.OrganizationId, ClassId = s.ClassificationId } equals new { cl.OrganizationId, ClassId = (long?)cl.Id } into clJoin
                         from cl in clJoin.DefaultIfEmpty()
                         where s.OrganizationId == organizationId && s.Id == id
                         select new
                         {
                             s,
                             ItemCode = it != null ? it.Code : null,
                             ItemName = it != null ? it.Name : null,
                             ClassCode = cl != null ? cl.Code : null,
                             ClassName = cl != null ? cl.Name : null,
                             RowVersion = EF.Property<byte[]>(s, "RowVersion")
                         }).SingleOrDefaultAsync(ct);

        if (row is null) return null;

        var lines = await (from l in db.QualitySpecLines.AsNoTracking()
                           join p in db.QualityParameters.AsNoTracking() on new { l.OrganizationId, ParamId = l.QualityParameterId } equals new { p.OrganizationId, ParamId = p.Id }
                           where l.OrganizationId == organizationId && l.QualitySpecId == id
                           orderby l.Sequence
                           select new QualitySpecLineSummary(
                               l.Id, l.Sequence, l.QualityParameterId,
                               p.Code, p.Name, p.Symbol, p.UnitSymbol, p.DataType, p.DecimalPlaces,
                               p.DefaultTestMethod, l.MinValue, l.MaxValue, l.TargetValue,
                               l.ExpectedTextSpec, l.IsMandatory, l.Notes))
            .ToListAsync(ct);

        var summary = new QualitySpecSummary(
            row.s.Id, row.s.OrganizationId, row.s.Code, row.s.Name, row.s.TargetKind,
            row.s.ItemId, row.ItemCode, row.ItemName,
            row.s.ClassificationId, row.ClassCode, row.ClassName,
            row.s.Checkpoint, row.s.Version, row.s.IsActive, lines.Count, row.s.Notes,
            Convert.ToBase64String(row.RowVersion));

        return new QualitySpecDetails(summary, lines);
    }

    private static string? FormatRangeSpec(decimal? min, decimal? max, decimal? target, string? unitSymbol)
    {
        var suffix = string.IsNullOrWhiteSpace(unitSymbol) ? string.Empty : $" {unitSymbol}";
        if (min.HasValue && max.HasValue)
            return $"{min.Value.ToString("0.######", CultureInfo.InvariantCulture)} – {max.Value.ToString("0.######", CultureInfo.InvariantCulture)}{suffix}";
        if (min.HasValue)
            return $">= {min.Value.ToString("0.######", CultureInfo.InvariantCulture)}{suffix}";
        if (max.HasValue)
            return $"<= {max.Value.ToString("0.######", CultureInfo.InvariantCulture)}{suffix}";
        if (target.HasValue)
            return $"{target.Value.ToString("0.######", CultureInfo.InvariantCulture)}{suffix}";
        return null;
    }

    private static QualityParameterSummary ToParameterSummary(QualityParameter p, byte[] rv) => new(
        p.Id, p.OrganizationId, p.Code, p.Name, p.Symbol, p.UnitSymbol,
        p.DataType, p.DecimalPlaces, p.DefaultTestMethod, p.IsActive, Convert.ToBase64String(rv));

    private async Task<Dictionary<long, string>> ResolveUserNamesAsync(IEnumerable<long?> userIds, CancellationToken ct)
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
        catch { return []; }
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
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlQualitySpecStore));
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
                catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(QualityErrors.ConcurrencyConflict); }
                catch (DbUpdateException ex) when (IsUniqueConflict(ex)) { return OrganizationResult<T>.Failure(ClassifyUniqueConflict(ex)); }
                catch (DbUpdateException ex) when (IsCheckViolation(ex)) { return OrganizationResult<T>.Failure(QualityErrors.SpecRangeInvalid); }
                catch (OrganizationAuditWriteException) { return OrganizationResult<T>.Failure(QualityErrors.AuditUnavailable); }
                catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(QualityErrors.OutcomeUnknown); }
                catch (InvalidOperationException ex) when (ex.Message == QualityErrors.ParentOrganizationInactive) { return OrganizationResult<T>.Failure(QualityErrors.ParentOrganizationInactive); }
                catch (InvalidOperationException ex) when (ex.Message == OrganizationAccessErrors.AccessDenied) { return OrganizationResult<T>.Failure(QualityErrors.AccessDenied); }
                catch (Exception ex) when (commitAttempted && Unavailable(ex)) { return OrganizationResult<T>.Failure(QualityErrors.OutcomeUnknown); }
            });
        }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlQualitySpecStore));
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
        => ex.InnerException is SqlException sql && sql.Number == 547;

    private static string ClassifyUniqueConflict(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? string.Empty;
        if (msg.Contains("UX_QualityParameter_Org_Code", StringComparison.Ordinal)) return QualityErrors.ParameterCodeDuplicate;
        if (msg.Contains("UX_QualitySpec_Org_Code_Version", StringComparison.Ordinal)) return QualityErrors.SpecCodeDuplicate;
        if (msg.Contains("UX_QualitySpec_ActiveItem", StringComparison.Ordinal) || msg.Contains("UX_QualitySpec_ActiveClass", StringComparison.Ordinal))
            return QualityErrors.SpecActiveConflict;
        return QualityErrors.ConcurrencyConflict;
    }

    private static bool Unavailable(Exception ex)
        => ex is SqlException or TimeoutException or DbUpdateException or InvalidOperationException { InnerException: SqlException };
}
