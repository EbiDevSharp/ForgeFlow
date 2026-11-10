using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Users;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class UserManagementSqlTests
{
    private sealed class SessionSource : ICurrentAuthenticatedSession
    {
        public SessionReference? Current;
        public ValueTask<SessionReference?> GetAsync(CancellationToken ct) => ValueTask.FromResult(Current);
    }

    private sealed class DeterministicClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<OrganizationResult<UserDetails>> CreateWithAuditTriggerAsync(
        UserUseCases users,
        long orgId,
        long siteId,
        string connection)
    {
        await using (var triggerDb = AuthSessionSqlTests.Auth(connection))
        {
            await triggerDb.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER [auth].[TR_Test_RejectSecurityEvent] ON [auth].[SecurityEvent] AFTER INSERT AS BEGIN THROW 50001, N'SyntheticAuditFailure', 1; END");
        }

        try
        {
            return await users.CreateOrganizationUser(new OrganizationScope.Organization(orgId), new CreateOrganizationUser(
                orgId,
                "rolled.back",
                AuthBootstrapTests.Password(),
                AssignOrganizationAdmin: false,
                SiteIds: [siteId],
                AssignSiteOperatorToSites: true));
        }
        finally
        {
            await using var cleanupDb = AuthSessionSqlTests.Auth(connection);
            await cleanupDb.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS [auth].[TR_Test_RejectSecurityEvent]");
        }
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
        return services.BuildServiceProvider();
    }

    [OrganizationSqlServerFact]
    public async Task Atomic_create_user_with_site_membership_and_role_writes_audit_and_fault_injection_rolls_back()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgId = initial.OrganizationId!.Value;
        var siteId = initial.SiteId!.Value;

        var adminSource = new SessionSource();
        await using var provider = BuildProvider(connection, adminSource);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var adminLogin = await sessions.LoginAsync("initial.admin", password);
        Assert.Equal("Auth.SignedIn", adminLogin.Code);
        adminSource.Current = adminLogin.Session;

        var operatorPassword = AuthBootstrapTests.Password();
        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var orgScope = new OrganizationScope.Organization(orgId);

            var created = await users.CreateOrganizationUser(orgScope, new CreateOrganizationUser(
                orgId,
                "  Site.Op_01  ",
                operatorPassword,
                AssignOrganizationAdmin: false,
                SiteIds: [siteId],
                AssignSiteOperatorToSites: true));

            Assert.True(created.Succeeded);
            Assert.Equal("Site.Op_01", created.Value!.UserName);
            Assert.True(created.Value.AccountIsActive);
            Assert.True(created.Value.MembershipIsActive);
            Assert.True(created.Value.BelongsToSingleOrganization);
            Assert.False(created.Value.IsOrganizationAdmin);
            var site = Assert.Single(created.Value.Sites);
            Assert.Equal(siteId, site.SiteId);
            Assert.True(site.MembershipIsActive);
            Assert.True(site.IsSiteOperator);

            var page = await users.ListOrganizationUsers(orgScope, orgId, new UserListQuery("Site.Op"));
            Assert.True(page.Succeeded);
            Assert.Equal(1, page.Value!.Total);
            Assert.Equal("Site.Op_01", page.Value.Items[0].UserName);
        }

        var opLogin = await sessions.LoginAsync("site.op_01", operatorPassword);
        Assert.Equal("Auth.SignedIn", opLogin.Code);

        using (var failScope = provider.CreateScope())
        {
            var users = failScope.ServiceProvider.GetRequiredService<UserUseCases>();
            var failed = await CreateWithAuditTriggerAsync(users, orgId, siteId, connection);

            Assert.False(failed.Succeeded);
            Assert.Equal(UserManagementErrors.AuditUnavailable, failed.Error!.Code);
        }

        await using var verify = AuthSessionSqlTests.Auth(connection);
        Assert.False(await verify.Users.AnyAsync(u => u.NormalizedUserName == "ROLLED.BACK"));
    }

    [OrganizationSqlServerFact]
    public async Task Cross_tenant_duplicate_and_multi_org_isolation_block_org_admin_from_global_disable()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var alphaId = initial.OrganizationId!.Value;

        var adminSource = new SessionSource();
        await using var provider = BuildProvider(connection, adminSource);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var adminLogin = await sessions.LoginAsync("initial.admin", password);
        adminSource.Current = adminLogin.Session;

        await using (var db = AuthSessionSqlTests.Auth(connection))
        {
            var now = DateTime.UtcNow;
            var actorId = initial.UserId!.Value;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [auth].[ScopedRoleAssignment] ([UserId],[RoleId],[ScopeKind],[OrganizationId],[SiteId],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({actorId},{(byte)FixedAccessRoles.DeploymentProvisioner},{(byte)0},{(long?)null},{(long?)null},{true},{now},{actorId})");
        }

        long betaId;
        long betaSiteId;
        long sharedUserId;
        using (var scope = provider.CreateScope())
        {
            var orgUseCases = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();
            var beta = await orgUseCases.CreateOrganization(new OrganizationScope.Platform(), new CreateOrganization("BETA", "Beta Corp"));
            Assert.True(beta.Succeeded);
            betaId = beta.Value!.Id;

            var attachAdminToBeta = await scope.ServiceProvider.GetRequiredService<UserUseCases>()
                .AttachExistingUserToOrganization(
                    new OrganizationScope.Platform(),
                    new AttachExistingUserToOrganization(betaId, "initial.admin", AssignOrganizationAdmin: true, SiteIds: null));
            Assert.True(attachAdminToBeta.Succeeded);

            var betaSite = await orgUseCases.CreateSite(new OrganizationScope.Organization(betaId), new CreateSite(betaId, "B1", "Beta Plant"));
            Assert.True(betaSite.Succeeded);
            betaSiteId = betaSite.Value!.Id;

            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var createdAlpha = await users.CreateOrganizationUser(
                new OrganizationScope.Organization(alphaId),
                new CreateOrganizationUser(alphaId, "shared.worker", AuthBootstrapTests.Password(), false, null));
            Assert.True(createdAlpha.Succeeded);
            sharedUserId = createdAlpha.Value!.UserId;

            // Duplicate username from another organization must return UserNameDuplicate without leaking Alpha.
            var dupInBeta = await users.CreateOrganizationUser(
                new OrganizationScope.Organization(betaId),
                new CreateOrganizationUser(betaId, "SHARED.WORKER", AuthBootstrapTests.Password(), false, [betaSiteId]));
            Assert.False(dupInBeta.Succeeded);
            Assert.Equal(UserManagementErrors.UserNameDuplicate, dupInBeta.Error!.Code);

            // Cross-org site assignment inside Alpha must return Site.NotFound.
            var wrongSite = await users.AssignSiteMembership(
                new OrganizationScope.Organization(alphaId),
                new AssignSiteMembership(alphaId, sharedUserId, betaSiteId));
            Assert.False(wrongSite.Succeeded);
            Assert.Equal(OrganizationSiteErrors.SiteNotFound, wrongSite.Error!.Code);

            // Platform attaches the existing account to Beta.
            var attached = await users.AttachExistingUserToOrganization(
                new OrganizationScope.Platform(),
                new AttachExistingUserToOrganization(betaId, "shared.worker", false, [betaSiteId], AssignSiteOperatorToSites: true));
            Assert.True(attached.Succeeded);
            Assert.False(attached.Value!.BelongsToSingleOrganization);

            // Single-org Admin in Alpha CANNOT disable global ApplicationAccount for a multi-org user.
            var blockedGlobalDisable = await users.SetUserActive(
                new OrganizationScope.Organization(alphaId),
                new SetUserActive(alphaId, sharedUserId, false, attached.Value.AccountRowVersion));
            Assert.False(blockedGlobalDisable.Succeeded);
            Assert.Equal(UserManagementErrors.AccessDenied, blockedGlobalDisable.Error!.Code);

            // Alpha Admin CAN deactivate Alpha OrganizationMembership while Beta remains active.
            var alphaDetail = (await users.GetOrganizationUser(new OrganizationScope.Organization(alphaId), alphaId, sharedUserId)).Value!;
            var orgMembershipDisabled = await users.SetOrganizationMembershipActive(
                new OrganizationScope.Organization(alphaId),
                new SetOrganizationMembershipActive(alphaId, sharedUserId, false, alphaDetail.MembershipRowVersion));
            Assert.True(orgMembershipDisabled.Succeeded);
            Assert.False(orgMembershipDisabled.Value!.MembershipIsActive);
            Assert.True(orgMembershipDisabled.Value.AccountIsActive);
        }

        await using var verify = AuthSessionSqlTests.Auth(connection);
        var betaMembership = await verify.OrganizationMemberships.SingleAsync(m => m.UserId == sharedUserId && m.OrganizationId == betaId);
        Assert.True(betaMembership.IsActive);
    }

    [OrganizationSqlServerFact]
    public async Task Disabling_single_org_user_revokes_sessions_atomically_and_unlock_recovers_lockout()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgId = initial.OrganizationId!.Value;

        var adminSource = new SessionSource();
        await using var provider = BuildProvider(connection, adminSource);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        adminSource.Current = (await sessions.LoginAsync("initial.admin", password)).Session;

        var workerPassword = AuthBootstrapTests.Password();
        UserDetails created;
        using (var scope = provider.CreateScope())
        {
            created = (await scope.ServiceProvider.GetRequiredService<UserUseCases>().CreateOrganizationUser(
                new OrganizationScope.Organization(orgId),
                new CreateOrganizationUser(orgId, "worker.one", workerPassword, false, null))).Value!;
        }

        var s1 = (await sessions.LoginAsync("worker.one", workerPassword)).Session!;
        var s2 = (await sessions.LoginAsync("worker.one", workerPassword)).Session!;
        Assert.Equal("Auth.Valid", await sessions.ValidateAsync(s1));

        UserDetails disabled;
        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            disabled = (await users.SetUserActive(
                new OrganizationScope.Organization(orgId),
                new SetUserActive(orgId, created.UserId, false, created.AccountRowVersion))).Value!;
            Assert.False(disabled.AccountIsActive);
        }

        Assert.Equal("Auth.SessionInvalid", await sessions.ValidateAsync(s1));
        Assert.Equal("Auth.SessionInvalid", await sessions.ValidateAsync(s2));
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("worker.one", workerPassword)).Code);

        await using (var verify = AuthSessionSqlTests.Auth(connection))
        {
            var storedSessions = await verify.Sessions.Where(s => s.UserId == created.UserId).ToListAsync();
            Assert.Equal(2, storedSessions.Count);
            Assert.All(storedSessions, s => Assert.Equal("AccountDisabled", s.RevokeReason));
        }

        // Reactivate, trigger brute-force lockout, then unlock via UnlockUser.
        UserDetails reactivated;
        using (var scope = provider.CreateScope())
        {
            reactivated = (await scope.ServiceProvider.GetRequiredService<UserUseCases>().SetUserActive(
                new OrganizationScope.Organization(orgId),
                new SetUserActive(orgId, created.UserId, true, disabled.AccountRowVersion))).Value!;
            Assert.True(reactivated.AccountIsActive);
        }

        for (var i = 0; i < 5; i++)
            Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("worker.one", AuthBootstrapTests.Password())).Code);
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("worker.one", workerPassword)).Code);

        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var lockedDetail = (await users.GetOrganizationUser(new OrganizationScope.Organization(orgId), orgId, created.UserId)).Value!;
            Assert.True(lockedDetail.IsLockedOut);

            var unlocked = await users.UnlockUser(
                new OrganizationScope.Organization(orgId),
                new UnlockUser(orgId, created.UserId, lockedDetail.AccountRowVersion));
            Assert.True(unlocked.Succeeded);
            Assert.False(unlocked.Value!.IsLockedOut);
        }

        Assert.Equal("Auth.SignedIn", (await sessions.LoginAsync("worker.one", workerPassword)).Code);

        // Admin reset password rotates stamp, revokes active sessions, and allows sign-in only with the new password.
        var resetSession = (await sessions.LoginAsync("worker.one", workerPassword)).Session!;
        var newAdminPassword = AuthBootstrapTests.Password();
        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var latest = (await users.GetOrganizationUser(new OrganizationScope.Organization(orgId), orgId, created.UserId)).Value!;
            var reset = await users.ResetUserPassword(
                new OrganizationScope.Organization(orgId),
                new ResetUserPassword(orgId, created.UserId, newAdminPassword, latest.AccountRowVersion));
            Assert.True(reset.Succeeded);
        }

        Assert.Equal("Auth.SessionInvalid", await sessions.ValidateAsync(resetSession));
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("worker.one", workerPassword)).Code);
        var afterAdminReset = await sessions.LoginAsync("worker.one", newAdminPassword);
        Assert.Equal("Auth.SignedIn", afterAdminReset.Code);

        // User self password change revokes previous sessions and preserves an active updated session.
        var extraSession = (await sessions.LoginAsync("worker.one", newAdminPassword)).Session!;
        var selfNewPassword = AuthBootstrapTests.Password();
        var selfChange = await sessions.ChangeOwnPasswordAsync(afterAdminReset.Session!, newAdminPassword, selfNewPassword);
        Assert.Equal("Auth.PasswordChanged", selfChange.Code);
        Assert.NotNull(selfChange.Session);
        Assert.Equal("Auth.Valid", await sessions.ValidateAsync(selfChange.Session!));
        Assert.Equal("Auth.SessionInvalid", await sessions.ValidateAsync(extraSession));
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("worker.one", newAdminPassword)).Code);
        Assert.Equal("Auth.SignedIn", (await sessions.LoginAsync("worker.one", selfNewPassword)).Code);
    }

    [OrganizationSqlServerFact]
    public async Task Barrier_synchronized_admins_cannot_leave_active_organization_without_active_admin_and_rowversion_conflicts()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgId = initial.OrganizationId!.Value;

        var admin1Source = new SessionSource();
        await using var provider1 = BuildProvider(connection, admin1Source);
        var admin1Sessions = provider1.GetRequiredService<RuntimeSessions>();
        admin1Source.Current = (await admin1Sessions.LoginAsync("initial.admin", password)).Session;

        var admin2Password = AuthBootstrapTests.Password();
        UserDetails admin2;
        UserDetails admin1Detail;
        using (var scope = provider1.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            admin2 = (await users.CreateOrganizationUser(
                new OrganizationScope.Organization(orgId),
                new CreateOrganizationUser(orgId, "second.admin", admin2Password, AssignOrganizationAdmin: true, SiteIds: null))).Value!;
            admin1Detail = (await users.GetOrganizationUser(new OrganizationScope.Organization(orgId), orgId, initial.UserId!.Value)).Value!;
        }

        var admin2Source = new SessionSource();
        await using var provider2 = BuildProvider(connection, admin2Source);
        admin2Source.Current = (await provider2.GetRequiredService<RuntimeSessions>().LoginAsync("second.admin", admin2Password)).Session;

        using var barrier = new Barrier(2);
        var t1 = Task.Run(async () =>
        {
            using var s = provider1.CreateScope();
            barrier.SignalAndWait();
            return await s.ServiceProvider.GetRequiredService<UserUseCases>().SetTenantRoleActive(
                new OrganizationScope.Organization(orgId),
                new SetTenantRoleActive(orgId, admin2.UserId, TenantRoleKind.OrganizationAdministrator, null, false, admin2.OrganizationAdminRoleRowVersion));
        });
        var t2 = Task.Run(async () =>
        {
            using var s = provider2.CreateScope();
            barrier.SignalAndWait();
            return await s.ServiceProvider.GetRequiredService<UserUseCases>().SetTenantRoleActive(
                new OrganizationScope.Organization(orgId),
                new SetTenantRoleActive(orgId, admin1Detail.UserId, TenantRoleKind.OrganizationAdministrator, null, false, admin1Detail.OrganizationAdminRoleRowVersion));
        });

        var results = await Task.WhenAll(t1, t2);
        Assert.Equal(1, results.Count(r => r.Succeeded));
        var failure = Assert.Single(results, r => !r.Succeeded);
        Assert.Equal(UserManagementErrors.LastOrganizationAdminRequired, failure.Error!.Code);

        // Deactivated admin loses permission immediately; the surviving admin runs no-op/stale checks without extra events.
        var winner = results.Single(r => r.Succeeded).Value!;
        var survivingProvider = winner.UserId == admin2.UserId ? provider1 : provider2;
        var revokedProvider = winner.UserId == admin2.UserId ? provider2 : provider1;

        using (var revokedScope = revokedProvider.CreateScope())
        {
            var denied = await revokedScope.ServiceProvider.GetRequiredService<UserUseCases>().ListOrganizationUsers(
                new OrganizationScope.Organization(orgId),
                orgId,
                new UserListQuery());
            Assert.False(denied.Succeeded);
            Assert.Equal(UserManagementErrors.AccessDenied, denied.Error!.Code);
        }

        await using var beforeVerify = AuthSessionSqlTests.Auth(connection);
        var auditCountBefore = await beforeVerify.SecurityEvents.CountAsync();

        using (var scope = survivingProvider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var noop = await users.SetTenantRoleActive(
                new OrganizationScope.Organization(orgId),
                new SetTenantRoleActive(orgId, winner.UserId, TenantRoleKind.OrganizationAdministrator, null, false, winner.OrganizationAdminRoleRowVersion));
            Assert.True(noop.Succeeded);

            var staleTarget = winner.UserId == admin2.UserId ? admin2 : admin1Detail;
            var stale = await users.SetTenantRoleActive(
                new OrganizationScope.Organization(orgId),
                new SetTenantRoleActive(orgId, staleTarget.UserId, TenantRoleKind.OrganizationAdministrator, null, false, staleTarget.OrganizationAdminRoleRowVersion));
            Assert.False(stale.Succeeded);
            Assert.Equal(UserManagementErrors.ConcurrencyConflict, stale.Error!.Code);
        }

        await using var afterVerify = AuthSessionSqlTests.Auth(connection);
        Assert.Equal(auditCountBefore, await afterVerify.SecurityEvents.CountAsync());
    }
}
