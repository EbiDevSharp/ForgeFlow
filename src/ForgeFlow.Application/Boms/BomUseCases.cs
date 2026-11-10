using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Boms;

/// <summary>
/// Secured use cases for Manufacturing BOMs, Versions, Context Dimensions,
/// deterministic BOM Resolver (Day 9 / A07 / A11), and multi-level BOM Explosion (Day 7 / A08 / A14).
/// </summary>
public sealed class BomUseCases(
    IBomOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    private const int MaxExplosionDepth = 32;

    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
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

    private Task<OrganizationResult<T>> RecordRejected<T>(
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

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    private static bool TryScopeOrganizationId(OrganizationScope? scope, long requestedOrgId, bool allowSite, out long orgId)
    {
        orgId = scope switch
        {
            OrganizationScope.Organization org => org.OrganizationId,
            OrganizationScope.Site site when allowSite => site.OrganizationId,
            _ => 0
        };
        return orgId > 0 && orgId == requestedOrgId;
    }

    private static byte[]? DecodeVersion(string? raw)
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

    private static BomListQuery Bound(BomListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static ContextDimensionListQuery Bound(ContextDimensionListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (BomRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (BomStoreUnavailableException) { return OrganizationResult<T>.Failure(BomErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<OrganizationPage<BomSummary>>> ListBoms(
        OrganizationScope? scope,
        long organizationId,
        BomListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<BomSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<BomSummary>>.Failure(BomErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<BomSummary>>.Success(await store.ListBoms(organizationId, Bound(query), ct)));
    }

    public async Task<OrganizationResult<BomDetails>> GetBom(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<BomDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);

        return await ExecuteStore(async () =>
        {
            var item = await store.GetBom(organizationId, id, ct);
            return item is null ? OrganizationResult<BomDetails>.Failure(BomErrors.NotFound) : OrganizationResult<BomDetails>.Success(item);
        });
    }

    public async Task<OrganizationResult<BomVersionDetails>> GetBomVersion(
        OrganizationScope? scope,
        long organizationId,
        long bomVersionId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || bomVersionId <= 0)
            return OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);

        return await ExecuteStore(async () =>
        {
            var item = await store.GetBomVersion(organizationId, bomVersionId, ct);
            return item is null ? OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound) : OrganizationResult<BomVersionDetails>.Success(item);
        });
    }

    public async Task<OrganizationResult<BomDetails>> CreateBom(
        OrganizationScope? scope,
        CreateBomRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomDetails>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);
        else
        {
            try
            {
                _ = Bom.Create(request.OrganizationId, request.Code, request.Name, request.PrimaryOutputItemId);
                res = await ExecuteStore(() => store.CreateBom(request, Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
            }
            catch (BomRuleException ex)
            {
                res = OrganizationResult<BomDetails>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "Bom", request.Code, "CreateBom");
    }

    public async Task<OrganizationResult<BomDetails>> UpdateBom(
        OrganizationScope? scope,
        UpdateBomRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomDetails>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.UpdateBom(
                request.OrganizationId, request.Id, version, request.Name,
                Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "Bom", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateBom");
    }

    public async Task<OrganizationResult<BomDetails>> SetBomActive(
        OrganizationScope? scope,
        SetBomActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomDetails>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.SetBomActive(
                request.OrganizationId, request.Id, version, request.IsActive,
                Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "Bom", request.Id.ToString(CultureInfo.InvariantCulture), "SetBomActive");
    }

    public async Task<OrganizationResult<BomVersionDetails>> CreateBomVersion(
        OrganizationScope? scope,
        CreateBomVersionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.NotFound);
        else
        {
            try
            {
                _ = BomVersion.CreateDraft(
                    request.OrganizationId, request.BomId, request.VersionNumber,
                    request.BaseOutputQuantity, request.BaseOutputUnitId,
                    request.EffectiveFromDate, request.EffectiveToDate, request.Notes);
                res = await ExecuteStore(() => store.CreateBomVersion(request, Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
            }
            catch (BomRuleException ex)
            {
                res = OrganizationResult<BomVersionDetails>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", $"{request.BomId}:v{request.VersionNumber}", "CreateBomVersion");
    }

    public async Task<OrganizationResult<BomVersionDetails>> UpdateDraftBomVersion(
        OrganizationScope? scope,
        UpdateDraftBomVersionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomVersionId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.UpdateDraftBomVersion(
                request.OrganizationId, request.BomVersionId, version, request,
                Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", request.BomVersionId.ToString(CultureInfo.InvariantCulture), "UpdateDraftBomVersion");
    }

    public async Task<OrganizationResult<BomVersionDetails>> SaveDraftBomLines(
        OrganizationScope? scope,
        SaveDraftBomLinesRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomVersionId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.SaveDraftBomLines(
                request.OrganizationId, request.BomVersionId, version, request.Inputs, request.Outputs,
                Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", request.BomVersionId.ToString(CultureInfo.InvariantCulture), "SaveDraftBomLines");
    }

    public async Task<OrganizationResult<BomVersionDetails>> SaveDraftBomApplicability(
        OrganizationScope? scope,
        SaveDraftBomApplicabilityRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomVersionId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.SaveDraftBomApplicability(
                request.OrganizationId, request.BomVersionId, version, request,
                Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", request.BomVersionId.ToString(CultureInfo.InvariantCulture), "SaveDraftBomApplicability");
    }

    public async Task<OrganizationResult<BomVersionDetails>> TransitionBomVersionStatus(
        OrganizationScope? scope,
        TransitionBomVersionStatusRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomVersionId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.TransitionBomVersionStatus(
                request.OrganizationId, request.BomVersionId, version, request.TargetStatus,
                Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", request.BomVersionId.ToString(CultureInfo.InvariantCulture), $"TransitionStatus:{request.TargetStatus}");
    }

    public async Task<OrganizationResult<OrganizationPage<ContextDimensionSummary>>> ListContextDimensions(
        OrganizationScope? scope,
        long organizationId,
        ContextDimensionListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ContextDimensionSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<ContextDimensionSummary>>.Failure(BomErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<ContextDimensionSummary>>.Success(
                await store.ListContextDimensions(organizationId, Bound(query), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<ContextDimensionSummary>>> ListActiveContextDimensions(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ContextDimensionSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<ContextDimensionSummary>>.Failure(BomErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<ContextDimensionSummary>>.Success(
                await store.ListActiveContextDimensions(organizationId, ct)));
    }

    public async Task<OrganizationResult<ContextDimensionSummary>> CreateContextDimension(
        OrganizationScope? scope,
        CreateContextDimensionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<ContextDimensionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ContextDimensionSummary>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.NotFound);
        else
        {
            try
            {
                _ = ContextDimension.Create(request.OrganizationId, request.Code, request.Name);
                res = await ExecuteStore(() => store.CreateContextDimension(request, Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
            }
            catch (BomRuleException ex)
            {
                res = OrganizationResult<ContextDimensionSummary>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "ContextDimension", request.Code, "CreateContextDimension");
    }

    public async Task<OrganizationResult<ContextDimensionSummary>> UpdateContextDimension(
        OrganizationScope? scope,
        UpdateContextDimensionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<ContextDimensionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ContextDimensionSummary>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ContextDimensionNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.UpdateContextDimension(
                request.OrganizationId, request.Id, version, request.Name,
                Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "ContextDimension", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateContextDimension");
    }

    public async Task<OrganizationResult<ContextDimensionSummary>> SetContextDimensionActive(
        OrganizationScope? scope,
        SetContextDimensionActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<ContextDimensionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ContextDimensionSummary>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ContextDimensionNotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await ExecuteStore(() => store.SetContextDimensionActive(
                request.OrganizationId, request.Id, version, request.IsActive,
                Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, "ContextDimension", request.Id.ToString(CultureInfo.InvariantCulture), "SetContextDimensionActive");
    }

    public async Task<OrganizationResult<SeedContextDimensionsResult>> SeedStandardContextDimensions(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<SeedContextDimensionsResult> res;
        if (auth.Error is not null) res = OrganizationResult<SeedContextDimensionsResult>.Failure(auth.Error);
        else if (!TryScopeOrganizationId(scope, organizationId, allowSite: false, out _))
            res = OrganizationResult<SeedContextDimensionsResult>.Failure(BomErrors.NotFound);
        else
            res = await ExecuteStore(() => store.SeedStandardContextDimensions(
                organizationId, Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await RecordRejected(res, auth.Actor, scope, organizationId, "ContextDimension", "SeedStandard", "SeedStandardContextDimensions");
    }

    public async Task<OrganizationResult<BomResolutionResultDto>> ResolveBom(
        OrganizationScope? scope,
        ResolveBomRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<BomResolutionResultDto>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: true, out _) || request.ItemId <= 0)
            return OrganizationResult<BomResolutionResultDto>.Failure(BomErrors.PrimaryItemInvalid);

        return await ExecuteStore(async () =>
        {
            var dataset = await store.LoadItemBomResolutionDataset(request.OrganizationId, request.ItemId, ct);
            if (dataset is null || !dataset.ItemIsActive)
                return OrganizationResult<BomResolutionResultDto>.Failure(BomErrors.PrimaryItemInvalid);

            var winner = ResolveFromDataset(dataset, request.ProductionDate, new ManufacturingContext(request.ContextValues));
            ValidateCandidateStructure(winner);

            var summary = winner.Details.Summary;
            return OrganizationResult<BomResolutionResultDto>.Success(new BomResolutionResultDto(
                winner.BomId,
                winner.BomCode,
                winner.BomName,
                summary.Id,
                summary.VersionNumber,
                summary.BaseOutputQuantity,
                summary.BaseOutputUnitId,
                summary.BaseOutputUnitCode,
                summary.BaseOutputUnitSymbol,
                summary.EffectiveFromDate,
                summary.EffectiveToDate));
        });
    }

    public async Task<OrganizationResult<BomExplosionResultDto>> ExplodeBom(
        OrganizationScope? scope,
        ExplodeBomRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<BomExplosionResultDto>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: true, out _) || request.ItemId <= 0)
            return OrganizationResult<BomExplosionResultDto>.Failure(BomErrors.PrimaryItemInvalid);

        // A14: Positive quantity must be rejected before explosion traversal.
        if (request.RequestedQuantity <= 0m)
            return OrganizationResult<BomExplosionResultDto>.Failure(BomErrors.QuantityInvalid);

        return await ExecuteStore(async () =>
        {
            var core = await ResolveAndExplodeCoreAsync(
                request.OrganizationId,
                request.ItemId,
                request.ProductionDate,
                request.RequestedQuantity,
                request.RequestedUnitId,
                request.ContextValues,
                ct);
            return core.Succeeded
                ? OrganizationResult<BomExplosionResultDto>.Success(core.Value!.Explosion)
                : OrganizationResult<BomExplosionResultDto>.Failure(core.Error!.Code, core.Error.Parameters);
        });
    }

    internal async Task<OrganizationResult<(BomResolutionResultDto Resolution, BomExplosionResultDto Explosion)>> ResolveAndExplodeCoreAsync(
        long organizationId,
        long itemId,
        DateOnly productionDate,
        decimal requestedQuantity,
        long? requestedUnitIdInput,
        IReadOnlyDictionary<string, string>? contextValues,
        CancellationToken ct)
    {
        if (requestedQuantity <= 0m)
            return OrganizationResult<(BomResolutionResultDto, BomExplosionResultDto)>.Failure(BomErrors.QuantityInvalid);

        var rootDataset = await store.LoadItemBomResolutionDataset(organizationId, itemId, ct);
        if (rootDataset is null || !rootDataset.ItemIsActive)
            return OrganizationResult<(BomResolutionResultDto, BomExplosionResultDto)>.Failure(BomErrors.PrimaryItemInvalid);

        var context = new ManufacturingContext(contextValues);
        var rootCandidate = ResolveFromDataset(rootDataset, productionDate, context);
        ValidateCandidateStructure(rootCandidate);

        var rootSummary = rootCandidate.Details.Summary;
        var requestedUnitId = requestedUnitIdInput ?? rootDataset.BaseUnitId;
        var requestedQtyInBaseOutput = requestedQuantity;

        if (requestedUnitId != rootSummary.BaseOutputUnitId)
        {
            var resolution = UnitConversionGraph.ConvertQuantity(
                requestedQuantity,
                requestedUnitId,
                rootSummary.BaseOutputUnitId,
                rootCandidate.ItemUnitEdges.Concat(rootDataset.GlobalUnitEdges));
            requestedQtyInBaseOutput = resolution.ConvertedQuantity;
        }

        var consumed = new List<BomExplosionLineDto>();
        var produced = new List<BomExplosionOutputLineDto>();
        var branchItems = new HashSet<long> { itemId };
        var datasetCache = new Dictionary<long, BomResolutionDataset?> { [itemId] = rootDataset };

        await ExplodeRecursiveAsync(
            organizationId,
            productionDate,
            context,
            level: 1,
            parentItemId: itemId,
            parentItemCode: rootDataset.ItemCode,
            candidate: rootCandidate,
            targetBaseOutputQuantity: requestedQtyInBaseOutput,
            globalEdges: rootDataset.GlobalUnitEdges,
            branchItems: branchItems,
            datasetCache: datasetCache,
            consumed: consumed,
            produced: produced,
            ct: ct);

        var reqUnitCode = requestedUnitId == rootSummary.BaseOutputUnitId ? rootSummary.BaseOutputUnitCode : rootDataset.BaseUnitCode;
        var reqUnitSym = requestedUnitId == rootSummary.BaseOutputUnitId ? rootSummary.BaseOutputUnitSymbol : rootDataset.BaseUnitSymbol;

        var resolutionDto = new BomResolutionResultDto(
            rootCandidate.BomId,
            rootCandidate.BomCode,
            rootCandidate.BomName,
            rootSummary.Id,
            rootSummary.VersionNumber,
            rootSummary.BaseOutputQuantity,
            rootSummary.BaseOutputUnitId,
            rootSummary.BaseOutputUnitCode,
            rootSummary.BaseOutputUnitSymbol,
            rootSummary.EffectiveFromDate,
            rootSummary.EffectiveToDate);

        var explosionDto = new BomExplosionResultDto(
            rootDataset.ItemId,
            rootDataset.ItemCode,
            rootDataset.ItemName,
            requestedQuantity,
            requestedUnitId,
            reqUnitCode,
            reqUnitSym,
            rootSummary.Id,
            rootSummary.VersionNumber,
            consumed,
            produced);

        return OrganizationResult<(BomResolutionResultDto, BomExplosionResultDto)>.Success((resolutionDto, explosionDto));
    }

    private async Task ExplodeRecursiveAsync(
        long organizationId,
        DateOnly productionDate,
        ManufacturingContext context,
        int level,
        long parentItemId,
        string parentItemCode,
        BomVersionCandidateRecord candidate,
        decimal targetBaseOutputQuantity,
        IReadOnlyList<UnitConversionEdge> globalEdges,
        HashSet<long> branchItems,
        Dictionary<long, BomResolutionDataset?> datasetCache,
        List<BomExplosionLineDto> consumed,
        List<BomExplosionOutputLineDto> produced,
        CancellationToken ct)
    {
        if (level > MaxExplosionDepth)
            throw new BomRuleException(BomErrors.CircularDependency);

        var scaleFactor = targetBaseOutputQuantity / candidate.Details.Summary.BaseOutputQuantity;

        foreach (var output in candidate.Details.Outputs.OrderBy(o => o.Sequence))
        {
            var qty = decimal.Round(output.Quantity * scaleFactor, 6, MidpointRounding.AwayFromZero);
            produced.Add(new BomExplosionOutputLineDto(
                level,
                output.ItemId,
                output.ItemCode,
                output.ItemName,
                output.OutputType,
                qty,
                output.UnitId,
                output.UnitCode,
                output.UnitSymbol));
        }

        foreach (var input in candidate.Details.Inputs.OrderBy(i => i.Sequence))
        {
            if (branchItems.Contains(input.ItemId))
                throw new BomRuleException(BomErrors.CircularDependency);

            var netQty = decimal.Round(input.Quantity * scaleFactor, 6, MidpointRounding.AwayFromZero);
            var scrapMultiplier = 1m + (input.ScrapPercentage / 100m);
            var grossQty = decimal.Round(input.Quantity * scaleFactor * scrapMultiplier, 6, MidpointRounding.AwayFromZero);

            if (!datasetCache.TryGetValue(input.ItemId, out var childDataset))
            {
                childDataset = await store.LoadItemBomResolutionDataset(organizationId, input.ItemId, ct);
                datasetCache[input.ItemId] = childDataset;
            }

            BomVersionCandidateRecord? childWinner = null;
            if (childDataset is not null && childDataset.ItemIsActive && childDataset.Candidates.Count > 0)
            {
                try
                {
                    childWinner = ResolveFromDataset(childDataset, productionDate, context);
                    ValidateCandidateStructure(childWinner);
                }
                catch (BomRuleException ex) when (ex.Code == BomErrors.NoActiveBomFound)
                {
                    // Child without active BOM for this date/context is treated as a leaf material (Day 3).
                    childWinner = null;
                }
            }

            consumed.Add(new BomExplosionLineDto(
                level,
                parentItemId,
                parentItemCode,
                input.ItemId,
                input.ItemCode,
                input.ItemName,
                netQty,
                input.ScrapPercentage,
                grossQty,
                input.UnitId,
                input.UnitCode,
                input.UnitSymbol,
                input.IsOptional,
                childWinner is not null,
                childWinner?.Details.Summary.Id));

            if (childWinner is not null)
            {
                var childBaseQty = grossQty;
                if (input.UnitId != childWinner.Details.Summary.BaseOutputUnitId)
                {
                    var edges = childWinner.ItemUnitEdges.Concat(globalEdges);
                    var converted = UnitConversionGraph.ConvertQuantity(
                        grossQty,
                        input.UnitId,
                        childWinner.Details.Summary.BaseOutputUnitId,
                        edges);
                    childBaseQty = converted.ConvertedQuantity;
                }

                branchItems.Add(input.ItemId);
                try
                {
                    await ExplodeRecursiveAsync(
                        organizationId,
                        productionDate,
                        context,
                        level + 1,
                        input.ItemId,
                        input.ItemCode,
                        childWinner,
                        childBaseQty,
                        globalEdges,
                        branchItems,
                        datasetCache,
                        consumed,
                        produced,
                        ct);
                }
                finally
                {
                    branchItems.Remove(input.ItemId);
                }
            }
        }
    }

    private static BomVersionCandidateRecord ResolveFromDataset(
        BomResolutionDataset dataset,
        DateOnly productionDate,
        ManufacturingContext context)
    {
        var domainCandidates = dataset.Candidates.Select(c => new BomCandidateVersion(
            c.BomId,
            c.Details.Summary.Id,
            c.Details.Summary.VersionNumber,
            c.Details.Summary.Status,
            c.Details.Summary.EffectiveFromDate,
            c.Details.Summary.EffectiveToDate,
            c.Details.Applicability is not null,
            c.Details.Applicability?.IsActive ?? false,
            c.Details.Applicability?.Conditions
                .Select(x => new BomCandidateCondition(x.DimensionCode, x.DimensionIsActive, x.Operator, x.Value))
                .ToArray() ?? []));

        var resolved = BomResolutionEngine.Resolve(productionDate, context, domainCandidates);
        return dataset.Candidates.Single(c => c.Details.Summary.Id == resolved.BomVersionId);
    }

    private static void ValidateCandidateStructure(BomVersionCandidateRecord candidate)
    {
        if (candidate.Details.Inputs.Count == 0)
            throw new BomRuleException(BomErrors.InputsRequired);

        var hasMain = candidate.Details.Outputs.Any(o =>
            o.OutputType == BomOutputType.MainProduct && o.ItemId == candidate.PrimaryOutputItemId);
        if (!hasMain)
            throw new BomRuleException(BomErrors.MainOutputRequired);

        if (candidate.Details.Inputs.Any(i => i.ItemId == candidate.PrimaryOutputItemId))
            throw new BomRuleException(BomErrors.DirectSelfCycle);
    }
}
