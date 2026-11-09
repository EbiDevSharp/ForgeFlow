using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.Organizations;

public static class OrganizationSiteErrors
{
    public const string OrganizationCodeInvalid = "Organization.CodeInvalid";
    public const string OrganizationNameInvalid = "Organization.NameInvalid";
    public const string OrganizationCodeDuplicate = "Organization.CodeDuplicate";
    public const string OrganizationNotFound = "Organization.NotFound";
    public const string OrganizationInactive = "Organization.Inactive";
    public const string OrganizationHasActiveSites = "Organization.HasActiveSites";
    public const string SiteCodeInvalid = "Site.CodeInvalid";
    public const string SiteNameInvalid = "Site.NameInvalid";
    public const string SiteCodeDuplicate = "Site.CodeDuplicate";
    public const string SiteNotFound = "Site.NotFound";
}

/// <summary>Stable machine code and safe parameters; presentation supplies the localized message.</summary>
public sealed class OrganizationSiteRuleException : Exception
{
    internal OrganizationSiteRuleException(string code, params (string Key, object Value)[] parameters)
        : base(code)
    {
        Code = code;
        Parameters = new ReadOnlyDictionary<string, object>(parameters.ToDictionary(p => p.Key, p => p.Value));
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
}
