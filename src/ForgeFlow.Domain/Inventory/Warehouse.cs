namespace ForgeFlow.Domain.Inventory;

public sealed class Warehouse
{
    private Warehouse() { }

    private Warehouse(long organizationId, long siteId, string code, string name, bool isDefaultForSite)
    {
        if (organizationId <= 0) throw new InventoryRuleException(InventoryErrors.ParentOrganizationInactive);
        if (siteId <= 0) throw new InventoryRuleException(InventoryErrors.SiteInvalid);

        OrganizationId = organizationId;
        SiteId = siteId;
        Code = InventoryText.Code(code, InventoryErrors.CodeInvalid);
        Name = InventoryText.Name(name);
        IsDefaultForSite = isDefaultForSite;
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsDefaultForSite { get; private set; }
    public bool IsActive { get; private set; }

    public static Warehouse Create(long organizationId, long siteId, string? code, string? name, bool isDefaultForSite) =>
        new(organizationId, siteId, code ?? string.Empty, name ?? string.Empty, isDefaultForSite);

    public void UpdateDetails(string? name, bool isDefaultForSite)
    {
        Name = InventoryText.Name(name);
        IsDefaultForSite = IsActive && isDefaultForSite;
    }

    public void ClearDefault() => IsDefaultForSite = false;

    public bool SetActive(bool isActive)
    {
        if (IsActive == isActive) return false;
        IsActive = isActive;
        if (!isActive) IsDefaultForSite = false;
        return true;
    }
}
