using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;

namespace ForgeFlow.Application.Boms.Slices;

public sealed class BomCatalogSlices(IBomOperationsStore store, BomSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<BomSummary>>> ListAsync(
        OrganizationScope? scope,
        long organizationId,
        BomListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<BomSummary>>.Failure(auth.Error);
        if (!support.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<BomSummary>>.Failure(BomErrors.NotFound);

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<BomSummary>>.Success(await store.ListBoms(organizationId, support.Bound(query), ct)));
    }

    public async Task<OrganizationResult<BomDetails>> GetAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<BomDetails>.Failure(auth.Error);
        if (!support.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);

        return await support.ExecuteStoreAsync(async () =>
        {
            var item = await store.GetBom(organizationId, id, ct);
            return item is null ? OrganizationResult<BomDetails>.Failure(BomErrors.NotFound) : OrganizationResult<BomDetails>.Success(item);
        });
    }

    public async Task<OrganizationResult<BomVersionDetails>> GetVersionAsync(
        OrganizationScope? scope,
        long organizationId,
        long bomVersionId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        if (!support.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || bomVersionId <= 0)
            return OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);

        return await support.ExecuteStoreAsync(async () =>
        {
            var item = await store.GetBomVersion(organizationId, bomVersionId, ct);
            return item is null ? OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound) : OrganizationResult<BomVersionDetails>.Success(item);
        });
    }

    public async Task<OrganizationResult<BomDetails>> CreateAsync(
        OrganizationScope? scope,
        CreateBomRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomDetails>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);
        else
        {
            try
            {
                _ = Bom.Create(request.OrganizationId, request.Code, request.Name, request.PrimaryOutputItemId);
                res = await support.ExecuteStoreAsync(() => store.CreateBom(request, support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
            }
            catch (BomRuleException ex)
            {
                res = OrganizationResult<BomDetails>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "Bom", request.Code, "CreateBom");
    }

    public async Task<OrganizationResult<BomDetails>> UpdateAsync(
        OrganizationScope? scope,
        UpdateBomRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomDetails>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(() => store.UpdateBom(
                request.OrganizationId, request.Id, version, request.Name,
                support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "Bom", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateBom");
    }

    public async Task<OrganizationResult<BomDetails>> SetActiveAsync(
        OrganizationScope? scope,
        SetBomActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomDetails>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<BomDetails>.Failure(BomErrors.NotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(() => store.SetBomActive(
                request.OrganizationId, request.Id, version, request.IsActive,
                support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "Bom", request.Id.ToString(CultureInfo.InvariantCulture), "SetBomActive");
    }

    public async Task<OrganizationResult<BomVersionDetails>> CreateVersionAsync(
        OrganizationScope? scope,
        CreateBomVersionRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.NotFound);
        else
        {
            try
            {
                _ = BomVersion.CreateDraft(
                    request.OrganizationId, request.BomId, request.VersionNumber,
                    request.BaseOutputQuantity, request.BaseOutputUnitId,
                    request.EffectiveFromDate, request.EffectiveToDate, request.Notes);
                res = await support.ExecuteStoreAsync(() => store.CreateBomVersion(request, support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
            }
            catch (BomRuleException ex)
            {
                res = OrganizationResult<BomVersionDetails>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", $"{request.BomId}:v{request.VersionNumber}", "CreateBomVersion");
    }

    public async Task<OrganizationResult<BomVersionDetails>> UpdateDraftVersionAsync(
        OrganizationScope? scope,
        UpdateDraftBomVersionRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomVersionId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(() => store.UpdateDraftBomVersion(
                request.OrganizationId, request.BomVersionId, version, request,
                support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", request.BomVersionId.ToString(CultureInfo.InvariantCulture), "UpdateDraftBomVersion");
    }

    public async Task<OrganizationResult<BomVersionDetails>> SaveDraftLinesAsync(
        OrganizationScope? scope,
        SaveDraftBomLinesRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomVersionId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(() => store.SaveDraftBomLines(
                request.OrganizationId, request.BomVersionId, version, request.Inputs, request.Outputs,
                support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", request.BomVersionId.ToString(CultureInfo.InvariantCulture), "SaveDraftBomLines");
    }

    public async Task<OrganizationResult<BomVersionDetails>> SaveDraftApplicabilityAsync(
        OrganizationScope? scope,
        SaveDraftBomApplicabilityRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomVersionId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(() => store.SaveDraftBomApplicability(
                request.OrganizationId, request.BomVersionId, version, request,
                support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", request.BomVersionId.ToString(CultureInfo.InvariantCulture), "SaveDraftBomApplicability");
    }

    public async Task<OrganizationResult<BomVersionDetails>> TransitionStatusAsync(
        OrganizationScope? scope,
        TransitionBomVersionStatusRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<BomVersionDetails> res;
        if (auth.Error is not null) res = OrganizationResult<BomVersionDetails>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.BomVersionId <= 0)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.VersionNotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<BomVersionDetails>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(() => store.TransitionBomVersionStatus(
                request.OrganizationId, request.BomVersionId, version, request.TargetStatus,
                support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "BomVersion", request.BomVersionId.ToString(CultureInfo.InvariantCulture), $"TransitionStatus:{request.TargetStatus}");
    }
}
