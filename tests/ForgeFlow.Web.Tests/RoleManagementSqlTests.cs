using System.Data;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Users;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class RoleManagementSqlTests
{
    private sealed class SessionSource : ICurrentAuthenticatedSession
    {
        public SessionReference? Current;
        public ValueTask<SessionReference?> GetAsync(CancellationToken ct) => ValueTask.FromResult(Current);
    }

    private sealed class DeterministicClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow.AddMinutes(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ServiceProvider BuildProvider(
        string connection,
        SessionSource source,
        DeterministicClock? clock = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddRuntimeIdentity(options =>
            options.UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth")));
        services.AddSingleton<TimeProvider>(clock ?? new DeterministicClock());
        services.AddSingleton<RuntimeSessions>();
        services.AddSingleton<ICurrentAuthenticatedSession>(source);
        services.AddScoped<ITrustedActorAccessor, SessionActorAccessor>();
        services.AddScoped<IOrganizationPermissionEvaluator, SessionPermissionEvaluator>();
        services.AddOrganizationPersistence(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ConnectionStrings:ForgeFlow", connection } })
            .Build());
        services.AddUserManagement();
        services.AddRoleManagement();
        return services.BuildServiceProvider();
    }

    private static async Task<OrganizationResult<T>> ExecuteWithAuditTriggerAsync<T>(
        string connection,
        Func<Task<OrganizationResult<T>>> action)
    {
        await using (var triggerDb = AuthSessionSqlTests.Auth(connection))
        {
            await triggerDb.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER [auth].[TR_Test_RejectSecurityEvent_Roles] ON [auth].[SecurityEvent] AFTER INSERT AS BEGIN THROW 50001, N'SyntheticAuditFailure', 1; END");
        }

        try
        {
            return await action();
        }
        finally
        {
            await using var cleanupDb = AuthSessionSqlTests.Auth(connection);
            await cleanupDb.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS [auth].[TR_Test_RejectSecurityEvent_Roles]");
        }
    }

    [Fact]
    public async Task AddRoleManagement_fails_closed_when_trusted_adapters_are_missing()
    {
        var services = new ServiceCollection();
        services.AddRuntimeIdentity(options =>
            options.UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=true;Encrypt=true"));
        services.AddOrganizationPersistence(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:ForgeFlow", "Server=invalid.invalid;Database=DesignOnly;Integrated Security=true;Encrypt=true" }
            })
            .Build());
        services.AddRoleManagement();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();

        var result = await roles.ListOrganizationRoles(new OrganizationScope.Organization(1), 1, new RoleListQuery());
        Assert.False(result.Succeeded);
        Assert.Equal(RolePermissionErrors.ActorRequired, result.Error!.Code);
    }

    [OrganizationSqlServerFact]
    public async Task S01_additive_upgrade_from_baseline_preserves_bootstrap_data_and_seeds_system_roles()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        await using var orgDb = database.Context();
        var connection = orgDb.Database.GetConnectionString()!;

        await using (var baselineAuth = AuthSessionSqlTests.Auth(connection))
            await baselineAuth.GetService<IMigrator>().MigrateAsync("20261108150853_IdentityBootstrapBaseline");

        // Populate a realistic baseline installation prior to Slice 03.
        var org = Organization.Create("ALPHA", "Alpha Corp");
        var site = org.CreateSite("HQ", "Main Site");
        orgDb.Organizations.Add(org);
        var bootstrapOp = Guid.NewGuid();
        var bootstrapNow = DateTime.UtcNow;
        await orgDb.SaveChangesAsync(new TrustedActor(1, ActorProvenance.ControlledBootstrap, bootstrapOp, Guid.NewGuid()));

        var password = AuthBootstrapTests.Password();
        var dummy = new ApplicationAccount { UserName = "initial.admin", NormalizedUserName = "INITIAL.ADMIN" };
        var hash = new PasswordHasher<ApplicationAccount>().HashPassword(dummy, password);
        var stamp = Guid.NewGuid().ToString("N");
        var concurrency = Guid.NewGuid().ToString();
        const string dataJson = "{}";
        const string operatorRef = "baseline-test-operator";

        await using (var seedDb = AuthSessionSqlTests.Auth(connection))
        {
            await seedDb.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [auth].[User] ([UserName], [NormalizedUserName], [EmailConfirmed], [PasswordHash], [SecurityStamp], [ConcurrencyStamp], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount], [IsActive], [CreatedAtUtc])
                VALUES ({dummy.UserName}, {dummy.NormalizedUserName}, {false}, {hash}, {stamp}, {concurrency}, {false}, {false}, {true}, {0}, {true}, {bootstrapNow});
                DECLARE @uid bigint = SCOPE_IDENTITY();
                INSERT INTO [auth].[OrganizationMembership] ([UserId], [OrganizationId], [IsActive], [CreatedAtUtc], [CreatedByActorId])
                VALUES (@uid, {org.Id}, {true}, {bootstrapNow}, @uid);
                INSERT INTO [auth].[SiteMembership] ([UserId], [OrganizationId], [SiteId], [IsActive], [CreatedAtUtc], [CreatedByActorId])
                VALUES (@uid, {org.Id}, {site.Id}, {true}, {bootstrapNow}, @uid);
                INSERT INTO [auth].[ScopedRoleAssignment] ([UserId], [RoleId], [ScopeKind], [OrganizationId], [SiteId], [IsActive], [CreatedAtUtc], [CreatedByActorId])
                VALUES (@uid, CAST(1 AS tinyint), CAST(1 AS tinyint), {org.Id}, NULL, {true}, {bootstrapNow}, @uid);
                INSERT INTO [auth].[SecurityEvent] ([EventCode], [OccurredAtUtc], [CorrelationId], [ActorKind], [ActorUserId], [SubjectUserId], [OrganizationId], [SiteId], [Outcome], [DataJson])
                VALUES ('BootstrapCompleted', {bootstrapNow}, {bootstrapOp}, CAST(0 AS tinyint), NULL, @uid, {org.Id}, {site.Id}, CAST(0 AS tinyint), {dataJson});
                UPDATE [auth].[BootstrapState] SET [State] = 1, [OperationId] = {bootstrapOp}, [FirstUserId] = @uid, [OrganizationId] = {org.Id}, [SiteId] = {site.Id}, [CompletedAtUtc] = {bootstrapNow}, [HostOperatorReference] = {operatorRef} WHERE [Id] = 1;
                """);
        }

        // Apply Slice 03 migration on top of the populated baseline database.
        await using (var upgradedAuth = AuthSessionSqlTests.Auth(connection))
        {
            await upgradedAuth.Database.MigrateAsync();
            var systemRoles = await upgradedAuth.RoleDefinitions
                .OrderBy(r => r.Id)
                .Select(r => new { r.Id, r.Name, r.IsSystem, r.ScopeKind })
                .ToListAsync();
            Assert.Equal(3, systemRoles.Count);
            Assert.Equal("OrganizationAdministrator", systemRoles[0].Name);
            Assert.Equal("SiteOperator", systemRoles[1].Name);
            Assert.Equal("DeploymentProvisioner", systemRoles[2].Name);
            Assert.True(await upgradedAuth.RolePermissions.CountAsync() >= 15);
            Assert.Equal(1, await upgradedAuth.BootstrapStates.CountAsync(s => s.State == 1));
            Assert.Equal(1, await upgradedAuth.Users.CountAsync());
            Assert.Equal(1, await upgradedAuth.ScopedRoleAssignments.CountAsync(r => r.RoleId == FixedAccessRoles.OrganizationAdministrator && r.IsActive));
        }

        // Verify upgraded system works end-to-end with existing bootstrapped admin.
        var source = new SessionSource();
        await using var provider = BuildProvider(connection, source);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var login = await sessions.LoginAsync("initial.admin", password);
        Assert.Equal("Auth.SignedIn", login.Code);
        source.Current = login.Session;

        await using var scope = provider.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();
        var list = await roles.ListOrganizationRoles(
            new OrganizationScope.Organization(org.Id),
            org.Id,
            new RoleListQuery(IncludeSystem: true));
        Assert.True(list.Succeeded, list.Error?.Code);
        Assert.Contains(list.Value!.Items, r => r.Id == FixedAccessRoles.OrganizationAdministrator && r.IsSystem);
    }

    [OrganizationSqlServerFact]
    public async Task S02_custom_org_role_and_S03_site_scoped_direct_grants_enforced_live_at_sql_boundaries()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgId = initial.OrganizationId!.Value;
        var siteA1 = initial.SiteId!.Value;

        var adminSource = new SessionSource();
        await using var adminProvider = BuildProvider(connection, adminSource);
        var adminSessions = adminProvider.GetRequiredService<RuntimeSessions>();
        var adminLogin = await adminSessions.LoginAsync("initial.admin", password);
        Assert.Equal("Auth.SignedIn", adminLogin.Code);
        adminSource.Current = adminLogin.Session;

        string siteA1RowVersion;
        long siteA2;
        string siteA2RowVersion;
        long readerUserId;
        long customRoleId;
        string customRoleRowVersion;
        string siteMembershipA1Version;

        await using (var scope = adminProvider.CreateAsyncScope())
        {
            var orgs = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();

            var getA1 = await orgs.GetSite(new OrganizationScope.Organization(orgId), orgId, siteA1);
            Assert.True(getA1.Succeeded);
            siteA1RowVersion = getA1.Value!.RowVersion;

            var createA2 = await orgs.CreateSite(new OrganizationScope.Organization(orgId), new CreateSite(orgId, "A2", "Alpha Site Two"));
            Assert.True(createA2.Succeeded);
            siteA2 = createA2.Value!.Id;
            siteA2RowVersion = createA2.Value.RowVersion;

            var createReader = await users.CreateOrganizationUser(new OrganizationScope.Organization(orgId), new CreateOrganizationUser(
                orgId,
                "reader.user",
                password,
                AssignOrganizationAdmin: false,
                SiteIds: [siteA1, siteA2],
                AssignSiteOperatorToSites: false));
            Assert.True(createReader.Succeeded);
            readerUserId = createReader.Value!.UserId;
            siteMembershipA1Version = createReader.Value.Sites.Single(m => m.SiteId == siteA1).MembershipRowVersion;

            // S02: Custom org role with only Sites.Read
            var createdRole = await roles.CreateOrganizationRole(new OrganizationScope.Organization(orgId), new CreateOrganizationRole(
                orgId,
                "Site Reader",
                RoleScopeTarget.Organization,
                [OrganizationPermissions.SitesRead]));
            Assert.True(createdRole.Succeeded);
            customRoleId = createdRole.Value!.Id;
            customRoleRowVersion = createdRole.Value.RowVersion;
            Assert.Equal([OrganizationPermissions.SitesRead], createdRole.Value.Permissions);

            var assigned = await roles.AssignUserRole(new OrganizationScope.Organization(orgId), new AssignUserRole(
                orgId,
                readerUserId,
                customRoleId,
                RoleScopeTarget.Organization));
            Assert.True(assigned.Succeeded);
        }

        // Log in as reader.user
        var readerSource = new SessionSource();
        await using var readerProvider = BuildProvider(connection, readerSource);
        var readerSessions = readerProvider.GetRequiredService<RuntimeSessions>();
        var readerLogin = await readerSessions.LoginAsync("reader.user", password);
        Assert.Equal("Auth.SignedIn", readerLogin.Code);
        readerSource.Current = readerLogin.Session;

        await using (var scope = readerProvider.CreateAsyncScope())
        {
            var orgs = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();

            var canReadSites = await orgs.ListSites(new OrganizationScope.Organization(orgId), orgId, new OrganizationListQuery());
            Assert.True(canReadSites.Succeeded);

            var deniedCreateSite = await orgs.CreateSite(new OrganizationScope.Organization(orgId), new CreateSite(orgId, "A3", "Denied"));
            Assert.False(deniedCreateSite.Succeeded);
            Assert.Equal(OrganizationAccessErrors.AccessDenied, deniedCreateSite.Error!.Code);

            var deniedRenameA1 = await orgs.RenameSite(new OrganizationScope.Site(orgId, siteA1), new RenameSite(orgId, siteA1, "Blocked", siteA1RowVersion));
            Assert.False(deniedRenameA1.Succeeded);
            Assert.Equal(OrganizationAccessErrors.AccessDenied, deniedRenameA1.Error!.Code);

            var deniedPlatform = await orgs.ListOrganizations(new OrganizationScope.Platform(), new OrganizationListQuery());
            Assert.False(deniedPlatform.Succeeded);
            Assert.Equal(OrganizationAccessErrors.AccessDenied, deniedPlatform.Error!.Code);
        }

        // S03: Grant Sites.Update directly to reader.user ONLY on siteA1
        await using (var scope = adminProvider.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();
            var grantSave = await roles.SaveUserOperationGrants(new OrganizationScope.Organization(orgId), new SaveUserOperationGrants(
                orgId,
                readerUserId,
                RoleScopeTarget.Site,
                siteA1,
                [new OperationGrantChange(OrganizationPermissions.SitesUpdate, IsActive: true)]));
            Assert.True(grantSave.Succeeded);
        }

        await using (var scope = readerProvider.CreateAsyncScope())
        {
            var orgs = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();

            var allowedRenameA1 = await orgs.RenameSite(new OrganizationScope.Site(orgId, siteA1), new RenameSite(orgId, siteA1, "Renamed A1", siteA1RowVersion));
            Assert.True(allowedRenameA1.Succeeded);
            siteA1RowVersion = allowedRenameA1.Value!.RowVersion;

            var deniedRenameA2 = await orgs.RenameSite(new OrganizationScope.Site(orgId, siteA2), new RenameSite(orgId, siteA2, "Denied A2", siteA2RowVersion));
            Assert.False(deniedRenameA2.Succeeded);
            Assert.Equal(OrganizationAccessErrors.AccessDenied, deniedRenameA2.Error!.Code);
        }

        // Deactivate reader.user's SiteMembership on siteA1 -> site-scoped direct grant immediately stops granting access
        await using (var scope = adminProvider.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var deactivatedMembership = await users.SetSiteMembershipActive(new OrganizationScope.Organization(orgId), new SetSiteMembershipActive(
                orgId,
                readerUserId,
                siteA1,
                IsActive: false,
                ExpectedMembershipRowVersion: siteMembershipA1Version));
            Assert.True(deactivatedMembership.Succeeded);
        }

        await using (var scope = readerProvider.CreateAsyncScope())
        {
            var orgs = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();
            var deniedAfterMembershipOff = await orgs.RenameSite(new OrganizationScope.Site(orgId, siteA1), new RenameSite(orgId, siteA1, "Denied Membership Off", siteA1RowVersion));
            Assert.False(deniedAfterMembershipOff.Succeeded);
            Assert.Equal(OrganizationAccessErrors.AccessDenied, deniedAfterMembershipOff.Error!.Code);
        }

        // Deactivate the custom role -> Sites.Read immediately stops granting access
        await using (var scope = adminProvider.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();
            var deactivatedRole = await roles.UpdateOrganizationRole(new OrganizationScope.Organization(orgId), new UpdateOrganizationRole(
                orgId,
                customRoleId,
                "Site Reader",
                IsActive: false,
                [OrganizationPermissions.SitesRead],
                customRoleRowVersion));
            Assert.True(deactivatedRole.Succeeded);
        }

        await using (var scope = readerProvider.CreateAsyncScope())
        {
            var orgs = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();
            var deniedAfterRoleOff = await orgs.ListSites(new OrganizationScope.Organization(orgId), orgId, new OrganizationListQuery());
            Assert.False(deniedAfterRoleOff.Succeeded);
            Assert.Equal(OrganizationAccessErrors.AccessDenied, deniedAfterRoleOff.Error!.Code);
        }
    }

    [OrganizationSqlServerFact]
    public async Task S04_cross_tenant_isolation_and_duplicate_role_names_scoped_per_organization()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgAId = initial.OrganizationId!.Value;

        var adminSource = new SessionSource();
        await using var provider = BuildProvider(connection, adminSource);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var login = await sessions.LoginAsync("initial.admin", password);
        Assert.Equal("Auth.SignedIn", login.Code);
        adminSource.Current = login.Session;

        await using (var platformGrantDb = AuthSessionSqlTests.Auth(connection))
            await platformGrantDb.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT [auth].[ScopedRoleAssignment] ([UserId],[RoleId],[ScopeKind],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({initial.UserId},{FixedAccessRoles.DeploymentProvisioner},0,1,SYSUTCDATETIME(),{initial.UserId})");

        long orgBId;
        long orgAUserId;
        long orgBUserId;
        long orgARoleId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var orgs = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();

            var createdOrgB = await orgs.CreateOrganization(new OrganizationScope.Platform(), new CreateOrganization("BETA", "Beta Org"));
            Assert.True(createdOrgB.Succeeded, createdOrgB.Error?.Code);
            orgBId = createdOrgB.Value!.Id;

            var attachAdminToBeta = await users.AttachExistingUserToOrganization(
                new OrganizationScope.Platform(),
                new AttachExistingUserToOrganization(orgBId, "initial.admin", AssignOrganizationAdmin: true, SiteIds: null));
            Assert.True(attachAdminToBeta.Succeeded, attachAdminToBeta.Error?.Code);

            var createdSiteB = await orgs.CreateSite(new OrganizationScope.Organization(orgBId), new CreateSite(orgBId, "B1", "Beta One"));
            Assert.True(createdSiteB.Succeeded, createdSiteB.Error?.Code);

            var userA = await users.CreateOrganizationUser(new OrganizationScope.Organization(orgAId), new CreateOrganizationUser(
                orgAId,
                "alpha.member",
                password,
                AssignOrganizationAdmin: false,
                SiteIds: [initial.SiteId!.Value],
                AssignSiteOperatorToSites: false));
            Assert.True(userA.Succeeded, userA.Error?.Code);
            orgAUserId = userA.Value!.UserId;

            var userB = await users.CreateOrganizationUser(new OrganizationScope.Organization(orgBId), new CreateOrganizationUser(
                orgBId,
                "beta.admin",
                password,
                AssignOrganizationAdmin: true,
                SiteIds: [createdSiteB.Value!.Id],
                AssignSiteOperatorToSites: false));
            Assert.True(userB.Succeeded, userB.Error?.Code);
            orgBUserId = userB.Value!.UserId;

            var roleA = await roles.CreateOrganizationRole(new OrganizationScope.Organization(orgAId), new CreateOrganizationRole(
                orgAId,
                "Auditor",
                RoleScopeTarget.Organization,
                [OrganizationPermissions.SitesRead]));
            Assert.True(roleA.Succeeded);
            orgARoleId = roleA.Value!.Id;

            // Duplicate inside same org -> rejected
            var dupSameOrg = await roles.CreateOrganizationRole(new OrganizationScope.Organization(orgAId), new CreateOrganizationRole(
                orgAId,
                "  auditor ",
                RoleScopeTarget.Organization,
                [OrganizationPermissions.SitesRead]));
            Assert.False(dupSameOrg.Succeeded);
            Assert.Equal(RolePermissionErrors.NameDuplicate, dupSameOrg.Error!.Code);

            // Platform/same admin creating "Auditor" in OrgB -> allowed (scoped per org)
            var roleB = await roles.CreateOrganizationRole(new OrganizationScope.Organization(orgBId), new CreateOrganizationRole(
                orgBId,
                "Auditor",
                RoleScopeTarget.Organization,
                [OrganizationPermissions.SitesRead]));
            Assert.True(roleB.Succeeded);

            // Cross-org target user -> rejected with NotFound (anti-enumeration)
            var crossUserAssign = await roles.AssignUserRole(new OrganizationScope.Organization(orgBId), new AssignUserRole(
                orgBId,
                orgAUserId,
                roleB.Value!.Id,
                RoleScopeTarget.Organization));
            Assert.False(crossUserAssign.Succeeded);
            Assert.Equal(RolePermissionErrors.NotFound, crossUserAssign.Error!.Code);
        }

        // OrgB admin cannot read or use OrgA roles or OrgA users
        var orgBSource = new SessionSource();
        await using var orgBProvider = BuildProvider(connection, orgBSource);
        var orgBSessions = orgBProvider.GetRequiredService<RuntimeSessions>();
        var orgBLogin = await orgBSessions.LoginAsync("beta.admin", password);
        Assert.Equal("Auth.SignedIn", orgBLogin.Code);
        orgBSource.Current = orgBLogin.Session;

        await using (var scope = orgBProvider.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();

            var deniedCrossScope = await roles.GetOrganizationRole(new OrganizationScope.Organization(orgAId), orgAId, orgARoleId);
            Assert.False(deniedCrossScope.Succeeded);
            Assert.Equal(RolePermissionErrors.AccessDenied, deniedCrossScope.Error!.Code);

            var foreignRoleNotFound = await roles.AssignUserRole(new OrganizationScope.Organization(orgBId), new AssignUserRole(
                orgBId,
                orgBUserId,
                orgARoleId,
                RoleScopeTarget.Organization));
            Assert.False(foreignRoleNotFound.Succeeded);
            Assert.Equal(RolePermissionErrors.NotFound, foreignRoleNotFound.Error!.Code);
        }
    }

    [OrganizationSqlServerFact]
    public async Task S05_audit_failure_rolls_back_and_S06_concurrency_and_zero_audit_noop()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgId = initial.OrganizationId!.Value;
        var siteId = initial.SiteId!.Value;

        var adminSource = new SessionSource();
        await using var provider = BuildProvider(connection, adminSource);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var login = await sessions.LoginAsync("initial.admin", password);
        Assert.Equal("Auth.SignedIn", login.Code);
        adminSource.Current = login.Session;

        long targetUserId;
        RoleDetails createdRole;

        await using (var scope = provider.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();

            var user = await users.CreateOrganizationUser(new OrganizationScope.Organization(orgId), new CreateOrganizationUser(
                orgId,
                "target.user",
                password,
                AssignOrganizationAdmin: false,
                SiteIds: [siteId],
                AssignSiteOperatorToSites: false));
            Assert.True(user.Succeeded);
            targetUserId = user.Value!.UserId;

            // S05: Audit trigger failure rolls back CreateOrganizationRole
            var failedCreate = await ExecuteWithAuditTriggerAsync(connection, () => roles.CreateOrganizationRole(
                new OrganizationScope.Organization(orgId),
                new CreateOrganizationRole(orgId, "RolledBackRole", RoleScopeTarget.Organization, [OrganizationPermissions.SitesRead])));
            Assert.False(failedCreate.Succeeded);
            Assert.Equal(RolePermissionErrors.AuditUnavailable, failedCreate.Error!.Code);

            // Also audit trigger failure rolls back SaveUserOperationGrants
            var failedGrant = await ExecuteWithAuditTriggerAsync(connection, () => roles.SaveUserOperationGrants(
                new OrganizationScope.Organization(orgId),
                new SaveUserOperationGrants(orgId, targetUserId, RoleScopeTarget.Organization, null, [new OperationGrantChange(OrganizationPermissions.SitesRead, true)])));
            Assert.False(failedGrant.Succeeded);
            Assert.Equal(RolePermissionErrors.AuditUnavailable, failedGrant.Error!.Code);

            var validRole = await roles.CreateOrganizationRole(new OrganizationScope.Organization(orgId), new CreateOrganizationRole(
                orgId,
                "AuditedRole",
                RoleScopeTarget.Organization,
                [OrganizationPermissions.SitesRead]));
            Assert.True(validRole.Succeeded);
            createdRole = validRole.Value!;
        }

        await using (var verifyDb = AuthSessionSqlTests.Auth(connection))
        {
            Assert.False(await verifyDb.RoleDefinitions.AnyAsync(r => r.Name == "RolledBackRole"));
            Assert.False(await verifyDb.UserPermissionGrants.AnyAsync(g => g.UserId == targetUserId));
        }

        // S06: Concurrency conflict and zero-audit no-op
        await using (var scope = provider.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();

            var firstUpdate = await roles.UpdateOrganizationRole(new OrganizationScope.Organization(orgId), new UpdateOrganizationRole(
                orgId,
                createdRole.Id,
                "AuditedRole",
                true,
                [OrganizationPermissions.SitesRead, OrganizationPermissions.SitesCreate],
                createdRole.RowVersion));
            Assert.True(firstUpdate.Succeeded);

            var staleUpdate = await roles.UpdateOrganizationRole(new OrganizationScope.Organization(orgId), new UpdateOrganizationRole(
                orgId,
                createdRole.Id,
                "AuditedRoleV2",
                true,
                [OrganizationPermissions.SitesRead],
                createdRole.RowVersion));
            Assert.False(staleUpdate.Succeeded);
            Assert.Equal(RolePermissionErrors.ConcurrencyConflict, staleUpdate.Error!.Code);

            int eventsBeforeNoop;
            await using (var countDb = AuthSessionSqlTests.Auth(connection))
                eventsBeforeNoop = await countDb.SecurityEvents.CountAsync();

            var noopUpdate = await roles.UpdateOrganizationRole(new OrganizationScope.Organization(orgId), new UpdateOrganizationRole(
                orgId,
                createdRole.Id,
                "AuditedRole",
                true,
                [OrganizationPermissions.SitesRead, OrganizationPermissions.SitesCreate],
                firstUpdate.Value!.RowVersion));
            Assert.True(noopUpdate.Succeeded);

            await using (var countDb = AuthSessionSqlTests.Auth(connection))
                Assert.Equal(eventsBeforeNoop, await countDb.SecurityEvents.CountAsync());
        }
    }

    [OrganizationSqlServerFact]
    public async Task S07_barrier_synchronized_admins_cannot_deactivate_last_built_in_organization_admin()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgId = initial.OrganizationId!.Value;
        var siteId = initial.SiteId!.Value;

        var adminSource = new SessionSource();
        await using var setupProvider = BuildProvider(connection, adminSource);
        var sessions = setupProvider.GetRequiredService<RuntimeSessions>();
        var adminLogin = await sessions.LoginAsync("initial.admin", password);
        Assert.Equal("Auth.SignedIn", adminLogin.Code);
        adminSource.Current = adminLogin.Session;

        long secondAdminId;
        await using (var scope = setupProvider.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var created = await users.CreateOrganizationUser(new OrganizationScope.Organization(orgId), new CreateOrganizationUser(
                orgId,
                "second.admin",
                password,
                AssignOrganizationAdmin: true,
                SiteIds: [siteId],
                AssignSiteOperatorToSites: false));
            Assert.True(created.Succeeded);
            secondAdminId = created.Value!.UserId;
        }

        // Load both OrganizationAdministrator assignment records
        long initialAdminId;
        long admin1AssignmentId;
        string admin1Version;
        long admin2AssignmentId;
        string admin2Version;

        await using (var scope = setupProvider.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var list = await users.ListOrganizationUsers(new OrganizationScope.Organization(orgId), orgId, new UserListQuery());
            initialAdminId = list.Value!.Items.Single(u => u.UserName == "initial.admin").UserId;

            var m1 = await roles.GetUserPermissions(new OrganizationScope.Organization(orgId), orgId, initialAdminId);
            var a1 = m1.Value!.AssignedRoles.Single(r => r.RoleId == FixedAccessRoles.OrganizationAdministrator);
            admin1AssignmentId = a1.AssignmentId;
            admin1Version = a1.RowVersion;

            var m2 = await roles.GetUserPermissions(new OrganizationScope.Organization(orgId), orgId, secondAdminId);
            var a2 = m2.Value!.AssignedRoles.Single(r => r.RoleId == FixedAccessRoles.OrganizationAdministrator);
            admin2AssignmentId = a2.AssignmentId;
            admin2Version = a2.RowVersion;

            // Barrier: concurrent deactivation of BOTH built-in org admin assignments
            var secondSource = new SessionSource();
            await using var secondProvider = BuildProvider(connection, secondSource);
            var secondSessions = secondProvider.GetRequiredService<RuntimeSessions>();
            var secondLogin = await secondSessions.LoginAsync("second.admin", password);
            Assert.Equal("Auth.SignedIn", secondLogin.Code);
            secondSource.Current = secondLogin.Session;

            using var barrier = new Barrier(2);
            var t1 = Task.Run(async () =>
            {
                await using var s1 = setupProvider.CreateAsyncScope();
                var r1 = s1.ServiceProvider.GetRequiredService<RoleUseCases>();
                barrier.SignalAndWait();
                return await r1.SetUserRoleAssignmentActive(
                    new OrganizationScope.Organization(orgId),
                    new SetUserRoleAssignmentActive(orgId, secondAdminId, admin2AssignmentId, false, admin2Version));
            });

            var t2 = Task.Run(async () =>
            {
                await using var s2 = secondProvider.CreateAsyncScope();
                var r2 = s2.ServiceProvider.GetRequiredService<RoleUseCases>();
                barrier.SignalAndWait();
                return await r2.SetUserRoleAssignmentActive(
                    new OrganizationScope.Organization(orgId),
                    new SetUserRoleAssignmentActive(orgId, initialAdminId, admin1AssignmentId, false, admin1Version));
            });

            var results = await Task.WhenAll(t1, t2);
            Assert.Equal(1, results.Count(r => r.Succeeded));
            var failure = results.Single(r => !r.Succeeded);
            Assert.Equal(RolePermissionErrors.LastOrganizationAdminRequired, failure.Error!.Code);
        }

        await using var verifyDb = AuthSessionSqlTests.Auth(connection);
        var activeBuiltInAdmins = await verifyDb.ScopedRoleAssignments.CountAsync(a =>
            a.OrganizationId == orgId && a.RoleId == FixedAccessRoles.OrganizationAdministrator && a.IsActive);
        Assert.Equal(1, activeBuiltInAdmins);
    }
}
