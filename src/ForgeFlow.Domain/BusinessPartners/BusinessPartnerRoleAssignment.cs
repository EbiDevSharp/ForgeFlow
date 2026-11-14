namespace ForgeFlow.Domain.BusinessPartners;

public sealed class BusinessPartnerRoleAssignment
{
    private BusinessPartnerRoleAssignment() { }

    public BusinessPartnerRoleAssignment(long organizationId, long businessPartnerId, int roleId)
    {
        OrganizationId = organizationId;
        BusinessPartnerId = businessPartnerId;
        RoleId = roleId;
    }

    public long OrganizationId { get; internal set; }
    public long BusinessPartnerId { get; internal set; }
    public int RoleId { get; private set; }
    public BusinessPartnerRoleDefinition? RoleDefinition { get; private set; }
}
