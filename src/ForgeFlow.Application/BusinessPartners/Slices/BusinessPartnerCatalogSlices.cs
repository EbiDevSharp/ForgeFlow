using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.BusinessPartners;

namespace ForgeFlow.Application.BusinessPartners.Slices;

public sealed class BusinessPartnerCatalogSlices(
    IBusinessPartnerStore store,
    BusinessPartnerSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<BusinessPartnerSummary>>> ListAsync(
        OrganizationScope? scope,
        long organizationId,
        BusinessPartnerListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, BusinessPartnerPermissions.Read, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<BusinessPartnerSummary>>.Failure(auth.Error);
        if (!BusinessPartnerSliceSupport.MatchesOrg(scope, organizationId))
            return OrganizationResult<OrganizationPage<BusinessPartnerSummary>>.Failure(BusinessPartnerErrors.AccessDenied);

        try
        {
            var bound = new BusinessPartnerListQuery(
                query.Search?.Trim(),
                query.RoleFilter,
                query.IncludeInactive,
                Math.Clamp(query.Page, 1, 1_000_000),
                Math.Clamp(query.PageSize, 1, 100),
                query.PersonTypeFilter);
            return OrganizationResult<OrganizationPage<BusinessPartnerSummary>>.Success(await store.List(organizationId, bound, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OrganizationResult<OrganizationPage<BusinessPartnerSummary>>.Failure("BusinessPartner.PersistenceUnavailable");
        }
    }

    public async Task<OrganizationResult<BusinessPartnerDetail>> GetAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, BusinessPartnerPermissions.Read, ct);
        if (auth.Error is not null) return OrganizationResult<BusinessPartnerDetail>.Failure(auth.Error);
        if (!BusinessPartnerSliceSupport.MatchesOrg(scope, organizationId))
            return OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.AccessDenied);
        if (id <= 0)
            return OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.NotFound);

        try
        {
            var found = await store.Get(organizationId, id, ct);
            return found is null
                ? OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.NotFound)
                : OrganizationResult<BusinessPartnerDetail>.Success(found);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OrganizationResult<BusinessPartnerDetail>.Failure("BusinessPartner.PersistenceUnavailable");
        }
    }

    public async Task<OrganizationResult<OrganizationPage<PartnerLookupItem>>> SearchLookupAsync(
        OrganizationScope? scope,
        long organizationId,
        PartnerLookupQuery query,
        CancellationToken ct = default)
    {
        var allowedPermissions = query.Role switch
        {
            PartnerLookupRole.Supplier => new[] { BusinessPartnerPermissions.Read, "Procurement.Read", "Procurement.ManageOrders" },
            PartnerLookupRole.Customer => new[] { BusinessPartnerPermissions.Read, "Sales.Read", "Sales.ManageOrders" },
            _ => new[] { BusinessPartnerPermissions.Read, "Procurement.Read", "Sales.Read" }
        };

        var auth = await support.AuthorizeAny(scope, allowedPermissions, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<PartnerLookupItem>>.Failure(auth.Error);
        if (!BusinessPartnerSliceSupport.MatchesOrg(scope, organizationId))
            return OrganizationResult<OrganizationPage<PartnerLookupItem>>.Failure(BusinessPartnerErrors.AccessDenied);

        try
        {
            var bound = new PartnerLookupQuery(
                query.Search?.Trim(),
                query.Role,
                Math.Clamp(query.Page, 1, 1_000_000),
                Math.Clamp(query.PageSize, 1, 100));
            return OrganizationResult<OrganizationPage<PartnerLookupItem>>.Success(await store.SearchLookup(organizationId, bound, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OrganizationResult<OrganizationPage<PartnerLookupItem>>.Failure("BusinessPartner.PersistenceUnavailable");
        }
    }

    public async Task<OrganizationResult<PartnerLookupItem?>> GetLookupItemAsync(
        OrganizationScope? scope,
        long organizationId,
        PartnerLookupRole role,
        long targetId,
        CancellationToken ct = default)
    {
        var allowedPermissions = role switch
        {
            PartnerLookupRole.Supplier => new[] { BusinessPartnerPermissions.Read, "Procurement.Read", "Procurement.ManageOrders" },
            PartnerLookupRole.Customer => new[] { BusinessPartnerPermissions.Read, "Sales.Read", "Sales.ManageOrders" },
            _ => new[] { BusinessPartnerPermissions.Read, "Procurement.Read", "Sales.Read" }
        };

        var auth = await support.AuthorizeAny(scope, allowedPermissions, ct);
        if (auth.Error is not null) return OrganizationResult<PartnerLookupItem?>.Failure(auth.Error);
        if (!BusinessPartnerSliceSupport.MatchesOrg(scope, organizationId))
            return OrganizationResult<PartnerLookupItem?>.Failure(BusinessPartnerErrors.AccessDenied);
        if (targetId <= 0)
            return OrganizationResult<PartnerLookupItem?>.Success(null);

        try
        {
            var item = await store.GetLookupItem(organizationId, role, targetId, ct);
            return OrganizationResult<PartnerLookupItem?>.Success(item);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OrganizationResult<PartnerLookupItem?>.Failure("BusinessPartner.PersistenceUnavailable");
        }
    }

    public async Task<IReadOnlyList<BusinessPartnerRoleDto>> ListRoleDefinitionsAsync(CancellationToken ct = default)
    {
        try
        {
            return await store.ListRoleDefinitions(ct);
        }
        catch
        {
            return
            [
                new(BusinessPartnerRoleDefinition.RoleSupplier, "SUPPLIER", "Supplier"),
                new(BusinessPartnerRoleDefinition.RoleCustomer, "CUSTOMER", "Customer"),
                new(BusinessPartnerRoleDefinition.RoleContractor, "CONTRACTOR", "Contractor")
            ];
        }
    }

    public async Task<IReadOnlyList<IranProvinceDto>> ListProvincesAsync(CancellationToken ct = default)
    {
        try
        {
            return await store.ListProvinces(ct);
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<IranCityDto>> ListCitiesAsync(int? provinceId, CancellationToken ct = default)
    {
        try
        {
            return await store.ListCities(provinceId, ct);
        }
        catch
        {
            return [];
        }
    }

    public async Task<OrganizationResult<BusinessPartnerDetail>> CreateAsync(
        OrganizationScope? scope,
        CreateBusinessPartnerRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, BusinessPartnerPermissions.Manage, ct);
        OrganizationResult<BusinessPartnerDetail> result;
        if (auth.Error is not null) result = OrganizationResult<BusinessPartnerDetail>.Failure(auth.Error);
        else if (!BusinessPartnerSliceSupport.MatchesOrganizationOnly(scope, request.OrganizationId))
            result = OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.AccessDenied);
        else
        {
            try
            {
                result = await store.Create(request, support.Access(auth.Actor!, scope!, BusinessPartnerPermissions.Manage), ct);
            }
            catch (BusinessPartnerRuleException ex)
            {
                result = OrganizationResult<BusinessPartnerDetail>.Failure(ex.Code);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = OrganizationResult<BusinessPartnerDetail>.Failure("BusinessPartner.PersistenceUnavailable");
            }
        }

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, "BusinessPartner", request.Code ?? "AUTO", nameof(CreateAsync));
    }

    public async Task<OrganizationResult<BusinessPartnerDetail>> UpdateAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        UpdateBusinessPartnerRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, BusinessPartnerPermissions.Manage, ct);
        OrganizationResult<BusinessPartnerDetail> result;
        if (auth.Error is not null) result = OrganizationResult<BusinessPartnerDetail>.Failure(auth.Error);
        else if (!BusinessPartnerSliceSupport.MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.NotFound);
        else if (BusinessPartnerSliceSupport.DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.Conflict);
        else
        {
            try
            {
                result = await store.Update(organizationId, id, ver, request, support.Access(auth.Actor!, scope!, BusinessPartnerPermissions.Manage), ct);
            }
            catch (BusinessPartnerRuleException ex)
            {
                result = OrganizationResult<BusinessPartnerDetail>.Failure(ex.Code);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = OrganizationResult<BusinessPartnerDetail>.Failure("BusinessPartner.PersistenceUnavailable");
            }
        }

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, "BusinessPartner", id.ToString(), nameof(UpdateAsync));
    }

    public async Task<OrganizationResult<BusinessPartnerSummary>> SetActiveAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        bool isActive,
        CancellationToken ct = default)
    {
        var auth = await support.Authorize(scope, BusinessPartnerPermissions.Manage, ct);
        OrganizationResult<BusinessPartnerSummary> result;
        if (auth.Error is not null) result = OrganizationResult<BusinessPartnerSummary>.Failure(auth.Error);
        else if (!BusinessPartnerSliceSupport.MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<BusinessPartnerSummary>.Failure(BusinessPartnerErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<BusinessPartnerSummary>.Failure(BusinessPartnerErrors.NotFound);
        else if (BusinessPartnerSliceSupport.DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<BusinessPartnerSummary>.Failure(BusinessPartnerErrors.Conflict);
        else
        {
            try
            {
                result = await store.SetActive(organizationId, id, ver, isActive, support.Access(auth.Actor!, scope!, BusinessPartnerPermissions.Manage), ct);
            }
            catch (BusinessPartnerRuleException ex)
            {
                result = OrganizationResult<BusinessPartnerSummary>.Failure(ex.Code);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = OrganizationResult<BusinessPartnerSummary>.Failure("BusinessPartner.PersistenceUnavailable");
            }
        }

        return await support.AuditFailureIfRejected(result, auth.Actor, scope, organizationId, "BusinessPartner", id.ToString(), nameof(SetActiveAsync));
    }
}
