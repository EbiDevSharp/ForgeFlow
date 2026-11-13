using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Reporting;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Reporting.Slices;

/// <summary>
/// Shared infrastructure, scope resolution, authorization, and conversion helpers for reporting slices.
/// </summary>
public sealed class ReportingSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    public const int Scale = 6;

    public IUserOperationRecorder? OperationRecorder => operationRecorder;
    public IOperationContextAccessor? OpContext => opContext;

    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAsync(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, ReportingErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, ReportingErrors.ActorRequired);
        if (scope is null) return (null, ReportingErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, ReportingErrors.AccessDenied);
        return (actor, null);
    }

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

    public static string FormatCsvDecimal(decimal value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    public static string? NormalizeSearch(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim();

    public static Dictionary<long, IReadOnlyList<UnitConversionEdge>> BuildEdgeCache(IReadOnlyList<ReportConversionEdgeRow> conversions)
    {
        var global = conversions
            .Where(c => !c.ItemId.HasValue)
            .Select(c => new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, IsItemSpecific: false))
            .ToArray();

        var cache = new Dictionary<long, IReadOnlyList<UnitConversionEdge>> { [0] = global };
        foreach (var group in conversions.Where(c => c.ItemId.HasValue).GroupBy(c => c.ItemId!.Value))
        {
            var specific = group
                .Select(c => new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, IsItemSpecific: true))
                .ToArray();
            cache[group.Key] = [.. specific, .. global];
        }
        return cache;
    }

    public static decimal ConvertToBase(
        decimal quantity,
        long fromUnitId,
        long itemId,
        long baseUnitId,
        Dictionary<long, IReadOnlyList<UnitConversionEdge>> edgeCache)
    {
        if (quantity == 0m) return 0m;
        if (fromUnitId == baseUnitId)
            return decimal.Round(quantity, Scale, MidpointRounding.AwayFromZero);

        var edges = edgeCache.TryGetValue(itemId, out var specific) ? specific : edgeCache[0];
        var res = UnitConversionGraph.ConvertQuantity(Math.Abs(quantity), fromUnitId, baseUnitId, edges);
        var signed = quantity < 0m ? -res.ConvertedQuantity : res.ConvertedQuantity;
        return decimal.Round(signed, Scale, MidpointRounding.AwayFromZero);
    }
}
