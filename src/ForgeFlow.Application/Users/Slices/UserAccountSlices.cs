using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Users;

namespace ForgeFlow.Application.Users.Slices;

/// <summary>
/// Operational vertical slice for user accounts, creation, profile queries, password resets, and lockouts.
/// </summary>
public sealed class UserAccountSlices(
    IUserOperationsStore store,
    UserSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<UserSummary>>> ListOrganizationUsersAsync(
        OrganizationScope? scope,
        long organizationId,
        UserListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UserPermissions.UsersRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<UserSummary>>.Failure(auth.Error);
        if (!UserSliceSupport.OrganizationMatches(scope, organizationId)) return OrganizationResult<OrganizationPage<UserSummary>>.Failure(UserManagementErrors.NotFound);
        return await UserSliceSupport.Execute(async () => OrganizationResult<OrganizationPage<UserSummary>>.Success(
            await store.ListOrganizationUsers(organizationId, UserSliceSupport.Bound(query), ct)));
    }

    public async Task<OrganizationResult<UserDetails>> GetOrganizationUserAsync(
        OrganizationScope? scope,
        long organizationId,
        long userId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, UserPermissions.UsersRead, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);
        if (!UserSliceSupport.OrganizationMatches(scope, organizationId) || userId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

        return await UserSliceSupport.Execute(async () =>
        {
            var value = await store.GetOrganizationUser(organizationId, userId, ct);
            return value is null
                ? OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound)
                : OrganizationResult<UserDetails>.Success(value);
        });
    }

    public async Task<OrganizationResult<UserDetails>> CreateOrganizationUserAsync(
        OrganizationScope? scope,
        CreateOrganizationUser request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersCreate;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!UserSliceSupport.OrganizationMatches(scope, request.OrganizationId))
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                var canonicalUserName = UserAccountName.Parse(request.UserName);
                if (!UserSliceSupport.HasValidInitialPassword(request.InitialPassword))
                    res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.PasswordInvalid, new Dictionary<string, object> { ["field"] = "InitialPassword" });
                else if (!UserSliceSupport.TryNormalizeSiteIds(request.SiteIds, out var siteIds))
                    res = OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
                else
                    res = await UserSliceSupport.Execute(() => store.CreateOrganizationUser(
                        request, canonicalUserName, siteIds, support.Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = UserSliceSupport.FailFromRule<UserDetails>(ex); }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserName, "CreateOrganizationUser");
    }

    public async Task<OrganizationResult<UserDetails>> AttachExistingUserToOrganizationAsync(
        OrganizationScope? scope,
        AttachExistingUserToOrganization request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersAttachOrganization;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (scope is not OrganizationScope.Platform)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.AccessDenied);
        else if (request.OrganizationId <= 0)
            res = OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.OrganizationNotFound);
        else
        {
            try
            {
                var canonicalUserName = UserAccountName.Parse(request.UserName);
                if (!UserSliceSupport.TryNormalizeSiteIds(request.SiteIds, out var siteIds))
                    res = OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
                else
                    res = await UserSliceSupport.Execute(() => store.AttachExistingUserToOrganization(
                        request, canonicalUserName, siteIds, support.Access(auth.Actor!, scope, permission), ct));
            }
            catch (UserRuleException ex) { res = UserSliceSupport.FailFromRule<UserDetails>(ex); }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId > 0 ? request.OrganizationId : null, null, "User", request.UserName, "AttachExistingUserToOrganization");
    }

    public async Task<OrganizationResult<UserDetails>> SetUserActiveAsync(
        OrganizationScope? scope,
        SetUserActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersSetActive;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!UserSliceSupport.MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            res = OrganizationResult<UserDetails>.Failure(scopeError!);
        else if (request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                if (!request.IsActive)
                    TenantAccessPolicy.EnsureNotSelfDeactivation(auth.Actor!.ActorId, request.UserId);

                if (UserSliceSupport.DecodeVersion(request.ExpectedAccountRowVersion) is not byte[] version)
                    res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
                else
                    res = await UserSliceSupport.Execute(() => store.SetUserActive(
                        request.OrganizationId, request.UserId, request.IsActive, version, support.Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = UserSliceSupport.FailFromRule<UserDetails>(ex); }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "SetUserActive");
    }

    public async Task<OrganizationResult<UserDetails>> UnlockUserAsync(
        OrganizationScope? scope,
        UnlockUser request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersUnlock;
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
            res = await UserSliceSupport.Execute(() => store.UnlockUser(
                request.OrganizationId, request.UserId, version, support.Access(auth.Actor!, scope!, permission), ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "UnlockUser");
    }

    public async Task<OrganizationResult<UserDetails>> ResetUserPasswordAsync(
        OrganizationScope? scope,
        ResetUserPassword request,
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
        else if (!UserSliceSupport.HasValidInitialPassword(request.NewPassword))
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.PasswordInvalid, new Dictionary<string, object> { ["field"] = "NewPassword" });
        else if (UserSliceSupport.DecodeVersion(request.ExpectedAccountRowVersion) is not byte[] version)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
        else
            res = await UserSliceSupport.Execute(() => store.ResetUserPassword(
                request.OrganizationId, request.UserId, request.NewPassword!, version, support.Access(auth.Actor!, scope!, permission), ct, request.RequirePasswordChange));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "ResetUserPassword");
    }
}
