using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Routings;

namespace ForgeFlow.Application.Routings.Slices;

/// <summary>
/// Shared authorization, scoping, rowversion decoding, and store exception handling support for routing vertical slices.
/// </summary>
public sealed class RoutingSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    public IUserOperationRecorder? OperationRecorder => operationRecorder;
    public IOperationContextAccessor? OpContext => opContext;

    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAsync(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, RoutingErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, RoutingErrors.ActorRequired);
        if (scope is null) return (null, RoutingErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, RoutingErrors.AccessDenied);
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

    public static bool TryResolveScope(OrganizationScope? scope, long requestedOrgId, long? requestedSiteId, out long? forcedSiteId)
    {
        forcedSiteId = null;
        if (requestedOrgId <= 0) return false;
        switch (scope)
        {
            case OrganizationScope.Organization org when org.OrganizationId == requestedOrgId:
                forcedSiteId = requestedSiteId;
                return true;
            case OrganizationScope.Site site when site.OrganizationId == requestedOrgId:
                if (requestedSiteId.HasValue && requestedSiteId.Value != site.SiteId)
                    return false;
                forcedSiteId = site.SiteId;
                return true;
            default:
                return false;
        }
    }

    public static byte[]? DecodeVersion(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            var bytes = Convert.FromBase64String(raw);
            return bytes.Length == 8 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static WorkCenterListQuery Bound(WorkCenterListQuery query, long? forcedSiteId) => query with
    {
        SiteId = forcedSiteId,
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    public static RoutingListQuery Bound(RoutingListQuery query, long? forcedSiteId) => query with
    {
        SiteId = forcedSiteId,
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    public static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (RoutingRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (RoutingStoreUnavailableException) { return OrganizationResult<T>.Failure(RoutingErrors.PersistenceUnavailable); }
    }

    public Task RecordRejectedAsync<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long organizationId,
        long? siteId,
        string entityType,
        string entityRef,
        string action)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result,
            operationRecorder,
            opContext,
            UserOperationCatalog.RoutingActionRejected,
            actor,
            scope,
            organizationId,
            siteId,
            entityType,
            entityRef,
            new { Action = action });
}
