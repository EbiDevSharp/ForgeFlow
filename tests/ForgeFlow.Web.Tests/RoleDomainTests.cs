using System.Globalization;
using System.Reflection;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Roles;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class RoleDomainTests
{
    [Fact]
    public void Permission_catalog_covers_all_declared_application_permission_constants()
    {
        var allConstants = typeof(OrganizationPermissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Concat(typeof(UserPermissions).GetFields(BindingFlags.Public | BindingFlags.Static))
            .Concat(typeof(RolePermissions).GetFields(BindingFlags.Public | BindingFlags.Static))
            .Concat(typeof(ForgeFlow.Application.Audit.AuditPermissions).GetFields(BindingFlags.Public | BindingFlags.Static))
            .Select(f => (string)f.GetValue(null)!)
            .ToArray();

        Assert.Equal(allConstants.Length, PermissionCatalog.All.Count);
        Assert.Equal(allConstants.Length, PermissionCatalog.All.Select(d => d.Code).Distinct(StringComparer.Ordinal).Count());
        foreach (var constant in allConstants)
            Assert.True(PermissionCatalog.TryGet(constant, out _), $"Missing '{constant}' in PermissionCatalog.All.");
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void Role_names_normalize_consistently_independent_of_culture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var parsed = RoleDefinitionName.Parse("  Site_Inspector_i  ");
            Assert.Equal("Site_Inspector_i", parsed.Value);
            Assert.Equal("SITE_INSPECTOR_I", parsed.Normalized);
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
    [InlineData("role\nname")]
    [InlineData("role\tname")]
    public void Invalid_role_names_fail_with_safe_parameters(string? raw)
    {
        var ex = Assert.Throws<RoleRuleException>(() => RoleDefinitionName.Parse(raw));
        Assert.Equal(RolePermissionErrors.NameInvalid, ex.Code);
        Assert.Equal("Name", ex.Parameters["field"]);
        Assert.Equal(RoleDefinitionName.MaxLength, ex.Parameters["maxLength"]);
    }

    [Fact]
    public void Role_name_length_boundary_accepts_100_and_rejects_101()
    {
        var ok = RoleDefinitionName.Parse(new string('A', RoleDefinitionName.MaxLength));
        Assert.Equal(RoleDefinitionName.MaxLength, ok.Value.Length);
        Assert.Throws<RoleRuleException>(() => RoleDefinitionName.Parse(new string('A', RoleDefinitionName.MaxLength + 1)));
    }

    [Fact]
    public void Tenant_permission_validation_enforces_catalog_and_scope_rules()
    {
        var unknown = Assert.Throws<RoleRuleException>(() => PermissionCatalog.ValidateTenantPermissions(1, ["Sites.Unknown"]));
        Assert.Equal(RolePermissionErrors.PermissionUnknown, unknown.Code);

        var platformEscalation = Assert.Throws<RoleRuleException>(() => PermissionCatalog.ValidateTenantPermissions(1, [OrganizationPermissions.OrganizationsCreate]));
        Assert.Equal(RolePermissionErrors.EscalationForbidden, platformEscalation.Code);

        var attachEscalation = Assert.Throws<RoleRuleException>(() => PermissionCatalog.ValidateTenantPermissions(1, [UserPermissions.UsersAttachOrganization]));
        Assert.Equal(RolePermissionErrors.EscalationForbidden, attachEscalation.Code);

        var siteInvalid = Assert.Throws<RoleRuleException>(() => PermissionCatalog.ValidateTenantPermissions(2, [OrganizationPermissions.SitesCreate]));
        Assert.Equal(RolePermissionErrors.PermissionScopeInvalid, siteInvalid.Code);

        var siteValid = PermissionCatalog.ValidateTenantPermissions(2, ["Sites.Update", "Sites.Read", "Sites.Update"]);
        Assert.Equal(["Sites.Read", "Sites.Update"], siteValid);
    }

    [Fact]
    public void System_roles_and_self_admin_revocation_are_protected()
    {
        Assert.Equal(
            RolePermissionErrors.SystemRoleImmutable,
            Assert.Throws<RoleRuleException>(() => PermissionCatalog.EnsureCustomRoleMutable(1, false)).Code);
        Assert.Equal(
            RolePermissionErrors.SystemRoleImmutable,
            Assert.Throws<RoleRuleException>(() => PermissionCatalog.EnsureCustomRoleMutable(100, true)).Code);
        PermissionCatalog.EnsureCustomRoleMutable(100, false);

        Assert.Equal(
            RolePermissionErrors.SelfDeactivationForbidden,
            Assert.Throws<RoleRuleException>(() => PermissionCatalog.EnsureNotSelfAdminRevocation(42, 42, 1)).Code);
        PermissionCatalog.EnsureNotSelfAdminRevocation(42, 43, 1);
    }
}
