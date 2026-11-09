using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.Organizations;

/// <summary>
/// Owns its complete Site collection. Future persistence must load the complete lifecycle aggregate
/// under the documented transaction lock; a partial/projection collection cannot authorize a write.
/// Deployment-wide organization-code uniqueness is a future Application/SQL responsibility.
/// </summary>
public sealed class Organization
{
    private readonly List<Site> sites = [];
    private readonly ReadOnlyCollection<Site> siteView;

    // Persistence materialization only; no public invalid-state constructor.
    private Organization()
    {
        Code = null!;
        Name = null!;
        siteView = sites.AsReadOnly();
    }

    private Organization(string? code, string? name)
    {
        Code = OrganizationSiteText.Code(code, OrganizationSiteErrors.OrganizationCodeInvalid);
        Name = OrganizationSiteText.Name(name, OrganizationSiteErrors.OrganizationNameInvalid);
        siteView = sites.AsReadOnly();
    }

    // Zero denotes a new, unpersisted entity. Infrastructure identity assignment is outside 01.1.
    public long Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyList<Site> Sites => siteView;

    public static Organization Create(string? code, string? name) => new(code, name);

    public Site CreateSite(string? code, string? name)
    {
        RequireActive();
        var site = new Site(this, code, name);
        if (sites.Any(s => s.Code == site.Code))
            throw new OrganizationSiteRuleException(OrganizationSiteErrors.SiteCodeDuplicate, ("field", "Code"));
        sites.Add(site);
        return site;
    }

    public bool Rename(string? name)
    {
        RequireActive();
        var value = OrganizationSiteText.Name(name, OrganizationSiteErrors.OrganizationNameInvalid);
        if (Name == value) return false;
        Name = value;
        return true;
    }

    public bool Activate()
    {
        if (IsActive) return false;
        IsActive = true;
        return true;
    }

    public bool Deactivate()
    {
        if (sites.Any(s => s.IsActive))
            throw new OrganizationSiteRuleException(OrganizationSiteErrors.OrganizationHasActiveSites);
        if (!IsActive) return false;
        IsActive = false;
        return true;
    }

    internal void RequireActive()
    {
        if (!IsActive) throw new OrganizationSiteRuleException(OrganizationSiteErrors.OrganizationInactive);
    }
}
