using System.Globalization;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Quality.Slices;

/// <summary>
/// Operational vertical slice for Quality Specification templates, parameter line bounds, and inspection application.
/// </summary>
public sealed class QualitySpecSlices(
    IQualitySpecOperationsStore store,
    QualitySpecSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<QualitySpecSummary>>> ListSpecsAsync(
        OrganizationScope? scope, long organizationId, QualitySpecListQuery query, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead, "Sales.Read");
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<QualitySpecSummary>>.Failure(auth.Error);
        if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, organizationId)) return OrganizationResult<OrganizationPage<QualitySpecSummary>>.Failure(QualityErrors.AccessDenied);
        try { return OrganizationResult<OrganizationPage<QualitySpecSummary>>.Success(await store.ListSpecs(organizationId, QualitySpecSliceSupport.Bound(query), ct)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<OrganizationPage<QualitySpecSummary>>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<QualitySpecDetails>> GetSpecAsync(
        OrganizationScope? scope, long organizationId, long id, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead, "Sales.Read");
        if (auth.Error is not null) return OrganizationResult<QualitySpecDetails>.Failure(auth.Error);
        if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, organizationId) || id <= 0) return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound);
        try
        {
            var spec = await store.GetSpec(organizationId, id, ct);
            return spec is null ? OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound) : OrganizationResult<QualitySpecDetails>.Success(spec);
        }
        catch (QualityStoreUnavailableException) { return OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<QualitySpecDetails?>> ResolveApplicableSpecAsync(
        OrganizationScope? scope, long organizationId, long itemId, QualitySpecCheckpoint checkpoint, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead, "Sales.Read");
        if (auth.Error is not null) return OrganizationResult<QualitySpecDetails?>.Failure(auth.Error);
        if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, organizationId) || itemId <= 0) return OrganizationResult<QualitySpecDetails?>.Failure(QualityErrors.ItemInvalid);
        try { return OrganizationResult<QualitySpecDetails?>.Success(await store.ResolveApplicableSpec(organizationId, itemId, checkpoint, ct)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<QualitySpecDetails?>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<QualitySpecDetails>> CreateSpecAsync(
        OrganizationScope? scope, CreateQualitySpecRequest request, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualitySpecDetails> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualitySpecDetails>.Failure(auth.Error);
        else if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, request.OrganizationId))
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.AccessDenied);
        else
            res = await QualitySpecSliceSupport.ExecuteStore(() =>
            {
                var resolved = QualitySpecSliceSupport.ValidateSpecLines(request.Lines);
                return store.CreateSpec(request, resolved, support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct);
            });

        await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualitySpec", request.Code, new { Action = "CreateSpec" });
        return res;
    }

    public async Task<OrganizationResult<QualitySpecDetails>> UpdateSpecAsync(
        OrganizationScope? scope, UpdateQualitySpecRequest request, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualitySpecDetails> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualitySpecDetails>.Failure(auth.Error);
        else if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, request.OrganizationId) || request.Id <= 0)
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound);
        else if (QualitySpecSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await QualitySpecSliceSupport.ExecuteStore(() =>
            {
                var name = QualityText.SpecName(request.Name);
                var notes = QualityText.OptionalNote(request.Notes);
                var resolved = QualitySpecSliceSupport.ValidateSpecLines(request.Lines);
                return store.UpdateSpec(request.OrganizationId, request.Id, version, name, request.Checkpoint, notes, resolved, support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct);
            });

        await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualitySpec", request.Id.ToString(CultureInfo.InvariantCulture), new { Action = "UpdateSpec" });
        return res;
    }

    public async Task<OrganizationResult<QualitySpecDetails>> SetSpecActiveAsync(
        OrganizationScope? scope, SetQualitySpecActiveRequest request, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualitySpecDetails> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualitySpecDetails>.Failure(auth.Error);
        else if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, request.OrganizationId) || request.Id <= 0)
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.SpecNotFound);
        else if (QualitySpecSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualitySpecDetails>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await QualitySpecSliceSupport.ExecuteStore(() => store.SetSpecActive(request.OrganizationId, request.Id, version, request.IsActive, support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await support.RecordRejected(res, auth.Actor, scope, request.OrganizationId, "QualitySpec", request.Id.ToString(CultureInfo.InvariantCulture), new { Action = "SetSpecActive" });
        return res;
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> ApplySpecToInspectionAsync(
        OrganizationScope? scope, long organizationId, long inspectionId, string? expectedRowVersion, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityManage);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        else if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, organizationId) || inspectionId <= 0)
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        else if (QualitySpecSliceSupport.DecodeVersion(expectedRowVersion) is not byte[] version)
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);
        else
            res = await QualitySpecSliceSupport.ExecuteStore(() => store.ApplySpecToInspection(organizationId, inspectionId, version, support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct));

        await support.RecordRejected(res, auth.Actor, scope, organizationId, "QualityInspection", inspectionId.ToString(CultureInfo.InvariantCulture), new { Action = "ApplySpecToInspection" });
        return res;
    }
}
