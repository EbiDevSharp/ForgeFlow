namespace ForgeFlow.Domain.BusinessPartners;

public sealed class BusinessPartnerRoleDefinition
{
    public const int RoleSupplier = 1;
    public const int RoleCustomer = 2;
    public const int RoleContractor = 3;

    private BusinessPartnerRoleDefinition()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public BusinessPartnerRoleDefinition(int id, string code, string name, bool isSystem, int displayOrder)
    {
        Id = id;
        Code = code;
        Name = name;
        IsSystem = isSystem;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public int Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsSystem { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
}
