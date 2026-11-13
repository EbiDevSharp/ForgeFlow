using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Quality.Slices;

/// <summary>
/// Shared authorization, scoping, validation, and execution support for quality specification slices.
/// </summary>
public sealed class QualitySpecSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    public IUserOperationRecorder? OperationRecorder => operationRecorder;
    public IOperationContextAccessor? OpContext => opContext;

    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAnyAsync(
        OrganizationScope? scope,
        CancellationToken ct,
        params string[] allowedPermissions)
    {
        if (actors is null) return (null, QualityErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, QualityErrors.ActorRequired);
        if (scope is null) return (null, QualityErrors.ScopeRequired);
        if (permissions is null) return (null, QualityErrors.AccessDenied);
        foreach (var perm in allowedPermissions)
        {
            if ((await permissions.EvaluateAsync(actor, scope, perm, ct)).IsAllowed)
                return (actor, null);
        }
        return (null, QualityErrors.AccessDenied);
    }

    public OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
        (await AuthorizeAnyAsync(scope, ct, permission)).Error is null);

    public static bool TryResolveOrgScope(OrganizationScope? scope, long requestedOrgId)
        => requestedOrgId > 0 && scope switch
        {
            OrganizationScope.Organization org => org.OrganizationId == requestedOrgId,
            OrganizationScope.Site site => site.OrganizationId == requestedOrgId,
            _ => false
        };

    public static byte[]? DecodeVersion(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try { return Convert.FromBase64String(token); }
        catch (FormatException) { return null; }
    }

    public static QualityParameterListQuery Bound(QualityParameterListQuery query) => query with
    {
        Page = Math.Max(1, query.Page),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
    };

    public static QualitySpecListQuery Bound(QualitySpecListQuery query) => query with
    {
        Page = Math.Max(1, query.Page),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
    };

    public static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters.ToDictionary(kv => kv.Key, kv => kv.Value)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<T>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public Task RecordRejected<T>(OrganizationResult<T> res, TrustedActor? actor, OrganizationScope? scope, long orgId, string entity, string? targetKey, object metadata)
        => OperationAuditHelper.RecordIfRejectedAsync(res, operationRecorder, opContext, UserOperationCatalog.QualityActionRejected, actor, scope, orgId, null, entity, targetKey, metadata);

    public static QualitySpecLineInputData[] ValidateSpecLines(IReadOnlyList<QualitySpecLineInput>? lines)
    {
        if (lines is null || lines.Count == 0)
            throw new QualityRuleException(QualityErrors.SpecLinesRequired);
        var seen = new HashSet<long>();
        var list = new List<QualitySpecLineInputData>(lines.Count);
        foreach (var l in lines)
        {
            if (l.QualityParameterId <= 0)
                throw new QualityRuleException(QualityErrors.ParameterNotFound);
            if (!seen.Add(l.QualityParameterId))
                throw new QualityRuleException(QualityErrors.ParameterCodeDuplicate);
            if (l.MinValue.HasValue && l.MaxValue.HasValue && l.MaxValue.Value < l.MinValue.Value)
                throw new QualityRuleException(QualityErrors.SpecRangeInvalid);

            list.Add(new QualitySpecLineInputData(
                l.QualityParameterId,
                l.MinValue,
                l.MaxValue,
                l.TargetValue,
                QualityText.OptionalSpecOrValue(l.ExpectedTextSpec),
                l.IsMandatory,
                QualityText.OptionalNote(l.Notes, QualityText.MaxLineNoteLength)));
        }
        return list.ToArray();
    }
}
