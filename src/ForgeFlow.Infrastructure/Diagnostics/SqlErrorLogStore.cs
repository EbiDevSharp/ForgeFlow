using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ForgeFlow.Infrastructure.Diagnostics;

public sealed class DiagnosticsStorageOptions
{
    public string FallbackLogDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "App_Data", "logs");
}

public sealed class SqlErrorLogStore(
    IDbContextFactory<DiagnosticsDbContext> factory,
    IServiceScopeFactory scopes,
    ILogger<SqlErrorLogStore> logger,
    DiagnosticsStorageOptions? storageOptions = null,
    TimeProvider? clock = null)
    : IErrorLogRecorder, IErrorLogStore, IUserOperationRecorder
{
    private static readonly object FallbackLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private string FallbackDirectory =>
        storageOptions?.FallbackLogDirectory ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "logs");

    public async Task<RecordedErrorResult> RecordExceptionAsync(
        Exception exception,
        ErrorLogRecordContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var correlationId = context.CorrelationId ?? Guid.NewGuid();
        var errorRef = BuildErrorReference(now);
        var redacted = DiagnosticRedactor.Redact(exception);
        var cleanPath = DiagnosticRedactor.RedactRequestPath(context.RequestPath);
        var cleanAgent = string.IsNullOrWhiteSpace(context.UserAgent)
            ? null
            : DiagnosticRedactor.ScrubText(context.UserAgent.Trim());

        var record = new ErrorLogRecord(
            errorRef,
            correlationId,
            now,
            context.Severity,
            context.Source,
            redacted.ExceptionType,
            redacted.SqlErrorNumber,
            redacted.RedactedMessage,
            redacted.RedactedStackTrace,
            cleanPath,
            context.ComponentName,
            context.ActorUserId,
            context.OrganizationId,
            context.SiteId,
            cleanAgent);

        logger.LogError(
            "ForgeFlowError ref={ErrorReference} correlation={CorrelationId} source={Source} severity={Severity} type={ExceptionType} sql={SqlNumber} actor={ActorId} org={OrgId} site={SiteId} msg={Message}",
            errorRef, correlationId, context.Source, context.Severity, redacted.ExceptionType, redacted.SqlErrorNumber,
            context.ActorUserId, context.OrganizationId, context.SiteId, redacted.RedactedMessage);

        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.ErrorLogs.Add(record);
            await db.SaveChangesAsync(ct);
            return new RecordedErrorResult(errorRef, correlationId, SavedToDatabase: true);
        }
        catch (Exception dbEx)
        {
            WriteFallbackJsonLine(record, DiagnosticRedactor.Redact(dbEx));
            return new RecordedErrorResult(errorRef, correlationId, SavedToDatabase: false);
        }
    }

    public async Task RecordAsync(UserOperationRecordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var correlationId = request.CorrelationId ?? Guid.NewGuid();
        var cleanMeta = string.IsNullOrWhiteSpace(request.MetadataJson)
            ? "{}"
            : DiagnosticRedactor.ScrubJson(request.MetadataJson);

        var record = new UserOperationLogRecord(
            now,
            request.OperationCode,
            request.Outcome,
            correlationId,
            request.ActorUserId,
            request.OrganizationId,
            request.SiteId,
            request.TargetEntityType,
            request.TargetEntityRef,
            request.FailureCode,
            cleanMeta);

        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.UserOperationLogs.Add(record);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            var red = DiagnosticRedactor.Redact(ex);
            logger.LogWarning(
                "UserOperationLog persistence failed code={Code} correlation={CorrelationId} error={Error}",
                request.OperationCode, correlationId, red.RedactedMessage);
        }
    }

    public async Task<OrganizationPage<ErrorLogSummary>> ListAsync(OrganizationScope scope, ErrorLogListQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var q = ApplyScopeFilter(db.ErrorLogs.AsNoTracking(), scope);

            if (query.Severity is ErrorSeverity sev)
                q = q.Where(e => e.Severity == sev);
            if (query.Source is ErrorSourceKind src)
                q = q.Where(e => e.Source == src);
            if (query.FromDate is DateOnly df)
            {
                var fromUtc = DiagnosticDateBounds.LocalDayStartToUtc(df);
                q = q.Where(e => e.OccurredAtUtc >= fromUtc);
            }
            if (query.ToDate is DateOnly dt)
            {
                var toExclusive = DiagnosticDateBounds.LocalDayEndExclusiveToUtc(dt);
                q = q.Where(e => e.OccurredAtUtc < toExclusive);
            }
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                if (Guid.TryParse(term, out var corr))
                {
                    q = q.Where(e => e.CorrelationId == corr
                                     || e.ErrorReference.Contains(term)
                                     || e.RedactedMessage.Contains(term));
                }
                else
                {
                    q = q.Where(e => e.ErrorReference.Contains(term)
                                     || e.ExceptionType.Contains(term)
                                     || e.RedactedMessage.Contains(term)
                                     || (e.RequestPath != null && e.RequestPath.Contains(term))
                                     || (e.ComponentName != null && e.ComponentName.Contains(term)));
                }
            }

            var total = await q.CountAsync(ct);
            if (total == 0)
                return new OrganizationPage<ErrorLogSummary>([], 0, query.Page, query.PageSize);

            var rows = await q
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenByDescending(e => e.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct);

            var actorNames = await ResolveUserNamesAsync(rows.Select(r => r.ActorUserId), ct);

            var items = rows.Select(e => new ErrorLogSummary(
                e.Id,
                e.ErrorReference,
                e.CorrelationId,
                e.OccurredAtUtc,
                e.Severity,
                e.Source,
                e.ExceptionType,
                e.SqlErrorNumber,
                e.RedactedMessage,
                e.RequestPath,
                e.ComponentName,
                e.ActorUserId,
                e.ActorUserId.HasValue && actorNames.TryGetValue(e.ActorUserId.Value, out var name) ? name : null,
                e.OrganizationId,
                e.SiteId)).ToArray();

            return new OrganizationPage<ErrorLogSummary>(items, total, query.Page, query.PageSize);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            throw new DiagnosticStoreUnavailableException(ex);
        }
    }

    public async Task<ErrorLogDetails?> GetAsync(OrganizationScope scope, long id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var e = await ApplyScopeFilter(db.ErrorLogs.AsNoTracking(), scope)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return null;

            var names = await ResolveUserNamesAsync([e.ActorUserId], ct);
            var summary = new ErrorLogSummary(
                e.Id,
                e.ErrorReference,
                e.CorrelationId,
                e.OccurredAtUtc,
                e.Severity,
                e.Source,
                e.ExceptionType,
                e.SqlErrorNumber,
                e.RedactedMessage,
                e.RequestPath,
                e.ComponentName,
                e.ActorUserId,
                e.ActorUserId.HasValue && names.TryGetValue(e.ActorUserId.Value, out var n) ? n : null,
                e.OrganizationId,
                e.SiteId);

            return new ErrorLogDetails(summary, e.RedactedStackTrace, e.UserAgent);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            throw new DiagnosticStoreUnavailableException(ex);
        }
    }

    public async Task<ErrorLogStats> GetStatsAsync(OrganizationScope scope, DateTime utcNow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var since = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).AddHours(-24);
            var recent = await ApplyScopeFilter(db.ErrorLogs.AsNoTracking(), scope)
                .Where(e => e.OccurredAtUtc >= since)
                .Select(e => new { e.Severity, e.SqlErrorNumber, e.Source })
                .ToListAsync(ct);

            return new ErrorLogStats(
                recent.Count,
                recent.Count(x => x.Severity == ErrorSeverity.Critical),
                recent.Count(x => x.SqlErrorNumber.HasValue),
                recent.Count(x => x.Source is ErrorSourceKind.HttpMiddleware or ErrorSourceKind.BlazorCircuit));
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            throw new DiagnosticStoreUnavailableException(ex);
        }
    }

    public async Task<int> PurgeBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.ErrorLogs
                .Where(e => e.OccurredAtUtc < DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Utc))
                .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            throw new DiagnosticStoreUnavailableException(ex);
        }
    }

    private async Task<Dictionary<long, string>> ResolveUserNamesAsync(IEnumerable<long?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(x => x is > 0).Select(x => x!.Value).Distinct().ToArray();
        if (ids.Length == 0)
            return [];

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var authDb = scope.ServiceProvider.GetService<AuthDbContext>();
            if (authDb is null) return [];

            return await authDb.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.UserName ?? string.Empty })
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        }
        catch
        {
            return [];
        }
    }

    private void WriteFallbackJsonLine(ErrorLogRecord record, RedactedExceptionReport dbFailure)
    {
        try
        {
            Directory.CreateDirectory(FallbackDirectory);
            var fileName = $"errors-{record.OccurredAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.jsonl";
            var fullPath = Path.Combine(FallbackDirectory, fileName);
            var payload = JsonSerializer.Serialize(new
            {
                record.ErrorReference,
                record.CorrelationId,
                record.OccurredAtUtc,
                Severity = record.Severity.ToString(),
                Source = record.Source.ToString(),
                record.ExceptionType,
                record.SqlErrorNumber,
                record.RedactedMessage,
                record.RedactedStackTrace,
                record.RequestPath,
                record.ComponentName,
                record.ActorUserId,
                record.OrganizationId,
                record.SiteId,
                DbFallbackReason = dbFailure.RedactedMessage
            }, JsonOptions);

            lock (FallbackLock)
            {
                File.AppendAllText(fullPath, payload + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Never throw from fallback diagnostic writer.
        }
    }

    private static string BuildErrorReference(DateTime utcNow)
    {
        Span<byte> bytes = stackalloc byte[3];
        RandomNumberGenerator.Fill(bytes);
        var hex = Convert.ToHexString(bytes);
        return $"FF-{utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{hex}";
    }

    private static IQueryable<ErrorLogRecord> ApplyScopeFilter(IQueryable<ErrorLogRecord> q, OrganizationScope scope) => scope switch
    {
        OrganizationScope.Platform => q,
        OrganizationScope.Organization org => q.Where(e => e.OrganizationId == org.OrganizationId),
        _ => q.Where(_ => false)
    };

    private static bool IsUnavailable(Exception ex) =>
        ex is DbException or SqlException or InvalidOperationException or TimeoutException;
}
