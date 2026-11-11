using System.Data.Common;
using System.Text.Encodings.Web;
using System.Text.Json;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Users;
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
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null) : IAuditOperationsStore, IMyActivityStore
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

    public async Task<OrganizationPage<MyActivityEntrySummary>> ListMyActivityAsync(
        long userId,
        MyActivityQuery query,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            return new OrganizationPage<MyActivityEntrySummary>([], 0, 1, Math.Clamp(query.PageSize, 1, 50));

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var fromUtc = query.RangeDays > 0
            ? (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime.AddDays(-query.RangeDays)
            : (DateTime?)null;
        var eventCode = string.IsNullOrWhiteSpace(query.EventCode) ? null : query.EventCode.Trim();
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

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

            var window = checked((page - 1) * pageSize + pageSize);

            var bCount = 0;
            List<(RawAuditRow Row, Guid? SessionId)> bRows = [];
            if (orgDb is not null)
            {
                var bq = orgDb.OrganizationChanges.AsNoTracking().Where(c => c.ActorId == userId);
                if (fromUtc.HasValue) bq = bq.Where(c => c.OccurredAtUtc >= fromUtc.Value);
                if (eventCode is not null) bq = bq.Where(c => c.EventCode == eventCode);
                if (search is not null)
                {
                    if (Guid.TryParse(search, out var corr))
                        bq = bq.Where(c => c.CorrelationId == corr || c.EventCode.Contains(search) || c.ChangesJson.Contains(search));
                    else
                        bq = bq.Where(c => c.EventCode.Contains(search) || c.ChangesJson.Contains(search));
                }
                bCount = await bq.CountAsync(ct);
                if (bCount > 0)
                {
                    bRows = (await bq.OrderByDescending(c => c.OccurredAtUtc).ThenByDescending(c => c.Id)
                        .Take(window)
                        .Select(c => new RawAuditRow(AuditSourceKind.Business, c.Id, c.OccurredAtUtc, c.EventCode, c.OrganizationId, c.SiteId, c.ActorId, null, null, null, c.CorrelationId, c.ChangesJson))
                        .ToListAsync(ct)).Select(r => (r, (Guid?)null)).ToList();
                }
            }

            var sq = authDb.SecurityEvents.AsNoTracking().Where(s =>
                s.ActorUserId == userId
                || (s.SubjectUserId == userId && (s.EventCode == "SignInFailed" || s.EventCode == "AccountLocked")));
            if (fromUtc.HasValue) sq = sq.Where(s => s.OccurredAtUtc >= fromUtc.Value);
            if (eventCode is not null) sq = sq.Where(s => s.EventCode == eventCode);
            if (search is not null)
            {
                if (Guid.TryParse(search, out var corr))
                    sq = sq.Where(s => s.CorrelationId == corr || s.EventCode.Contains(search) || s.DataJson.Contains(search));
                else
                    sq = sq.Where(s => s.EventCode.Contains(search) || s.DataJson.Contains(search));
            }
            var sCount = await sq.CountAsync(ct);
            List<(RawAuditRow Row, Guid? SessionId)> sRows = [];
            if (sCount > 0)
            {
                sRows = (await sq.OrderByDescending(s => s.OccurredAtUtc).ThenByDescending(s => s.Id)
                    .Take(window)
                    .Select(s => new
                    {
                        Row = new RawAuditRow(AuditSourceKind.Security, s.Id, s.OccurredAtUtc, s.EventCode, s.OrganizationId, s.SiteId, s.ActorUserId, s.HostOperatorReference, s.SubjectUserId, s.Outcome, s.CorrelationId, s.DataJson),
                        s.SessionId
                    }).ToListAsync(ct)).Select(x => (x.Row, x.SessionId)).ToList();
            }

            var oCount = 0;
            List<(RawAuditRow Row, Guid? SessionId)> oRows = [];
            if (diagDb is not null)
            {
                try
                {
                    var oq = diagDb.UserOperationLogs.AsNoTracking().Where(o => o.ActorUserId == userId);
                    if (fromUtc.HasValue) oq = oq.Where(o => o.OccurredAtUtc >= fromUtc.Value);
                    if (eventCode is not null) oq = oq.Where(o => o.OperationCode == eventCode);
                    if (search is not null)
                    {
                        if (Guid.TryParse(search, out var corr))
                            oq = oq.Where(o => o.CorrelationId == corr || o.OperationCode.Contains(search) || (o.TargetEntityRef != null && o.TargetEntityRef.Contains(search)) || (o.FailureCode != null && o.FailureCode.Contains(search)) || o.MetadataJson.Contains(search));
                        else
                            oq = oq.Where(o => o.OperationCode.Contains(search) || (o.TargetEntityRef != null && o.TargetEntityRef.Contains(search)) || (o.FailureCode != null && o.FailureCode.Contains(search)) || o.MetadataJson.Contains(search));
                    }
                    oCount = await oq.CountAsync(ct);
                    if (oCount > 0)
                    {
                        var ops = await oq.OrderByDescending(o => o.OccurredAtUtc).ThenByDescending(o => o.Id).Take(window).ToListAsync(ct);
                        oRows = ops.Select(o => (ToOperationRow(o), (Guid?)null)).ToList();
                    }
                }
                catch (SqlException)
                {
                    oCount = 0;
                    oRows = [];
                }
            }

            var total = bCount + sCount + oCount;
            if (total == 0)
                return new OrganizationPage<MyActivityEntrySummary>([], 0, page, pageSize);

            var pageItems = bRows.Concat(sRows).Concat(oRows)
                .OrderByDescending(x => x.Row.OccurredAtUtc)
                .ThenBy(x => x.Row.Source)
                .ThenByDescending(x => x.Row.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();

            var sessionIds = pageItems.Where(x => x.SessionId.HasValue).Select(x => x.SessionId!.Value).Distinct().ToArray();
            var sessionMeta = sessionIds.Length == 0
                ? new Dictionary<Guid, (string? IpAddress, string? DeviceSummary)>()
                : (await authDb.Sessions.AsNoTracking()
                    .Where(s => sessionIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.IpAddress, s.DeviceSummary })
                    .ToListAsync(ct))
                    .ToDictionary(s => s.Id, s => (s.IpAddress, s.DeviceSummary));

            var results = pageItems.Select(item =>
            {
                var (ip, device) = ExtractIpAndDevice(item.Row.RawJson);
                if (item.SessionId.HasValue && sessionMeta.TryGetValue(item.SessionId.Value, out var sMeta))
                {
                    ip ??= sMeta.IpAddress;
                    device ??= sMeta.DeviceSummary;
                }
                return new MyActivityEntrySummary(
                    item.Row.Source,
                    item.Row.Id,
                    DateTime.SpecifyKind(item.Row.OccurredAtUtc, DateTimeKind.Utc),
                    item.Row.EventCode,
                    ip,
                    device,
                    item.Row.Outcome,
                    item.Row.CorrelationId,
                    item.Row.RawJson);
            }).ToArray();

            return new OrganizationPage<MyActivityEntrySummary>(results, total, page, pageSize);
        }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlAuditOperationsStore));
            throw new AuditStoreUnavailableException(ex);
        }
    }

    public async Task<MyActivityEntryDetails?> GetMyActivityEntryAsync(
        long userId,
        AuditSourceKind source,
        long entryId,
        CancellationToken ct = default)
    {
        if (userId <= 0 || entryId <= 0)
            return null;

        try
        {
            await using var serviceScope = scopes.CreateAsyncScope();
            var authDb = serviceScope.ServiceProvider.GetRequiredService<AuthDbContext>();

            RawAuditRow? row = null;
            Guid? sessionId = null;

            if (source == AuditSourceKind.Business)
            {
                var orgDbFactory = serviceScope.ServiceProvider.GetService<IDbContextFactory<OrganizationDbContext>>();
                await using var orgDb = orgDbFactory is not null
                    ? await orgDbFactory.CreateDbContextAsync(ct)
                    : serviceScope.ServiceProvider.GetService<OrganizationDbContext>();
                if (orgDb is not null)
                {
                    row = await orgDb.OrganizationChanges.AsNoTracking()
                        .Where(c => c.Id == entryId && c.ActorId == userId)
                        .Select(c => new RawAuditRow(AuditSourceKind.Business, c.Id, c.OccurredAtUtc, c.EventCode, c.OrganizationId, c.SiteId, c.ActorId, null, null, null, c.CorrelationId, c.ChangesJson))
                        .SingleOrDefaultAsync(ct);
                }
            }
            else if (source == AuditSourceKind.Security)
            {
                var sec = await authDb.SecurityEvents.AsNoTracking()
                    .Where(s => s.Id == entryId && (s.ActorUserId == userId || (s.SubjectUserId == userId && (s.EventCode == "SignInFailed" || s.EventCode == "AccountLocked"))))
                    .Select(s => new
                    {
                        Row = new RawAuditRow(AuditSourceKind.Security, s.Id, s.OccurredAtUtc, s.EventCode, s.OrganizationId, s.SiteId, s.ActorUserId, s.HostOperatorReference, s.SubjectUserId, s.Outcome, s.CorrelationId, s.DataJson),
                        s.SessionId
                    })
                    .SingleOrDefaultAsync(ct);
                row = sec?.Row;
                sessionId = sec?.SessionId;
            }
            else if (source == AuditSourceKind.Operations)
            {
                var diagDbFactory = serviceScope.ServiceProvider.GetService<IDbContextFactory<DiagnosticsDbContext>>();
                await using var diagDb = diagDbFactory is not null
                    ? await diagDbFactory.CreateDbContextAsync(ct)
                    : serviceScope.ServiceProvider.GetService<DiagnosticsDbContext>();
                if (diagDb is not null)
                {
                    try
                    {
                        var op = await diagDb.UserOperationLogs.AsNoTracking()
                            .SingleOrDefaultAsync(o => o.Id == entryId && o.ActorUserId == userId, ct);
                        if (op is not null) row = ToOperationRow(op);
                    }
                    catch (SqlException) { }
                }
            }

            if (row is null)
                return null;

            var (ip, device) = ExtractIpAndDevice(row.RawJson);
            if (sessionId.HasValue)
            {
                var sMeta = await authDb.Sessions.AsNoTracking()
                    .Where(s => s.Id == sessionId.Value)
                    .Select(s => new { s.IpAddress, s.DeviceSummary })
                    .SingleOrDefaultAsync(ct);
                if (sMeta is not null)
                {
                    ip ??= sMeta.IpAddress;
                    device ??= sMeta.DeviceSummary;
                }
            }

            var summary = new MyActivityEntrySummary(
                row.Source,
                row.Id,
                DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc),
                row.EventCode,
                ip,
                device,
                row.Outcome,
                row.CorrelationId,
                row.RawJson);
            return new MyActivityEntryDetails(summary, AuditPayloadParser.ParseFields(row.RawJson));
        }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlAuditOperationsStore));
            throw new AuditStoreUnavailableException(ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListMyActivityEventCodesAsync(long userId, CancellationToken ct = default)
    {
        if (userId <= 0) return [];
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

            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var code in await authDb.SecurityEvents.AsNoTracking()
                .Where(s => s.ActorUserId == userId || (s.SubjectUserId == userId && (s.EventCode == "SignInFailed" || s.EventCode == "AccountLocked")))
                .Select(s => s.EventCode).Distinct().ToListAsync(ct))
            {
                codes.Add(code);
            }

            if (orgDb is not null)
            {
                foreach (var code in await orgDb.OrganizationChanges.AsNoTracking()
                    .Where(c => c.ActorId == userId).Select(c => c.EventCode).Distinct().ToListAsync(ct))
                {
                    codes.Add(code);
                }
            }

            if (diagDb is not null)
            {
                try
                {
                    foreach (var code in await diagDb.UserOperationLogs.AsNoTracking()
                        .Where(o => o.ActorUserId == userId).Select(o => o.OperationCode).Distinct().ToListAsync(ct))
                    {
                        codes.Add(code);
                    }
                }
                catch (SqlException) { }
            }

            return codes.OrderBy(c => c, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlAuditOperationsStore));
            throw new AuditStoreUnavailableException(ex);
        }
    }

    private static (string? IpAddress, string? DeviceSummary) ExtractIpAndDevice(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);

            string? ReadStr(string name) =>
                doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                    ? (string.IsNullOrWhiteSpace(p.GetString()) ? null : p.GetString()!.Trim())
                    : null;

            var ip = ReadStr("ip") ?? ReadStr("clientIp");
            var device = ReadStr("device");
            if (device is null && ReadStr("userAgent") is { } ua)
                device = UserAgentDeviceParser.Parse(ua).DeviceSummary;

            return (ip, device);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

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
