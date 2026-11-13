using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Sales;

namespace ForgeFlow.Application.Sales.Slices;

/// <summary>Vertical slice handlers for Organization-shared Customer master operations.</summary>
internal sealed class CustomerSlices(ISalesOperationsStore store, SalesSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<CustomerSummary>>> ListAsync(
        OrganizationScope? scope, long organizationId, CustomerListQuery query, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<OrganizationPage<CustomerSummary>>.Failure(auth.Error);

        if (!support.TryResolveSiteScope(scope, organizationId, null, out _))
            return OrganizationResult<OrganizationPage<CustomerSummary>>.Failure(SalesErrors.AccessDenied);

        var bounded = query with
        {
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
        };

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<CustomerSummary>>.Success(
                await store.ListCustomers(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<CustomerSummary>> CreateAsync(
        OrganizationScope? scope, CreateCustomerRequest request, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.ManageCustomers, ct);
        OrganizationResult<CustomerSummary> result;

        if (auth.Error is not null)
            result = OrganizationResult<CustomerSummary>.Failure(auth.Error);
        else if (!support.MatchesOrganizationOnly(scope, request.OrganizationId))
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.AccessDenied);
        else
            result = await support.ExecuteStoreAsync(() =>
                store.CreateCustomer(request, support.UtcNow, support.Access(auth.Actor!, scope!, SalesPermissions.ManageCustomers), ct));

        return await support.AuditFailureIfRejectedAsync(result, auth.Actor, scope, request.OrganizationId, null, "Customer", request.Code, nameof(CreateAsync));
    }

    public async Task<OrganizationResult<CustomerSummary>> UpdateAsync(
        OrganizationScope? scope, long organizationId, long customerId, UpdateCustomerRequest request, string? expectedVersion, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.ManageCustomers, ct);
        OrganizationResult<CustomerSummary> result;

        if (auth.Error is not null)
            result = OrganizationResult<CustomerSummary>.Failure(auth.Error);
        else if (!support.MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.AccessDenied);
        else if (customerId <= 0)
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.CustomerNotFound);
        else if (support.DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await support.ExecuteStoreAsync(() =>
                store.UpdateCustomer(organizationId, customerId, request, version, support.UtcNow, support.Access(auth.Actor!, scope!, SalesPermissions.ManageCustomers), ct));

        return await support.AuditFailureIfRejectedAsync(result, auth.Actor, scope, organizationId, null, "Customer", customerId.ToString(), nameof(UpdateAsync));
    }

    public async Task<OrganizationResult<CustomerSummary>> SetActiveAsync(
        OrganizationScope? scope, long organizationId, long customerId, bool isActive, string? expectedVersion, CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, SalesPermissions.ManageCustomers, ct);
        OrganizationResult<CustomerSummary> result;

        if (auth.Error is not null)
            result = OrganizationResult<CustomerSummary>.Failure(auth.Error);
        else if (!support.MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.AccessDenied);
        else if (customerId <= 0)
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.CustomerNotFound);
        else if (support.DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await support.ExecuteStoreAsync(() =>
                store.SetCustomerActive(organizationId, customerId, isActive, version, support.UtcNow, support.Access(auth.Actor!, scope!, SalesPermissions.ManageCustomers), ct));

        return await support.AuditFailureIfRejectedAsync(result, auth.Actor, scope, organizationId, null, "Customer", customerId.ToString(), nameof(SetActiveAsync));
    }
}
