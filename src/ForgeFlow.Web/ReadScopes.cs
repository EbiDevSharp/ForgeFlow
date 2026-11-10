using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Web.Organizations;

public sealed record ScopeDisplayInfo(string? OrgCode, string? OrgName, string? SiteCode, string? SiteName);

public sealed class ReadScopes(IServiceScopeFactory scopes, ITrustedActorAccessor actors, IOrganizationPermissionEvaluator permissions, OrganizationUseCases? queries = null)
{
    public async Task<IReadOnlyList<OrganizationScope>> Load()
    {
        var actor = await actors.ResolveAsync(default);
        if (actor is null) return [];
        await using var operation = scopes.CreateAsyncScope();
        var db = operation.ServiceProvider.GetRequiredService<AuthDbContext>();
        var grants = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(x => x.UserId == actor.ActorId && x.IsActive && x.OrganizationId != null)
            .Select(x => new { x.OrganizationId, x.SiteId }).Distinct().ToListAsync();
        var result = new List<OrganizationScope>();
        foreach (var grant in grants)
        {
            OrganizationScope scope = grant.SiteId is long site
                ? new OrganizationScope.Site(grant.OrganizationId!.Value, site)
                : new OrganizationScope.Organization(grant.OrganizationId!.Value);
            if ((await permissions.EvaluateAsync(actor, scope, OrganizationPermissions.SitesRead, default)).IsAllowed)
                result.Add(scope);
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<OrganizationScope, ScopeDisplayInfo>> LoadDisplayInfoAsync(IReadOnlyList<OrganizationScope> authorizedScopes)
    {
        var map = new Dictionary<OrganizationScope, ScopeDisplayInfo>();
        if (queries is null || authorizedScopes.Count == 0) return map;
        foreach (var scope in authorizedScopes)
        {
            if (scope is OrganizationScope.Organization org)
            {
                var res = await queries.GetOrganization(org, org.OrganizationId);
                if (res.Succeeded && res.Value is not null)
                    map[scope] = new(res.Value.Code, res.Value.Name, null, null);
            }
            else if (scope is OrganizationScope.Site site)
            {
                var siteRes = await queries.GetSite(site, site.OrganizationId, site.SiteId);
                var orgRes = await queries.GetOrganization(new OrganizationScope.Organization(site.OrganizationId), site.OrganizationId);
                map[scope] = new(
                    orgRes.Succeeded ? orgRes.Value?.Code : null,
                    orgRes.Succeeded ? orgRes.Value?.Name : null,
                    siteRes.Succeeded ? siteRes.Value?.Code : null,
                    siteRes.Succeeded ? siteRes.Value?.Name : null);
            }
        }
        return map;
    }
}
