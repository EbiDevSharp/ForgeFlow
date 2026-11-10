using System.Globalization;
using ForgeFlow.Domain.Users;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class UserDomainTests
{
    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void Usernames_canonicalize_consistently_independent_of_current_culture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var parsed = UserAccountName.Parse("  i.Admin_01+org@forge-flow.local  ");
            Assert.Equal("i.Admin_01+org@forge-flow.local", parsed.Value);
            Assert.Equal("I.ADMIN_01+ORG@FORGE-FLOW.LOCAL", parsed.Normalized);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ali\tuser")]
    [InlineData("ali user")]
    [InlineData("کاربر")]
    [InlineData("user!name")]
    [InlineData("user\nname")]
    public void Invalid_usernames_fail_with_stable_code_and_safe_parameters(string? raw)
    {
        var ex = Assert.Throws<UserRuleException>(() => UserAccountName.Parse(raw));
        Assert.Equal(UserManagementErrors.UserNameInvalid, ex.Code);
        Assert.Equal("UserName", ex.Parameters["field"]);
        Assert.Equal(UserAccountName.MaxLength, ex.Parameters["maxLength"]);
    }

    [Fact]
    public void Username_length_boundary_accepts_128_and_rejects_129()
    {
        var max = new string('a', UserAccountName.MaxLength);
        var parsed = UserAccountName.Parse(max);
        Assert.Equal(UserAccountName.MaxLength, parsed.Value.Length);

        var ex = Assert.Throws<UserRuleException>(() => UserAccountName.Parse(max + "b"));
        Assert.Equal(UserManagementErrors.UserNameInvalid, ex.Code);
    }

    [Fact]
    public void Tenant_role_scope_validation_rejects_mismatched_site_and_organization()
    {
        TenantAccessPolicy.ValidateTenantRoleScope(TenantRoleKind.OrganizationAdministrator, 10, null);
        TenantAccessPolicy.ValidateTenantRoleScope(TenantRoleKind.SiteOperator, 10, 20);

        Assert.Equal(
            UserManagementErrors.RoleScopeInvalid,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.ValidateTenantRoleScope(TenantRoleKind.OrganizationAdministrator, 10, 20)).Code);
        Assert.Equal(
            UserManagementErrors.RoleScopeInvalid,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.ValidateTenantRoleScope(TenantRoleKind.SiteOperator, 10, null)).Code);
        Assert.Equal(
            UserManagementErrors.RoleScopeInvalid,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.ValidateTenantRoleScope(TenantRoleKind.SiteOperator, 0, 20)).Code);
        Assert.Equal(
            UserManagementErrors.RoleScopeInvalid,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.ValidateTenantRoleScope((TenantRoleKind)99, 10, null)).Code);
    }

    [Fact]
    public void Self_deactivation_and_last_admin_safeguards_throw_expected_codes()
    {
        Assert.Equal(
            UserManagementErrors.SelfDeactivationForbidden,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.EnsureNotSelfDeactivation(42, 42)).Code);
        TenantAccessPolicy.EnsureNotSelfDeactivation(42, 43);

        Assert.Equal(
            UserManagementErrors.LastOrganizationAdminRequired,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.EnsureOrganizationRetainsActiveAdmin(organizationIsActive: true, activeAdminCountAfterChange: 0)).Code);
        TenantAccessPolicy.EnsureOrganizationRetainsActiveAdmin(organizationIsActive: false, activeAdminCountAfterChange: 0);
        TenantAccessPolicy.EnsureOrganizationRetainsActiveAdmin(organizationIsActive: true, activeAdminCountAfterChange: 1);

        Assert.Equal(
            UserManagementErrors.LastPlatformAdminRequired,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.EnsurePlatformRetainsActiveProvisioner(0)).Code);
        TenantAccessPolicy.EnsurePlatformRetainsActiveProvisioner(1);
    }

    [Fact]
    public void Membership_and_role_prerequisites_validate_active_hierarchy()
    {
        Assert.Equal(
            UserManagementErrors.ParentOrganizationInactive,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.EnsureSiteMembershipPrerequisites(false, true, true)).Code);
        Assert.Equal(
            UserManagementErrors.OrganizationMembershipRequired,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.EnsureSiteMembershipPrerequisites(true, true, false)).Code);
        Assert.Equal(
            UserManagementErrors.ParentSiteInactive,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.EnsureSiteMembershipPrerequisites(true, false, true)).Code);
        TenantAccessPolicy.EnsureSiteMembershipPrerequisites(true, true, true);

        Assert.Equal(
            UserManagementErrors.SiteMembershipRequired,
            Assert.Throws<UserRuleException>(() => TenantAccessPolicy.EnsureTenantRolePrerequisites(TenantRoleKind.SiteOperator, true, true, true, false)).Code);
        TenantAccessPolicy.EnsureTenantRolePrerequisites(TenantRoleKind.OrganizationAdministrator, true, true, false, false);
        TenantAccessPolicy.EnsureTenantRolePrerequisites(TenantRoleKind.SiteOperator, true, true, true, true);
    }
}
