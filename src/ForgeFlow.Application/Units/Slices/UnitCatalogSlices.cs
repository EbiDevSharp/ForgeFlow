using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Units.Slices;

/// <summary>Vertical slice handler for unit of measure catalog CRUD and state transitions.</summary>
internal sealed class UnitCatalogSlices(
    IUnitOperationsStore store,
    UnitSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<UnitSummary>>> ListAsync(
        OrganizationScope? scope,
        long organizationId,
        UnitListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<UnitSummary>>.Failure(auth.Error);
        if (!UnitSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<UnitSummary>>.Failure(UnitErrors.NotFound);

        return await UnitSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<UnitSummary>>.Success(
                await store.ListUnits(organizationId, UnitSliceSupport.Bound(query), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<UnitSummary>>> ListActiveAsync(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<UnitSummary>>.Failure(auth.Error);
        if (!UnitSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<UnitSummary>>.Failure(UnitErrors.NotFound);

        return await UnitSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<IReadOnlyList<UnitSummary>>.Success(
                await store.ListActiveUnits(organizationId, ct)));
    }

    public async Task<OrganizationResult<UnitSummary>> GetAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<UnitSummary>.Failure(auth.Error);
        if (!UnitSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);

        return await UnitSliceSupport.ExecuteStoreAsync(async () =>
        {
            var item = await store.GetUnit(organizationId, id, ct);
            return item is null
                ? OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound)
                : OrganizationResult<UnitSummary>.Success(item);
        });
    }

    public async Task<OrganizationResult<UnitSummary>> CreateAsync(
        OrganizationScope? scope,
        CreateUnitRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<UnitSummary> res;
        if (auth.Error is not null) res = OrganizationResult<UnitSummary>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);
        else
        {
            try
            {
                _ = Unit.Create(request.OrganizationId, request.Code, request.Name, request.Symbol, request.Dimension, request.PrecisionScale);
                res = await UnitSliceSupport.ExecuteStoreAsync(() => store.CreateUnit(request, support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage), ct));
            }
            catch (UnitRuleException ex)
            {
                res = OrganizationResult<UnitSummary>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Unit", request.Code, "CreateUnit");
    }

    public async Task<OrganizationResult<UnitSummary>> UpdateAsync(
        OrganizationScope? scope,
        UpdateUnitRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<UnitSummary> res;
        if (auth.Error is not null) res = OrganizationResult<UnitSummary>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);
        else if (UnitSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<UnitSummary>.Failure(UnitErrors.ConcurrencyConflict);
        else
            res = await UnitSliceSupport.ExecuteStoreAsync(() => store.UpdateUnit(
                request.OrganizationId,
                request.Id,
                version,
                request.Name,
                request.Symbol,
                request.PrecisionScale,
                support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Unit", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateUnit");
    }

    public async Task<OrganizationResult<UnitSummary>> SetActiveAsync(
        OrganizationScope? scope,
        SetUnitActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<UnitSummary> res;
        if (auth.Error is not null) res = OrganizationResult<UnitSummary>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);
        else if (UnitSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<UnitSummary>.Failure(UnitErrors.ConcurrencyConflict);
        else
            res = await UnitSliceSupport.ExecuteStoreAsync(() => store.SetUnitActive(
                request.OrganizationId,
                request.Id,
                version,
                request.IsActive,
                support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Unit", request.Id.ToString(CultureInfo.InvariantCulture), "SetUnitActive");
    }
}
