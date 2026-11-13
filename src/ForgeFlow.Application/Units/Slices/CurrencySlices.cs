using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Units.Slices;

/// <summary>Vertical slice handler for currency catalog CRUD and state transitions.</summary>
internal sealed class CurrencySlices(
    IUnitOperationsStore store,
    UnitSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<CurrencySummary>>> ListAsync(
        OrganizationScope? scope,
        long organizationId,
        CurrencyListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<CurrencySummary>>.Failure(auth.Error);
        if (!UnitSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<CurrencySummary>>.Failure(UnitErrors.NotFound);

        var bound = UnitSliceSupport.Bound(query);

        return await UnitSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<CurrencySummary>>.Success(
                await store.ListCurrencies(organizationId, bound, ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<CurrencySummary>>> ListActiveAsync(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<CurrencySummary>>.Failure(auth.Error);
        if (!UnitSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<CurrencySummary>>.Failure(UnitErrors.NotFound);

        return await UnitSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<IReadOnlyList<CurrencySummary>>.Success(
                await store.ListActiveCurrencies(organizationId, ct)));
    }

    public async Task<OrganizationResult<CurrencySummary>> GetAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<CurrencySummary>.Failure(auth.Error);
        if (!UnitSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<CurrencySummary>.Failure(UnitErrors.CurrencyNotFound);

        return await UnitSliceSupport.ExecuteStoreAsync(async () =>
        {
            var item = await store.GetCurrency(organizationId, id, ct);
            return item is null
                ? OrganizationResult<CurrencySummary>.Failure(UnitErrors.CurrencyNotFound)
                : OrganizationResult<CurrencySummary>.Success(item);
        });
    }

    public async Task<OrganizationResult<CurrencySummary>> CreateAsync(
        OrganizationScope? scope,
        CreateCurrencyRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<CurrencySummary> res;
        if (auth.Error is not null) res = OrganizationResult<CurrencySummary>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<CurrencySummary>.Failure(UnitErrors.NotFound);
        else
            res = await UnitSliceSupport.ExecuteStoreAsync(() => store.CreateCurrency(request, support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage), ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Currency", request.Code, nameof(CreateAsync));
    }

    public async Task<OrganizationResult<CurrencySummary>> UpdateAsync(
        OrganizationScope? scope,
        UpdateCurrencyRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<CurrencySummary> res;
        if (auth.Error is not null) res = OrganizationResult<CurrencySummary>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<CurrencySummary>.Failure(UnitErrors.CurrencyNotFound);
        else if (UnitSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<CurrencySummary>.Failure(UnitErrors.ConcurrencyConflict);
        else
            res = await UnitSliceSupport.ExecuteStoreAsync(() => store.UpdateCurrency(
                request.OrganizationId,
                request.Id,
                version,
                request.Name,
                request.Symbol,
                request.PrecisionScale,
                support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Currency", request.Id.ToString(CultureInfo.InvariantCulture), nameof(UpdateAsync));
    }

    public async Task<OrganizationResult<CurrencySummary>> SetActiveAsync(
        OrganizationScope? scope,
        SetCurrencyActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<CurrencySummary> res;
        if (auth.Error is not null) res = OrganizationResult<CurrencySummary>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<CurrencySummary>.Failure(UnitErrors.CurrencyNotFound);
        else if (UnitSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<CurrencySummary>.Failure(UnitErrors.ConcurrencyConflict);
        else
            res = await UnitSliceSupport.ExecuteStoreAsync(() => store.SetCurrencyActive(
                request.OrganizationId,
                request.Id,
                version,
                request.IsActive,
                support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "Currency", request.Id.ToString(CultureInfo.InvariantCulture), nameof(SetActiveAsync));
    }
}
