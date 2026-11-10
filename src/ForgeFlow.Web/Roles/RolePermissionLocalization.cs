using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Users;
using ForgeFlow.Web.Localization;
using Microsoft.Extensions.Localization;

namespace ForgeFlow.Web.Roles;

public static class RolePermissionLocalization
{
    public static readonly IReadOnlyList<PermissionModule> TenantModules =
    [
        PermissionModule.Organizations,
        PermissionModule.Sites,
        PermissionModule.Users,
        PermissionModule.Roles,
        PermissionModule.Audit
    ];

    public static readonly IReadOnlyList<PermissionDescriptor> OrganizationScopePermissions =
        PermissionCatalog.All
            .Where(d => d.Applicability != PermissionScopeApplicability.PlatformOnly)
            .ToArray();

    public static readonly IReadOnlyList<PermissionDescriptor> SiteScopePermissions =
        PermissionCatalog.All
            .Where(d => d.Applicability == PermissionScopeApplicability.OrganizationOrSite)
            .ToArray();

    public static string PageTitleKey(PermissionModule module) => module switch
    {
        PermissionModule.Organizations => "PermPageOrganizations",
        PermissionModule.Sites => "PermPageSites",
        PermissionModule.Users => "PermPageUsers",
        PermissionModule.Roles => "PermPageRoles",
        PermissionModule.Audit => "PermPageAudit",
        _ => "PermPageOrganizations"
    };

    public static string OperationLabelKey(string permissionCode) => permissionCode switch
    {
        "Organizations.Read" or "Sites.Read" or "Users.Read" or "Roles.Read" or "Audit.Read" => "PermOpRead",
        "Organizations.Create" or "Sites.Create" or "Users.Create" => "PermOpCreate",
        "Organizations.Update" or "Sites.Update" => "PermOpUpdate",
        "Organizations.SetActive" or "Sites.SetActive" or "Users.SetActive" => "PermOpSetActive",
        "Users.Unlock" => "PermOpUnlock",
        "Users.ResetPassword" => "PermOpResetPassword",
        "Memberships.Manage" => "PermOpManageMemberships",
        "Roles.AssignFixed" => "PermOpAssignFixedRoles",
        "Roles.Manage" => "PermOpManageRoles",
        "Permissions.Assign" => "PermOpAssignPermissions",
        _ => "PermOpRead"
    };

    public static string RoleDisplayName(string roleName, bool isSystemRole, IStringLocalizer<SharedResources> text)
    {
        if (!isSystemRole) return roleName;
        return roleName switch
        {
            "OrganizationAdministrator" => text["RoleBuiltInOrgAdmin"],
            "SiteOperator" => text["RoleBuiltInSiteOperator"],
            "DeploymentProvisioner" => text["RoleBuiltInProvisioner"],
            _ => roleName
        };
    }

    public static string FailureKey(string code) => code switch
    {
        RolePermissionErrors.NameInvalid => "RoleErrorInvalidName",
        RolePermissionErrors.NameDuplicate => "RoleErrorDuplicateName",
        RolePermissionErrors.SystemRoleImmutable => "RoleErrorSystemImmutable",
        RolePermissionErrors.PermissionUnknown => "RoleErrorPermissionUnknown",
        RolePermissionErrors.PermissionScopeInvalid or UserManagementErrors.RoleScopeInvalid => "RoleErrorPermissionScopeInvalid",
        RolePermissionErrors.EscalationForbidden => "ReadDenied",
        RolePermissionErrors.MembershipRequired or UserManagementErrors.OrganizationMembershipRequired => "UserErrorOrgMembershipRequired",
        RolePermissionErrors.LastOrganizationAdminRequired or UserManagementErrors.LastOrganizationAdminRequired => "UserErrorLastOrgAdmin",
        RolePermissionErrors.SelfDeactivationForbidden or UserManagementErrors.SelfDeactivationForbidden => "UserErrorSelfDeactivation",
        RolePermissionErrors.ParentOrganizationInactive or UserManagementErrors.ParentOrganizationInactive or OrganizationSiteErrors.OrganizationInactive => "WriteParentInactive",
        RolePermissionErrors.ParentSiteInactive or UserManagementErrors.ParentSiteInactive => "UserErrorParentSiteInactive",
        RolePermissionErrors.ConcurrencyConflict or UserManagementErrors.ConcurrencyConflict or OrganizationAccessErrors.ConcurrencyConflict => "WriteConflict",
        RolePermissionErrors.OutcomeUnknown or UserManagementErrors.OutcomeUnknown or OrganizationAccessErrors.OutcomeUnknown => "WriteOutcomeUnknown",
        RolePermissionErrors.AccessDenied or RolePermissionErrors.ActorRequired or RolePermissionErrors.ScopeRequired
            or UserManagementErrors.AccessDenied or UserManagementErrors.ActorRequired or UserManagementErrors.ScopeRequired
            or AuditErrors.AccessDenied or AuditErrors.ActorRequired or AuditErrors.ScopeRequired => "ReadDenied",
        RolePermissionErrors.NotFound or UserManagementErrors.NotFound or OrganizationSiteErrors.OrganizationNotFound or OrganizationSiteErrors.SiteNotFound
            or AuditErrors.NotFound => "ReadNotFound",
        AuditErrors.InvalidQuery => "ReadError",
        _ => "WriteUnavailable"
    };

    public static void ToggleOperationWithAutoRead(
        HashSet<string> selectedCodes,
        PermissionDescriptor descriptor,
        bool isChecked,
        IReadOnlyList<PermissionDescriptor> availableDescriptors)
    {
        var isRead = descriptor.ActionKey == "Read";
        if (isChecked)
        {
            selectedCodes.Add(descriptor.Code);
            if (!isRead)
            {
                var readPerm = availableDescriptors.FirstOrDefault(x => x.Module == descriptor.Module && x.ActionKey == "Read");
                if (readPerm is not null)
                    selectedCodes.Add(readPerm.Code);
            }
        }
        else
        {
            selectedCodes.Remove(descriptor.Code);
            if (isRead)
            {
                foreach (var item in availableDescriptors.Where(x => x.Module == descriptor.Module && x.ActionKey != "Read"))
                {
                    selectedCodes.Remove(item.Code);
                }
            }
        }
    }

    public static void ToggleModuleAll(
        HashSet<string> selectedCodes,
        PermissionModule module,
        bool isChecked,
        IReadOnlyList<PermissionDescriptor> availableDescriptors)
    {
        foreach (var item in availableDescriptors.Where(x => x.Module == module))
        {
            if (isChecked) selectedCodes.Add(item.Code);
            else selectedCodes.Remove(item.Code);
        }
    }
}
