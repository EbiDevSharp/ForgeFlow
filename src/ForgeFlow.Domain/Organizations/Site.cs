namespace ForgeFlow.Domain.Organizations;

public sealed class Site
{
    private readonly Organization organization;

    private Site()
    {
        organization = null!;
        Code = null!;
        Name = null!;
    }

    // Only the owning aggregate can expose a new Site; no public detached-site constructor.
    internal Site(Organization organization, string? code, string? name)
    {
        this.organization = organization;
        OrganizationId = organization.Id;
        Code = OrganizationSiteText.Code(code, OrganizationSiteErrors.SiteCodeInvalid);
        Name = OrganizationSiteText.Name(name, OrganizationSiteErrors.SiteNameInvalid);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public Organization Organization => organization;
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; } = true;

    public bool Rename(string? name)
    {
        organization.RequireActive();
        var value = OrganizationSiteText.Name(name, OrganizationSiteErrors.SiteNameInvalid);
        if (Name == value) return false;
        Name = value;
        return true;
    }

    public bool Activate()
    {
        organization.RequireActive();
        if (IsActive) return false;
        IsActive = true;
        return true;
    }

    public bool Deactivate()
    {
        organization.RequireActive();
        if (!IsActive) return false;
        IsActive = false;
        return true;
    }
}
