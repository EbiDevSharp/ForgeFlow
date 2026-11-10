using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Users;

namespace ForgeFlow.Application.Users;

/// <summary>Secured user, membership, and fixed tenant-role use cases; fails closed without trusted adapters.</summary>
public sealed class UserUseCases(
    IUserOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null)
{
    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(OrganizationScope? scope, string permission, CancellationToken ct)
    {
        if (actors is null) return (null, UserManagementErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, UserManagementErrors.ActorRequired);
        if (scope is null) return (null, UserManagementErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, UserManagementErrors.AccessDenied);
        return (actor, null);
    }

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
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, request.OrganizationId))
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

        UserAccountName canonicalUserName;
        try { canonicalUserName = UserAccountName.Parse(request.UserName); }
        catch (UserRuleException ex) { return FailFromRule<UserDetails>(ex); }

        if (!HasValidInitialPassword(request.InitialPassword))
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.PasswordInvalid, new Dictionary<string, object> { ["field"] = "InitialPassword" });

        if (!TryNormalizeSiteIds(request.SiteIds, out var siteIds))
            return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);

        return await Execute(() => store.CreateOrganizationUser(
            request, canonicalUserName, siteIds, Access(auth.Actor!, scope!, permission), ct));
    }

    public async Task<OrganizationResult<UserDetails>> AttachExistingUserToOrganization(
        OrganizationScope? scope,
        AttachExistingUserToOrganization request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersAttachOrganization;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);
        if (scope is not OrganizationScope.Platform)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.AccessDenied);
        if (request.OrganizationId <= 0)
            return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.OrganizationNotFound);

        UserAccountName canonicalUserName;
        try { canonicalUserName = UserAccountName.Parse(request.UserName); }
        catch (UserRuleException ex) { return FailFromRule<UserDetails>(ex); }

        if (!TryNormalizeSiteIds(request.SiteIds, out var siteIds))
            return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);

        return await Execute(() => store.AttachExistingUserToOrganization(
            request, canonicalUserName, siteIds, Access(auth.Actor!, scope, permission), ct));
    }

    public async Task<OrganizationResult<UserDetails>> SetUserActive(
        OrganizationScope? scope,
        SetUserActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersSetActive;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);

        if (!MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            return OrganizationResult<UserDetails>.Failure(scopeError!);

        if (request.UserId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

        if (!request.IsActive)
        {
            try { TenantAccessPolicy.EnsureNotSelfDeactivation(auth.Actor!.ActorId, request.UserId); }
            catch (UserRuleException ex) { return FailFromRule<UserDetails>(ex); }
        }

        var version = DecodeVersion(request.ExpectedAccountRowVersion);
        if (version is null) return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

        return await Execute(() => store.SetUserActive(
            request.OrganizationId, request.UserId, request.IsActive, version, Access(auth.Actor!, scope!, permission), ct));
    }

    public async Task<OrganizationResult<UserDetails>> UnlockUser(
        OrganizationScope? scope,
        UnlockUser request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersUnlock;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);

        if (!MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            return OrganizationResult<UserDetails>.Failure(scopeError!);

        if (request.UserId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

        var version = DecodeVersion(request.ExpectedAccountRowVersion);
        if (version is null) return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

        return await Execute(() => store.UnlockUser(
            request.OrganizationId, request.UserId, version, Access(auth.Actor!, scope!, permission), ct));
    }

    public async Task<OrganizationResult<UserDetails>> ResetUserPassword(
        OrganizationScope? scope,
        ResetUserPassword request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.UsersResetPassword;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);

        if (!MatchesAccountScope(scope!, request.OrganizationId, out var scopeError))
            return OrganizationResult<UserDetails>.Failure(scopeError!);

        if (request.UserId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

        if (!HasValidInitialPassword(request.NewPassword))
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.PasswordInvalid, new Dictionary<string, object> { ["field"] = "NewPassword" });

        var version = DecodeVersion(request.ExpectedAccountRowVersion);
        if (version is null) return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

        return await Execute(() => store.ResetUserPassword(
            request.OrganizationId, request.UserId, request.NewPassword!, version, Access(auth.Actor!, scope!, permission), ct));
    }

    public async Task<OrganizationResult<UserDetails>> SetOrganizationMembershipActive(
        OrganizationScope? scope,
        SetOrganizationMembershipActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.MembershipsManage;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

        if (!request.IsActive)
        {
            try { TenantAccessPolicy.EnsureNotSelfDeactivation(auth.Actor!.ActorId, request.UserId); }
            catch (UserRuleException ex) { return FailFromRule<UserDetails>(ex); }
        }

        var version = DecodeVersion(request.ExpectedMembershipRowVersion);
        if (version is null) return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

        return await Execute(() => store.SetOrganizationMembershipActive(
            request.OrganizationId, request.UserId, request.IsActive, version, Access(auth.Actor!, scope!, permission), ct));
    }

    public async Task<OrganizationResult<UserDetails>> AssignSiteMembership(
        OrganizationScope? scope,
        AssignSiteMembership request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.MembershipsManage;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        if (request.SiteId <= 0)
            return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);

        return await Execute(() => store.AssignSiteMembership(request, Access(auth.Actor!, scope!, permission), ct));
    }

    public async Task<OrganizationResult<UserDetails>> SetSiteMembershipActive(
        OrganizationScope? scope,
        SetSiteMembershipActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.MembershipsManage;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        if (request.SiteId <= 0)
            return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);

        var version = DecodeVersion(request.ExpectedMembershipRowVersion);
        if (version is null) return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

        return await Execute(() => store.SetSiteMembershipActive(
            request.OrganizationId, request.UserId, request.SiteId, request.IsActive, version, Access(auth.Actor!, scope!, permission), ct));
    }

    public async Task<OrganizationResult<UserDetails>> AssignTenantRole(
        OrganizationScope? scope,
        AssignTenantRole request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.RolesAssignFixed;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

        try { TenantAccessPolicy.ValidateTenantRoleScope(request.Role, request.OrganizationId, request.SiteId); }
        catch (UserRuleException ex) { return FailFromRule<UserDetails>(ex); }

        return await Execute(() => store.AssignTenantRole(request, Access(auth.Actor!, scope!, permission), ct));
    }

    public async Task<OrganizationResult<UserDetails>> SetTenantRoleActive(
        OrganizationScope? scope,
        SetTenantRoleActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.RolesAssignFixed;
        var auth = await Authorize(scope, permission, ct);
        if (auth.Error is not null) return OrganizationResult<UserDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

        try
        {
            TenantAccessPolicy.ValidateTenantRoleScope(request.Role, request.OrganizationId, request.SiteId);
            if (!request.IsActive && request.Role == TenantRoleKind.OrganizationAdministrator)
                TenantAccessPolicy.EnsureNotSelfDeactivation(auth.Actor!.ActorId, request.UserId);
        }
        catch (UserRuleException ex) { return FailFromRule<UserDetails>(ex); }

        var version = DecodeVersion(request.ExpectedRoleRowVersion);
        if (version is null) return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

        return await Execute(() => store.SetTenantRoleActive(
            request.OrganizationId, request.UserId, request.Role, request.SiteId, request.IsActive, version, Access(auth.Actor!, scope!, permission), ct));
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
