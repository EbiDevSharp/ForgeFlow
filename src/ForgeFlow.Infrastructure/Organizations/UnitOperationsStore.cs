using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class UnitOperationsStore(Func<IDbContextFactory<OrganizationDbContext>> getFactory) : IUnitOperationsStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public UnitOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    private static UnitSummary ToSummary(OrganizationDbContext db, Unit unit) => new(
        unit.Id,
        unit.OrganizationId,
        unit.Code,
        unit.Name,
        unit.Symbol,
        unit.Dimension,
        unit.PrecisionScale,
        unit.IsActive,
        Convert.ToBase64String(db.Entry(unit).Property<byte[]>("RowVersion").CurrentValue!));

    private static UnitConversionSummary ToConversionSummary(
        OrganizationDbContext db,
        UnitConversion conversion,
        Unit fromUnit,
        Unit toUnit) => new(
        conversion.Id,
        conversion.OrganizationId,
        conversion.FromUnitId,
        fromUnit.Code,
        fromUnit.Name,
        fromUnit.Symbol,
        conversion.ToUnitId,
        toUnit.Code,
        toUnit.Name,
        toUnit.Symbol,
        conversion.Factor,
        conversion.IsActive,
        Convert.ToBase64String(db.Entry(conversion).Property<byte[]>("RowVersion").CurrentValue!));

    public Task<OrganizationPage<UnitSummary>> ListUnits(long organizationId, UnitListQuery query, CancellationToken ct) => Read(async db =>
    {
        var rows = db.Units.Where(u => u.OrganizationId == organizationId && (query.IncludeInactive || u.IsActive));
        if (query.Dimension.HasValue)
            rows = rows.Where(u => u.Dimension == query.Dimension.Value);
        if (!string.IsNullOrEmpty(query.Search))
            rows = rows.Where(u => u.Code.Contains(query.Search) || u.Name.Contains(query.Search) || u.Symbol.Contains(query.Search));

        var count = await rows.CountAsync(ct);
        var page = await rows.OrderBy(u => u.Code).ThenBy(u => u.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new OrganizationPage<UnitSummary>(page.Select(u => ToSummary(db, u)).ToArray(), count, query.Page, query.PageSize);
    }, ct);

    public Task<IReadOnlyList<UnitSummary>> ListActiveUnits(long organizationId, CancellationToken ct) => Read<IReadOnlyList<UnitSummary>>(async db =>
    {
        var units = await db.Units
            .Where(u => u.OrganizationId == organizationId && u.IsActive)
            .OrderBy(u => u.Code)
            .ThenBy(u => u.Id)
            .ToListAsync(ct);

        return units.Select(u => ToSummary(db, u)).ToArray();
    }, ct);

    public Task<UnitSummary?> GetUnit(long organizationId, long id, CancellationToken ct) => Read(async db =>
    {
        var unit = await db.Units.SingleOrDefaultAsync(u => u.OrganizationId == organizationId && u.Id == id, ct);
        return unit is null ? null : ToSummary(db, unit);
    }, ct);

    public Task<OrganizationResult<UnitSummary>> CreateUnit(CreateUnitRequest request, OrganizationWriteAccess access, CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<UnitSummary>.Failure(org.Error);

            var unit = Unit.Create(request.OrganizationId, request.Code, request.Name, request.Symbol, request.Dimension, request.PrecisionScale);
            if (await db.Units.AnyAsync(u => u.OrganizationId == request.OrganizationId && u.Code == unit.Code, ct))
                return OrganizationResult<UnitSummary>.Failure(UnitErrors.CodeDuplicate);

            db.Units.Add(unit);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<UnitSummary>.Success(ToSummary(db, unit));
        }, ct);

    public Task<OrganizationResult<UnitSummary>> UpdateUnit(
        long organizationId,
        long id,
        byte[] version,
        string? name,
        string? symbol,
        byte precisionScale,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<UnitSummary>.Failure(org.Error);

            var unit = await db.Units.SingleOrDefaultAsync(u => u.OrganizationId == organizationId && u.Id == id, ct);
            if (unit is null) return OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);
            if (!Matches(db, unit, version)) return OrganizationResult<UnitSummary>.Failure(UnitErrors.ConcurrencyConflict);

            if (unit.Update(name, symbol, precisionScale))
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<UnitSummary>.Success(ToSummary(db, unit));
        }, ct);

    public Task<OrganizationResult<UnitSummary>> SetUnitActive(
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
            if (orgRow is null) return OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);
            if (isActive && !orgRow.IsActive) return OrganizationResult<UnitSummary>.Failure(UnitErrors.ParentOrganizationInactive);

            var unit = await db.Units.SingleOrDefaultAsync(u => u.OrganizationId == organizationId && u.Id == id, ct);
            if (unit is null) return OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);
            if (!Matches(db, unit, version)) return OrganizationResult<UnitSummary>.Failure(UnitErrors.ConcurrencyConflict);

            var changed = isActive ? unit.Activate() : unit.Deactivate();
            if (changed)
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<UnitSummary>.Success(ToSummary(db, unit));
        }, ct);

    public Task<OrganizationPage<UnitConversionSummary>> ListConversions(long organizationId, UnitConversionListQuery query, CancellationToken ct) => Read(async db =>
    {
        var joined = from c in db.UnitConversions
                     join f in db.Units on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                     join t in db.Units on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                     where c.OrganizationId == organizationId && (query.IncludeInactive || c.IsActive)
                     select new { c, f, t };

        if (query.UnitId is long filterUnitId && filterUnitId > 0)
            joined = joined.Where(x => x.c.FromUnitId == filterUnitId || x.c.ToUnitId == filterUnitId);

        if (!string.IsNullOrEmpty(query.Search))
        {
            joined = joined.Where(x =>
                x.f.Code.Contains(query.Search) || x.f.Name.Contains(query.Search)
                || x.t.Code.Contains(query.Search) || x.t.Name.Contains(query.Search));
        }

        var count = await joined.CountAsync(ct);
        var page = await joined
            .OrderBy(x => x.f.Code)
            .ThenBy(x => x.t.Code)
            .ThenBy(x => x.c.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new OrganizationPage<UnitConversionSummary>(
            page.Select(x => ToConversionSummary(db, x.c, x.f, x.t)).ToArray(),
            count,
            query.Page,
            query.PageSize);
    }, ct);

    public Task<UnitConversionSummary?> GetConversion(long organizationId, long id, CancellationToken ct) => Read(async db =>
    {
        var item = await (from c in db.UnitConversions
                          join f in db.Units on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                          join t in db.Units on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                          where c.OrganizationId == organizationId && c.Id == id
                          select new { c, f, t }).SingleOrDefaultAsync(ct);

        return item is null ? null : ToConversionSummary(db, item.c, item.f, item.t);
    }, ct);

    public Task<IReadOnlyList<UnitConversionEdge>> ListActiveConversionEdges(long organizationId, CancellationToken ct) => Read<IReadOnlyList<UnitConversionEdge>>(async db =>
    {
        var edges = await (from c in db.UnitConversions.AsNoTracking()
                           join f in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                           join t in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                           where c.OrganizationId == organizationId && c.IsActive && f.IsActive && t.IsActive
                           select new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, false))
            .ToListAsync(ct);
        return edges;
    }, ct);

    public Task<OrganizationResult<UnitConversionSummary>> CreateConversion(
        CreateUnitConversionRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
            if (org.Error is not null) return OrganizationResult<UnitConversionSummary>.Failure(org.Error);

            var conversion = UnitConversion.Create(request.OrganizationId, request.FromUnitId, request.ToUnitId, request.Factor);

            var fromUnit = await db.Units.SingleOrDefaultAsync(u => u.OrganizationId == request.OrganizationId && u.Id == request.FromUnitId, ct);
            var toUnit = await db.Units.SingleOrDefaultAsync(u => u.OrganizationId == request.OrganizationId && u.Id == request.ToUnitId, ct);
            if (fromUnit is null || toUnit is null)
                return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.NotFound);
            if (!fromUnit.IsActive || !toUnit.IsActive)
                return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.UnitInactive);

            if (await db.UnitConversions.AnyAsync(c =>
                c.OrganizationId == request.OrganizationId
                && c.FromUnitId == request.FromUnitId
                && c.ToUnitId == request.ToUnitId, ct))
            {
                return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionDuplicate);
            }

            await ValidateGraphConsistencyAsync(db, request.OrganizationId, conversion, excludeConversionId: null, ct);

            db.UnitConversions.Add(conversion);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<UnitConversionSummary>.Success(ToConversionSummary(db, conversion, fromUnit, toUnit));
        }, ct);

    public Task<OrganizationResult<UnitConversionSummary>> UpdateConversionFactor(
        long organizationId,
        long id,
        byte[] version,
        decimal factor,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<UnitConversionSummary>.Failure(org.Error);

            var conversion = await db.UnitConversions.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, ct);
            if (conversion is null) return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound);
            if (!Matches(db, conversion, version)) return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConcurrencyConflict);

            var fromUnit = await db.Units.SingleAsync(u => u.OrganizationId == organizationId && u.Id == conversion.FromUnitId, ct);
            var toUnit = await db.Units.SingleAsync(u => u.OrganizationId == organizationId && u.Id == conversion.ToUnitId, ct);

            if (conversion.UpdateFactor(factor))
            {
                await ValidateGraphConsistencyAsync(db, organizationId, conversion, conversion.Id, ct);
                await db.SaveInOperation(access.Actor, ct);
            }

            return OrganizationResult<UnitConversionSummary>.Success(ToConversionSummary(db, conversion, fromUnit, toUnit));
        }, ct);

    public Task<OrganizationResult<UnitConversionSummary>> SetConversionActive(
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
            if (orgRow is null) return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.NotFound);
            if (isActive && !orgRow.IsActive) return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ParentOrganizationInactive);

            var conversion = await db.UnitConversions.SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, ct);
            if (conversion is null) return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound);
            if (!Matches(db, conversion, version)) return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConcurrencyConflict);

            var fromUnit = await db.Units.SingleAsync(u => u.OrganizationId == organizationId && u.Id == conversion.FromUnitId, ct);
            var toUnit = await db.Units.SingleAsync(u => u.OrganizationId == organizationId && u.Id == conversion.ToUnitId, ct);
            if (isActive && (!fromUnit.IsActive || !toUnit.IsActive))
                return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.UnitInactive);

            var changed = isActive ? conversion.Activate() : conversion.Deactivate();
            if (changed)
            {
                if (isActive)
                    await ValidateGraphConsistencyAsync(db, organizationId, conversion, conversion.Id, ct);
                await db.SaveInOperation(access.Actor, ct);
            }

            return OrganizationResult<UnitConversionSummary>.Success(ToConversionSummary(db, conversion, fromUnit, toUnit));
        }, ct);

    public Task<OrganizationResult<SeedStandardUnitsResult>> SeedStandardUnits(
        long organizationId,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => Write(access, async db =>
        {
            var org = await LockAndRequireActiveOrganization(db, organizationId, ct);
            if (org.Error is not null) return OrganizationResult<SeedStandardUnitsResult>.Failure(org.Error);

            var existingUnits = await db.Units
                .Where(u => u.OrganizationId == organizationId)
                .ToDictionaryAsync(u => u.Code, StringComparer.Ordinal, ct);

            var specs = new (string Code, string Name, string Symbol, UnitDimension Dimension, byte Precision)[]
            {
                ("KG", "Kilogram", "kg", UnitDimension.Mass, 3),
                ("G", "Gram", "g", UnitDimension.Mass, 3),
                ("TON", "Metric Ton", "t", UnitDimension.Mass, 6),
                ("L", "Liter", "L", UnitDimension.Volume, 3),
                ("ML", "Milliliter", "mL", UnitDimension.Volume, 3),
                ("M", "Meter", "m", UnitDimension.Length, 3),
                ("CM", "Centimeter", "cm", UnitDimension.Length, 2),
                ("MM", "Millimeter", "mm", UnitDimension.Length, 2),
                ("M2", "Square Meter", "m²", UnitDimension.Area, 3),
                ("PCS", "Piece", "pcs", UnitDimension.Count, 0),
                ("BOX", "Box", "box", UnitDimension.Count, 0),
                ("HR", "Hour", "h", UnitDimension.Time, 2),
                ("MIN", "Minute", "min", UnitDimension.Time, 2),
                ("SEC", "Second", "s", UnitDimension.Time, 2)
            };

            var unitsCreated = 0;
            foreach (var spec in specs)
            {
                if (existingUnits.ContainsKey(spec.Code))
                    continue;

                var unit = Unit.Create(organizationId, spec.Code, spec.Name, spec.Symbol, spec.Dimension, spec.Precision);
                db.Units.Add(unit);
                existingUnits[spec.Code] = unit;
                unitsCreated++;
            }

            if (unitsCreated > 0)
                await db.SaveInOperation(access.Actor, ct);

            var existingPairs = await db.UnitConversions
                .Where(c => c.OrganizationId == organizationId)
                .Select(c => new { c.FromUnitId, c.ToUnitId })
                .ToListAsync(ct);
            var pairSet = existingPairs.Select(x => (x.FromUnitId, x.ToUnitId)).ToHashSet();

            var conversionSpecs = new (string FromCode, string ToCode, decimal Factor)[]
            {
                ("TON", "KG", 1000m),
                ("KG", "G", 1000m),
                ("L", "ML", 1000m),
                ("M", "CM", 100m),
                ("CM", "MM", 10m),
                ("HR", "MIN", 60m),
                ("MIN", "SEC", 60m)
            };

            var conversionsCreated = 0;
            foreach (var convSpec in conversionSpecs)
            {
                if (!existingUnits.TryGetValue(convSpec.FromCode, out var fromUnit) || !fromUnit.IsActive) continue;
                if (!existingUnits.TryGetValue(convSpec.ToCode, out var toUnit) || !toUnit.IsActive) continue;
                if (pairSet.Contains((fromUnit.Id, toUnit.Id))) continue;

                var conversion = UnitConversion.Create(organizationId, fromUnit.Id, toUnit.Id, convSpec.Factor);
                db.UnitConversions.Add(conversion);
                pairSet.Add((fromUnit.Id, toUnit.Id));
                conversionsCreated++;
            }

            if (conversionsCreated > 0)
                await db.SaveInOperation(access.Actor, ct);

            return OrganizationResult<SeedStandardUnitsResult>.Success(new(unitsCreated, conversionsCreated));
        }, ct);

    private static async Task ValidateGraphConsistencyAsync(
        OrganizationDbContext db,
        long organizationId,
        UnitConversion candidate,
        long? excludeConversionId,
        CancellationToken ct)
    {
        var activeEdges = await (from c in db.UnitConversions.AsNoTracking()
                                 join f in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.FromUnitId } equals new { f.OrganizationId, UnitId = f.Id }
                                 join t in db.Units.AsNoTracking() on new { c.OrganizationId, UnitId = c.ToUnitId } equals new { t.OrganizationId, UnitId = t.Id }
                                 where c.OrganizationId == organizationId
                                       && c.IsActive
                                       && f.IsActive
                                       && t.IsActive
                                       && (!excludeConversionId.HasValue || c.Id != excludeConversionId.Value)
                                 select new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, false))
            .ToListAsync(ct);

        activeEdges.Add(new UnitConversionEdge(candidate.FromUnitId, candidate.ToUnitId, candidate.Factor, false));
        _ = UnitConversionGraph.ResolveFactor(candidate.FromUnitId, candidate.ToUnitId, activeEdges);
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
            throw new UnitStoreUnavailableException(ex);
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
                    return OrganizationResult<T>.Failure(UnitErrors.AccessDenied);

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
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(UnitErrors.ConcurrencyConflict); }
        catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(UnitErrors.OutcomeUnknown); }
        catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
        { return OrganizationResult<T>.Failure(UnitErrors.AuditUnavailable); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627
            && sql.Message.Contains("'UX_Unit_Organization_Code'", StringComparison.Ordinal))
        { return OrganizationResult<T>.Failure(UnitErrors.CodeDuplicate); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627
            && sql.Message.Contains("'UX_UnitConversion_Organization_From_To'", StringComparison.Ordinal))
        { return OrganizationResult<T>.Failure(UnitErrors.ConversionDuplicate); }
        catch (InvalidOperationException ex) when (ex.Message == UnitErrors.ParentOrganizationInactive)
        { return OrganizationResult<T>.Failure(UnitErrors.ParentOrganizationInactive); }
        catch (InvalidOperationException ex) when (ex.Message == OrganizationSiteErrors.OrganizationNotFound)
        { return OrganizationResult<T>.Failure(UnitErrors.NotFound); }
        catch (Exception ex) when (Unavailable(ex))
        { return OrganizationResult<T>.Failure(UnitErrors.PersistenceUnavailable); }
    }

    private static async Task<(bool Ok, string? Error)> LockAndRequireActiveOrganization(OrganizationDbContext db, long organizationId, CancellationToken ct)
    {
        await LockOrganization(db, organizationId, ct);
        var org = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return (false, UnitErrors.NotFound);
        if (!org.IsActive) return (false, UnitErrors.ParentOrganizationInactive);
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
