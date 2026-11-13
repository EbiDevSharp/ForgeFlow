using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Procurement;

namespace ForgeFlow.Application.Procurement.Slices;

internal sealed class SupplierSlices(IProcurementOperationsStore store, ProcurementSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<SupplierSummary>>> ListSuppliers(
        OrganizationScope? scope,
        long organizationId,
        SupplierListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ProcurementRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<SupplierSummary>>.Failure(auth.Error);
        if (!ProcurementSliceSupport.TryResolveSiteScope(scope, organizationId, null, out _))
            return OrganizationResult<OrganizationPage<SupplierSummary>>.Failure(ProcurementErrors.AccessDenied);

        try
        {
            var bound = new SupplierListQuery(
                query.Search?.Trim(),
                query.IncludeInactive,
                Math.Clamp(query.Page, 1, 1_000_000),
                Math.Clamp(query.PageSize, 1, 100));
            return OrganizationResult<OrganizationPage<SupplierSummary>>.Success(await store.ListSuppliers(organizationId, bound, ct));
        }
        catch (ProcurementStoreUnavailableException)
        {
            return OrganizationResult<OrganizationPage<SupplierSummary>>.Failure(ProcurementErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<SupplierSummary>> GetSupplier(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ProcurementRead, ct);
        if (auth.Error is not null) return OrganizationResult<SupplierSummary>.Failure(auth.Error);
        if (!ProcurementSliceSupport.TryResolveSiteScope(scope, organizationId, null, out _))
            return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.AccessDenied);
        if (id <= 0)
            return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound);

        try
        {
            var found = await store.GetSupplier(organizationId, id, ct);
            return found is null
                ? OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound)
                : OrganizationResult<SupplierSummary>.Success(found);
        }
        catch (ProcurementStoreUnavailableException)
        {
            return OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<SupplierSummary>> CreateSupplier(
        OrganizationScope? scope,
        CreateSupplierRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ManageSuppliers, ct);
        OrganizationResult<SupplierSummary> result;
        if (auth.Error is not null) result = OrganizationResult<SupplierSummary>.Failure(auth.Error);
        else if (!ProcurementSliceSupport.MatchesOrganizationOnly(scope, request.OrganizationId))
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.AccessDenied);
        else
            result = await ProcurementSliceSupport.ExecuteStore(() => store.CreateSupplier(request, support.Access(auth.Actor!, scope!, ProcurementPermissions.ManageSuppliers), ct));

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, null, "Supplier", request.Code, nameof(CreateSupplier));
    }

    public async Task<OrganizationResult<SupplierSummary>> UpdateSupplier(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        UpdateSupplierRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ManageSuppliers, ct);
        OrganizationResult<SupplierSummary> result;
        if (auth.Error is not null) result = OrganizationResult<SupplierSummary>.Failure(auth.Error);
        else if (!ProcurementSliceSupport.MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound);
        else if (ProcurementSliceSupport.DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.ConcurrencyConflict);
        else
            result = await ProcurementSliceSupport.ExecuteStore(() => store.UpdateSupplier(organizationId, id, ver, request, support.Access(auth.Actor!, scope!, ProcurementPermissions.ManageSuppliers), ct));

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, null, "Supplier", id.ToString(), nameof(UpdateSupplier));
    }

    public async Task<OrganizationResult<SupplierSummary>> SetSupplierActive(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        bool isActive,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, ProcurementPermissions.ManageSuppliers, ct);
        OrganizationResult<SupplierSummary> result;
        if (auth.Error is not null) result = OrganizationResult<SupplierSummary>.Failure(auth.Error);
        else if (!ProcurementSliceSupport.MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.SupplierNotFound);
        else if (ProcurementSliceSupport.DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<SupplierSummary>.Failure(ProcurementErrors.ConcurrencyConflict);
        else
            result = await ProcurementSliceSupport.ExecuteStore(() => store.SetSupplierActive(organizationId, id, ver, isActive, support.Access(auth.Actor!, scope!, ProcurementPermissions.ManageSuppliers), ct));

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, null, "Supplier", id.ToString(), nameof(SetSupplierActive));
    }
}
