using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Users;

namespace ForgeFlow.Application.Users.Slices;

/// <summary>
/// Operational vertical slice for tenant role assignments, 2FA emergency resets, and session revocations.
/// </summary>
public sealed class UserSecuritySlices(
    IUserOperationsStore store,
    UserSliceSupport support)
{
    public async Task<OrganizationResult<UserDetails>> AssignTenantRoleAsync(
        OrganizationScope? scope,
        AssignTenantRole request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.RolesAssignFixed;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!UserSliceSupport.OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                TenantAccessPolicy.ValidateTenantRoleScope(request.Role, request.OrganizationId, request.SiteId);
                res = await UserSliceSupport.Execute(() => store.AssignTenantRole(request, support.Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = UserSliceSupport.FailFromRule<UserDetails>(ex); }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "TenantRole", request.UserId.ToString(CultureInfo.InvariantCulture), "AssignTenantRole");
    }

    public async Task<OrganizationResult<UserDetails>> SetTenantRoleActiveAsync(
        OrganizationScope? scope,
        SetTenantRoleActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.RolesAssignFixed;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!UserSliceSupport.OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                TenantAccessPolicy.ValidateTenantRoleScope(request.Role, request.OrganizationId, request.SiteId);
                if (!request.IsActive && request.Role == TenantRoleKind.OrganizationAdministrator)
                    TenantAccessPolicy.EnsureNotSelfDeactivation(auth.Actor!.ActorId, request.UserId);

                if (UserSliceSupport.DecodeVersion(request.ExpectedRoleRowVersion) is not byte[] version)
                    res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
                else
                    res = await UserSliceSupport.Execute(() => store.SetTenantRoleActive(
                        request.OrganizationId, request.UserId, request.Role, request.SiteId, request.IsActive, version, support.Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = UserSliceSupport.FailFromRule<UserDetails>(ex); }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "TenantRole", request.UserId.ToString(CultureInfo.InvariantCulture), "SetTenantRoleActive");
    }

    public async Task<OrganizationResult<UserDetails>> ResetUserTwoFactorAsync(
        OrganizationScope? scope,
        ResetUserTwoFactor request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersResetTwoFactor;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!UserSliceSupport.MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            res = OrganizationResult<UserDetails>.Failure(scopeError!);
        else if (request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (auth.Actor!.ActorId == request.UserId)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.CannotResetOwnTwoFactor);
        else if (!request.IdentityVerified || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 300)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.TwoFactorReasonRequired);
        else if (UserSliceSupport.DecodeVersion(request.ExpectedAccountRowVersion) is not byte[] version)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
        else
            res = await UserSliceSupport.Execute(() => store.ResetUserTwoFactor(
                request.OrganizationId, request.UserId, request.Reason.Trim(), version, support.Access(auth.Actor!, scope!, permission), ct));

        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "ResetUserTwoFactor");
    }

    public async Task<OrganizationResult<UserDetails>> RevokeUserSessionsAsync(
        OrganizationScope? scope,
        RevokeUserSessions request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersResetPassword;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!UserSliceSupport.MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            res = OrganizationResult<UserDetails>.Failure(scopeError!);
        else if (request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (UserSliceSupport.DecodeVersion(request.ExpectedAccountRowVersion) is not byte[] version)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
        else
            res = await UserSliceSupport.Execute(() => store.RevokeUserSessions(
                request.OrganizationId, request.UserId, version, support.Access(auth.Actor!, scope!, permission), ct));

        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "RevokeUserSessions");
    }
}
