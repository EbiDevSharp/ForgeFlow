using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Routings;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.ProductionOrders.Slices;

public sealed class ProductionOrderSliceSupport(
    IBomOperationsStore bomStore,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    IRoutingOperationsStore? routingStore = null)
{
    private readonly BomUseCases bomEngine = new(bomStore, actors, permissions);

    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAsync(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, ProductionOrderErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, ProductionOrderErrors.ActorRequired);
        if (scope is null) return (null, ProductionOrderErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, ProductionOrderErrors.AccessDenied);
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

    public ProductionOrderListQuery Bound(ProductionOrderListQuery query, long? forcedSiteId) => query with
    {
        SiteId = forcedSiteId,
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    public async Task<OrganizationResult<T>> ExecuteStoreAsync<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (ProductionOrderRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (BomRuleException ex) { return OrganizationResult<T>.Failure(MapBomError(ex.Code), ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(MapUnitError(ex.Code), ex.Parameters); }
        catch (BomStoreUnavailableException) { return OrganizationResult<T>.Failure(ProductionOrderErrors.PersistenceUnavailable); }
        catch (RoutingStoreUnavailableException) { return OrganizationResult<T>.Failure(ProductionOrderErrors.PersistenceUnavailable); }
        catch (ProductionOrderStoreUnavailableException) { return OrganizationResult<T>.Failure(ProductionOrderErrors.PersistenceUnavailable); }
    }

    public static string MapBomError(string code) => code switch
    {
        BomErrors.NoActiveBomFound => ProductionOrderErrors.NoActiveBomFound,
        BomErrors.AmbiguousResolution => ProductionOrderErrors.AmbiguousResolution,
        BomErrors.PrimaryItemInvalid or BomErrors.InputItemInvalid or BomErrors.OutputItemInvalid => ProductionOrderErrors.ItemInvalid,
        BomErrors.PrimaryItemNotManufacturable => ProductionOrderErrors.ItemNotManufacturable,
        BomErrors.UnitInvalid => ProductionOrderErrors.UnitInvalid,
        BomErrors.QuantityInvalid or BomErrors.BaseOutputQuantityInvalid => ProductionOrderErrors.QuantityInvalid,
        _ => ProductionOrderErrors.NoActiveBomFound
    };

    public static string MapUnitError(string code) => code switch
    {
        UnitErrors.ConversionQuantityInvalid => ProductionOrderErrors.QuantityInvalid,
        _ => ProductionOrderErrors.UnitInvalid
    };

    public Task RecordIfRejectedAsync<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long organizationId,
        long? siteId,
        long orderId,
        string action)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result,
            operationRecorder,
            opContext,
            UserOperationCatalog.ProductionOrderActionRejected,
            actor,
            scope,
            organizationId,
            siteId,
            "ProductionOrder",
            orderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new { Action = action });

    public Task RecordIfRejectedAsync<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long organizationId,
        long? siteId,
        string entityRef,
        string action)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result,
            operationRecorder,
            opContext,
            UserOperationCatalog.ProductionOrderActionRejected,
            actor,
            scope,
            organizationId,
            siteId,
            "ProductionOrder",
            entityRef,
            new { Action = action });

    public async Task<OrganizationResult<ResolvedBomSnapshotWrite>> BuildSnapshotAsync(
        long organizationId,
        string siteCode,
        long itemId,
        DateOnly plannedStartDate,
        decimal requestedQuantity,
        long? requestedUnitId,
        IReadOnlyDictionary<string, string>? contextValues,
        CancellationToken ct)
    {
        var mergedContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (contextValues is not null)
        {
            foreach (var (k, v) in contextValues)
            {
                if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                    mergedContext[k.Trim().ToUpperInvariant()] = v.Trim();
            }
        }
        if (!mergedContext.ContainsKey("PLANT") && !string.IsNullOrWhiteSpace(siteCode))
            mergedContext["PLANT"] = siteCode.Trim();

        var core = await bomEngine.ResolveAndExplodeCoreAsync(
            organizationId, itemId, plannedStartDate, requestedQuantity, requestedUnitId, mergedContext, ct);
        if (!core.Succeeded)
            return OrganizationResult<ResolvedBomSnapshotWrite>.Failure(MapBomError(core.Error!.Code), core.Error.Parameters);

        var (res, exp) = core.Value!;
        return OrganizationResult<ResolvedBomSnapshotWrite>.Success(new ResolvedBomSnapshotWrite(
            res.BomId,
            res.BomCode,
            res.BomVersionId,
            res.VersionNumber,
            res.BaseOutputQuantity,
            res.BaseOutputUnitId,
            exp.RequestedUnitId,
            exp.ConsumedInputs,
            exp.ProducedOutputs,
            mergedContext));
    }

    public async Task<OrganizationResult<ResolvedRoutingSnapshotWrite?>> BuildRoutingSnapshotAsync(
        long organizationId,
        long siteId,
        long itemId,
        decimal requestedQuantity,
        long? requestedRoutingId,
        CancellationToken ct)
    {
        if (routingStore is null)
            return OrganizationResult<ResolvedRoutingSnapshotWrite?>.Success(null);

        var resolved = await routingStore.ResolveRoutingForProductionOrder(organizationId, siteId, itemId, requestedRoutingId, ct);
        if (resolved is null)
        {
            if (requestedRoutingId.HasValue)
                return OrganizationResult<ResolvedRoutingSnapshotWrite?>.Failure(ProductionOrderErrors.RoutingInvalid);
            return OrganizationResult<ResolvedRoutingSnapshotWrite?>.Success(null);
        }

        if (resolved.Operations.Count == 0)
            return OrganizationResult<ResolvedRoutingSnapshotWrite?>.Failure(ProductionOrderErrors.RoutingInvalid);

        var ops = resolved.Operations
            .OrderBy(o => o.Sequence)
            .Select(o => new ResolvedRoutingOperationSnapshotWrite(
                o.Sequence,
                o.OperationCode,
                o.OperationName,
                o.WorkCenterId,
                o.SetupMinutes,
                decimal.Round(o.RunMinutesPerUnit * requestedQuantity, 2, MidpointRounding.AwayFromZero),
                o.QueueMinutes,
                o.WaitMinutes,
                o.RequiresQualityCheck,
                o.Instructions))
            .ToArray();

        return OrganizationResult<ResolvedRoutingSnapshotWrite?>.Success(
            new ResolvedRoutingSnapshotWrite(resolved.Summary.Id, resolved.Summary.Code, ops));
    }
}
