using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Users;

namespace ForgeFlow.Application.Users;

public static class UserPermissions
{
    public const string UsersRead = "Users.Read";
    public const string UsersCreate = "Users.Create";
    public const string UsersSetActive = "Users.SetActive";
    public const string UsersUnlock = "Users.Unlock";
    public const string UsersResetPassword = "Users.ResetPassword";
    public const string UsersResetTwoFactor = "Users.ResetTwoFactor";
    public const string UsersAttachOrganization = "Users.AttachOrganization";
    public const string MembershipsManage = "Memberships.Manage";
    public const string RolesAssignFixed = "Roles.AssignFixed";
}

public sealed record UserListQuery(string? Search = null, bool IncludeInactive = false, int Page = 1, int PageSize = 25);

public sealed record UserSummary(
    long UserId,
    string UserName,
    bool AccountIsActive,
    bool MembershipIsActive,
    bool IsLockedOut,
    bool IsOrganizationAdmin,
    int ActiveSiteMembershipCount,
    string AccountRowVersion,
    string MembershipRowVersion);

public sealed record UserSiteMembershipDetails(
    long SiteId,
    string SiteCode,
    string SiteName,
    bool SiteIsActive,
    bool MembershipIsActive,
    bool IsSiteOperator,
    string MembershipRowVersion,
    string? SiteOperatorRoleRowVersion);

public sealed record UserDetails(
    long UserId,
    long OrganizationId,
    string UserName,
    bool AccountIsActive,
    bool MembershipIsActive,
    bool IsLockedOut,
    string? PreferredCulture,
    bool BelongsToSingleOrganization,
    bool IsOrganizationAdmin,
    string? OrganizationAdminRoleRowVersion,
    string AccountRowVersion,
    string MembershipRowVersion,
    IReadOnlyList<UserSiteMembershipDetails> Sites,
    bool TwoFactorEnabled = false,
    DateTime? TwoFactorEnabledAtUtc = null,
    int RecoveryCodesRemaining = 0,
    int ActiveSessionCount = 0);

public sealed record CreateOrganizationUser(
    long OrganizationId,
    string? UserName,
    string? InitialPassword,
    bool AssignOrganizationAdmin,
    IReadOnlyList<long>? SiteIds,
    bool AssignSiteOperatorToSites = false,
    bool RequirePasswordChange = true);

public sealed record AttachExistingUserToOrganization(
    long OrganizationId,
    string? UserName,
    bool AssignOrganizationAdmin,
    IReadOnlyList<long>? SiteIds,
    bool AssignSiteOperatorToSites = false,
    bool RequirePasswordChange = true);

public sealed record SetUserActive(long? OrganizationId, long UserId, bool IsActive, string? ExpectedAccountRowVersion);
public sealed record SetOrganizationMembershipActive(long OrganizationId, long UserId, bool IsActive, string? ExpectedMembershipRowVersion);
public sealed record AssignSiteMembership(long OrganizationId, long UserId, long SiteId, bool AssignSiteOperator = false);
public sealed record SetSiteMembershipActive(long OrganizationId, long UserId, long SiteId, bool IsActive, string? ExpectedMembershipRowVersion);
public sealed record AssignTenantRole(long OrganizationId, long UserId, TenantRoleKind Role, long? SiteId = null);
public sealed record SetTenantRoleActive(long OrganizationId, long UserId, TenantRoleKind Role, long? SiteId, bool IsActive, string? ExpectedRoleRowVersion);
public sealed record UnlockUser(long? OrganizationId, long UserId, string? ExpectedAccountRowVersion);
public sealed record ResetUserPassword(long? OrganizationId, long UserId, string? NewPassword, string? ExpectedAccountRowVersion, bool RequirePasswordChange = true);
public sealed record ResetUserTwoFactor(long? OrganizationId, long UserId, string? Reason, bool IdentityVerified, string? ExpectedAccountRowVersion);
public sealed record RevokeUserSessions(long? OrganizationId, long UserId, string? ExpectedAccountRowVersion);

public sealed class UserStoreUnavailableException(Exception inner) : Exception(UserManagementErrors.PersistenceUnavailable, inner);

/// <summary>Specific persistence port for user, membership, and fixed tenant role commands.</summary>
public interface IUserOperationsStore
{
    Task<OrganizationPage<UserSummary>> ListOrganizationUsers(long organizationId, UserListQuery query, CancellationToken ct);
    Task<UserDetails?> GetOrganizationUser(long organizationId, long userId, CancellationToken ct);
    Task<OrganizationResult<UserDetails>> CreateOrganizationUser(
        CreateOrganizationUser request,
        UserAccountName canonicalUserName,
        IReadOnlyList<long> distinctSiteIds,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserDetails>> AttachExistingUserToOrganization(
        AttachExistingUserToOrganization request,
        UserAccountName canonicalUserName,
        IReadOnlyList<long> distinctSiteIds,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserDetails>> SetUserActive(
        long? organizationId,
        long userId,
        bool isActive,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserDetails>> SetOrganizationMembershipActive(
        long organizationId,
        long userId,
        bool isActive,
        byte[] expectedMembershipVersion,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserDetails>> AssignSiteMembership(AssignSiteMembership request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<UserDetails>> SetSiteMembershipActive(
        long organizationId,
        long userId,
        long siteId,
        bool isActive,
        byte[] expectedMembershipVersion,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserDetails>> AssignTenantRole(AssignTenantRole request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<UserDetails>> SetTenantRoleActive(
        long organizationId,
        long userId,
        TenantRoleKind role,
        long? siteId,
        bool isActive,
        byte[] expectedRoleVersion,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserDetails>> UnlockUser(
        long? organizationId,
        long userId,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserDetails>> ResetUserPassword(
        long? organizationId,
        long userId,
        string newPassword,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct,
        bool requirePasswordChange = true);
    Task<OrganizationResult<UserDetails>> ResetUserTwoFactor(
        long? organizationId,
        long userId,
        string reason,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct);
    Task<OrganizationResult<UserDetails>> RevokeUserSessions(
        long? organizationId,
        long userId,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct);
}
