using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class OrganizationOperationsStore(Func<IDbContextFactory<OrganizationDbContext>> getFactory) : IOrganizationOperationsStore
{
    private IDbContextFactory<OrganizationDbContext> factory => getFactory();
    public OrganizationOperationsStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }
    private static OrganizationDetails Detail(OrganizationDbContext db, Organization org) => new(org.Id, org.Code, org.Name, org.IsActive,
        Convert.ToBase64String(db.Entry(org).Property<byte[]>("RowVersion").CurrentValue!));
    private static SiteDetails Detail(OrganizationDbContext db, Site site) => new(site.Id, site.OrganizationId, site.Code, site.Name, site.IsActive,
        Convert.ToBase64String(db.Entry(site).Property<byte[]>("RowVersion").CurrentValue!));

    public Task<OrganizationDetails?> GetOrganization(long organizationId, CancellationToken ct) => Read(async db =>
    {
        var org = await db.Organizations.IgnoreAutoIncludes().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        return org is null ? null : Detail(db, org);
    }, ct);
    public Task<SiteDetails?> GetSite(long organizationId, long siteId, CancellationToken ct) => Read(async db =>
    {
        var site = await db.Sites.SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == siteId, ct);
        return site is null ? null : Detail(db, site);
    }, ct);
    public Task<OrganizationPage<OrganizationDetails>> ListOrganizations(long organizationId, OrganizationListQuery query, CancellationToken ct) => Read(async db =>
    {
        var rows = db.Organizations.IgnoreAutoIncludes().Where(o => o.Id == organizationId && (query.IncludeInactive || o.IsActive));
        if (!string.IsNullOrEmpty(query.Search)) rows = rows.Where(o => o.Code.Contains(query.Search) || o.Name.Contains(query.Search));
        var count = await rows.CountAsync(ct);
        var page = await rows.OrderBy(o => o.Code).ThenBy(o => o.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new OrganizationPage<OrganizationDetails>(page.Select(o => Detail(db, o)).ToArray(), count, query.Page, query.PageSize);
    }, ct);
    public Task<OrganizationPage<SiteDetails>> ListSites(long organizationId, long? siteId, OrganizationListQuery query, CancellationToken ct) => Read(async db =>
    {
        var rows = db.Sites.Where(s => s.OrganizationId == organizationId && (!siteId.HasValue || s.Id == siteId.Value) && (query.IncludeInactive || s.IsActive));
        if (!string.IsNullOrEmpty(query.Search)) rows = rows.Where(s => s.Code.Contains(query.Search) || s.Name.Contains(query.Search));
        var count = await rows.CountAsync(ct);
        var page = await rows.OrderBy(s => s.Code).ThenBy(s => s.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new OrganizationPage<SiteDetails>(page.Select(s => Detail(db, s)).ToArray(), count, query.Page, query.PageSize);
    }, ct);

    private async Task<T> Read<T>(Func<OrganizationDbContext, Task<T>> query, CancellationToken ct)
    {
        try { await using var db = await factory.CreateDbContextAsync(ct); return await query(db); }
        catch (Exception ex) when (Unavailable(ex)) { throw new OrganizationStoreUnavailableException(ex); }
    }
    private static bool Unavailable(Exception exception) => exception switch
    {
        SqlException sql => sql.Number is -2 or 53 or 64 or 233 or 1205 or 4060 or 18456 or 40197 or 40501 or 40613,
        TimeoutException => true,
        DbUpdateException or RetryLimitExceededException when exception.InnerException is not null => Unavailable(exception.InnerException),
        InvalidOperationException when exception.Message == OrganizationAccessErrors.PersistenceUnavailable => true,
        _ => false
    };

    public Task<OrganizationResult<OrganizationDetails>> CreateOrganization(CreateOrganization request, OrganizationWriteAccess access, CancellationToken ct)
        => Write(access, async db =>
        {
            var org = Organization.Create(request.Code, request.Name);
            db.Add(org);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<OrganizationDetails>.Success(Detail(db, org));
        }, ct);

    public Task<OrganizationResult<SiteDetails>> CreateSite(CreateSite request, OrganizationWriteAccess access, CancellationToken ct)
        => Write(access, async db =>
        {
            await Lock(db, request.OrganizationId, ct);
            var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == request.OrganizationId, ct);
            if (org is null) return OrganizationResult<SiteDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
            var site = org.CreateSite(request.Code, request.Name);
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<SiteDetails>.Success(Detail(db, site));
        }, ct);

    public Task<OrganizationResult<OrganizationDetails>> UpdateOrganization(long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct)
        => Write(access, async db =>
        {
            await Lock(db, id, ct);
            var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == id, ct);
            if (org is null) return OrganizationResult<OrganizationDetails>.Failure(OrganizationSiteErrors.OrganizationNotFound);
            if (!Matches(db, org, version)) return OrganizationResult<OrganizationDetails>.Failure(OrganizationAccessErrors.ConcurrencyConflict);
            switch (mutation)
            {
                case OrganizationMutation.Rename: org.Rename(name); break;
                case OrganizationMutation.Activate: org.Activate(); break;
                case OrganizationMutation.Deactivate: org.Deactivate(); break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<OrganizationDetails>.Success(Detail(db, org));
        }, ct);

    public Task<OrganizationResult<SiteDetails>> UpdateSite(long organizationId, long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct)
        => Write(access, async db =>
        {
            await Lock(db, organizationId, ct);
            var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId, ct);
            var site = org?.Sites.SingleOrDefault(s => s.Id == id);
            if (site is null) return OrganizationResult<SiteDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
            if (!Matches(db, site, version)) return OrganizationResult<SiteDetails>.Failure(OrganizationAccessErrors.ConcurrencyConflict);
            switch (mutation)
            {
                case OrganizationMutation.Rename: site.Rename(name); break;
                case OrganizationMutation.Activate: site.Activate(); break;
                case OrganizationMutation.Deactivate: site.Deactivate(); break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            await db.SaveInOperation(access.Actor, ct);
            return OrganizationResult<SiteDetails>.Success(Detail(db, site));
        }, ct);

    private static bool Matches(OrganizationDbContext db, object entity, byte[] version)
        => ((byte[])db.Entry(entity).Property("RowVersion").CurrentValue!).SequenceEqual(version);

    private static Task Lock(OrganizationDbContext db, long id, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}", ct);

    private async Task<OrganizationResult<T>> Write<T>(OrganizationWriteAccess access,
        Func<OrganizationDbContext, Task<OrganizationResult<T>>> operation, CancellationToken ct)
    {
        try
        {
            await using var strategyContext = await factory.CreateDbContextAsync(ct);
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                // Revalidate on each attempt and discard its context/transaction before any replay.
                if (!await access.Revalidate(ct)) return OrganizationResult<T>.Failure(OrganizationAccessErrors.AccessDenied);
                var commitAttempted = false;
                try
                {
                    await using var db = await factory.CreateDbContextAsync(ct);
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    var result = await operation(db);
                    if (!result.Succeeded) return result;
                    commitAttempted = true;
                    await transaction.CommitAsync(ct);
                    return result;
                }
                catch (Exception ex) when (commitAttempted && ex is not OrganizationCommitOutcomeUnknownException)
                {
                    // Includes a disposal failure after a successful commit: never let a retry strategy replay it.
                    throw new OrganizationCommitOutcomeUnknownException(ex);
                }
            });
        }
        catch (OrganizationSiteRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(OrganizationAccessErrors.ConcurrencyConflict); }
        catch (OrganizationCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(OrganizationAccessErrors.OutcomeUnknown); }
        catch (OrganizationAuditWriteException ex) when (ex.InnerException is SqlException sql && (sql.Number == 229 || sql.Number >= 50000))
        { return OrganizationResult<T>.Failure(OrganizationAccessErrors.AuditUnavailable); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627
            && sql.Message.Contains("'UX_Organization_Code'", StringComparison.Ordinal))
        { return OrganizationResult<T>.Failure(OrganizationSiteErrors.OrganizationCodeDuplicate); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627
            && sql.Message.Contains("'UX_Site_Organization_Code'", StringComparison.Ordinal))
        { return OrganizationResult<T>.Failure(OrganizationSiteErrors.SiteCodeDuplicate); }
        catch (InvalidOperationException ex) when (ex.Message is OrganizationSiteErrors.OrganizationInactive or OrganizationSiteErrors.OrganizationHasActiveSites or OrganizationSiteErrors.OrganizationNotFound)
        { return OrganizationResult<T>.Failure(ex.Message); }
        catch (Exception ex) when (Unavailable(ex)) { return OrganizationResult<T>.Failure(OrganizationAccessErrors.PersistenceUnavailable); }
    }
}
