using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Units.Slices;

/// <summary>Vertical slice handler for unit conversions, multi-hop conversion graph resolution, and standard unit seeding.</summary>
internal sealed class UnitConversionSlices(
    IUnitOperationsStore store,
    UnitSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<UnitConversionSummary>>> ListAsync(
        OrganizationScope? scope,
        long organizationId,
        UnitConversionListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<UnitConversionSummary>>.Failure(auth.Error);
        if (!UnitSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<UnitConversionSummary>>.Failure(UnitErrors.NotFound);

        return await UnitSliceSupport.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<UnitConversionSummary>>.Success(
                await store.ListConversions(organizationId, UnitSliceSupport.Bound(query), ct)));
    }

    public async Task<OrganizationResult<UnitConversionSummary>> GetAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<UnitConversionSummary>.Failure(auth.Error);
        if (!UnitSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound);

        return await UnitSliceSupport.ExecuteStoreAsync(async () =>
        {
            var item = await store.GetConversion(organizationId, id, ct);
            return item is null
                ? OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound)
                : OrganizationResult<UnitConversionSummary>.Success(item);
        });
    }

    public async Task<OrganizationResult<UnitConversionSummary>> CreateAsync(
        OrganizationScope? scope,
        CreateUnitConversionRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<UnitConversionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<UnitConversionSummary>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            res = OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.NotFound);
        else
        {
            try
            {
                _ = UnitConversion.Create(request.OrganizationId, request.FromUnitId, request.ToUnitId, request.Factor);
                res = await UnitSliceSupport.ExecuteStoreAsync(() => store.CreateConversion(request, support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage), ct));
            }
            catch (UnitRuleException ex)
            {
                res = OrganizationResult<UnitConversionSummary>.Failure(ex.Code, ex.Parameters);
            }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "UnitConversion", $"{request.FromUnitId}->{request.ToUnitId}", "CreateConversion");
    }

    public async Task<OrganizationResult<UnitConversionSummary>> UpdateFactorAsync(
        OrganizationScope? scope,
        UpdateUnitConversionFactorRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<UnitConversionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<UnitConversionSummary>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound);
        else if (UnitSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConcurrencyConflict);
        else
            res = await UnitSliceSupport.ExecuteStoreAsync(() => store.UpdateConversionFactor(
                request.OrganizationId,
                request.Id,
                version,
                request.Factor,
                support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "UnitConversion", request.Id.ToString(CultureInfo.InvariantCulture), "UpdateConversionFactor");
    }

    public async Task<OrganizationResult<UnitConversionSummary>> SetActiveAsync(
        OrganizationScope? scope,
        SetUnitConversionActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<UnitConversionSummary> res;
        if (auth.Error is not null) res = OrganizationResult<UnitConversionSummary>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            res = OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound);
        else if (UnitSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConcurrencyConflict);
        else
            res = await UnitSliceSupport.ExecuteStoreAsync(() => store.SetConversionActive(
                request.OrganizationId,
                request.Id,
                version,
                request.IsActive,
                support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
                ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, "UnitConversion", request.Id.ToString(CultureInfo.InvariantCulture), "SetConversionActive");
    }

    public async Task<OrganizationResult<UnitConversionPreviewResult>> PreviewConversionAsync(
        OrganizationScope? scope,
        ConvertQuantityRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<UnitConversionPreviewResult>.Failure(auth.Error);
        if (!UnitSliceSupport.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: true, out _))
            return OrganizationResult<UnitConversionPreviewResult>.Failure(UnitErrors.NotFound);

        return await UnitSliceSupport.ExecuteStoreAsync(async () =>
        {
            if (request.Quantity <= 0m)
                return OrganizationResult<UnitConversionPreviewResult>.Failure(UnitErrors.ConversionQuantityInvalid);

            var fromUnit = await store.GetUnit(request.OrganizationId, request.FromUnitId, ct);
            var toUnit = await store.GetUnit(request.OrganizationId, request.ToUnitId, ct);
            if (fromUnit is null || toUnit is null)
                return OrganizationResult<UnitConversionPreviewResult>.Failure(UnitErrors.NotFound);
            if (!fromUnit.IsActive || !toUnit.IsActive)
                return OrganizationResult<UnitConversionPreviewResult>.Failure(UnitErrors.UnitInactive);

            var edges = await store.ListActiveConversionEdges(request.OrganizationId, ct);
            var resolution = UnitConversionGraph.ConvertQuantity(request.Quantity, request.FromUnitId, request.ToUnitId, edges);

            return OrganizationResult<UnitConversionPreviewResult>.Success(new UnitConversionPreviewResult(
                request.FromUnitId,
                request.ToUnitId,
                request.Quantity,
                resolution.Factor,
                resolution.ConvertedQuantity,
                resolution.Hops));
        });
    }

    public async Task<OrganizationResult<SeedStandardUnitsResult>> SeedStandardUnitsAsync(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UnitPermissions.UnitsManage, ct);
        OrganizationResult<SeedStandardUnitsResult> res;
        if (auth.Error is not null) res = OrganizationResult<SeedStandardUnitsResult>.Failure(auth.Error);
        else if (!UnitSliceSupport.TryScopeOrganizationId(scope, organizationId, allowSite: false, out _))
            res = OrganizationResult<SeedStandardUnitsResult>.Failure(UnitErrors.NotFound);
        else
            res = await UnitSliceSupport.ExecuteStoreAsync(() =>
                store.SeedStandardUnits(organizationId, support.Access(auth.Actor!, scope!, UnitPermissions.UnitsManage), ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, organizationId, "Unit", "SeedStandard", "SeedStandardUnits");
    }
}
