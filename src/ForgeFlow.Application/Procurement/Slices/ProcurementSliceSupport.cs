using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Procurement.Slices;

internal sealed class ProcurementSliceSupport(
    ITrustedActorAccessor? actors,
    IOrganizationPermissionEvaluator? permissions,
    IUserOperationRecorder? operationRecorder,
    IOperationContextAccessor? opContext,
    TimeProvider? clock)
{
    public DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, ProcurementErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, ProcurementErrors.ActorRequired);
        if (scope is null) return (null, ProcurementErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, ProcurementErrors.AccessDenied);
        return (actor, null);
    }

    public OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    public static bool TryResolveSiteScope(OrganizationScope? scope, long requestedOrgId, long? requestedSiteId, out long? forcedSiteId)
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

    public static bool MatchesOrganizationOnly(OrganizationScope? scope, long organizationId)
        => scope is OrganizationScope.Organization org && org.OrganizationId == organizationId && organizationId > 0;

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

    public static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (ProcurementRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code == UnitErrors.ConversionQuantityInvalid ? ProcurementErrors.QuantityInvalid : ProcurementErrors.LineUnitInvalid, ex.Parameters); }
        catch (ProcurementStoreUnavailableException) { return OrganizationResult<T>.Failure(ProcurementErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<T>> AuditFailureIfRejected<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long? orgId,
        long? siteId,
        string targetType,
        string? targetRef,
        string actionName)
    {
        if (result.Succeeded || result.Error is null)
            return result;

        await OperationAuditHelper.RecordAsync(
            operationRecorder,
            opContext,
            UserOperationCatalog.ProcurementActionRejected,
            OperationAuditHelper.MapFailureOutcome(result.Error.Code),
            actor,
            scope,
            orgId,
            siteId,
            targetType,
            targetRef,
            result.Error.Code,
            new { Action = actionName });
        return result;
    }
}
