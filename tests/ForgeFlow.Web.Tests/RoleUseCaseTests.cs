using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Domain.Roles;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class RoleUseCaseTests
{
    private static readonly string ValidVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

    private sealed class ActorStub : ITrustedActorAccessor
    {
        public TrustedActor? Actor = new(42, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(Actor);
    }

    private sealed class GrantsStub(long allowedOrgId, HashSet<string> allowedPermissions) : IOrganizationPermissionEvaluator
    {
        public bool Revoked;
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope requested, string permission, CancellationToken ct)
        {
            var orgMatches = requested switch
            {
                OrganizationScope.Organization o => o.OrganizationId == allowedOrgId,
                OrganizationScope.Site s => s.OrganizationId == allowedOrgId,
                _ => false
            };
            return ValueTask.FromResult(new PermissionDecision(!Revoked && actor.ActorId == 42 && orgMatches && allowedPermissions.Contains(permission)));
        }
    }

    private sealed class StoreStub : IRoleOperationsStore
    {
        public int Calls;
        public bool ThrowUnavailable;
        public RoleListQuery? LastQuery;
        public IReadOnlyList<string>? LastPermissions;

        private void OnCall()
        {
            Calls++;
            if (ThrowUnavailable) throw new RoleStoreUnavailableException(new InvalidOperationException("offline"));
        }

        private static RoleDetails SampleRole(long orgId, long roleId, IReadOnlyList<string> perms)
            => new(roleId, orgId, "Custom", false, RoleScopeTarget.Organization, true, ValidVersion, perms);

        private static UserPermissionMatrix SampleMatrix(long orgId, long userId)
            => new(userId, orgId, "alice", true, true, [], []);

        public Task<OrganizationPage<RoleSummary>> ListOrganizationRoles(long organizationId, RoleListQuery query, CancellationToken ct)
        {
            OnCall();
            LastQuery = query;
            return Task.FromResult(new OrganizationPage<RoleSummary>([], 0, query.Page, query.PageSize));
        }

        public Task<RoleDetails?> GetOrganizationRole(long organizationId, long roleId, CancellationToken ct)
        {
            OnCall();
            return Task.FromResult<RoleDetails?>(roleId == 999 ? null : SampleRole(organizationId, roleId, []));
        }

        public async Task<OrganizationResult<RoleDetails>> CreateOrganizationRole(
            long organizationId, RoleDefinitionName canonicalName, RoleScopeTarget targetScope, IReadOnlyList<string> permissionCodes, OrganizationWriteAccess access, CancellationToken ct)
        {
            OnCall();
            LastPermissions = permissionCodes;
            return await access.Revalidate(ct)
                ? OrganizationResult<RoleDetails>.Success(SampleRole(organizationId, 100, permissionCodes))
                : OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.AccessDenied);
        }

        public async Task<OrganizationResult<RoleDetails>> UpdateOrganizationRole(
            long organizationId, long roleId, RoleDefinitionName canonicalName, bool isActive, IReadOnlyList<string> permissionCodes, byte[] expectedVersion, OrganizationWriteAccess access, CancellationToken ct)
        {
            OnCall();
            LastPermissions = permissionCodes;
            return await access.Revalidate(ct)
                ? OrganizationResult<RoleDetails>.Success(SampleRole(organizationId, roleId, permissionCodes))
                : OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.AccessDenied);
        }

        public Task<UserPermissionMatrix?> GetUserPermissions(long organizationId, long userId, CancellationToken ct)
        {
            OnCall();
            return Task.FromResult<UserPermissionMatrix?>(userId == 999 ? null : SampleMatrix(organizationId, userId));
        }

        public async Task<OrganizationResult<UserPermissionMatrix>> AssignUserRole(AssignUserRole request, OrganizationWriteAccess access, CancellationToken ct)
        {
            OnCall();
            return await access.Revalidate(ct)
                ? OrganizationResult<UserPermissionMatrix>.Success(SampleMatrix(request.OrganizationId, request.UserId))
                : OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.AccessDenied);
        }

        public async Task<OrganizationResult<UserPermissionMatrix>> SetUserRoleAssignmentActive(
            long organizationId, long userId, long assignmentId, bool isActive, byte[] expectedVersion, OrganizationWriteAccess access, CancellationToken ct)
        {
            OnCall();
            return await access.Revalidate(ct)
                ? OrganizationResult<UserPermissionMatrix>.Success(SampleMatrix(organizationId, userId))
                : OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.AccessDenied);
        }

        public async Task<OrganizationResult<UserPermissionMatrix>> SaveUserOperationGrants(
            long organizationId, long userId, RoleScopeTarget targetScope, long? siteId, IReadOnlyList<(string PermissionCode, bool IsActive, byte[]? ExpectedVersion)> normalizedChanges, OrganizationWriteAccess access, CancellationToken ct)
        {
            OnCall();
            return await access.Revalidate(ct)
                ? OrganizationResult<UserPermissionMatrix>.Success(SampleMatrix(organizationId, userId))
                : OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.AccessDenied);
        }
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("scope")]
    [InlineData("permission")]
    [InlineData("revoked")]
    public async Task Missing_context_or_denied_permission_fails_before_store_access(string missing)
    {
        var store = new StoreStub();
        var actor = new ActorStub();
        var scope = new OrganizationScope.Organization(1);
        var grants = new GrantsStub(1, [RolePermissions.RolesRead]) { Revoked = missing == "revoked" };
        if (missing == "actor") actor.Actor = null;

        var useCases = new RoleUseCases(store, actor, missing == "permission" ? null : grants);
        var result = await useCases.GetOrganizationRole(missing == "scope" ? null : scope, 1, 100);

        Assert.False(result.Succeeded);
        Assert.Equal(0, store.Calls);
        Assert.Equal(
            missing == "actor" ? RolePermissionErrors.ActorRequired
            : missing == "scope" ? RolePermissionErrors.ScopeRequired
            : RolePermissionErrors.AccessDenied,
            result.Error!.Code);
    }

    [Fact]
    public async Task Guessed_cross_org_ids_return_identical_not_found()
    {
        var store = new StoreStub();
        var scope = new OrganizationScope.Organization(1);
        var useCases = new RoleUseCases(store, new ActorStub(), new GrantsStub(1, [RolePermissions.RolesRead]));

        var crossOrg = await useCases.GetOrganizationRole(scope, 2, 100);
        Assert.Equal(0, store.Calls);
        Assert.Equal(RolePermissionErrors.NotFound, crossOrg.Error!.Code);

        var absent = await useCases.GetOrganizationRole(scope, 1, 999);
        Assert.Equal(1, store.Calls);
        Assert.Equal(RolePermissionErrors.NotFound, absent.Error!.Code);
    }

    [Fact]
    public async Task Escalation_ceiling_blocks_granting_permissions_caller_does_not_hold()
    {
        var store = new StoreStub();
        var scope = new OrganizationScope.Organization(1);
        // Caller has Roles.Manage and Permissions.Assign + Sites.Read, NOT Sites.Update.
        var grants = new GrantsStub(1, [RolePermissions.RolesManage, RolePermissions.PermissionsAssign, OrganizationPermissions.SitesRead]);
        var useCases = new RoleUseCases(store, new ActorStub(), grants);

        var escalatedRole = await useCases.CreateOrganizationRole(scope, new CreateOrganizationRole(
            1, "Editor", RoleScopeTarget.Organization, [OrganizationPermissions.SitesRead, OrganizationPermissions.SitesUpdate]));
        Assert.False(escalatedRole.Succeeded);
        Assert.Equal(RolePermissionErrors.EscalationForbidden, escalatedRole.Error!.Code);

        var escalatedGrant = await useCases.SaveUserOperationGrants(scope, new SaveUserOperationGrants(
            1, 77, RoleScopeTarget.Organization, null, [new(OrganizationPermissions.SitesUpdate, true)]));
        Assert.False(escalatedGrant.Succeeded);
        Assert.Equal(RolePermissionErrors.EscalationForbidden, escalatedGrant.Error!.Code);

        var provisionerGrant = await useCases.AssignUserRole(scope, new AssignUserRole(
            1, 77, PermissionCatalog.DeploymentProvisionerRoleId, RoleScopeTarget.Organization));
        Assert.False(provisionerGrant.Succeeded);
        Assert.Equal(RolePermissionErrors.EscalationForbidden, provisionerGrant.Error!.Code);

        Assert.Equal(0, store.Calls);

        var validRole = await useCases.CreateOrganizationRole(scope, new CreateOrganizationRole(
            1, "Site Reader", RoleScopeTarget.Site, [OrganizationPermissions.SitesRead]));
        Assert.True(validRole.Succeeded);
        Assert.Equal(["Sites.Read"], store.LastPermissions);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task System_role_updates_and_invalid_versions_fail_before_store()
    {
        var store = new StoreStub();
        var scope = new OrganizationScope.Organization(1);
        var useCases = new RoleUseCases(store, new ActorStub(), new GrantsStub(1, [RolePermissions.RolesManage, OrganizationPermissions.SitesRead]));

        var systemRoleUpdate = await useCases.UpdateOrganizationRole(scope, new UpdateOrganizationRole(
            1, 1, "Renamed Admin", true, [OrganizationPermissions.SitesRead], ValidVersion));
        Assert.False(systemRoleUpdate.Succeeded);
        Assert.Equal(RolePermissionErrors.SystemRoleImmutable, systemRoleUpdate.Error!.Code);

        var badVersion = await useCases.UpdateOrganizationRole(scope, new UpdateOrganizationRole(
            1, 100, "Valid Custom", true, [OrganizationPermissions.SitesRead], "bad-base64"));
        Assert.False(badVersion.Succeeded);
        Assert.Equal(RolePermissionErrors.ConcurrencyConflict, badVersion.Error!.Code);

        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task List_bounds_paging_and_store_unavailability_maps_cleanly()
    {
        var store = new StoreStub();
        var scope = new OrganizationScope.Organization(3);
        var useCases = new RoleUseCases(store, new ActorStub(), new GrantsStub(3, [RolePermissions.RolesRead, RolePermissions.PermissionsAssign]));

        var list = await useCases.ListOrganizationRoles(scope, 3, new RoleListQuery("  Mgr ", true, true, -2, 500));
        Assert.True(list.Succeeded);
        Assert.Equal("Mgr", store.LastQuery!.Search);
        Assert.Equal(1, store.LastQuery.Page);
        Assert.Equal(100, store.LastQuery.PageSize);

        store.ThrowUnavailable = true;
        var unavailable = await useCases.SetUserRoleAssignmentActive(scope, new SetUserRoleAssignmentActive(3, 5, 9, false, ValidVersion));
        Assert.False(unavailable.Succeeded);
        Assert.Equal(RolePermissionErrors.PersistenceUnavailable, unavailable.Error!.Code);
    }
}
