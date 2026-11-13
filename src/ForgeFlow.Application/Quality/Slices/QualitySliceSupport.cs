using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Quality.Slices;

public sealed class QualitySliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    public DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAsync(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, QualityErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, QualityErrors.ActorRequired);
        if (scope is null) return (null, QualityErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, QualityErrors.AccessDenied);
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

    public bool TryResolveScope(OrganizationScope? scope, long requestedOrgId, long? requestedSiteId, out long? forcedSiteId)
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

    public async Task<OrganizationResult<T>> ExecuteStoreAsync<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(MapInventoryError(ex.Code), ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(MapUnitError(ex.Code), ex.Parameters); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<T>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public static string MapInventoryError(string code) => code switch
    {
        InventoryErrors.InsufficientStock => QualityErrors.InsufficientAvailableStock,
        InventoryErrors.WarehouseNotFound or InventoryErrors.WarehouseInactive => QualityErrors.WarehouseInvalid,
        InventoryErrors.SiteInactive => QualityErrors.SiteInactive,
        InventoryErrors.ParentOrganizationInactive => QualityErrors.ParentOrganizationInactive,
        InventoryErrors.LotCodeInvalid => QualityErrors.LotCodeInvalid,
        _ => QualityErrors.QuantityInvalid
    };

    public static string MapUnitError(string code) => code switch
    {
        UnitErrors.ConversionQuantityInvalid => QualityErrors.QuantityInvalid,
        _ => QualityErrors.UnitInvalid
    };

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
            UserOperationCatalog.QualityActionRejected,
            OperationAuditHelper.MapFailureOutcome(result.Error.Code),
            actor,
            scope,
            orgId,
            siteId,
            targetType,
            targetRef,
            result.Error.Code,
            new { Action = actionName, Error = result.Error.Code });

        return result;
    }
}
