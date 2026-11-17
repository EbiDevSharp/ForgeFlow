using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.BusinessPartners;

namespace ForgeFlow.Application.BusinessPartners.Slices;

public sealed class BusinessPartnerSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public TimeProvider Clock => _clock;

    public static bool MatchesOrg(OrganizationScope? scope, long orgId) => scope switch
    {
        OrganizationScope.Organization o => o.OrganizationId == orgId,
        OrganizationScope.Site s => s.OrganizationId == orgId,
        _ => false
    };

    public static bool MatchesOrganizationOnly(OrganizationScope? scope, long orgId) =>
        scope is OrganizationScope.Organization o && o.OrganizationId == orgId;

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

    public OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    public async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (scope is null) return (null, BusinessPartnerErrors.AccessDenied);
        if (actors is null) return (null, BusinessPartnerErrors.AccessDenied);

        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, BusinessPartnerErrors.AccessDenied);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, BusinessPartnerErrors.AccessDenied);

        return (actor, null);
    }

    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAny(
        OrganizationScope? scope,
        IReadOnlyList<string> requiredPermissions,
        CancellationToken ct)
    {
        if (scope is null || actors is null) return (null, BusinessPartnerErrors.AccessDenied);

        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, BusinessPartnerErrors.AccessDenied);
        if (permissions is null) return (null, BusinessPartnerErrors.AccessDenied);

        foreach (var p in requiredPermissions)
        {
            if ((await permissions.EvaluateAsync(actor, scope, p, ct)).IsAllowed)
                return (actor, null);
        }

        return (null, BusinessPartnerErrors.AccessDenied);
    }

    public async Task<OrganizationResult<T>> AuditFailureIfRejected<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long orgId,
        string targetType,
        string? targetRef,
        string actionName)
    {
        if (result.Succeeded || result.Error is null)
            return result;

        await OperationAuditHelper.RecordAsync(
            operationRecorder,
            opContext,
            UserOperationCatalog.BusinessPartnerActionRejected,
            OperationAuditHelper.MapFailureOutcome(result.Error.Code),
            actor,
            scope,
            orgId,
            null,
            targetType,
            targetRef,
            result.Error.Code,
            new { Action = actionName });
        return result;
    }
}
