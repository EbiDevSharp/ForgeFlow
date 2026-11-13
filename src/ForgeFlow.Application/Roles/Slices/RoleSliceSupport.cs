using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;

namespace ForgeFlow.Application.Roles.Slices;

/// <summary>
/// Shared authorization, anti-escalation validation, and error translation support for role management slices.
/// </summary>
public sealed class RoleSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    public IUserOperationRecorder? OperationRecorder => operationRecorder;
    public IOperationContextAccessor? OpContext => opContext;

    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAsync(OrganizationScope? scope, string permission, CancellationToken ct)
    {
        if (actors is null) return (null, RolePermissionErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, RolePermissionErrors.ActorRequired);
        if (scope is null) return (actor, RolePermissionErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (actor, RolePermissionErrors.AccessDenied);
        return (actor, null);
    }

    public Task<OrganizationResult<T>> RecordRejectedAsync<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long organizationId,
        long? siteId,
        string entityType,
        string? entityRef,
        string action)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result, operationRecorder, opContext, UserOperationCatalog.RoleActionRejected,
            actor, scope, organizationId, siteId, entityType, entityRef, new { Action = action });

    public OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await AuthorizeAsync(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    public static bool OrganizationMatches(OrganizationScope? scope, long organizationId)
        => scope is OrganizationScope.Organization org && org.OrganizationId == organizationId && organizationId > 0;

    public static byte[]? DecodeVersion(string? raw)
    {
        if (raw is null) return null;
        try { var bytes = Convert.FromBase64String(raw); return bytes.Length == 8 ? bytes : null; }
        catch (FormatException) { return null; }
    }

    public static RoleListQuery Bound(RoleListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    public static async Task<OrganizationResult<T>> Execute<T>(Func<Task<OrganizationResult<T>>> operation)
    {
        try { return await operation(); }
        catch (RoleStoreUnavailableException) { return OrganizationResult<T>.Failure(RolePermissionErrors.PersistenceUnavailable); }
    }

    public static OrganizationResult<T> FailFromRule<T>(RoleRuleException ex)
        => OrganizationResult<T>.Failure(ex.Code, ex.Parameters);

    public async Task<bool> CallerHoldsAllAsync(
        TrustedActor actor,
        OrganizationScope scope,
        IEnumerable<string> requiredCodes,
        CancellationToken ct)
    {
        foreach (var code in requiredCodes)
        {
            if (!(await permissions!.EvaluateAsync(actor, scope, code, ct)).IsAllowed)
                return false;
        }
        return true;
    }
}
