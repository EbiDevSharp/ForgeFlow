using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Application.BusinessPartners;

public interface IBusinessPartnerStore
{
    Task<OrganizationPage<BusinessPartnerSummary>> List(
        long organizationId,
        BusinessPartnerListQuery query,
        CancellationToken ct);

    Task<BusinessPartnerDetail?> Get(
        long organizationId,
        long id,
        CancellationToken ct);

    Task<IReadOnlyList<BusinessPartnerRoleDto>> ListRoleDefinitions(
        CancellationToken ct);

    Task<OrganizationResult<BusinessPartnerDetail>> Create(
        CreateBusinessPartnerRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<BusinessPartnerDetail>> Update(
        long organizationId,
        long id,
        byte[] rowVersion,
        UpdateBusinessPartnerRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<BusinessPartnerSummary>> SetActive(
        long organizationId,
        long id,
        byte[] rowVersion,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct);
}
