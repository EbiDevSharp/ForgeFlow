using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Audit.Slices;

/// <summary>
/// Shared authorization, validation, and CSV formatting support for audit vertical slices.
/// </summary>
public sealed class AuditSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    public const int MaxExportRows = 2000;

    public IUserOperationRecorder? OperationRecorder => operationRecorder;
    public IOperationContextAccessor? OpContext => opContext;

    public async Task<(bool Allowed, TrustedActor? Actor, string? Error)> AuthorizePermissionAsync(
        OrganizationScope scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null || permissions is null)
            return (false, null, AuditErrors.ActorRequired);

        TrustedActor? actor;
        try { actor = await actors.ResolveAsync(ct); }
        catch { return (false, null, AuditErrors.PersistenceUnavailable); }

        if (actor is null || actor.ActorId <= 0)
            return (false, null, AuditErrors.ActorRequired);

        bool allowed;
        try { allowed = (await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed; }
        catch { return (false, actor, AuditErrors.PersistenceUnavailable); }

        return allowed ? (true, actor, null) : (false, actor, AuditErrors.AccessDenied);
    }

    public static bool TryNormalizeQuery(
        OrganizationScope scope,
        AuditListQuery query,
        out AuditListQuery? normalized,
        bool allowExportPageSize = false)
    {
        normalized = null;
        var maxPageSize = allowExportPageSize ? MaxExportRows : 100;
        if (!Enum.IsDefined(query.Category)
            || !Enum.IsDefined(query.OutcomeFilter)
            || query.Page is < 1 or > 100000
            || query.PageSize < 1 || query.PageSize > maxPageSize
            || (query.ActorUserId.HasValue && query.ActorUserId.Value <= 0)
            || (query.SiteId.HasValue && query.SiteId.Value <= 0)
            || (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate.Value > query.ToDate.Value))
            return false;

        if (scope is OrganizationScope.Site siteScope && query.SiteId.HasValue && query.SiteId.Value != siteScope.SiteId)
            return false;

        var eventCode = string.IsNullOrWhiteSpace(query.EventCode) ? null : query.EventCode.Trim();
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if ((eventCode is not null && eventCode.Length > 64)
            || (search is not null && search.Length > 100))
            return false;

        normalized = query with { EventCode = eventCode, Search = search };
        return true;
    }

    public static string FormatOutcomeForCsv(AuditEntrySummary entry)
    {
        if (entry.Source == AuditSourceKind.Business) return "Succeeded";
        return entry.Outcome switch
        {
            0 => "Succeeded",
            1 => "Denied",
            2 => "Failed",
            _ => "Succeeded"
        };
    }
}
