using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlDocumentSequenceStore(
    Func<IDbContextFactory<OrganizationDbContext>> dbFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null) : IDocumentSequenceStore
{
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
            foreach (var docType in allTypes)
            {
                if (byType.TryGetValue(docType, out var row))
                {
                    result.Add(ToSummary(row.x, row.RowVersion, utcNow));
                }
                else
                {
                    var def = DocumentSequence.CreateDefault(organizationId, docType);
                    result.Add(new DocumentSequenceSummary(
                        0, organizationId, docType, def.Prefix, def.Separator, def.YearMode,
                        def.PaddingDigits, def.NextNumber, def.AllowManualOverride,
                        def.FormatPreview(utcNow), string.Empty));
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
            var row = await db.DocumentSequences.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.DocumentType == documentType)
                .Select(x => new { x, RowVersion = EF.Property<byte[]>(x, "RowVersion") })
                .SingleOrDefaultAsync(ct);

            if (row is not null)
                return ToSummary(row.x, row.RowVersion, utcNow);

            var def = DocumentSequence.CreateDefault(organizationId, documentType);
            return new DocumentSequenceSummary(
                0, organizationId, documentType, def.Prefix, def.Separator, def.YearMode,
                def.PaddingDigits, def.NextNumber, def.AllowManualOverride,
                def.FormatPreview(utcNow), string.Empty);
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

                var currentVersion = (byte[])db.Entry(seq).Property("RowVersion").CurrentValue!;
                if (version.Length > 0 && !currentVersion.SequenceEqual(version))
                    return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.ConcurrencyConflict);

                if (seq.Update(request.Prefix, request.Separator, request.YearMode, request.PaddingDigits, request.NextNumber, request.AllowManualOverride))
                    await db.SaveInOperation(access.Actor, ct);

                await tx.CommitAsync(ct);
                return OrganizationResult<DocumentSequenceSummary>.Success(
                    ToSummary(seq, (byte[])db.Entry(seq).Property("RowVersion").CurrentValue!, utcNow));
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

    internal static async Task<string> AllocateAsync(
        OrganizationDbContext db,
        long organizationId,
        DocumentSequenceType documentType,
        string? requestedNumber,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requestedNumber))
            return requestedNumber.Trim().ToUpperInvariant();

        var docTypeByte = (byte)documentType;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[DocumentSequence] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [DocumentType] = {docTypeByte}", ct);

        var seq = await db.DocumentSequences
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.DocumentType == documentType, ct);

        if (seq is null)
        {
            seq = DocumentSequence.CreateDefault(organizationId, documentType);
            db.DocumentSequences.Add(seq);
        }

        return seq.AllocateNextNumber(utcNow);
    }

    private static async Task<bool> LockOrganizationAsync(OrganizationDbContext db, long organizationId, CancellationToken ct)
    {
        return await db.Database.SqlQueryRaw<bool>(
            "SELECT [IsActive] AS [Value] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}", organizationId)
            .SingleOrDefaultAsync(ct);
    }

    private static DocumentSequenceSummary ToSummary(DocumentSequence seq, byte[] rowVersion, DateTime utcNow) => new(
        seq.Id,
        seq.OrganizationId,
        seq.DocumentType,
        seq.Prefix,
        seq.Separator,
        seq.YearMode,
        seq.PaddingDigits,
        seq.NextNumber,
        seq.AllowManualOverride,
        seq.FormatPreview(utcNow),
        Convert.ToBase64String(rowVersion));

    private static bool Unavailable(Exception exception) => exception switch
    {
        SqlException sql => sql.Number is -2 or 53 or 64 or 233 or 1205 or 4060 or 18456 or 40197 or 40501 or 40613,
        TimeoutException => true,
        DbUpdateException or RetryLimitExceededException when exception.InnerException is not null => Unavailable(exception.InnerException),
        InvalidOperationException when exception.Message == OrganizationAccessErrors.PersistenceUnavailable => true,
        _ => false
    };
}
