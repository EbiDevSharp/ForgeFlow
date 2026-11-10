using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Units;
using ForgeFlow.Application.Users;
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
        var roleGrants = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(x => x.UserId == actor.ActorId && x.IsActive && x.OrganizationId != null)
            .Select(x => new { OrganizationId = x.OrganizationId!.Value, x.SiteId }).Distinct().ToListAsync();
        var directGrants = await db.UserPermissionGrants.AsNoTracking()
            .Where(x => x.UserId == actor.ActorId && x.IsActive)
            .Select(x => new { x.OrganizationId, x.SiteId }).Distinct().ToListAsync();
        var grants = roleGrants.Concat(directGrants).Distinct().OrderBy(x => x.OrganizationId).ThenBy(x => x.SiteId).ToList();
        var result = new List<OrganizationScope>();
        foreach (var grant in grants)
        {
            OrganizationScope scope = grant.SiteId is long site
                ? new OrganizationScope.Site(grant.OrganizationId, site)
                : new OrganizationScope.Organization(grant.OrganizationId);
            if ((await permissions.EvaluateAsync(actor, scope, OrganizationPermissions.SitesRead, default)).IsAllowed)
                result.Add(scope);
        }
        return result;
    }

    public Task<IReadOnlyList<OrganizationScope>> LoadOrganizationScopesAsync() =>
        LoadOrganizationPermissionScopesAsync(OrganizationPermissions.OrganizationsRead);

    public Task<IReadOnlyList<OrganizationScope>> LoadUserScopesAsync() =>
        LoadOrganizationPermissionScopesAsync(UserPermissions.UsersRead);

    public Task<IReadOnlyList<OrganizationScope>> LoadRoleScopesAsync() =>
        LoadOrganizationPermissionScopesAsync(RolePermissions.RolesRead);

    public Task<IReadOnlyList<OrganizationScope>> LoadUnitScopesAsync() =>
        LoadOrganizationOrSitePermissionScopesAsync(UnitPermissions.UnitsRead);

    public Task<IReadOnlyList<OrganizationScope>> LoadItemScopesAsync() =>
        LoadOrganizationOrSitePermissionScopesAsync(ItemPermissions.ItemsRead);

    public Task<IReadOnlyList<OrganizationScope>> LoadBomScopesAsync() =>
        LoadOrganizationOrSitePermissionScopesAsync(ForgeFlow.Application.Boms.BomPermissions.BomsRead);

    public Task<IReadOnlyList<OrganizationScope>> LoadProductionOrderScopesAsync() =>
        LoadOrganizationOrSitePermissionScopesAsync(ForgeFlow.Application.ProductionOrders.ProductionOrderPermissions.ProductionOrdersRead);

    public Task<IReadOnlyList<OrganizationScope>> LoadInventoryScopesAsync() =>
        LoadOrganizationOrSitePermissionScopesAsync(ForgeFlow.Application.Inventory.InventoryPermissions.InventoryRead);

    public Task<IReadOnlyList<OrganizationScope>> LoadReportScopesAsync() =>
        LoadOrganizationOrSitePermissionScopesAsync(ForgeFlow.Application.Reporting.ReportingPermissions.ReportsRead);

    public async Task<IReadOnlyList<OrganizationScope>> LoadAuditScopesAsync()
    {
        var actor = await actors.ResolveAsync(default);
        if (actor is null) return [];
        await using var operation = scopes.CreateAsyncScope();
        var db = operation.ServiceProvider.GetRequiredService<AuthDbContext>();
        var result = new List<OrganizationScope>();
        var hasPlatform = await db.ScopedRoleAssignments.AsNoTracking()
            .AnyAsync(x => x.UserId == actor.ActorId && x.IsActive && x.ScopeKind == 0);
        if (hasPlatform)
        {
            var platform = new OrganizationScope.Platform();
            if ((await permissions.EvaluateAsync(actor, platform, AuditPermissions.AuditRead, default)).IsAllowed)
                result.Add(platform);
        }

        var roleGrants = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(x => x.UserId == actor.ActorId && x.IsActive && x.OrganizationId != null)
            .Select(x => new { OrganizationId = x.OrganizationId!.Value, x.SiteId }).Distinct().ToListAsync();
        var directGrants = await db.UserPermissionGrants.AsNoTracking()
            .Where(x => x.UserId == actor.ActorId && x.IsActive)
            .Select(x => new { x.OrganizationId, x.SiteId }).Distinct().ToListAsync();
        var grants = roleGrants.Concat(directGrants).Distinct()
            .OrderBy(x => x.OrganizationId)
            .ThenBy(x => x.SiteId.HasValue ? 1 : 0)
            .ThenBy(x => x.SiteId)
            .ToList();
        foreach (var grant in grants)
        {
            OrganizationScope scope = grant.SiteId is long site
                ? new OrganizationScope.Site(grant.OrganizationId, site)
                : new OrganizationScope.Organization(grant.OrganizationId);
            if ((await permissions.EvaluateAsync(actor, scope, AuditPermissions.AuditRead, default)).IsAllowed)
                result.Add(scope);
        }
        return result;
    }

    private async Task<IReadOnlyList<OrganizationScope>> LoadOrganizationPermissionScopesAsync(string permission)
    {
        var actor = await actors.ResolveAsync(default);
        if (actor is null) return [];
        await using var operation = scopes.CreateAsyncScope();
        var db = operation.ServiceProvider.GetRequiredService<AuthDbContext>();
        var roleOrgIds = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(x => x.UserId == actor.ActorId && x.IsActive && x.OrganizationId != null && x.ScopeKind == 1)
            .Select(x => x.OrganizationId!.Value).Distinct().ToListAsync();
        var directOrgIds = await db.UserPermissionGrants.AsNoTracking()
            .Where(x => x.UserId == actor.ActorId && x.IsActive && x.ScopeKind == 1)
            .Select(x => x.OrganizationId).Distinct().ToListAsync();
        var orgIds = roleOrgIds.Concat(directOrgIds).Distinct().OrderBy(id => id).ToList();
        var result = new List<OrganizationScope>();
        foreach (var orgId in orgIds)
        {
            var scope = new OrganizationScope.Organization(orgId);
            if ((await permissions.EvaluateAsync(actor, scope, permission, default)).IsAllowed)
                result.Add(scope);
        }
        return result;
    }

    private async Task<IReadOnlyList<OrganizationScope>> LoadOrganizationOrSitePermissionScopesAsync(string permission)
    {
        var actor = await actors.ResolveAsync(default);
        if (actor is null) return [];
        await using var operation = scopes.CreateAsyncScope();
        var db = operation.ServiceProvider.GetRequiredService<AuthDbContext>();
        var roleGrants = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(x => x.UserId == actor.ActorId && x.IsActive && x.OrganizationId != null)
            .Select(x => new { OrganizationId = x.OrganizationId!.Value, x.SiteId }).Distinct().ToListAsync();
        var directGrants = await db.UserPermissionGrants.AsNoTracking()
            .Where(x => x.UserId == actor.ActorId && x.IsActive)
            .Select(x => new { x.OrganizationId, x.SiteId }).Distinct().ToListAsync();
        var grants = roleGrants.Concat(directGrants).Distinct()
            .OrderBy(x => x.OrganizationId)
            .ThenBy(x => x.SiteId.HasValue ? 1 : 0)
            .ThenBy(x => x.SiteId)
            .ToList();
        var result = new List<OrganizationScope>();
        foreach (var grant in grants)
        {
            OrganizationScope scope = grant.SiteId is long site
                ? new OrganizationScope.Site(grant.OrganizationId, site)
                : new OrganizationScope.Organization(grant.OrganizationId);
            if ((await permissions.EvaluateAsync(actor, scope, permission, default)).IsAllowed)
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
