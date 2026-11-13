using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;

namespace ForgeFlow.Application.Boms.Slices;

public sealed class ContextDimensionSlices(IBomOperationsStore store, BomSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<ContextDimensionSummary>>> ListAsync(
        OrganizationScope? scope,
        long organizationId,
        ContextDimensionListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ContextDimensionSummary>>.Failure(auth.Error);
        if (!support.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<ContextDimensionSummary>>.Failure(BomErrors.NotFound);

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<ContextDimensionSummary>>.Success(
                await store.ListContextDimensions(organizationId, support.Bound(query), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<ContextDimensionSummary>>> ListActiveAsync(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ContextDimensionSummary>>.Failure(auth.Error);
        if (!support.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<ContextDimensionSummary>>.Failure(BomErrors.NotFound);

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<IReadOnlyList<ContextDimensionSummary>>.Success(
                await store.ListActiveContextDimensions(organizationId, ct)));
    }

    public async Task<OrganizationResult<ContextDimensionSummary>> CreateAsync(
        OrganizationScope? scope,
        CreateContextDimensionRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<ContextDimensionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ContextDimensionSummary>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.NotFound);
        else
        {
            try
            {
                _ = ContextDimension.Create(request.OrganizationId, request.Code, request.Name);
                res = await support.ExecuteStoreAsync(() => store.CreateContextDimension(request, support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
            }
            catch (BomRuleException ex)
            {
                res = OrganizationResult<ContextDimensionSummary>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "ContextDimension", request.Code, "CreateContextDimension");
    }

    public async Task<OrganizationResult<ContextDimensionSummary>> UpdateAsync(
        OrganizationScope? scope,
        UpdateContextDimensionRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<ContextDimensionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ContextDimensionSummary>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ContextDimensionNotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(() => store.UpdateContextDimension(
                request.OrganizationId, request.Id, version, request.Name,
                support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "ContextDimension", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateContextDimension");
    }

    public async Task<OrganizationResult<ContextDimensionSummary>> SetActiveAsync(
        OrganizationScope? scope,
        SetContextDimensionActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<ContextDimensionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ContextDimensionSummary>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ContextDimensionNotFound);
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<ContextDimensionSummary>.Failure(BomErrors.ConcurrencyConflict);
        else
            res = await support.ExecuteStoreAsync(() => store.SetContextDimensionActive(
                request.OrganizationId, request.Id, version, request.IsActive,
                support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "ContextDimension", request.Id.ToString(CultureInfo.InvariantCulture), "SetContextDimensionActive");
    }

    public async Task<OrganizationResult<SeedContextDimensionsResult>> SeedStandardAsync(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsManage, ct);
        OrganizationResult<SeedContextDimensionsResult> res;
        if (auth.Error is not null) res = OrganizationResult<SeedContextDimensionsResult>.Failure(auth.Error);
        else if (!support.TryScopeOrganizationId(scope, organizationId, allowSite: false, out _))
            res = OrganizationResult<SeedContextDimensionsResult>.Failure(BomErrors.NotFound);
        else
            res = await support.ExecuteStoreAsync(() => store.SeedStandardContextDimensions(
                organizationId, support.Access(auth.Actor!, scope!, BomPermissions.BomsManage), ct));
        return await support.RecordRejected(res, auth.Actor, scope, organizationId, "ContextDimension", "SeedStandard", "SeedStandardContextDimensions");
    }
}
