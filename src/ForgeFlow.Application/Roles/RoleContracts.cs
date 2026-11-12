using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Roles;

namespace ForgeFlow.Application.Roles;

public static class RolePermissions
{
    public const string RolesRead = "Roles.Read";
    public const string RolesManage = "Roles.Manage";
    public const string PermissionsAssign = "Permissions.Assign";
}

public enum RoleScopeTarget : byte
{
    Organization = 1,
    Site = 2
}

public sealed record RoleListQuery(string? Search = null, bool IncludeInactive = false, bool IncludeSystem = true, int Page = 1, int PageSize = 25);

public sealed record RoleSummary(
    long Id,
    long? OrganizationId,
    string Name,
    bool IsSystem,
    RoleScopeTarget? ScopeTarget,
    bool IsActive,
    int PermissionCount,
    string RowVersion);

public sealed record RoleDetails(
    long Id,
    long? OrganizationId,
    string Name,
    bool IsSystem,
    RoleScopeTarget? ScopeTarget,
    bool IsActive,
    string RowVersion,
    IReadOnlyList<string> Permissions);

public sealed record UserAssignedRoleDetails(
    long AssignmentId,
    long RoleId,
    string RoleName,
    bool IsSystem,
    RoleScopeTarget ScopeTarget,
    long? SiteId,
    bool IsActive,
    string RowVersion);

public sealed record UserDirectPermissionDetails(
    long GrantId,
    RoleScopeTarget ScopeTarget,
    long? SiteId,
    string PermissionCode,
    bool IsActive,
    string RowVersion);

public sealed record UserEffectivePermissionCell(
    byte ScopeKind,
    long? SiteId,
    string PermissionCode,
    bool GrantedByRole,
    bool GrantedDirectly,
    IReadOnlyList<string> GrantingRoleNames);

public sealed record UserPermissionGroup(
    byte ScopeKind,
    long? SiteId,
    ForgeFlow.Domain.Roles.PermissionModule Module,
    IReadOnlyList<UserEffectivePermissionCell> Operations);

public sealed record UserPermissionMatrix(
    long UserId,
    long OrganizationId,
    string UserName,
    bool AccountIsActive,
    bool OrganizationMembershipIsActive,
    IReadOnlyList<UserAssignedRoleDetails> AssignedRoles,
    IReadOnlyList<UserDirectPermissionDetails> DirectGrants,
    IReadOnlyList<UserEffectivePermissionCell>? EffectiveCells = null,
    IReadOnlyList<UserPermissionGroup>? Groups = null);

public sealed record CreateOrganizationRole(
    long OrganizationId,
    string? Name,
    RoleScopeTarget TargetScope,
    IReadOnlyList<string>? PermissionCodes);

public sealed record UpdateOrganizationRole(
    long OrganizationId,
    long RoleId,
    string? Name,
    bool IsActive,
    IReadOnlyList<string>? PermissionCodes,
    string? ExpectedRowVersion);

public sealed record AssignUserRole(
    long OrganizationId,
    long UserId,
    long RoleId,
    RoleScopeTarget TargetScope,
    long? SiteId = null);

public sealed record SetUserRoleAssignmentActive(
    long OrganizationId,
    long UserId,
    long AssignmentId,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record OperationGrantChange(string? PermissionCode, bool IsActive, string? ExpectedRowVersion = null);

public sealed record SaveUserOperationGrants(
    long OrganizationId,
    long UserId,
    RoleScopeTarget TargetScope,
    long? SiteId,
    IReadOnlyList<OperationGrantChange>? Changes);

public sealed class RoleStoreUnavailableException(Exception inner) : Exception(RolePermissionErrors.PersistenceUnavailable, inner);

/// <summary>Specific persistence port for custom roles, scoped role assignments, and per-user page operation grants.</summary>
public interface IRoleOperationsStore
{
    Task<OrganizationPage<RoleSummary>> ListOrganizationRoles(long organizationId, RoleListQuery query, CancellationToken ct);
    Task<RoleDetails?> GetOrganizationRole(long organizationId, long roleId, CancellationToken ct);
    Task<OrganizationResult<RoleDetails>> CreateOrganizationRole(
        long organizationId,
        RoleDefinitionName canonicalName,
        RoleScopeTarget targetScope,
        IReadOnlyList<string> permissionCodes,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<RoleDetails>> UpdateOrganizationRole(
        long organizationId,
        long roleId,
        RoleDefinitionName canonicalName,
        bool isActive,
        IReadOnlyList<string> permissionCodes,
        byte[] expectedVersion,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<UserPermissionMatrix?> GetUserPermissions(long organizationId, long userId, CancellationToken ct);
    Task<OrganizationResult<UserPermissionMatrix>> AssignUserRole(
        AssignUserRole request,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserPermissionMatrix>> SetUserRoleAssignmentActive(
        long organizationId,
        long userId,
        long assignmentId,
        bool isActive,
        byte[] expectedVersion,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserPermissionMatrix>> SaveUserOperationGrants(
        long organizationId,
        long userId,
        RoleScopeTarget targetScope,
        long? siteId,
        IReadOnlyList<(string PermissionCode, bool IsActive, byte[]? ExpectedVersion)> normalizedChanges,
        OrganizationWriteAccess access,
        CancellationToken ct);
}
