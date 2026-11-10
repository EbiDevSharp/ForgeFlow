namespace ForgeFlow.Domain.Users;

public enum TenantRoleKind : byte
{
    OrganizationAdministrator = 1,
    SiteOperator = 2
}

/// <summary>Pure domain invariants for user/membership lifecycle and self/last-admin safeguards.</summary>
public static class TenantAccessPolicy
{
    public static void ValidateTenantRoleScope(TenantRoleKind role, long organizationId, long? siteId)
    {
        if (!Enum.IsDefined(role))
            throw new UserRuleException(UserManagementErrors.RoleScopeInvalid, ("field", "Role"));

        if (organizationId <= 0)
            throw new UserRuleException(UserManagementErrors.RoleScopeInvalid, ("field", "OrganizationId"));

        if (role == TenantRoleKind.OrganizationAdministrator && siteId is not null)
            throw new UserRuleException(UserManagementErrors.RoleScopeInvalid, ("field", "SiteId"));

        if (role == TenantRoleKind.SiteOperator && siteId is not > 0)
            throw new UserRuleException(UserManagementErrors.RoleScopeInvalid, ("field", "SiteId"));
    }

    public static void EnsureNotSelfDeactivation(long actorUserId, long targetUserId)
    {
        if (actorUserId == targetUserId)
            throw new UserRuleException(UserManagementErrors.SelfDeactivationForbidden);
    }

    public static void EnsureOrganizationRetainsActiveAdmin(bool organizationIsActive, int activeAdminCountAfterChange)
    {
        if (organizationIsActive && activeAdminCountAfterChange < 1)
            throw new UserRuleException(UserManagementErrors.LastOrganizationAdminRequired);
    }

    public static void EnsurePlatformRetainsActiveProvisioner(int activeProvisionerCountAfterChange)
    {
        if (activeProvisionerCountAfterChange < 1)
            throw new UserRuleException(UserManagementErrors.LastPlatformAdminRequired);
    }

    public static void EnsureSiteMembershipPrerequisites(bool organizationIsActive, bool siteIsActive, bool organizationMembershipIsActive)
    {
        if (!organizationIsActive)
            throw new UserRuleException(UserManagementErrors.ParentOrganizationInactive);

        if (!organizationMembershipIsActive)
            throw new UserRuleException(UserManagementErrors.OrganizationMembershipRequired);

        if (!siteIsActive)
            throw new UserRuleException(UserManagementErrors.ParentSiteInactive);
    }

    public static void EnsureTenantRolePrerequisites(
        TenantRoleKind role,
        bool organizationIsActive,
        bool organizationMembershipIsActive,
        bool siteIsActive,
        bool siteMembershipIsActive)
    {
        if (!Enum.IsDefined(role))
            throw new UserRuleException(UserManagementErrors.RoleScopeInvalid, ("field", "Role"));

        if (!organizationIsActive)
            throw new UserRuleException(UserManagementErrors.ParentOrganizationInactive);

        if (!organizationMembershipIsActive)
            throw new UserRuleException(UserManagementErrors.OrganizationMembershipRequired);

        if (role == TenantRoleKind.SiteOperator)
        {
            if (!siteIsActive)
                throw new UserRuleException(UserManagementErrors.ParentSiteInactive);

            if (!siteMembershipIsActive)
                throw new UserRuleException(UserManagementErrors.SiteMembershipRequired);
        }
    }
}
