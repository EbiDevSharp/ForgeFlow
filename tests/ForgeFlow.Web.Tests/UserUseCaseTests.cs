using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Users;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class UserUseCaseTests
{
    private static readonly string ValidVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

    private sealed class ActorSource : ITrustedActorAccessor
    {
        internal TrustedActor? Actor = new(42, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(Actor);
    }

    private sealed class Grants(string permission, OrganizationScope scope) : IOrganizationPermissionEvaluator
    {
        internal bool Revoked;
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope requested, string operation, CancellationToken ct)
        {
            var matches = (requested, scope) switch
            {
                (OrganizationScope.Platform, OrganizationScope.Platform) => true,
                (OrganizationScope.Organization a, OrganizationScope.Organization b) => a.OrganizationId == b.OrganizationId,
                (OrganizationScope.Site a, OrganizationScope.Site b) => a.OrganizationId == b.OrganizationId && a.SiteId == b.SiteId,
                _ => false
            };
            return ValueTask.FromResult(new PermissionDecision(!Revoked && actor.ActorId == 42 && permission == operation && matches));
        }
    }

    private sealed class StoreStub : IUserOperationsStore
    {
        internal int Calls;
        internal bool ThrowUnavailable;
        internal long? LastOrganizationId;
        internal UserListQuery? LastQuery;
        internal IReadOnlyList<long>? LastSiteIds;
        internal UserAccountName? LastUserName;
        internal TrustedActor? LastActor;

        private static UserDetails Details(long orgId, long userId) => new(
            userId, orgId, "alice", true, true, false, null, true, false, null, ValidVersion, ValidVersion, []);

        private void OnCall()
        {
            Calls++;
            if (ThrowUnavailable) throw new UserStoreUnavailableException(new InvalidOperationException("offline"));
        }

        public Task<OrganizationPage<UserSummary>> ListOrganizationUsers(long organizationId, UserListQuery query, CancellationToken ct)
        {
            OnCall();
            LastOrganizationId = organizationId;
            LastQuery = query;
            return Task.FromResult(new OrganizationPage<UserSummary>([], 0, query.Page, query.PageSize));
        }

        public Task<UserDetails?> GetOrganizationUser(long organizationId, long userId, CancellationToken ct)
        {
            OnCall();
            LastOrganizationId = organizationId;
            return Task.FromResult<UserDetails?>(userId == 999 ? null : Details(organizationId, userId));
        }

        private async Task<OrganizationResult<UserDetails>> Write(long orgId, long userId, OrganizationWriteAccess access, CancellationToken ct)
        {
            OnCall();
            LastActor = access.Actor;
            return await access.Revalidate(ct)
                ? OrganizationResult<UserDetails>.Success(Details(orgId, userId))
                : OrganizationResult<UserDetails>.Failure(UserManagementErrors.AccessDenied);
        }

        public Task<OrganizationResult<UserDetails>> CreateOrganizationUser(CreateOrganizationUser request, UserAccountName canonicalUserName, IReadOnlyList<long> distinctSiteIds, OrganizationWriteAccess access, CancellationToken ct)
        {
            LastUserName = canonicalUserName;
            LastSiteIds = distinctSiteIds;
            return Write(request.OrganizationId, 100, access, ct);
        }

        public Task<OrganizationResult<UserDetails>> AttachExistingUserToOrganization(AttachExistingUserToOrganization request, UserAccountName canonicalUserName, IReadOnlyList<long> distinctSiteIds, OrganizationWriteAccess access, CancellationToken ct)
        {
            LastUserName = canonicalUserName;
            LastSiteIds = distinctSiteIds;
            return Write(request.OrganizationId, 100, access, ct);
        }

        public Task<OrganizationResult<UserDetails>> SetUserActive(long? organizationId, long userId, bool isActive, byte[] expectedAccountVersion, OrganizationWriteAccess access, CancellationToken ct)
            => Write(organizationId ?? 1, userId, access, ct);

        public Task<OrganizationResult<UserDetails>> SetOrganizationMembershipActive(long organizationId, long userId, bool isActive, byte[] expectedMembershipVersion, OrganizationWriteAccess access, CancellationToken ct)
            => Write(organizationId, userId, access, ct);

        public Task<OrganizationResult<UserDetails>> AssignSiteMembership(AssignSiteMembership request, OrganizationWriteAccess access, CancellationToken ct)
            => Write(request.OrganizationId, request.UserId, access, ct);

        public Task<OrganizationResult<UserDetails>> SetSiteMembershipActive(long organizationId, long userId, long siteId, bool isActive, byte[] expectedMembershipVersion, OrganizationWriteAccess access, CancellationToken ct)
            => Write(organizationId, userId, access, ct);

        public Task<OrganizationResult<UserDetails>> AssignTenantRole(AssignTenantRole request, OrganizationWriteAccess access, CancellationToken ct)
            => Write(request.OrganizationId, request.UserId, access, ct);

        public Task<OrganizationResult<UserDetails>> SetTenantRoleActive(long organizationId, long userId, TenantRoleKind role, long? siteId, bool isActive, byte[] expectedRoleVersion, OrganizationWriteAccess access, CancellationToken ct)
            => Write(organizationId, userId, access, ct);

        public Task<OrganizationResult<UserDetails>> UnlockUser(long? organizationId, long userId, byte[] expectedAccountVersion, OrganizationWriteAccess access, CancellationToken ct)
            => Write(organizationId ?? 1, userId, access, ct);

        public Task<OrganizationResult<UserDetails>> ResetUserPassword(long? organizationId, long userId, string newPassword, byte[] expectedAccountVersion, OrganizationWriteAccess access, CancellationToken ct)
            => Write(organizationId ?? 1, userId, access, ct);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("scope")]
    [InlineData("permission")]
    [InlineData("revoked")]
    public async Task Missing_context_or_denied_permission_fails_before_store_access(string missing)
    {
        var store = new StoreStub();
        var actor = new ActorSource();
        var scope = new OrganizationScope.Organization(1);
        var grants = new Grants(UserPermissions.UsersRead, scope) { Revoked = missing == "revoked" };
        if (missing == "actor") actor.Actor = null;

        var useCases = new UserUseCases(store, actor, missing == "permission" ? null : grants);
        var result = await useCases.GetOrganizationUser(missing == "scope" ? null : scope, 1, 55);

        Assert.False(result.Succeeded);
        Assert.Equal(0, store.Calls);
        Assert.Equal(
            missing == "actor" ? UserManagementErrors.ActorRequired
            : missing == "scope" ? UserManagementErrors.ScopeRequired
            : UserManagementErrors.AccessDenied,
            result.Error!.Code);
    }

    [Fact]
    public async Task Unauthorized_caller_does_not_receive_validation_details()
    {
        var store = new StoreStub();
        var scope = new OrganizationScope.Organization(1);
        var useCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.UsersRead, scope));

        var result = await useCases.CreateOrganizationUser(scope, new CreateOrganizationUser(1, "invalid user", "", true, null));

        Assert.False(result.Succeeded);
        Assert.Equal(0, store.Calls);
        Assert.Equal(UserManagementErrors.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Guessed_cross_organization_ids_and_missing_users_return_identical_not_found()
    {
        var store = new StoreStub();
        var scope1 = new OrganizationScope.Organization(1);
        var useCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.UsersRead, scope1));

        var crossOrg = (await useCases.GetOrganizationUser(scope1, 2, 55)).Error;
        Assert.Equal(0, store.Calls);

        var missing = (await useCases.GetOrganizationUser(scope1, 1, 999)).Error;
        Assert.Equal(1, store.Calls);
        Assert.Equal(UserManagementErrors.NotFound, crossOrg!.Code);
        Assert.Equal(crossOrg.Code, missing!.Code);
        Assert.Empty(crossOrg.Parameters);
        Assert.Empty(missing.Parameters);
    }

    [Fact]
    public async Task List_queries_clamp_paging_and_trim_search()
    {
        var store = new StoreStub();
        var scope = new OrganizationScope.Organization(7);
        var useCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.UsersRead, scope));

        var result = await useCases.ListOrganizationUsers(scope, 7, new UserListQuery("  ali  ", true, Page: -5, PageSize: 500));

        Assert.True(result.Succeeded);
        Assert.Equal(7, store.LastOrganizationId);
        Assert.Equal("ali", store.LastQuery!.Search);
        Assert.Equal(1, store.LastQuery.Page);
        Assert.Equal(100, store.LastQuery.PageSize);
    }

    [Fact]
    public async Task Create_user_validates_username_password_and_deduplicates_site_ids_without_leaking_secrets()
    {
        var store = new StoreStub();
        var scope = new OrganizationScope.Organization(5);
        var useCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.UsersCreate, scope));

        var badPassword = await useCases.CreateOrganizationUser(scope, new CreateOrganizationUser(5, "alice.smith", "   ", false, null));
        Assert.False(badPassword.Succeeded);
        Assert.Equal(UserManagementErrors.PasswordInvalid, badPassword.Error!.Code);
        Assert.Equal("InitialPassword", badPassword.Error.Parameters["field"]);
        Assert.DoesNotContain("Password", badPassword.Error.Parameters.Values);

        var badSite = await useCases.CreateOrganizationUser(scope, new CreateOrganizationUser(5, "alice.smith", "Strong!123", false, [10, 0]));
        Assert.False(badSite.Succeeded);
        Assert.Equal(OrganizationSiteErrors.SiteNotFound, badSite.Error!.Code);
        Assert.Equal(0, store.Calls);

        var ok = await useCases.CreateOrganizationUser(scope, new CreateOrganizationUser(5, " alice.smith ", "Strong!123", true, [10, 20, 10]));
        Assert.True(ok.Succeeded);
        Assert.Equal("ALICE.SMITH", store.LastUserName!.Value.Normalized);
        Assert.Equal([10L, 20L], store.LastSiteIds);
        Assert.Equal(42, store.LastActor!.ActorId);
    }

    [Fact]
    public async Task Attach_existing_user_requires_platform_scope()
    {
        var store = new StoreStub();
        var orgScope = new OrganizationScope.Organization(5);
        var orgUseCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.UsersAttachOrganization, orgScope));
        Assert.Equal(
            UserManagementErrors.AccessDenied,
            (await orgUseCases.AttachExistingUserToOrganization(orgScope, new AttachExistingUserToOrganization(5, "alice", false, null))).Error!.Code);
        Assert.Equal(0, store.Calls);

        var platformScope = new OrganizationScope.Platform();
        var platformUseCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.UsersAttachOrganization, platformScope));
        Assert.True((await platformUseCases.AttachExistingUserToOrganization(platformScope, new AttachExistingUserToOrganization(5, "alice", false, [3]))).Succeeded);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task Self_deactivation_is_blocked_before_store_for_account_membership_and_org_admin_role()
    {
        var store = new StoreStub();
        var scope = new OrganizationScope.Organization(1);

        var accountUseCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.UsersSetActive, scope));
        Assert.Equal(
            UserManagementErrors.SelfDeactivationForbidden,
            (await accountUseCases.SetUserActive(scope, new SetUserActive(1, 42, false, ValidVersion))).Error!.Code);

        var membershipUseCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.MembershipsManage, scope));
        Assert.Equal(
            UserManagementErrors.SelfDeactivationForbidden,
            (await membershipUseCases.SetOrganizationMembershipActive(scope, new SetOrganizationMembershipActive(1, 42, false, ValidVersion))).Error!.Code);

        var roleUseCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.RolesAssignFixed, scope));
        Assert.Equal(
            UserManagementErrors.SelfDeactivationForbidden,
            (await roleUseCases.SetTenantRoleActive(scope, new SetTenantRoleActive(1, 42, TenantRoleKind.OrganizationAdministrator, null, false, ValidVersion))).Error!.Code);

        Assert.Equal(0, store.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-base64")]
    [InlineData("AAAA")]
    public async Task Invalid_rowversion_fails_with_concurrency_conflict_before_store(string? badVersion)
    {
        var store = new StoreStub();
        var scope = new OrganizationScope.Organization(1);
        var useCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.UsersSetActive, scope));

        var result = await useCases.SetUserActive(scope, new SetUserActive(1, 99, false, badVersion));

        Assert.False(result.Succeeded);
        Assert.Equal(UserManagementErrors.ConcurrencyConflict, result.Error!.Code);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Account_scope_mismatch_and_store_unavailability_map_cleanly()
    {
        var store = new StoreStub { ThrowUnavailable = true };
        var platform = new OrganizationScope.Platform();
        var platformUseCases = new UserUseCases(store, new ActorSource(), new Grants(UserPermissions.UsersUnlock, platform));

        var mismatchedPlatform = await platformUseCases.UnlockUser(platform, new UnlockUser(1, 99, ValidVersion));
        Assert.Equal(UserManagementErrors.AccessDenied, mismatchedPlatform.Error!.Code);
        Assert.Equal(0, store.Calls);

        var unavailable = await platformUseCases.UnlockUser(platform, new UnlockUser(null, 99, ValidVersion));
        Assert.Equal(UserManagementErrors.PersistenceUnavailable, unavailable.Error!.Code);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task User_management_registration_fails_closed_without_trusted_adapters()
    {
        var services = new ServiceCollection()
            .AddRuntimeIdentity(o => o.UseSqlServer("Server=invalid.invalid;Database=missing;Integrated Security=True;Encrypt=True;Connect Timeout=1"))
            .AddUserManagement();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var useCases = scope.ServiceProvider.GetRequiredService<UserUseCases>();

        var result = await useCases.GetOrganizationUser(new OrganizationScope.Organization(1), 1, 1);

        Assert.False(result.Succeeded);
        Assert.Equal(UserManagementErrors.ActorRequired, result.Error!.Code);
    }

    [Fact]
    public void Fixed_role_policy_grants_expected_user_management_permissions_and_rejects_site_operator()
    {
        var orgScope = new OrganizationScope.Organization(1);
        var siteScope = new OrganizationScope.Site(1, 2);
        var platformScope = new OrganizationScope.Platform();

        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, orgScope, UserPermissions.UsersRead));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, orgScope, UserPermissions.UsersCreate));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, orgScope, UserPermissions.MembershipsManage));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, orgScope, UserPermissions.RolesAssignFixed));
        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, orgScope, UserPermissions.UsersAttachOrganization));

        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.DeploymentProvisioner, platformScope, UserPermissions.UsersAttachOrganization));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.DeploymentProvisioner, platformScope, UserPermissions.UsersSetActive));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.DeploymentProvisioner, platformScope, UserPermissions.UsersUnlock));
        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.DeploymentProvisioner, orgScope, UserPermissions.UsersRead));

        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.SiteOperator, siteScope, UserPermissions.UsersRead));
        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.SiteOperator, orgScope, UserPermissions.UsersRead));
    }
}
