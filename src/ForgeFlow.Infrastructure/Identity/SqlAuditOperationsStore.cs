using System.Data.Common;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class SqlAuditOperationsStore(IServiceScopeFactory scopes) : IAuditOperationsStore
{
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

            var includeBusiness = query.Category is AuditCategory.All or AuditCategory.Business
                                  && scope is not OrganizationScope.Platform
                                  && orgDb is not null;
            var includeSecurity = query.Category is AuditCategory.All or AuditCategory.Security;

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
                            s.OrganizationId,
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

            var total = businessCount + securityCount;
            if (total == 0)
                return new OrganizationPage<AuditEntrySummary>([], 0, query.Page, query.PageSize);

            var pageRaw = businessRows
                .Concat(securityRows)
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
                var query = ApplySecurityScope(authDb.SecurityEvents.AsNoTracking(), scope)
                    .Where(s => s.Id == entryId);

                row = await query
                    .Select(s => new RawAuditRow(
                        AuditSourceKind.Security,
                        s.Id,
                        s.OccurredAtUtc,
                        s.EventCode,
                        s.OrganizationId,
                        s.SiteId,
                        s.ActorUserId,
                        s.HostOperatorReference,
                        s.SubjectUserId,
                        s.Outcome,
                        s.CorrelationId,
                        s.DataJson))
                    .SingleOrDefaultAsync(ct);
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
            throw new AuditStoreUnavailableException(ex);
        }
    }

    private static IQueryable<OrganizationChange> BuildBusinessQuery(
        OrganizationDbContext orgDb,
        OrganizationScope scope,
        AuditListQuery query)
    {
        var rows = ApplyBusinessScope(orgDb.OrganizationChanges.AsNoTracking(), scope);

        if (!string.IsNullOrEmpty(query.EventCode))
            rows = rows.Where(c => c.EventCode == query.EventCode);

        if (!string.IsNullOrEmpty(query.Search))
        {
            var term = query.Search;
            if (Guid.TryParse(term, out var corrId))
            {
                rows = rows.Where(c => c.CorrelationId == corrId || c.EventCode.Contains(term) || c.ChangesJson.Contains(term));
            }
            else
            {
                rows = rows.Where(c => c.EventCode.Contains(term) || c.ChangesJson.Contains(term));
            }
        }

        return rows;
    }

    private static IQueryable<SecurityEvent> BuildSecurityQuery(
        AuthDbContext authDb,
        OrganizationScope scope,
        AuditListQuery query)
    {
        var rows = ApplySecurityScope(authDb.SecurityEvents.AsNoTracking(), scope);

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

    private static IQueryable<OrganizationChange> ApplyBusinessScope(
        IQueryable<OrganizationChange> query,
        OrganizationScope scope)
        => scope switch
        {
            OrganizationScope.Organization org => query.Where(c => c.OrganizationId == org.OrganizationId),
            OrganizationScope.Site site => query.Where(c => c.OrganizationId == site.OrganizationId && c.SiteId == site.SiteId),
            _ => query.Where(_ => false)
        };

    private static IQueryable<SecurityEvent> ApplySecurityScope(
        IQueryable<SecurityEvent> query,
        OrganizationScope scope)
        => scope switch
        {
            OrganizationScope.Platform => query.Where(s => s.OrganizationId == null),
            OrganizationScope.Organization org => query.Where(s => s.OrganizationId == org.OrganizationId),
            OrganizationScope.Site site => query.Where(s => s.OrganizationId == site.OrganizationId && s.SiteId == site.SiteId),
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

    private static bool IsTransientUnavailable(Exception ex)
        => ex is DbException or SqlException or InvalidOperationException or TimeoutException;

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
