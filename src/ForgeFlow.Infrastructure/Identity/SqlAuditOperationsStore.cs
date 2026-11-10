using System.Data.Common;
using System.Text.Encodings.Web;
using System.Text.Json;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Diagnostics;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class SqlAuditOperationsStore(
    IServiceScopeFactory scopes,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null) : IAuditOperationsStore
{
    private static readonly JsonSerializerOptions SafeJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<OrganizationPage<AuditEntrySummary>> ListAuditEntries(
        OrganizationScope scope,
        AuditListQuery query,
        CancellationToken ct = default)
    {
        try
        {
            await using var serviceScope = scopes.CreateAsyncScope();
            var authDb = serviceScope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var orgDbFactory = serviceScope.ServiceProvider.GetService<IDbContextFactory<OrganizationDbContext>>();
            await using var orgDb = orgDbFactory is not null
                ? await orgDbFactory.CreateDbContextAsync(ct)
                : serviceScope.ServiceProvider.GetService<OrganizationDbContext>();
            var diagDbFactory = serviceScope.ServiceProvider.GetService<IDbContextFactory<DiagnosticsDbContext>>();
            await using var diagDb = diagDbFactory is not null
                ? await diagDbFactory.CreateDbContextAsync(ct)
                : serviceScope.ServiceProvider.GetService<DiagnosticsDbContext>();

            var includeBusiness = query.Category is AuditCategory.All or AuditCategory.Business
                                  && scope is not OrganizationScope.Platform
                                  && query.OutcomeFilter != AuditOutcomeFilter.RejectedOrFailed
                                  && orgDb is not null;
            var includeSecurity = query.Category is AuditCategory.All or AuditCategory.Security;
            var includeOperations = query.Category is AuditCategory.All or AuditCategory.Operations
                                    && diagDb is not null;

            var skip = (query.Page - 1) * query.PageSize;
            var take = query.PageSize;
            var window = checked(skip + take);

            var businessCount = 0;
            List<RawAuditRow> businessRows = [];
            if (includeBusiness && orgDb is not null)
            {
                var bQuery = BuildBusinessQuery(orgDb, scope, query);
                businessCount = await bQuery.CountAsync(ct);
                if (businessCount > 0)
                {
                    businessRows = await bQuery
                        .OrderByDescending(c => c.OccurredAtUtc)
                        .ThenByDescending(c => c.Id)
                        .Take(window)
                        .Select(c => new RawAuditRow(
                            AuditSourceKind.Business,
                            c.Id,
                            c.OccurredAtUtc,
                            c.EventCode,
                            c.OrganizationId,
                            c.SiteId,
                            c.ActorId,
                            null,
                            null,
                            null,
                            c.CorrelationId,
                            c.ChangesJson))
                        .ToListAsync(ct);
                }
            }

            var securityCount = 0;
            List<RawAuditRow> securityRows = [];
            if (includeSecurity)
            {
                long? fallbackOrgId = scope is OrganizationScope.Organization scopedOrg ? scopedOrg.OrganizationId : null;
                var sQuery = BuildSecurityQuery(authDb, scope, query);
                securityCount = await sQuery.CountAsync(ct);
                if (securityCount > 0)
                {
                    securityRows = await sQuery
                        .OrderByDescending(s => s.OccurredAtUtc)
                        .ThenByDescending(s => s.Id)
                        .Take(window)
                        .Select(s => new RawAuditRow(
                            AuditSourceKind.Security,
                            s.Id,
                            s.OccurredAtUtc,
                            s.EventCode,
                            s.OrganizationId ?? fallbackOrgId,
                            s.SiteId,
                            s.ActorUserId,
                            s.HostOperatorReference,
                            s.SubjectUserId,
                            s.Outcome,
                            s.CorrelationId,
                            s.DataJson))
                        .ToListAsync(ct);
                }
            }

            var operationCount = 0;
            List<RawAuditRow> operationRows = [];
            if (includeOperations && diagDb is not null)
            {
                try
                {
                    var opQuery = BuildOperationsQuery(diagDb, scope, query);
                    operationCount = await opQuery.CountAsync(ct);
                    if (operationCount > 0)
                    {
                        var rawOps = await opQuery
                            .OrderByDescending(o => o.OccurredAtUtc)
                            .ThenByDescending(o => o.Id)
                            .Take(window)
                            .ToListAsync(ct);
                        operationRows = rawOps.Select(ToOperationRow).ToList();
                    }
                }
                catch (SqlException)
                {
                    // Legacy test DBs without diag/ops schema still return business + security audit rows cleanly.
                    operationCount = 0;
                    operationRows = [];
                }
            }

            var total = businessCount + securityCount + operationCount;
            if (total == 0)
                return new OrganizationPage<AuditEntrySummary>([], 0, query.Page, query.PageSize);

            var pageRaw = businessRows
                .Concat(securityRows)
                .Concat(operationRows)
                .OrderByDescending(r => r.OccurredAtUtc)
                .ThenBy(r => r.Source)
                .ThenByDescending(r => r.Id)
                .Skip(skip)
                .Take(take)
                .ToArray();

            var items = await EnrichUserNamesAsync(authDb, pageRaw, ct);
            return new OrganizationPage<AuditEntrySummary>(items, total, query.Page, query.PageSize);
        }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlAuditOperationsStore));
            throw new AuditStoreUnavailableException(ex);
        }
    }

    public async Task<AuditEntryDetails?> GetAuditEntry(
        OrganizationScope scope,
        AuditSourceKind source,
        long entryId,
        CancellationToken ct = default)
    {
        try
        {
            await using var serviceScope = scopes.CreateAsyncScope();
            var authDb = serviceScope.ServiceProvider.GetRequiredService<AuthDbContext>();

            RawAuditRow? row = null;
            if (source == AuditSourceKind.Business)
            {
                if (scope is OrganizationScope.Platform)
                    return null;

                var orgDbFactory = serviceScope.ServiceProvider.GetService<IDbContextFactory<OrganizationDbContext>>();
                await using var orgDb = orgDbFactory is not null
                    ? await orgDbFactory.CreateDbContextAsync(ct)
                    : serviceScope.ServiceProvider.GetService<OrganizationDbContext>();
                if (orgDb is null)
                    return null;

                var query = ApplyBusinessScope(orgDb.OrganizationChanges.AsNoTracking(), scope)
                    .Where(c => c.Id == entryId);

                row = await query
                    .Select(c => new RawAuditRow(
                        AuditSourceKind.Business,
                        c.Id,
                        c.OccurredAtUtc,
                        c.EventCode,
                        c.OrganizationId,
                        c.SiteId,
                        c.ActorId,
                        null,
                        null,
                        null,
                        c.CorrelationId,
                        c.ChangesJson))
                    .SingleOrDefaultAsync(ct);
            }
            else if (source == AuditSourceKind.Security)
            {
                long? fallbackOrgId = scope is OrganizationScope.Organization scopedOrg ? scopedOrg.OrganizationId : null;
                var query = ApplySecurityScope(authDb, authDb.SecurityEvents.AsNoTracking(), scope)
                    .Where(s => s.Id == entryId);

                row = await query
                    .Select(s => new RawAuditRow(
                        AuditSourceKind.Security,
                        s.Id,
                        s.OccurredAtUtc,
                        s.EventCode,
                        s.OrganizationId ?? fallbackOrgId,
                        s.SiteId,
                        s.ActorUserId,
                        s.HostOperatorReference,
                        s.SubjectUserId,
                        s.Outcome,
                        s.CorrelationId,
                        s.DataJson))
                    .SingleOrDefaultAsync(ct);
            }
            else if (source == AuditSourceKind.Operations)
            {
                var diagDbFactory = serviceScope.ServiceProvider.GetService<IDbContextFactory<DiagnosticsDbContext>>();
                await using var diagDb = diagDbFactory is not null
                    ? await diagDbFactory.CreateDbContextAsync(ct)
                    : serviceScope.ServiceProvider.GetService<DiagnosticsDbContext>();
                if (diagDb is null)
                    return null;

                var op = await ApplyOperationsScope(diagDb.UserOperationLogs.AsNoTracking(), scope)
                    .SingleOrDefaultAsync(o => o.Id == entryId, ct);
                if (op is not null)
                    row = ToOperationRow(op);
            }

            if (row is null)
                return null;

            var enriched = await EnrichUserNamesAsync(authDb, [row], ct);
            var summary = enriched[0];
            var parsedFields = AuditPayloadParser.ParseFields(summary.RawJson);
            return new AuditEntryDetails(summary, parsedFields);
        }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlAuditOperationsStore));
            throw new AuditStoreUnavailableException(ex);
        }
    }

    public async Task<IReadOnlyList<AuditActorOption>> ListActors(
        OrganizationScope scope,
        CancellationToken ct = default)
    {
        try
        {
            await using var serviceScope = scopes.CreateAsyncScope();
            var authDb = serviceScope.ServiceProvider.GetRequiredService<AuthDbContext>();

            IQueryable<ApplicationAccount> usersQuery = scope switch
            {
                OrganizationScope.Organization org =>
                    from m in authDb.OrganizationMemberships.AsNoTracking()
                    join u in authDb.Users.AsNoTracking() on m.UserId equals u.Id
                    where m.OrganizationId == org.OrganizationId
                    select u,
                OrganizationScope.Site site =>
                    from m in authDb.SiteMemberships.AsNoTracking()
                    join u in authDb.Users.AsNoTracking() on m.UserId equals u.Id
                    where m.OrganizationId == site.OrganizationId && m.SiteId == site.SiteId
                    select u,
                OrganizationScope.Platform =>
                    from r in authDb.ScopedRoleAssignments.AsNoTracking()
                    join u in authDb.Users.AsNoTracking() on r.UserId equals u.Id
                    where r.ScopeKind == 0
                    select u,
                _ => Enumerable.Empty<ApplicationAccount>().AsQueryable()
            };

            return await usersQuery
                .Where(u => u.UserName != null)
                .Select(u => new AuditActorOption(u.Id, u.UserName!))
                .Distinct()
                .OrderBy(u => u.UserName)
                .ToListAsync(ct);
        }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlAuditOperationsStore));
            throw new AuditStoreUnavailableException(ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListDistinctEventCodes(
        OrganizationScope scope,
        AuditCategory category,
        CancellationToken ct = default)
    {
        try
        {
            await using var serviceScope = scopes.CreateAsyncScope();
            var authDb = serviceScope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var codes = new HashSet<string>(StringComparer.Ordinal);

            if (category is AuditCategory.All or AuditCategory.Business && scope is not OrganizationScope.Platform)
            {
                var orgDbFactory = serviceScope.ServiceProvider.GetService<IDbContextFactory<OrganizationDbContext>>();
                await using var orgDb = orgDbFactory is not null
                    ? await orgDbFactory.CreateDbContextAsync(ct)
                    : serviceScope.ServiceProvider.GetService<OrganizationDbContext>();
                if (orgDb is not null)
                {
                    var biz = await ApplyBusinessScope(orgDb.OrganizationChanges.AsNoTracking(), scope)
                        .Select(c => c.EventCode)
                        .Distinct()
                        .ToListAsync(ct);
                    codes.UnionWith(biz);
                }
            }

            if (category is AuditCategory.All or AuditCategory.Security)
            {
                var sec = await ApplySecurityScope(authDb, authDb.SecurityEvents.AsNoTracking(), scope)
                    .Select(s => s.EventCode)
                    .Distinct()
                    .ToListAsync(ct);
                codes.UnionWith(sec);
            }

            if (category is AuditCategory.All or AuditCategory.Operations)
            {
                var diagDbFactory = serviceScope.ServiceProvider.GetService<IDbContextFactory<DiagnosticsDbContext>>();
                await using var diagDb = diagDbFactory is not null
                    ? await diagDbFactory.CreateDbContextAsync(ct)
                    : serviceScope.ServiceProvider.GetService<DiagnosticsDbContext>();
                if (diagDb is not null)
                {
                    try
                    {
                        var ops = await ApplyOperationsScope(diagDb.UserOperationLogs.AsNoTracking(), scope)
                            .Select(o => o.OperationCode)
                            .Distinct()
                            .ToListAsync(ct);
                        codes.UnionWith(ops);
                    }
                    catch (SqlException) { /* gracefully ignore if diag/ops schema not yet upgraded */ }
                }
            }

            return codes.OrderBy(c => c, StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlAuditOperationsStore));
            throw new AuditStoreUnavailableException(ex);
        }
    }

    private static RawAuditRow ToOperationRow(UserOperationLogRecord op)
    {
        byte mappedOutcome = op.Outcome switch
        {
            UserOperationOutcome.Succeeded => 0,
            UserOperationOutcome.AccessDenied => 1,
            _ => 2
        };

        var json = JsonSerializer.Serialize(new
        {
            Outcome = op.Outcome.ToString(),
            op.TargetEntityType,
            op.TargetEntityRef,
            op.FailureCode,
            Metadata = op.MetadataJson
        }, SafeJson);

        return new RawAuditRow(
            AuditSourceKind.Operations,
            op.Id,
            op.OccurredAtUtc,
            op.OperationCode,
            op.OrganizationId,
            op.SiteId,
            op.ActorUserId,
            null,
            null,
            mappedOutcome,
            op.CorrelationId,
            json);
    }

    private static IQueryable<OrganizationChange> BuildBusinessQuery(
        OrganizationDbContext orgDb,
        OrganizationScope scope,
        AuditListQuery query)
    {
        var rows = ApplyBusinessScope(orgDb.OrganizationChanges.AsNoTracking(), scope);

        if (query.ActorUserId is long actorId)
            rows = rows.Where(c => c.ActorId == actorId);
        if (query.SiteId is long siteId)
            rows = rows.Where(c => c.SiteId == siteId);
        if (query.FromDate is DateOnly df)
        {
            var fromUtc = DiagnosticDateBounds.LocalDayStartToUtc(df);
            rows = rows.Where(c => c.OccurredAtUtc >= fromUtc);
        }
        if (query.ToDate is DateOnly dt)
        {
            var toExclusive = DiagnosticDateBounds.LocalDayEndExclusiveToUtc(dt);
            rows = rows.Where(c => c.OccurredAtUtc < toExclusive);
        }
        if (!string.IsNullOrEmpty(query.EventCode))
            rows = rows.Where(c => c.EventCode == query.EventCode);

        if (!string.IsNullOrEmpty(query.Search))
        {
            var term = query.Search;
            if (Guid.TryParse(term, out var corrId))
                rows = rows.Where(c => c.CorrelationId == corrId || c.EventCode.Contains(term) || c.ChangesJson.Contains(term));
            else
                rows = rows.Where(c => c.EventCode.Contains(term) || c.ChangesJson.Contains(term));
        }

        return rows;
    }

    private static IQueryable<SecurityEvent> BuildSecurityQuery(
        AuthDbContext authDb,
        OrganizationScope scope,
        AuditListQuery query)
    {
        var rows = ApplySecurityScope(authDb, authDb.SecurityEvents.AsNoTracking(), scope);

        if (query.ActorUserId is long actorId)
            rows = rows.Where(s => s.ActorUserId == actorId || s.SubjectUserId == actorId);
        if (query.SiteId is long siteId)
            rows = rows.Where(s => s.SiteId == siteId);
        if (query.OutcomeFilter == AuditOutcomeFilter.Succeeded)
            rows = rows.Where(s => s.Outcome == 0);
        else if (query.OutcomeFilter == AuditOutcomeFilter.RejectedOrFailed)
            rows = rows.Where(s => s.Outcome != 0);
        if (query.FromDate is DateOnly df)
        {
            var fromUtc = DiagnosticDateBounds.LocalDayStartToUtc(df);
            rows = rows.Where(s => s.OccurredAtUtc >= fromUtc);
        }
        if (query.ToDate is DateOnly dt)
        {
            var toExclusive = DiagnosticDateBounds.LocalDayEndExclusiveToUtc(dt);
            rows = rows.Where(s => s.OccurredAtUtc < toExclusive);
        }
        if (!string.IsNullOrEmpty(query.EventCode))
            rows = rows.Where(s => s.EventCode == query.EventCode);

        if (!string.IsNullOrEmpty(query.Search))
        {
            var term = query.Search;
            if (Guid.TryParse(term, out var corrId))
            {
                rows = rows.Where(s => s.CorrelationId == corrId
                                       || s.EventCode.Contains(term)
                                       || s.DataJson.Contains(term)
                                       || (s.HostOperatorReference != null && s.HostOperatorReference.Contains(term)));
            }
            else
            {
                rows = rows.Where(s => s.EventCode.Contains(term)
                                       || s.DataJson.Contains(term)
                                       || (s.HostOperatorReference != null && s.HostOperatorReference.Contains(term)));
            }
        }

        return rows;
    }

    private static IQueryable<UserOperationLogRecord> BuildOperationsQuery(
        DiagnosticsDbContext diagDb,
        OrganizationScope scope,
        AuditListQuery query)
    {
        var rows = ApplyOperationsScope(diagDb.UserOperationLogs.AsNoTracking(), scope);

        if (query.ActorUserId is long actorId)
            rows = rows.Where(o => o.ActorUserId == actorId);
        if (query.SiteId is long siteId)
            rows = rows.Where(o => o.SiteId == siteId);
        if (query.OutcomeFilter == AuditOutcomeFilter.Succeeded)
            rows = rows.Where(o => o.Outcome == UserOperationOutcome.Succeeded);
        else if (query.OutcomeFilter == AuditOutcomeFilter.RejectedOrFailed)
            rows = rows.Where(o => o.Outcome != UserOperationOutcome.Succeeded);
        if (query.FromDate is DateOnly df)
        {
            var fromUtc = DiagnosticDateBounds.LocalDayStartToUtc(df);
            rows = rows.Where(o => o.OccurredAtUtc >= fromUtc);
        }
        if (query.ToDate is DateOnly dt)
        {
            var toExclusive = DiagnosticDateBounds.LocalDayEndExclusiveToUtc(dt);
            rows = rows.Where(o => o.OccurredAtUtc < toExclusive);
        }
        if (!string.IsNullOrEmpty(query.EventCode))
            rows = rows.Where(o => o.OperationCode == query.EventCode);

        if (!string.IsNullOrEmpty(query.Search))
        {
            var term = query.Search;
            if (Guid.TryParse(term, out var corrId))
            {
                rows = rows.Where(o => o.CorrelationId == corrId
                                       || o.OperationCode.Contains(term)
                                       || (o.TargetEntityRef != null && o.TargetEntityRef.Contains(term))
                                       || (o.FailureCode != null && o.FailureCode.Contains(term))
                                       || o.MetadataJson.Contains(term));
            }
            else
            {
                rows = rows.Where(o => o.OperationCode.Contains(term)
                                       || (o.TargetEntityRef != null && o.TargetEntityRef.Contains(term))
                                       || (o.FailureCode != null && o.FailureCode.Contains(term))
                                       || o.MetadataJson.Contains(term));
            }
        }

        return rows;
    }

    private static IQueryable<OrganizationChange> ApplyBusinessScope(
        IQueryable<OrganizationChange> query,
        OrganizationScope scope)
        => scope switch
        {
            OrganizationScope.Organization org => query.Where(c => c.OrganizationId == org.OrganizationId),
            OrganizationScope.Site site => query.Where(c => c.OrganizationId == site.OrganizationId && c.SiteId == site.SiteId),
            _ => query.Where(_ => false)
        };

    private static readonly string[] MemberSessionEventCodes =
    [
        "SessionIssued",
        "SessionRevoked",
        "SignInFailed",
        "AccountLocked"
    ];

    private static IQueryable<SecurityEvent> ApplySecurityScope(
        AuthDbContext authDb,
        IQueryable<SecurityEvent> query,
        OrganizationScope scope)
        => scope switch
        {
            OrganizationScope.Platform => query.Where(s => s.OrganizationId == null),
            OrganizationScope.Organization org => query.Where(s =>
                s.OrganizationId == org.OrganizationId
                || (s.OrganizationId == null
                    && s.SiteId == null
                    && s.SubjectUserId != null
                    && MemberSessionEventCodes.Contains(s.EventCode)
                    && authDb.OrganizationMemberships.Any(m =>
                        m.OrganizationId == org.OrganizationId
                        && m.IsActive
                        && m.UserId == s.SubjectUserId.Value))),
            OrganizationScope.Site site => query.Where(s => s.OrganizationId == site.OrganizationId && s.SiteId == site.SiteId),
            _ => query.Where(_ => false)
        };

    private static IQueryable<UserOperationLogRecord> ApplyOperationsScope(
        IQueryable<UserOperationLogRecord> query,
        OrganizationScope scope)
        => scope switch
        {
            OrganizationScope.Platform => query.Where(o => o.OrganizationId == null),
            OrganizationScope.Organization org => query.Where(o => o.OrganizationId == org.OrganizationId),
            OrganizationScope.Site site => query.Where(o => o.OrganizationId == site.OrganizationId && o.SiteId == site.SiteId),
            _ => query.Where(_ => false)
        };

    private static async Task<AuditEntrySummary[]> EnrichUserNamesAsync(
        AuthDbContext authDb,
        IReadOnlyList<RawAuditRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        var userIds = rows
            .SelectMany(r => new[] { r.ActorUserId, r.SubjectUserId })
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        Dictionary<long, string> userNames = [];
        if (userIds.Length > 0)
        {
            userNames = await authDb.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, UserName = u.UserName ?? string.Empty })
                .ToDictionaryAsync(x => x.Id, x => x.UserName, ct);
        }

        return rows.Select(r => new AuditEntrySummary(
            r.Source,
            r.Id,
            DateTime.SpecifyKind(r.OccurredAtUtc, DateTimeKind.Utc),
            r.EventCode,
            r.OrganizationId,
            r.SiteId,
            r.ActorUserId,
            r.ActorUserId is > 0 && userNames.TryGetValue(r.ActorUserId.Value, out var actorName) ? actorName : null,
            r.HostOperatorReference,
            r.SubjectUserId,
            r.SubjectUserId is > 0 && userNames.TryGetValue(r.SubjectUserId.Value, out var subjectName) ? subjectName : null,
            r.Outcome,
            r.CorrelationId,
            r.RawJson)).ToArray();
    }

    private static bool IsTransientUnavailable(Exception ex) =>
        ex is DbException or SqlException or InvalidOperationException or TimeoutException;

    private sealed record RawAuditRow(
        AuditSourceKind Source,
        long Id,
        DateTime OccurredAtUtc,
        string EventCode,
        long? OrganizationId,
        long? SiteId,
        long? ActorUserId,
        string? HostOperatorReference,
        long? SubjectUserId,
        byte? Outcome,
        Guid CorrelationId,
        string RawJson);
}
