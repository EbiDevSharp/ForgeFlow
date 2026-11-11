namespace ForgeFlow.Domain.Roles;

public enum PermissionModule : byte
{
    Organizations = 1,
    Sites = 2,
    Users = 3,
    Roles = 4,
    Audit = 5,
    Units = 6,
    Items = 7,
    Boms = 8,
    ProductionOrders = 9,
    Inventory = 10,
    Reports = 11,
    Diagnostics = 12,
    Quality = 13,
    Procurement = 14
}

public enum PermissionScopeApplicability : byte
{
    PlatformOnly = 0,
    OrganizationOnly = 1,
    OrganizationOrSite = 2
}

public sealed record PermissionDescriptor(
    string Code,
    PermissionModule Module,
    string ActionKey,
    PermissionScopeApplicability Applicability);

/// <summary>Single authoritative permission catalog and invariant checks for implemented pages and operations.</summary>
public static class PermissionCatalog
{
    public const long OrganizationAdministratorRoleId = 1;
    public const long SiteOperatorRoleId = 2;
    public const long DeploymentProvisionerRoleId = 3;
    public const long MaxSystemRoleId = 3;

    public static readonly IReadOnlyList<PermissionDescriptor> All =
    [
        new("Organizations.Read", PermissionModule.Organizations, "Read", PermissionScopeApplicability.OrganizationOnly),
        new("Organizations.Create", PermissionModule.Organizations, "Create", PermissionScopeApplicability.PlatformOnly),
        new("Organizations.Update", PermissionModule.Organizations, "Update", PermissionScopeApplicability.OrganizationOnly),
        new("Organizations.SetActive", PermissionModule.Organizations, "SetActive", PermissionScopeApplicability.OrganizationOnly),

        new("Sites.Read", PermissionModule.Sites, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("Sites.Create", PermissionModule.Sites, "Create", PermissionScopeApplicability.OrganizationOnly),
        new("Sites.Update", PermissionModule.Sites, "Update", PermissionScopeApplicability.OrganizationOrSite),
        new("Sites.SetActive", PermissionModule.Sites, "SetActive", PermissionScopeApplicability.OrganizationOrSite),

        new("Users.Read", PermissionModule.Users, "Read", PermissionScopeApplicability.OrganizationOnly),
        new("Users.Create", PermissionModule.Users, "Create", PermissionScopeApplicability.OrganizationOnly),
        new("Users.SetActive", PermissionModule.Users, "SetActive", PermissionScopeApplicability.OrganizationOnly),
        new("Users.Unlock", PermissionModule.Users, "Unlock", PermissionScopeApplicability.OrganizationOnly),
        new("Users.ResetPassword", PermissionModule.Users, "ResetPassword", PermissionScopeApplicability.OrganizationOnly),
        new("Users.AttachOrganization", PermissionModule.Users, "AttachOrganization", PermissionScopeApplicability.PlatformOnly),
        new("Memberships.Manage", PermissionModule.Users, "ManageMemberships", PermissionScopeApplicability.OrganizationOnly),
        new("Roles.AssignFixed", PermissionModule.Users, "AssignFixedRoles", PermissionScopeApplicability.OrganizationOnly),

        new("Roles.Read", PermissionModule.Roles, "Read", PermissionScopeApplicability.OrganizationOnly),
        new("Roles.Manage", PermissionModule.Roles, "Manage", PermissionScopeApplicability.OrganizationOnly),
        new("Permissions.Assign", PermissionModule.Roles, "AssignPermissions", PermissionScopeApplicability.OrganizationOnly),

        new("Audit.Read", PermissionModule.Audit, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("Audit.Export", PermissionModule.Audit, "ExportAudit", PermissionScopeApplicability.OrganizationOrSite),
        new("Diagnostics.ErrorLogsRead", PermissionModule.Diagnostics, "ReadErrorLogs", PermissionScopeApplicability.OrganizationOnly),
        new("Diagnostics.ErrorLogsManage", PermissionModule.Diagnostics, "ManageErrorLogs", PermissionScopeApplicability.PlatformOnly),

        new("Units.Read", PermissionModule.Units, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("Units.Manage", PermissionModule.Units, "ManageUnits", PermissionScopeApplicability.OrganizationOnly),

        new("Items.Read", PermissionModule.Items, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("Items.Manage", PermissionModule.Items, "ManageItems", PermissionScopeApplicability.OrganizationOnly),

        new("Boms.Read", PermissionModule.Boms, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("Boms.Manage", PermissionModule.Boms, "ManageBoms", PermissionScopeApplicability.OrganizationOnly),

        new("ProductionOrders.Read", PermissionModule.ProductionOrders, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("ProductionOrders.Manage", PermissionModule.ProductionOrders, "ManageProductionOrders", PermissionScopeApplicability.OrganizationOrSite),

        new("Inventory.Read", PermissionModule.Inventory, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("Inventory.Manage", PermissionModule.Inventory, "ManageInventory", PermissionScopeApplicability.OrganizationOrSite),
        new("Reports.Read", PermissionModule.Reports, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("Reports.Export", PermissionModule.Reports, "ExportReports", PermissionScopeApplicability.OrganizationOrSite),
        new("Quality.Read", PermissionModule.Quality, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("Quality.Manage", PermissionModule.Quality, "ManageQuality", PermissionScopeApplicability.OrganizationOrSite),
        new("Procurement.Read", PermissionModule.Procurement, "Read", PermissionScopeApplicability.OrganizationOrSite),
        new("Procurement.ManageSuppliers", PermissionModule.Procurement, "ManageSuppliers", PermissionScopeApplicability.OrganizationOnly),
        new("Procurement.ManageOrders", PermissionModule.Procurement, "ManageOrders", PermissionScopeApplicability.OrganizationOrSite),
        new("Procurement.ReceiveOrders", PermissionModule.Procurement, "ReceiveOrders", PermissionScopeApplicability.OrganizationOrSite)
    ];

    private static readonly Dictionary<string, PermissionDescriptor> ByCode =
        All.ToDictionary(d => d.Code, StringComparer.Ordinal);

    public static bool TryGet(string? code, out PermissionDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            descriptor = null!;
            return false;
        }
        return ByCode.TryGetValue(code.Trim(), out descriptor!);
    }

    public static IReadOnlyList<string> ValidateTenantPermissions(byte targetScopeKind, IEnumerable<string>? rawCodes)
    {
        if (targetScopeKind is not (1 or 2))
            throw new RoleRuleException(RolePermissionErrors.PermissionScopeInvalid, ("field", "ScopeKind"));

        if (rawCodes is null)
            return [];

        var validated = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in rawCodes)
        {
            if (!TryGet(raw, out var descriptor))
                throw new RoleRuleException(RolePermissionErrors.PermissionUnknown, ("field", "Permissions"));

            if (descriptor.Applicability == PermissionScopeApplicability.PlatformOnly)
                throw new RoleRuleException(RolePermissionErrors.EscalationForbidden);

            if (targetScopeKind == 2 && descriptor.Applicability != PermissionScopeApplicability.OrganizationOrSite)
                throw new RoleRuleException(RolePermissionErrors.PermissionScopeInvalid, ("field", "ScopeKind"));

            validated.Add(descriptor.Code);
        }

        return validated.ToArray();
    }

    public static void EnsureCustomRoleMutable(long roleId, bool isSystem)
    {
        if (isSystem || roleId is >= 1 and <= MaxSystemRoleId)
            throw new RoleRuleException(RolePermissionErrors.SystemRoleImmutable);
    }

    public static void EnsureNotSelfAdminRevocation(long actorUserId, long targetUserId, long roleId)
    {
        if (actorUserId == targetUserId && roleId == OrganizationAdministratorRoleId)
            throw new RoleRuleException(RolePermissionErrors.SelfDeactivationForbidden);
    }

    public static bool IsGrantedToBuiltInOrganizationAdmin(string permissionCode, byte scopeKind)
    {
        if (!TryGet(permissionCode, out var descriptor))
            return false;

        return scopeKind switch
        {
            1 => descriptor.Applicability != PermissionScopeApplicability.PlatformOnly,
            2 => descriptor.Applicability == PermissionScopeApplicability.OrganizationOrSite
                 && descriptor.Module is PermissionModule.Sites or PermissionModule.Audit or PermissionModule.Units or PermissionModule.Items or PermissionModule.Boms or PermissionModule.ProductionOrders or PermissionModule.Inventory or PermissionModule.Reports or PermissionModule.Quality or PermissionModule.Procurement,
            _ => false
        };
    }
}
