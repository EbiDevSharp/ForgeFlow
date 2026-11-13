using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Users.Slices;

namespace ForgeFlow.Application.Users;

/// <summary>
/// Secured facade for user, membership, and fixed tenant-role use cases.
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Users.Slices"/>.
/// </summary>
public sealed class UserUseCases
{
    private readonly UserAccountSlices accountSlices;
    private readonly UserMembershipSlices membershipSlices;
    private readonly UserSecuritySlices securitySlices;

    public UserUseCases(
        IUserOperationsStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null)
    {
        var support = new UserSliceSupport(actors, permissions, operationRecorder, opContext);
        accountSlices = new UserAccountSlices(store, support);
        membershipSlices = new UserMembershipSlices(store, support);
        securitySlices = new UserSecuritySlices(store, support);
    }

    public Task<OrganizationResult<OrganizationPage<UserSummary>>> ListOrganizationUsers(
        OrganizationScope? scope,
        long organizationId,
        UserListQuery query,
        CancellationToken ct = default)
        => accountSlices.ListOrganizationUsersAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<UserDetails>> GetOrganizationUser(
        OrganizationScope? scope,
        long organizationId,
        long userId,
        CancellationToken ct = default)
        => accountSlices.GetOrganizationUserAsync(scope, organizationId, userId, ct);

    public Task<OrganizationResult<UserDetails>> CreateOrganizationUser(
        OrganizationScope? scope,
        CreateOrganizationUser request,
        CancellationToken ct = default)
        => accountSlices.CreateOrganizationUserAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> AttachExistingUserToOrganization(
        OrganizationScope? scope,
        AttachExistingUserToOrganization request,
        CancellationToken ct = default)
        => accountSlices.AttachExistingUserToOrganizationAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> SetUserActive(
        OrganizationScope? scope,
        SetUserActive request,
        CancellationToken ct = default)
        => accountSlices.SetUserActiveAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> UnlockUser(
        OrganizationScope? scope,
        UnlockUser request,
        CancellationToken ct = default)
        => accountSlices.UnlockUserAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> ResetUserPassword(
        OrganizationScope? scope,
        ResetUserPassword request,
        CancellationToken ct = default)
        => accountSlices.ResetUserPasswordAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> SetOrganizationMembershipActive(
        OrganizationScope? scope,
        SetOrganizationMembershipActive request,
        CancellationToken ct = default)
        => membershipSlices.SetOrganizationMembershipActiveAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> AssignSiteMembership(
        OrganizationScope? scope,
        AssignSiteMembership request,
        CancellationToken ct = default)
        => membershipSlices.AssignSiteMembershipAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> SetSiteMembershipActive(
        OrganizationScope? scope,
        SetSiteMembershipActive request,
        CancellationToken ct = default)
        => membershipSlices.SetSiteMembershipActiveAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> AssignTenantRole(
        OrganizationScope? scope,
        AssignTenantRole request,
        CancellationToken ct = default)
        => securitySlices.AssignTenantRoleAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> SetTenantRoleActive(
        OrganizationScope? scope,
        SetTenantRoleActive request,
        CancellationToken ct = default)
        => securitySlices.SetTenantRoleActiveAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> ResetUserTwoFactor(
        OrganizationScope? scope,
        ResetUserTwoFactor request,
        CancellationToken ct = default)
        => securitySlices.ResetUserTwoFactorAsync(scope, request, ct);

    public Task<OrganizationResult<UserDetails>> RevokeUserSessions(
        OrganizationScope? scope,
        RevokeUserSessions request,
        CancellationToken ct = default)
        => securitySlices.RevokeUserSessionsAsync(scope, request, ct);
}
