using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Users;

namespace ForgeFlow.Application.Users;

/// <summary>Secured user, membership, and fixed tenant-role use cases; fails closed without trusted adapters.</summary>
public sealed class UserUseCases(
    IUserOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(OrganizationScope? scope, string permission, CancellationToken ct)
    {
        if (actors is null) return (null, UserManagementErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, UserManagementErrors.ActorRequired);
        if (scope is null) return (actor, UserManagementErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (actor, UserManagementErrors.AccessDenied);
        return (actor, null);
    }

    private Task<OrganizationResult<T>> RecordRejected<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long? organizationId,
        long? siteId,
        string entityType,
        string? entityRef,
        string action)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result, operationRecorder, opContext, UserOperationCatalog.UserActionRejected,
            actor, scope, organizationId, siteId, entityType, entityRef, new { Action = action });

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    private static bool OrganizationMatches(OrganizationScope? scope, long organizationId)
        => scope is OrganizationScope.Organization org && org.OrganizationId == organizationId && organizationId > 0;

    private static byte[]? DecodeVersion(string? raw)
    {
        if (raw is null) return null;
        try { var bytes = Convert.FromBase64String(raw); return bytes.Length == 8 ? bytes : null; }
        catch (FormatException) { return null; }
    }

    private static UserListQuery Bound(UserListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static bool TryNormalizeSiteIds(IReadOnlyList<long>? rawSiteIds, out IReadOnlyList<long> distinctSiteIds)
    {
        if (rawSiteIds is null || rawSiteIds.Count == 0)
        {
            distinctSiteIds = [];
            return true;
        }

        if (rawSiteIds.Any(id => id <= 0))
        {
            distinctSiteIds = [];
            return false;
        }

        distinctSiteIds = rawSiteIds.Distinct().ToArray();
        return true;
    }

    private static bool HasValidInitialPassword(string? password)
        => !string.IsNullOrWhiteSpace(password) && !password.Any(char.IsControl);

    private static async Task<OrganizationResult<T>> Execute<T>(Func<Task<OrganizationResult<T>>> operation)
    {
        try { return await operation(); }
        catch (UserStoreUnavailableException) { return OrganizationResult<T>.Failure(UserManagementErrors.PersistenceUnavailable); }
    }

    private static OrganizationResult<T> FailFromRule<T>(UserRuleException ex)
        => OrganizationResult<T>.Failure(ex.Code, ex.Parameters);

    public async Task<OrganizationResult<OrganizationPage<UserSummary>>> ListOrganizationUsers(
        OrganizationScope? scope,
        long organizationId,
        UserListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UserPermissions.UsersRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<UserSummary>>.Failure(auth.Error);
        if (!OrganizationMatches(scope, organizationId)) return OrganizationResult<OrganizationPage<UserSummary>>.Failure(UserManagementErrors.NotFound);
        return await Execute(async () => OrganizationResult<OrganizationPage<UserSummary>>.Success(
            await store.ListOrganizationUsers(organizationId, Bound(query), ct)));
    }

    public async Task<OrganizationResult<UserDetails>> GetOrganizationUser(
        OrganizationScope? scope,
        long organizationId,
        long userId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UserPermissions.UsersRead, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, organizationId) || userId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

        return await Execute(async () =>
        {
            var value = await store.GetOrganizationUser(organizationId, userId, ct);
            return value is null
                ? OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound)
                : OrganizationResult<UserDetails>.Success(value);
        });
    }

    public async Task<OrganizationResult<UserDetails>> CreateOrganizationUser(
        OrganizationScope? scope,
        CreateOrganizationUser request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersCreate;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId))
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                var canonicalUserName = UserAccountName.Parse(request.UserName);
                if (!HasValidInitialPassword(request.InitialPassword))
                    res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.PasswordInvalid, new Dictionary<string, object> { ["field"] = "InitialPassword" });
                else if (!TryNormalizeSiteIds(request.SiteIds, out var siteIds))
                    res = OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
                else
                    res = await Execute(() => store.CreateOrganizationUser(
                        request, canonicalUserName, siteIds, Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = FailFromRule<UserDetails>(ex); }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserName, "CreateOrganizationUser");
    }

    public async Task<OrganizationResult<UserDetails>> AttachExistingUserToOrganization(
        OrganizationScope? scope,
        AttachExistingUserToOrganization request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersAttachOrganization;
        var auth = await Authorize(scope, permission, ct);
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
                if (!TryNormalizeSiteIds(request.SiteIds, out var siteIds))
                    res = OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
                else
                    res = await Execute(() => store.AttachExistingUserToOrganization(
                        request, canonicalUserName, siteIds, Access(auth.Actor!, scope, permission), ct));
            }
            catch (UserRuleException ex) { res = FailFromRule<UserDetails>(ex); }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId > 0 ? request.OrganizationId : null, null, "User", request.UserName, "AttachExistingUserToOrganization");
    }

    public async Task<OrganizationResult<UserDetails>> SetUserActive(
        OrganizationScope? scope,
        SetUserActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersSetActive;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            res = OrganizationResult<UserDetails>.Failure(scopeError!);
        else if (request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                if (!request.IsActive)
                    TenantAccessPolicy.EnsureNotSelfDeactivation(auth.Actor!.ActorId, request.UserId);

                if (DecodeVersion(request.ExpectedAccountRowVersion) is not byte[] version)
                    res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
                else
                    res = await Execute(() => store.SetUserActive(
                        request.OrganizationId, request.UserId, request.IsActive, version, Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = FailFromRule<UserDetails>(ex); }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "SetUserActive");
    }

    public async Task<OrganizationResult<UserDetails>> UnlockUser(
        OrganizationScope? scope,
        UnlockUser request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersUnlock;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            res = OrganizationResult<UserDetails>.Failure(scopeError!);
        else if (request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (DecodeVersion(request.ExpectedAccountRowVersion) is not byte[] version)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
        else
            res = await Execute(() => store.UnlockUser(
                request.OrganizationId, request.UserId, version, Access(auth.Actor!, scope!, permission), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "UnlockUser");
    }

    public async Task<OrganizationResult<UserDetails>> ResetUserPassword(
        OrganizationScope? scope,
        ResetUserPassword request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersResetPassword;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            res = OrganizationResult<UserDetails>.Failure(scopeError!);
        else if (request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (!HasValidInitialPassword(request.NewPassword))
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.PasswordInvalid, new Dictionary<string, object> { ["field"] = "NewPassword" });
        else if (DecodeVersion(request.ExpectedAccountRowVersion) is not byte[] version)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
        else
            res = await Execute(() => store.ResetUserPassword(
                request.OrganizationId, request.UserId, request.NewPassword!, version, Access(auth.Actor!, scope!, permission), ct, request.RequirePasswordChange));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "ResetUserPassword");
    }

    public async Task<OrganizationResult<UserDetails>> SetOrganizationMembershipActive(
        OrganizationScope? scope,
        SetOrganizationMembershipActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.MembershipsManage;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                if (!request.IsActive)
                    TenantAccessPolicy.EnsureNotSelfDeactivation(auth.Actor!.ActorId, request.UserId);

                if (DecodeVersion(request.ExpectedMembershipRowVersion) is not byte[] version)
                    res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
                else
                    res = await Execute(() => store.SetOrganizationMembershipActive(
                        request.OrganizationId, request.UserId, request.IsActive, version, Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = FailFromRule<UserDetails>(ex); }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "OrganizationMembership", request.UserId.ToString(CultureInfo.InvariantCulture), "SetOrganizationMembershipActive");
    }

    public async Task<OrganizationResult<UserDetails>> AssignSiteMembership(
        OrganizationScope? scope,
        AssignSiteMembership request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.MembershipsManage;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (request.SiteId <= 0)
            res = OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
        else
            res = await Execute(() => store.AssignSiteMembership(request, Access(auth.Actor!, scope!, permission), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null, "SiteMembership", request.UserId.ToString(CultureInfo.InvariantCulture), "AssignSiteMembership");
    }

    public async Task<OrganizationResult<UserDetails>> SetSiteMembershipActive(
        OrganizationScope? scope,
        SetSiteMembershipActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.MembershipsManage;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (request.SiteId <= 0)
            res = OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
        else if (DecodeVersion(request.ExpectedMembershipRowVersion) is not byte[] version)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
        else
            res = await Execute(() => store.SetSiteMembershipActive(
                request.OrganizationId, request.UserId, request.SiteId, request.IsActive, version, Access(auth.Actor!, scope!, permission), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null, "SiteMembership", request.UserId.ToString(CultureInfo.InvariantCulture), "SetSiteMembershipActive");
    }

    public async Task<OrganizationResult<UserDetails>> AssignTenantRole(
        OrganizationScope? scope,
        AssignTenantRole request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.RolesAssignFixed;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                TenantAccessPolicy.ValidateTenantRoleScope(request.Role, request.OrganizationId, request.SiteId);
                res = await Execute(() => store.AssignTenantRole(request, Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = FailFromRule<UserDetails>(ex); }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "TenantRole", request.UserId.ToString(CultureInfo.InvariantCulture), "AssignTenantRole");
    }

    public async Task<OrganizationResult<UserDetails>> SetTenantRoleActive(
        OrganizationScope? scope,
        SetTenantRoleActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.RolesAssignFixed;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                TenantAccessPolicy.ValidateTenantRoleScope(request.Role, request.OrganizationId, request.SiteId);
                if (!request.IsActive && request.Role == TenantRoleKind.OrganizationAdministrator)
                    TenantAccessPolicy.EnsureNotSelfDeactivation(auth.Actor!.ActorId, request.UserId);

                if (DecodeVersion(request.ExpectedRoleRowVersion) is not byte[] version)
                    res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
                else
                    res = await Execute(() => store.SetTenantRoleActive(
                        request.OrganizationId, request.UserId, request.Role, request.SiteId, request.IsActive, version, Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = FailFromRule<UserDetails>(ex); }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "TenantRole", request.UserId.ToString(CultureInfo.InvariantCulture), "SetTenantRoleActive");
    }

    public async Task<OrganizationResult<UserDetails>> ResetUserTwoFactor(
        OrganizationScope? scope,
        ResetUserTwoFactor request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersResetTwoFactor;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            res = OrganizationResult<UserDetails>.Failure(scopeError!);
        else if (request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (auth.Actor!.ActorId == request.UserId)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.CannotResetOwnTwoFactor);
        else if (!request.IdentityVerified || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 300)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.TwoFactorReasonRequired);
        else if (DecodeVersion(request.ExpectedAccountRowVersion) is not byte[] version)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
        else
            res = await Execute(() => store.ResetUserTwoFactor(
                request.OrganizationId, request.UserId, request.Reason.Trim(), version, Access(auth.Actor!, scope!, permission), ct));

        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "ResetUserTwoFactor");
    }

    public async Task<OrganizationResult<UserDetails>> RevokeUserSessions(
        OrganizationScope? scope,
        RevokeUserSessions request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersResetPassword;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            res = OrganizationResult<UserDetails>.Failure(scopeError!);
        else if (request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (DecodeVersion(request.ExpectedAccountRowVersion) is not byte[] version)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
        else
            res = await Execute(() => store.RevokeUserSessions(
                request.OrganizationId, request.UserId, version, Access(auth.Actor!, scope!, permission), ct));

        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "User", request.UserId.ToString(CultureInfo.InvariantCulture), "RevokeUserSessions");
    }

    private static bool MatchesAccountScope(OrganizationScope scope, long? organizationId, out string? error)
    {
        switch (scope)
        {
            case OrganizationScope.Platform:
                if (organizationId is not null)
                {
                    error = UserManagementErrors.AccessDenied;
                    return false;
                }
                error = null;
                return true;

            case OrganizationScope.Organization org:
                if (organizationId != org.OrganizationId)
                {
                    error = UserManagementErrors.NotFound;
                    return false;
                }
                error = null;
                return true;

            default:
                error = UserManagementErrors.AccessDenied;
                return false;
        }
    }
}
