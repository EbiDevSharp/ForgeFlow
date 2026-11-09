using System.Collections.ObjectModel;

namespace ForgeFlow.Application.Organizations;

public sealed record OrganizationDetails(long Id, string Code, string Name, bool IsActive, string RowVersion);
public sealed record SiteDetails(long Id, long OrganizationId, string Code, string Name, bool IsActive, string RowVersion);
public sealed record OrganizationPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record OrganizationListQuery(string? Search = null, bool IncludeInactive = false, int Page = 1, int PageSize = 25);
public sealed record CreateOrganization(string? Code, string? Name);
public sealed record CreateSite(long OrganizationId, string? Code, string? Name);
public sealed record RenameOrganization(long Id, string? Name, string? ExpectedRowVersion);
public sealed record SetOrganizationActive(long Id, bool IsActive, string? ExpectedRowVersion);
public sealed record RenameSite(long OrganizationId, long Id, string? Name, string? ExpectedRowVersion);
public sealed record SetSiteActive(long OrganizationId, long Id, bool IsActive, string? ExpectedRowVersion);

public sealed record OrganizationFailure(string Code, IReadOnlyDictionary<string, object> Parameters);
public sealed class OrganizationStoreUnavailableException(Exception inner) : Exception(OrganizationAccessErrors.PersistenceUnavailable, inner);
public sealed record OrganizationResult<T>(T? Value, OrganizationFailure? Error)
{
    public bool Succeeded => Error is null;
    public static OrganizationResult<T> Success(T value) => new(value, null);
    public static OrganizationResult<T> Failure(string code, IReadOnlyDictionary<string, object>? parameters = null)
        => new(default, new(code, new ReadOnlyDictionary<string, object>(
            parameters is null ? new Dictionary<string, object>() : new Dictionary<string, object>(parameters))));
}

public enum OrganizationMutation { Rename, Activate, Deactivate }

/// <summary>Attribution plus revalidation callback from the secured service; not a caller request DTO.</summary>
public sealed record OrganizationWriteAccess(TrustedActor Actor, Func<CancellationToken, ValueTask<bool>> Revalidate);

/// <summary>Specific persistence port, not a generic repository. Implementation must enforce owner predicates.</summary>
public interface IOrganizationOperationsStore
{
    Task<OrganizationPage<OrganizationDetails>> ListOrganizations(long organizationId, OrganizationListQuery query, CancellationToken ct);
    Task<OrganizationDetails?> GetOrganization(long organizationId, CancellationToken ct);
    Task<OrganizationPage<SiteDetails>> ListSites(long organizationId, long? siteId, OrganizationListQuery query, CancellationToken ct);
    Task<SiteDetails?> GetSite(long organizationId, long siteId, CancellationToken ct);
    Task<OrganizationResult<OrganizationDetails>> CreateOrganization(CreateOrganization request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SiteDetails>> CreateSite(CreateSite request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<OrganizationDetails>> UpdateOrganization(long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SiteDetails>> UpdateSite(long organizationId, long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct);
}

/// <summary>No trusted adapters means denial before any store access. AUTH-01 supplies real adapters later.</summary>
public sealed class OrganizationUseCases(IOrganizationOperationsStore store,
    ITrustedActorAccessor? actors = null, IOrganizationPermissionEvaluator? permissions = null)
{
    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(OrganizationScope? scope, string permission, CancellationToken ct)
    {
        if (actors is null) return (null, OrganizationAccessErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, OrganizationAccessErrors.ActorRequired);
        if (scope is null) return (null, OrganizationAccessErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, OrganizationAccessErrors.AccessDenied);
        return (actor, null);
    }

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    private static bool OrganizationMatches(OrganizationScope? scope, long id) => scope is OrganizationScope.Organization org && org.OrganizationId == id;
    private static bool SiteMatches(OrganizationScope? scope, long orgId, long? siteId = null) => scope switch
    {
        OrganizationScope.Organization org => org.OrganizationId == orgId,
        OrganizationScope.Site site => site.OrganizationId == orgId && (siteId is null || site.SiteId == siteId),
        _ => false
    };
    private static byte[]? Version(string? raw)
    {
        if (raw is null) return null;
        try { var bytes = Convert.FromBase64String(raw); return bytes.Length == 8 ? bytes : null; }
        catch (FormatException) { return null; }
    }
    private static OrganizationListQuery Bound(OrganizationListQuery query) => query with
    { Page = Math.Clamp(query.Page, 1, 1_000_000), PageSize = Math.Clamp(query.PageSize, 1, 100), Search = query.Search?.Trim() };

    private static async Task<OrganizationResult<T>> Read<T>(Func<Task<OrganizationResult<T>>> query)
    {
        try { return await query(); }
        catch (OrganizationStoreUnavailableException) { return OrganizationResult<T>.Failure(OrganizationAccessErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<OrganizationPage<OrganizationDetails>>> ListOrganizations(OrganizationScope? scope, OrganizationListQuery query, CancellationToken ct = default)
    {
        var auth = await Authorize(scope, OrganizationPermissions.OrganizationsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<OrganizationDetails>>.Failure(auth.Error);
        if (scope is not OrganizationScope.Organization org) return OrganizationResult<OrganizationPage<OrganizationDetails>>.Failure(OrganizationAccessErrors.AccessDenied);
        return await Read(async () => OrganizationResult<OrganizationPage<OrganizationDetails>>.Success(await store.ListOrganizations(org.OrganizationId, Bound(query), ct)));
    }
    public async Task<OrganizationResult<OrganizationDetails>> GetOrganization(OrganizationScope? scope, long id, CancellationToken ct = default)
    {
        var auth = await Authorize(scope, OrganizationPermissions.OrganizationsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, id)) return OrganizationResult<OrganizationDetails>.Failure("Organization.NotFound");
        return await Read(async () => {
            var value = await store.GetOrganization(id, ct);
            return value is null ? OrganizationResult<OrganizationDetails>.Failure("Organization.NotFound") : OrganizationResult<OrganizationDetails>.Success(value);
        });
    }
    public async Task<OrganizationResult<OrganizationPage<SiteDetails>>> ListSites(OrganizationScope? scope, long organizationId, OrganizationListQuery query, CancellationToken ct = default)
    {
        var auth = await Authorize(scope, OrganizationPermissions.SitesRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<SiteDetails>>.Failure(auth.Error);
        if (!SiteMatches(scope, organizationId)) return OrganizationResult<OrganizationPage<SiteDetails>>.Failure("Site.NotFound");
        return await Read(async () => OrganizationResult<OrganizationPage<SiteDetails>>.Success(await store.ListSites(organizationId, (scope as OrganizationScope.Site)?.SiteId, Bound(query), ct)));
    }
    public async Task<OrganizationResult<SiteDetails>> GetSite(OrganizationScope? scope, long organizationId, long id, CancellationToken ct = default)
    {
        var auth = await Authorize(scope, OrganizationPermissions.SitesRead, ct);
        if (auth.Error is not null) return OrganizationResult<SiteDetails>.Failure(auth.Error);
        if (!SiteMatches(scope, organizationId, id)) return OrganizationResult<SiteDetails>.Failure("Site.NotFound");
        return await Read(async () => {
            var value = await store.GetSite(organizationId, id, ct);
            return value is null ? OrganizationResult<SiteDetails>.Failure("Site.NotFound") : OrganizationResult<SiteDetails>.Success(value);
        });
    }
    public async Task<OrganizationResult<OrganizationDetails>> CreateOrganization(OrganizationScope? scope, CreateOrganization request, CancellationToken ct = default)
    {
        var permission = OrganizationPermissions.OrganizationsCreate;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationDetails>.Failure(auth.Error);
        if (scope is not OrganizationScope.Platform) return OrganizationResult<OrganizationDetails>.Failure(OrganizationAccessErrors.AccessDenied);
        return await store.CreateOrganization(request, Access(auth.Actor!, scope, permission), ct);
    }
    public async Task<OrganizationResult<SiteDetails>> CreateSite(OrganizationScope? scope, CreateSite request, CancellationToken ct = default)
    {
        var permission = OrganizationPermissions.SitesCreate;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<SiteDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, request.OrganizationId)) return OrganizationResult<SiteDetails>.Failure("Site.NotFound");
        return await store.CreateSite(request, Access(auth.Actor!, scope!, permission), ct);
    }
    public Task<OrganizationResult<OrganizationDetails>> RenameOrganization(OrganizationScope? scope, RenameOrganization request, CancellationToken ct = default)
        => ChangeOrganization(scope, request.Id, request.ExpectedRowVersion, OrganizationMutation.Rename, request.Name, ct);
    public Task<OrganizationResult<OrganizationDetails>> SetOrganizationActive(OrganizationScope? scope, SetOrganizationActive request, CancellationToken ct = default)
        => ChangeOrganization(scope, request.Id, request.ExpectedRowVersion, request.IsActive ? OrganizationMutation.Activate : OrganizationMutation.Deactivate, null, ct);
    public Task<OrganizationResult<SiteDetails>> RenameSite(OrganizationScope? scope, RenameSite request, CancellationToken ct = default)
        => ChangeSite(scope, request.OrganizationId, request.Id, request.ExpectedRowVersion, OrganizationMutation.Rename, request.Name, ct);
    public Task<OrganizationResult<SiteDetails>> SetSiteActive(OrganizationScope? scope, SetSiteActive request, CancellationToken ct = default)
        => ChangeSite(scope, request.OrganizationId, request.Id, request.ExpectedRowVersion, request.IsActive ? OrganizationMutation.Activate : OrganizationMutation.Deactivate, null, ct);

    private async Task<OrganizationResult<OrganizationDetails>> ChangeOrganization(OrganizationScope? scope, long id, string? expected, OrganizationMutation mutation, string? name, CancellationToken ct)
    {
        var permission = mutation == OrganizationMutation.Rename ? OrganizationPermissions.OrganizationsUpdate : OrganizationPermissions.OrganizationsSetActive;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, id)) return OrganizationResult<OrganizationDetails>.Failure("Organization.NotFound");
        var version = Version(expected);
        if (version is null) return OrganizationResult<OrganizationDetails>.Failure(OrganizationAccessErrors.ConcurrencyConflict);
        return await store.UpdateOrganization(id, version, mutation, name, Access(auth.Actor!, scope!, permission), ct);
    }
    private async Task<OrganizationResult<SiteDetails>> ChangeSite(OrganizationScope? scope, long orgId, long id, string? expected, OrganizationMutation mutation, string? name, CancellationToken ct)
    {
        var permission = mutation == OrganizationMutation.Rename ? OrganizationPermissions.SitesUpdate : OrganizationPermissions.SitesSetActive;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<SiteDetails>.Failure(auth.Error);
        if (!SiteMatches(scope, orgId, id)) return OrganizationResult<SiteDetails>.Failure("Site.NotFound");
        var version = Version(expected);
        if (version is null) return OrganizationResult<SiteDetails>.Failure(OrganizationAccessErrors.ConcurrencyConflict);
        return await store.UpdateSite(orgId, id, version, mutation, name, Access(auth.Actor!, scope!, permission), ct);
    }
}
