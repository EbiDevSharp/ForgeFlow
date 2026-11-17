using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Inventory.Slices;

public sealed class InventorySliceSupport(
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
        var result = await AuthorizeAnyAsync(scope, ct, permission);
        return (result.Actor, result.Error);
    }

    public async ValueTask<(TrustedActor? Actor, string? Permission, string? Error)> AuthorizeAnyAsync(
        OrganizationScope? scope,
        CancellationToken ct,
        params string[] allowedPermissions)
    {
        if (actors is null) return (null, null, InventoryErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, null, InventoryErrors.ActorRequired);
        if (scope is null) return (null, null, InventoryErrors.ScopeRequired);
        if (permissions is null) return (null, null, InventoryErrors.AccessDenied);

        foreach (var perm in allowedPermissions)
        {
            if ((await permissions.EvaluateAsync(actor, scope, perm, ct)).IsAllowed)
                return (actor, perm, null);
        }
        return (null, null, InventoryErrors.AccessDenied);
    }

    public OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await AuthorizeAnyAsync(scope, ct, permission);
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
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(MapUnitError(ex.Code), ex.Parameters); }
        catch (InventoryStoreUnavailableException) { return OrganizationResult<T>.Failure(InventoryErrors.PersistenceUnavailable); }
    }

    public static string MapUnitError(string code) => code switch
    {
        UnitErrors.ConversionQuantityInvalid => InventoryErrors.QuantityInvalid,
        _ => InventoryErrors.UnitInvalid
    };

    public Task RecordIfConsequentialRejectionAsync<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long organizationId,
        long? siteId,
        string entityType,
        string? entityRef,
        object metadata)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result,
            operationRecorder,
            opContext,
            UserOperationCatalog.InventoryActionRejected,
            actor,
            scope,
            organizationId,
            siteId,
            entityType,
            entityRef,
            metadata);
}
