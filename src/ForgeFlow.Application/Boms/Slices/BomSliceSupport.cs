using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Boms.Slices;

public sealed class BomSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAsync(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, BomErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, BomErrors.ActorRequired);
        if (scope is null) return (actor, BomErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (actor, BomErrors.AccessDenied);
        return (actor, null);
    }

    public Task<OrganizationResult<T>> RecordRejected<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long organizationId,
        string entityType,
        string? entityRef,
        string action)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result, operationRecorder, opContext, UserOperationCatalog.BomActionRejected,
            actor, scope, organizationId, null, entityType, entityRef, new { Action = action });

    public OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await AuthorizeAsync(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    public bool TryScopeOrganizationId(OrganizationScope? scope, long requestedOrgId, bool allowSite, out long orgId)
    {
        orgId = scope switch
        {
            OrganizationScope.Organization org => org.OrganizationId,
            OrganizationScope.Site site when allowSite => site.OrganizationId,
            _ => 0
        };
        return orgId > 0 && orgId == requestedOrgId;
    }

    public byte[]? DecodeVersion(string? raw)
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

    public BomListQuery Bound(BomListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    public ContextDimensionListQuery Bound(ContextDimensionListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    public async Task<OrganizationResult<T>> ExecuteStoreAsync<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (BomRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (BomStoreUnavailableException) { return OrganizationResult<T>.Failure(BomErrors.PersistenceUnavailable); }
    }
}
