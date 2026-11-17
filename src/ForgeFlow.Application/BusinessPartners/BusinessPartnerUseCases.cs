using ForgeFlow.Application.BusinessPartners.Slices;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Application.BusinessPartners;

/// <summary>
/// Secured vertical-slice facade for Business Partners (Suppliers, Customers, Contractors).
/// </summary>
public sealed class BusinessPartnerUseCases(
    IBusinessPartnerStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private readonly BusinessPartnerCatalogSlices _catalogSlices = new(
        store,
        new BusinessPartnerSliceSupport(actors, permissions, operationRecorder, opContext, clock));

    public Task<OrganizationResult<OrganizationPage<BusinessPartnerSummary>>> List(
        OrganizationScope? scope,
        long organizationId,
        BusinessPartnerListQuery query,
        CancellationToken ct = default)
        => _catalogSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<BusinessPartnerDetail>> Get(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => _catalogSlices.GetAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<OrganizationPage<PartnerLookupItem>>> SearchLookup(
        OrganizationScope? scope,
        long organizationId,
        PartnerLookupQuery query,
        CancellationToken ct = default)
        => _catalogSlices.SearchLookupAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<PartnerLookupItem?>> GetLookupItem(
        OrganizationScope? scope,
        long organizationId,
        PartnerLookupRole role,
        long targetId,
        CancellationToken ct = default)
        => _catalogSlices.GetLookupItemAsync(scope, organizationId, role, targetId, ct);

    public Task<IReadOnlyList<BusinessPartnerRoleDto>> ListRoleDefinitions(CancellationToken ct = default)
        => _catalogSlices.ListRoleDefinitionsAsync(ct);

    public Task<IReadOnlyList<IranProvinceDto>> ListProvinces(CancellationToken ct = default)
        => _catalogSlices.ListProvincesAsync(ct);

    public Task<IReadOnlyList<IranCityDto>> ListCities(int? provinceId, CancellationToken ct = default)
        => _catalogSlices.ListCitiesAsync(provinceId, ct);

    public Task<OrganizationResult<BusinessPartnerDetail>> Create(
        OrganizationScope? scope,
        CreateBusinessPartnerRequest request,
        CancellationToken ct = default)
        => _catalogSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<BusinessPartnerDetail>> Update(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        UpdateBusinessPartnerRequest request,
        CancellationToken ct = default)
        => _catalogSlices.UpdateAsync(scope, organizationId, id, expectedVersion, request, ct);

    public Task<OrganizationResult<BusinessPartnerSummary>> SetActive(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        bool isActive,
        CancellationToken ct = default)
        => _catalogSlices.SetActiveAsync(scope, organizationId, id, expectedVersion, isActive, ct);
}
