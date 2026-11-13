using System.Globalization;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Quality.Slices;

/// <summary>
/// Operational vertical slice for Quality Parameter definition, numeric bounds, and active lifecycle.
/// </summary>
public sealed class QualityParameterSlices(
    IQualitySpecOperationsStore store,
    QualitySpecSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<QualityParameterSummary>>> ListParametersAsync(
        OrganizationScope? scope, long organizationId, QualityParameterListQuery query, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead, "Sales.Read");
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<QualityParameterSummary>>.Failure(auth.Error);
        if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, organizationId)) return OrganizationResult<OrganizationPage<QualityParameterSummary>>.Failure(QualityErrors.AccessDenied);
        try { return OrganizationResult<OrganizationPage<QualityParameterSummary>>.Success(await store.ListParameters(organizationId, QualitySpecSliceSupport.Bound(query), ct)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<OrganizationPage<QualityParameterSummary>>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<QualityParameterSummary>> CreateParameterAsync(
        OrganizationScope? scope, CreateQualityParameterRequest request, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualityParameterSummary> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualityParameterSummary>.Failure(auth.Error);
        else if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, request.OrganizationId))
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.AccessDenied);
        else
            res = await QualitySpecSliceSupport.ExecuteStore(() => store.CreateParameter(request, support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualityParameter", request.Code, new { Action = "CreateParameter" });
        return res;
    }

    public async Task<OrganizationResult<QualityParameterSummary>> UpdateParameterAsync(
        OrganizationScope? scope, UpdateQualityParameterRequest request, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualityParameterSummary> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualityParameterSummary>.Failure(auth.Error);
        else if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, request.OrganizationId) || request.Id <= 0)
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ParameterNotFound);
        else if (QualitySpecSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await QualitySpecSliceSupport.ExecuteStore(() => store.UpdateParameter(request.OrganizationId, request.Id, version, request, support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualityParameter", request.Id.ToString(CultureInfo.InvariantCulture), new { Action = "UpdateParameter" });
        return res;
    }

    public async Task<OrganizationResult<QualityParameterSummary>> SetParameterActiveAsync(
        OrganizationScope? scope, SetQualityParameterActiveRequest request, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualityParameterSummary> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualityParameterSummary>.Failure(auth.Error);
        else if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, request.OrganizationId) || request.Id <= 0)
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ParameterNotFound);
        else if (QualitySpecSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualityParameterSummary>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await QualitySpecSliceSupport.ExecuteStore(() => store.SetParameterActive(request.OrganizationId, request.Id, version, request.IsActive, support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualityParameter", request.Id.ToString(CultureInfo.InvariantCulture), new { Action = "SetParameterActive" });
        return res;
    }
}
