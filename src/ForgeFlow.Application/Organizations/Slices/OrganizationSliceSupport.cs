using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;

namespace ForgeFlow.Application.Organizations.Slices;

/// <summary>
/// Shared authorization, scoping, version decoding, and execution helpers for organization and site vertical slices.
/// </summary>
public sealed class OrganizationSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    public IUserOperationRecorder? OperationRecorder => operationRecorder;
    public IOperationContextAccessor? OpContext => opContext;

    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAsync(OrganizationScope? scope, string permission, CancellationToken ct)
    {
        if (actors is null) return (null, OrganizationAccessErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, OrganizationAccessErrors.ActorRequired);
        if (scope is null) return (actor, OrganizationAccessErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (actor, OrganizationAccessErrors.AccessDenied);
        return (actor, null);
    }

    public OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await AuthorizeAsync(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    public static bool OrganizationMatches(OrganizationScope? scope, long id) => scope is OrganizationScope.Organization org && org.OrganizationId == id;

    public static bool SiteMatches(OrganizationScope? scope, long orgId, long? siteId = null) => scope switch
    {
        OrganizationScope.Organization org => org.OrganizationId == orgId,
        OrganizationScope.Site site => site.OrganizationId == orgId && (siteId is null || site.SiteId == siteId),
        _ => false
    };

    public static byte[]? Version(string? raw)
    {
        if (raw is null) return null;
        try { var bytes = Convert.FromBase64String(raw); return bytes.Length == 8 ? bytes : null; }
        catch (FormatException) { return null; }
    }

    public static OrganizationListQuery Bound(OrganizationListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    public static async Task<OrganizationResult<T>> Read<T>(Func<Task<OrganizationResult<T>>> query)
    {
        try { return await query(); }
        catch (OrganizationStoreUnavailableException) { return OrganizationResult<T>.Failure(OrganizationAccessErrors.PersistenceUnavailable); }
    }
}
