using System.Globalization;
using ForgeFlow.Domain.Organizations;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class OrganizationDomainTests
{
    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void Codes_are_ASCII_canonical_identifiers_in_every_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var organization = Organization.Create("  i-1_a  ", "  شرکت نمونه  ");
            var site = organization.CreateSite(" site_2-b ", " Plant A ");
            Assert.Equal("I-1_A", organization.Code);
            Assert.Equal("SITE_2-B", site.Code);
            Assert.Equal("شرکت نمونه", organization.Name);
            Assert.Equal("Plant A", site.Name);
            Assert.Same(organization, site.Organization);
            Assert.True(organization.IsActive);
            Assert.True(site.IsActive);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-ABC")]
    [InlineData("_ABC")]
    [InlineData("A B")]
    [InlineData("A\tB")]
    [InlineData("A\n")]
    [InlineData("\u00a0ABC")]
    [InlineData("A\u00a0")]
    [InlineData("کد")]
    [InlineData("۱۲۳")]
    [InlineData("١٢٣")]
    [InlineData("ＡBC")]
    [InlineData("İTEM")]
    [InlineData("A/B")]
    public void Invalid_codes_are_rejected_without_leaking_raw_input(string? code)
    {
        var organization = Organization.Create("ORG", "Company");
        var orgError = Assert.Throws<OrganizationSiteRuleException>(() => Organization.Create(code, "Company"));
        var siteError = Assert.Throws<OrganizationSiteRuleException>(() => organization.CreateSite(code, "Plant"));
        Assert.Equal(OrganizationSiteErrors.OrganizationCodeInvalid, orgError.Code);
        Assert.Equal(OrganizationSiteErrors.SiteCodeInvalid, siteError.Code);
        Assert.Equal(orgError.Code, orgError.Message);
        Assert.Equal("Code", orgError.Parameters["field"]);
        Assert.Equal(32, orgError.Parameters["maxLength"]);
        Assert.Equal(2, orgError.Parameters.Count);
        Assert.Empty(organization.Sites);
    }

    [Fact]
    public void Code_and_name_boundaries_are_checked_after_documented_trimming()
    {
        Assert.Equal("1", Organization.Create("1", "A").Code);
        var organization = Organization.Create(" " + new string('a', 32) + " ", new string('ن', 200));
        Assert.Equal(new string('A', 32), organization.Code);
        Assert.Equal(200, organization.Name.Length);
        Assert.Equal(32, organization.CreateSite(new string('b', 32), new string('X', 200)).Code.Length);
        Assert.Throws<OrganizationSiteRuleException>(() => Organization.Create(new string('A', 33), "A"));
        Assert.Throws<OrganizationSiteRuleException>(() => organization.CreateSite(new string('B', 33), "A"));
        Assert.Throws<OrganizationSiteRuleException>(() => Organization.Create("A", new string('X', 201)));
        Assert.Throws<OrganizationSiteRuleException>(() => organization.CreateSite("B", new string('X', 201)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \u00a0 ")]
    [InlineData("\tName")]
    [InlineData("Name\n")]
    [InlineData("Na\0me")]
    public void Invalid_names_fail_creation_and_rename_atomically(string? name)
    {
        var organization = Organization.Create("ORG", "Company");
        var site = organization.CreateSite("SITE", "Plant");
        Assert.Equal(OrganizationSiteErrors.OrganizationNameInvalid,
            Assert.Throws<OrganizationSiteRuleException>(() => Organization.Create("ORG", name)).Code);
        Assert.Equal(OrganizationSiteErrors.SiteNameInvalid,
            Assert.Throws<OrganizationSiteRuleException>(() => organization.CreateSite("OTHER", name)).Code);
        Assert.Equal(OrganizationSiteErrors.OrganizationNameInvalid,
            Assert.Throws<OrganizationSiteRuleException>(() => organization.Rename(name)).Code);
        Assert.Equal(OrganizationSiteErrors.SiteNameInvalid,
            Assert.Throws<OrganizationSiteRuleException>(() => site.Rename(name)).Code);
        Assert.Equal("Company", organization.Name);
        Assert.Equal("Plant", site.Name);
        Assert.Single(organization.Sites);
    }

    [Fact]
    public void Rename_preserves_business_text_without_translation_and_reports_no_ops()
    {
        var organization = Organization.Create("ORG", "Company");
        var site = organization.CreateSite("S1", "Plant");
        Assert.True(organization.Rename(" شرکت ی ک ي ك "));
        Assert.Equal("شرکت ی ک ي ك", organization.Name);
        Assert.False(organization.Rename(" شرکت ی ک ي ك "));
        Assert.True(site.Rename("سایت\u200cیک"));
        Assert.Equal("سایت\u200cیک", site.Name);
        Assert.False(site.Rename(" سایت\u200cیک "));
        Assert.Equal("ORG", organization.Code);
        Assert.Equal("S1", site.Code);
    }

    [Fact]
    public void Active_sites_prevent_parent_deactivation_without_cascading()
    {
        var organization = Organization.Create("ORG", "Company");
        var first = organization.CreateSite("S1", "Same name");
        var second = organization.CreateSite("S2", "Same name");
        first.Deactivate();
        var error = Assert.Throws<OrganizationSiteRuleException>(() => organization.Deactivate());
        Assert.Equal(OrganizationSiteErrors.OrganizationHasActiveSites, error.Code);
        Assert.Empty(error.Parameters);
        Assert.True(organization.IsActive);
        Assert.False(first.IsActive);
        Assert.True(second.IsActive);
        second.Deactivate();
        Assert.True(organization.Deactivate());
        Assert.False(organization.Deactivate());
        Assert.False(organization.IsActive);
        Assert.True(organization.Activate());
        Assert.False(organization.Activate());
        Assert.All(organization.Sites, site => Assert.False(site.IsActive));
        Assert.True(first.Activate());
        Assert.False(first.Activate());
        Assert.True(first.Deactivate());
        Assert.False(first.Deactivate());
    }

    [Fact]
    public void Inactive_parent_blocks_site_mutations_and_ordinary_organization_rename()
    {
        var organization = Organization.Create("ORG", "Company");
        var site = organization.CreateSite("S1", "Plant");
        site.Deactivate();
        organization.Deactivate();
        Action[] actions = [() => organization.CreateSite("S2", "Other"), () => site.Activate(),
            () => site.Deactivate(), () => site.Rename("Changed"), () => organization.Rename("Changed")];
        foreach (var action in actions)
            Assert.Equal(OrganizationSiteErrors.OrganizationInactive,
                Assert.Throws<OrganizationSiteRuleException>(action).Code);
        Assert.Single(organization.Sites);
        Assert.Equal("Plant", site.Name);
        Assert.Equal("Company", organization.Name);
        Assert.False(site.IsActive);
    }

    [Fact]
    public void Inactive_site_codes_remain_reserved_but_another_organization_can_use_them()
    {
        var alpha = Organization.Create("ALPHA", "Company");
        var beta = Organization.Create("BETA", "Company");
        var site = alpha.CreateSite("s1", "Plant");
        site.Deactivate();
        Assert.Equal(OrganizationSiteErrors.SiteCodeDuplicate,
            Assert.Throws<OrganizationSiteRuleException>(() => alpha.CreateSite(" S1 ", "Other")).Code);
        Assert.Single(alpha.Sites);
        Assert.NotSame(site.Organization, beta.CreateSite("s1", "Plant").Organization);
        Assert.False(site.IsActive);
    }

    [Fact]
    public void Sites_cannot_be_removed_through_the_exposed_collection()
    {
        var organization = Organization.Create("ORG", "Company");
        organization.CreateSite("S1", "Plant");
        var collection = Assert.IsAssignableFrom<ICollection<Site>>(organization.Sites);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Single(organization.Sites);
        Assert.Throws<OrganizationSiteRuleException>(() => organization.Deactivate());
    }
}
