using System.Data;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Common;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Common;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlDocumentSequenceStore(
    Func<IDbContextFactory<OrganizationDbContext>> dbFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null,
    IOrganizationTimeZoneProvider? timeZoneProvider = null) : IDocumentSequenceStore
{
    private readonly IOrganizationTimeZoneProvider tzProvider = timeZoneProvider ?? new DefaultOrganizationTimeZoneProvider();

    public SqlDocumentSequenceStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public async Task<IReadOnlyList<DocumentSequenceSummary>> ListSequences(
        long organizationId,
        DateTime utcNow,
        OrganizationWriteAccess? seedAccess,
        CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory().CreateDbContextAsync(ct);
            db.CurrentOrganizationId = organizationId;
            var existing = await db.DocumentSequences.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId)
                .Select(x => new { x, RowVersion = EF.Property<byte[]>(x, "RowVersion") })
                .ToListAsync(ct);

            var allTypes = Enum.GetValues<DocumentSequenceType>();
            if (seedAccess is not null && existing.Count < allTypes.Length && await seedAccess.Revalidate(ct))
            {
                var strategy = db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    await LockOrganizationAsync(db, organizationId, ct);
                    var existingTypes = await db.DocumentSequences
                        .Where(x => x.OrganizationId == organizationId)
                        .Select(x => x.DocumentType)
                        .ToListAsync(ct);

                    var added = false;
                    foreach (var docType in allTypes)
                    {
                        if (!existingTypes.Contains(docType))
                        {
                            db.DocumentSequences.Add(DocumentSequence.CreateDefault(organizationId, docType));
                            added = true;
                        }
                    }

                    if (added)
                        await db.SaveInOperation(seedAccess.Actor, ct);
                    await tx.CommitAsync(ct);
                });

                existing = await db.DocumentSequences.AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId)
                    .Select(x => new { x, RowVersion = EF.Property<byte[]>(x, "RowVersion") })
                    .ToListAsync(ct);
            }

            var byType = existing.ToDictionary(e => e.x.DocumentType);
            var result = new List<DocumentSequenceSummary>(allTypes.Length);
            var tz = tzProvider.GetTimeZone(organizationId);
            foreach (var docType in allTypes)
            {
                if (byType.TryGetValue(docType, out var row))
                {
                    result.Add(ToSummary(row.x, row.RowVersion, utcNow, tz));
                }
                else
                {
                    var def = DocumentSequence.CreateDefault(organizationId, docType);
                    result.Add(new DocumentSequenceSummary(
                        0, organizationId, docType, def.Prefix, def.Separator, def.YearMode,
                        def.PaddingDigits, def.NextNumber, def.AllowManualOverride,
                        def.FormatPreview(utcNow, tz), string.Empty));
                }
            }

            return result;
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlDocumentSequenceStore));
            throw new DocumentSequenceStoreUnavailableException(DocumentSequenceErrors.PersistenceUnavailable, ex);
        }
    }

    public async Task<DocumentSequenceSummary> GetPreview(
        long organizationId,
        DocumentSequenceType documentType,
        DateTime utcNow,
        CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory().CreateDbContextAsync(ct);
            db.CurrentOrganizationId = organizationId;
            var row = await db.DocumentSequences.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.DocumentType == documentType)
                .Select(x => new { x, RowVersion = EF.Property<byte[]>(x, "RowVersion") })
                .SingleOrDefaultAsync(ct);

            var tz = tzProvider.GetTimeZone(organizationId);
            if (row is not null)
                return ToSummary(row.x, row.RowVersion, utcNow, tz);

            var def = DocumentSequence.CreateDefault(organizationId, documentType);
            return new DocumentSequenceSummary(
                0, organizationId, documentType, def.Prefix, def.Separator, def.YearMode,
                def.PaddingDigits, def.NextNumber, def.AllowManualOverride,
                def.FormatPreview(utcNow, tz), string.Empty);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlDocumentSequenceStore));
            throw new DocumentSequenceStoreUnavailableException(DocumentSequenceErrors.PersistenceUnavailable, ex);
        }
    }

    public async Task<OrganizationResult<DocumentSequenceSummary>> UpdateSequence(
        long organizationId,
        DocumentSequenceType documentType,
        UpdateDocumentSequenceRequest request,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct)
    {
        if (!await access.Revalidate(ct))
            return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.AccessDenied);

        try
        {
            await using var db = await dbFactory().CreateDbContextAsync(ct);
            db.CurrentOrganizationId = organizationId;
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var active = await LockOrganizationAsync(db, organizationId, ct);
                if (!active)
                    return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.OrganizationInvalid);

                var docTypeByte = (byte)documentType;
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT [Id] FROM [organization].[DocumentSequence] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [DocumentType] = {docTypeByte}", ct);

                var seq = await db.DocumentSequences
                    .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.DocumentType == documentType, ct);

                if (seq is null)
                    return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.NotFound);

                var currentVersion = seq.RowVersion;
                if (version.Length > 0 && !currentVersion.SequenceEqual(version))
                    return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.ConcurrencyConflict);

                if (seq.Update(request.Prefix, request.Separator, request.YearMode, request.PaddingDigits, request.NextNumber, request.AllowManualOverride))
                    await db.SaveInOperation(access.Actor, ct);

                await tx.CommitAsync(ct);
                return OrganizationResult<DocumentSequenceSummary>.Success(
                    ToSummary(seq, seq.RowVersion, utcNow));
            });
        }
        catch (DocumentSequenceRuleException ex)
        {
            return OrganizationResult<DocumentSequenceSummary>.Failure(ex.Code, ex.Parameters);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.ConcurrencyConflict);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlDocumentSequenceStore), access.Actor.ActorId);
            return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.PersistenceUnavailable);
        }
    }

    public async Task<string> AllocateNextNumber(
        long organizationId,
        DocumentSequenceType documentType,
        string? requestedNumber,
        DateTime utcNow,
        CancellationToken ct,
        TimeZoneInfo? timeZone = null)
    {
        try
        {
            var tz = timeZone ?? tzProvider.GetTimeZone(organizationId);
            return await AllocateAsync(dbFactory(), organizationId, documentType, requestedNumber, utcNow, ct, tz);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlDocumentSequenceStore));
            throw new DocumentSequenceStoreUnavailableException(DocumentSequenceErrors.PersistenceUnavailable, ex);
        }
    }

    public static async Task<string> AllocateAsync(
        IDbContextFactory<OrganizationDbContext> dbFactory,
        long organizationId,
        DocumentSequenceType documentType,
        string? requestedNumber,
        DateTime utcNow,
        CancellationToken ct,
        TimeZoneInfo? timeZone = null,
        int maxRetries = DocumentSequenceRetryPolicy.DefaultMaxRetries)
    {
        if (!string.IsNullOrWhiteSpace(requestedNumber))
            return requestedNumber.Trim().ToUpperInvariant();

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                db.CurrentOrganizationId = organizationId;
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

                var docTypeByte = (byte)documentType;
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT [Id] FROM [organization].[DocumentSequence] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [DocumentType] = {docTypeByte}", ct);

                var seq = await db.DocumentSequences
                    .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.DocumentType == documentType, ct);

                if (seq is null)
                {
                    await LockOrganizationAsync(db, organizationId, ct);
                    seq = await db.DocumentSequences
                        .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.DocumentType == documentType, ct);

                    if (seq is null)
                    {
                        seq = DocumentSequence.CreateDefault(organizationId, documentType);
                        db.DocumentSequences.Add(seq);
                    }
                }

                var tz = timeZone ?? OrganizationCalendarUtils.DefaultTimeZone;
                var allocated = seq.AllocateNextNumber(utcNow, tz);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return allocated;
            }
            catch (Exception ex) when (DocumentSequenceRetryPolicy.IsTransient(ex) && attempt < maxRetries - 1)
            {
                var delay = DocumentSequenceRetryPolicy.CalculateDelay(attempt);
                await Task.Delay(delay, ct);
            }
        }

        throw new InvalidOperationException("Failed to allocate document sequence number within the retry limit.");
    }

    public static async Task<string> AllocateAsync(
        OrganizationDbContext db,
        long organizationId,
        DocumentSequenceType documentType,
        string? requestedNumber,
        DateTime utcNow,
        CancellationToken ct,
        TimeZoneInfo? timeZone = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedNumber))
            return requestedNumber.Trim().ToUpperInvariant();

        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(DocumentSequenceErrors.TransactionRequired);

        DocumentSequence? seq = null;
        long previousNextNumber = 0;
        int? previousLastAllocatedYear = null;

        try
        {
            var docTypeByte = (byte)documentType;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [organization].[DocumentSequence] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [DocumentType] = {docTypeByte}", ct);

            seq = await db.DocumentSequences
                .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.DocumentType == documentType, ct);

            if (seq is null)
            {
                // Serialize first-time sequence creation on the parent Organization row so concurrent
                // transactions cannot both observe null and collide on UX_DocumentSequence_Org_Type.
                await LockOrganizationAsync(db, organizationId, ct);
                seq = await db.DocumentSequences
                    .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.DocumentType == documentType, ct);

                if (seq is null)
                {
                    seq = DocumentSequence.CreateDefault(organizationId, documentType);
                    db.DocumentSequences.Add(seq);
                }
            }

            previousNextNumber = seq.NextNumber;
            previousLastAllocatedYear = seq.LastAllocatedYear;

            var tz = timeZone ?? OrganizationCalendarUtils.DefaultTimeZone;
            return seq.AllocateNextNumber(utcNow, tz);
        }
        catch (Exception)
        {
            if (seq is not null)
            {
                seq.RevertAllocation(previousNextNumber, previousLastAllocatedYear);
                DetachEntity(db, seq);
            }
            throw;
        }
    }

    private static void DetachEntity(OrganizationDbContext db, object entity)
    {
        try
        {
            var entry = db.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }
        catch
        {
            // best-effort detach
        }
    }

    private static async Task<bool> LockOrganizationAsync(OrganizationDbContext db, long organizationId, CancellationToken ct)
    {
        return await db.Database.SqlQueryRaw<bool>(
            "SELECT [IsActive] AS [Value] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}", organizationId)
            .SingleOrDefaultAsync(ct);
    }

    private static DocumentSequenceSummary ToSummary(DocumentSequence seq, byte[] rowVersion, DateTime utcNow, TimeZoneInfo? timeZone = null) => new(
        seq.Id,
        seq.OrganizationId,
        seq.DocumentType,
        seq.Prefix,
        seq.Separator,
        seq.YearMode,
        seq.PaddingDigits,
        seq.NextNumber,
        seq.AllowManualOverride,
        seq.FormatPreview(utcNow, timeZone),
        Convert.ToBase64String(rowVersion));

    private static bool Unavailable(Exception exception) => exception switch
    {
        SqlException sql => sql.Number is -2 or 53 or 64 or 233 or 1205 or 1222 or 4060 or 18456 or 40197 or 40501 or 40613,
        TimeoutException => true,
        DbUpdateException or RetryLimitExceededException when exception.InnerException is not null => Unavailable(exception.InnerException),
        InvalidOperationException when exception.Message == OrganizationAccessErrors.PersistenceUnavailable => true,
        _ => false
    };
}
