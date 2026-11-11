using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Quality;

public sealed class QualitySpecUseCases(
    IQualitySpecOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    private async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAny(
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

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
        (await AuthorizeAny(scope, ct, permission)).Error is null);

    private static bool TryResolveOrgScope(OrganizationScope? scope, long requestedOrgId)
        => requestedOrgId > 0 && scope switch
        {
            OrganizationScope.Organization org => org.OrganizationId == requestedOrgId,
            OrganizationScope.Site site => site.OrganizationId == requestedOrgId,
            _ => false
        };

    private static byte[]? DecodeVersion(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try { return Convert.FromBase64String(token); }
        catch (FormatException) { return null; }
    }

    private static QualityParameterListQuery Bound(QualityParameterListQuery query) => query with
    {
        Page = Math.Max(1, query.Page),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
    };

    private static QualitySpecListQuery Bound(QualitySpecListQuery query) => query with
    {
        Page = Math.Max(1, query.Page),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
    };

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters.ToDictionary(kv => kv.Key, kv => kv.Value)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<T>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    private Task RecordRejected<T>(OrganizationResult<T> res, TrustedActor? actor, OrganizationScope? scope, long orgId, string entity, string? targetKey, object metadata)
        => OperationAuditHelper.RecordIfRejectedAsync(res, operationRecorder, opContext, UserOperationCatalog.QualityActionRejected, actor, scope, orgId, null, entity, targetKey, metadata);

    public async Task<OrganizationResult<OrganizationPage<QualityParameterSummary>>> ListParameters(
        OrganizationScope? scope, long organizationId, QualityParameterListQuery query, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<QualityParameterSummary>>.Failure(auth.Error);
        if (!TryResolveOrgScope(scope, organizationId)) return OrganizationResult<OrganizationPage<QualityParameterSummary>>.Failure(QualityErrors.AccessDenied);
        try { return OrganizationResult<OrganizationPage<QualityParameterSummary>>.Success(await store.ListParameters(organizationId, Bound(query), ct)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<OrganizationPage<QualityParameterSummary>>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<QualityParameterSummary>> CreateParameter(
        OrganizationScope? scope, CreateQualityParameterRequest request, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualityParameterSummary> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualityParameterSummary>.Failure(auth.Error);
        else if (!TryResolveOrgScope(scope, request.OrganizationId))
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.AccessDenied);
        else
            res = await ExecuteStore(() => store.CreateParameter(request, Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualityParameter", request.Code, new { Action = "CreateParameter" });
        return res;
    }

    public async Task<OrganizationResult<QualityParameterSummary>> UpdateParameter(
        OrganizationScope? scope, UpdateQualityParameterRequest request, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualityParameterSummary> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualityParameterSummary>.Failure(auth.Error);
        else if (!TryResolveOrgScope(scope, request.OrganizationId) || request.Id <= 0)
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ParameterNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.UpdateParameter(request.OrganizationId, request.Id, version, request, Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualityParameter", request.Id.ToString(CultureInfo.InvariantCulture), new { Action = "UpdateParameter" });
        return res;
    }

    public async Task<OrganizationResult<QualityParameterSummary>> SetParameterActive(
        OrganizationScope? scope, SetQualityParameterActiveRequest request, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualityParameterSummary> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualityParameterSummary>.Failure(auth.Error);
        else if (!TryResolveOrgScope(scope, request.OrganizationId) || request.Id <= 0)
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ParameterNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.SetParameterActive(request.OrganizationId, request.Id, version, request.IsActive, Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualityParameter", request.Id.ToString(CultureInfo.InvariantCulture), new { Action = "SetParameterActive" });
        return res;
    }

    public async Task<OrganizationResult<OrganizationPage<QualitySpecSummary>>> ListSpecs(
        OrganizationScope? scope, long organizationId, QualitySpecListQuery query, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<QualitySpecSummary>>.Failure(auth.Error);
        if (!TryResolveOrgScope(scope, organizationId)) return OrganizationResult<OrganizationPage<QualitySpecSummary>>.Failure(QualityErrors.AccessDenied);
        try { return OrganizationResult<OrganizationPage<QualitySpecSummary>>.Success(await store.ListSpecs(organizationId, Bound(query), ct)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<OrganizationPage<QualitySpecSummary>>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<QualitySpecDetails>> GetSpec(
        OrganizationScope? scope, long organizationId, long id, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead);
        if (auth.Error is not null) return OrganizationResult<QualitySpecDetails>.Failure(auth.Error);
        if (!TryResolveOrgScope(scope, organizationId) || id <= 0) return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound);
        try
        {
            var spec = await store.GetSpec(organizationId, id, ct);
            return spec is null ? OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound) : OrganizationResult<QualitySpecDetails>.Success(spec);
        }
        catch (QualityStoreUnavailableException) { return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<QualitySpecDetails?>> ResolveApplicableSpec(
        OrganizationScope? scope, long organizationId, long itemId, QualitySpecCheckpoint checkpoint, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead);
        if (auth.Error is not null) return OrganizationResult<QualitySpecDetails?>.Failure(auth.Error);
        if (!TryResolveOrgScope(scope, organizationId) || itemId <= 0) return OrganizationResult<QualitySpecDetails?>.Failure(QualityErrors.ItemInvalid);
        try { return OrganizationResult<QualitySpecDetails?>.Success(await store.ResolveApplicableSpec(organizationId, itemId, checkpoint, ct)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<QualitySpecDetails?>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<QualitySpecDetails>> CreateSpec(
        OrganizationScope? scope, CreateQualitySpecRequest request, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualitySpecDetails> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualitySpecDetails>.Failure(auth.Error);
        else if (!TryResolveOrgScope(scope, request.OrganizationId))
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.AccessDenied);
        else
            res = await ExecuteStore(() =>
            {
                var resolved = ValidateSpecLines(request.Lines);
                return store.CreateSpec(request, resolved, Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct);
            });

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualitySpec", request.Code, new { Action = "CreateSpec" });
        return res;
    }

    public async Task<OrganizationResult<QualitySpecDetails>> UpdateSpec(
        OrganizationScope? scope, UpdateQualitySpecRequest request, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualitySpecDetails> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualitySpecDetails>.Failure(auth.Error);
        else if (!TryResolveOrgScope(scope, request.OrganizationId) || request.Id <= 0)
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() =>
            {
                var name = QualityText.SpecName(request.Name);
                var notes = QualityText.OptionalNote(request.Notes);
                var resolved = ValidateSpecLines(request.Lines);
                return store.UpdateSpec(request.OrganizationId, request.Id, version, name, request.Checkpoint, notes, resolved, Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct);
            });

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualitySpec", request.Id.ToString(CultureInfo.InvariantCulture), new { Action = "UpdateSpec" });
        return res;
    }

    public async Task<OrganizationResult<QualitySpecDetails>> SetSpecActive(
        OrganizationScope? scope, SetQualitySpecActiveRequest request, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualitySpecDetails> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualitySpecDetails>.Failure(auth.Error);
        else if (!TryResolveOrgScope(scope, request.OrganizationId) || request.Id <= 0)
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.SetSpecActive(request.OrganizationId, request.Id, version, request.IsActive, Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualitySpec", request.Id.ToString(CultureInfo.InvariantCulture), new { Action = "SetSpecActive" });
        return res;
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> ApplySpecToInspection(
        OrganizationScope? scope, long organizationId, long inspectionId, string? expectedRowVersion, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        else if (!TryResolveOrgScope(scope, organizationId) || inspectionId <= 0)
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        else if (DecodeVersion(expectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.ApplySpecToInspection(organizationId, inspectionId, version, Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await RecordRejected(res, auth.Actor, scope, organizationId, "QualityInspection", inspectionId.ToString(CultureInfo.InvariantCulture), new { Action = "ApplySpecToInspection" });
        return res;
    }

    public async Task<OrganizationResult<LotCertificateSummary>> GetLotCertificate(
        OrganizationScope? scope, long organizationId, long itemId, string? lotCode, CancellationToken ct = default)
    {
        var auth = await AuthorizeAny(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead);
        if (auth.Error is not null) return OrganizationResult<LotCertificateSummary>.Failure(auth.Error);
        if (!TryResolveOrgScope(scope, organizationId)) return OrganizationResult<LotCertificateSummary>.Failure(QualityErrors.AccessDenied);
        try
        {
            var normalizedLot = QualityText.RequiredLotCode(lotCode);
            if (itemId <= 0) return OrganizationResult<LotCertificateSummary>.Failure(QualityErrors.ItemInvalid);
            var cert = await store.GetLotCertificate(organizationId, itemId, normalizedLot, ct);
            return cert is null ? OrganizationResult<LotCertificateSummary>.Failure(QualityErrors.LotNotFound) : OrganizationResult<LotCertificateSummary>.Success(cert);
        }
        catch (QualityRuleException ex) { return OrganizationResult<LotCertificateSummary>.Failure(ex.Code, ex.Parameters.ToDictionary(kv => kv.Key, kv => kv.Value)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<LotCertificateSummary>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    private static QualitySpecLineInputData[] ValidateSpecLines(IReadOnlyList<QualitySpecLineInput>? lines)
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
