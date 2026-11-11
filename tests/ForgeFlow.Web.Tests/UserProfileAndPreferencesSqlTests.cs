using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Users;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class UserProfileAndPreferencesSqlTests
{
    private sealed class Source : ICurrentAuthenticatedSession
    {
        public SessionReference? Current { get; set; }
        public ValueTask<SessionReference?> GetAsync(CancellationToken ct) => ValueTask.FromResult(Current);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ServiceProvider Provider(string connection, Source source, Clock? clock = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddRuntimeIdentity(o => o.UseSqlServer(connection, s => s.MigrationsHistoryTable("__EFMigrationsHistory", "auth")));
        services.AddSingleton<TimeProvider>(clock ?? new Clock());
        services.AddSingleton<RuntimeSessions>();
        services.AddSingleton<IUserNotificationPublisher>(sp => sp.GetRequiredService<RuntimeSessions>());
        services.AddSingleton<ICurrentAuthenticatedSession>(source);
        services.AddScoped<ITrustedActorAccessor, SessionActorAccessor>();
        services.AddScoped<IOrganizationPermissionEvaluator, SessionPermissionEvaluator>();
        services.AddAuditManagement();
        services.AddUserManagement();
        services.AddRoleManagement();
        services.AddOrganizationPersistence(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ConnectionStrings:ForgeFlow", connection } })
            .Build());
        return services.BuildServiceProvider();
    }

    [OrganizationSqlServerFact]
    public async Task First_login_seeds_preferences_from_initial_culture_and_save_updates_sync_and_audit()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, _) = await AuthSessionSqlTests.Seed(database);
        var clock = new Clock();
        await using var provider = Provider(connection, new Source(), clock);
        var sessions = provider.GetRequiredService<RuntimeSessions>();

        var login = await sessions.LoginAsync("initial.admin", password, initialCulture: "en-US");
        Assert.Equal("Auth.SignedIn", login.Code);
        Assert.NotNull(login.Session);

        var seeded = await sessions.GetPreferencesAsync(login.Session!);
        Assert.NotNull(seeded);
        Assert.Equal("en-US", seeded!.Language);
        Assert.Equal("gregorian", seeded.CalendarSystem);
        Assert.Equal("light", seeded.Theme);
        Assert.Equal("Asia/Tehran", seeded.TimeZoneId);

        clock.Now = clock.Now.AddMinutes(5);
        var invalid = await sessions.SavePreferencesAsync(login.Session!, new("fr-FR", "dark", "persian", "Asia/Tehran"));
        Assert.Equal("Preferences.InvalidInput", invalid.Code);

        var saved = await sessions.SavePreferencesAsync(login.Session!, new("fa-IR", "dark", "gregorian", "Europe/Berlin"));
        Assert.Equal("Preferences.Saved", saved.Code);
        Assert.NotNull(saved.Preferences);
        Assert.Equal("fa-IR", saved.Preferences!.Language);
        Assert.Equal("dark", saved.Preferences.Theme);
        Assert.Equal("gregorian", saved.Preferences.CalendarSystem);
        Assert.Equal("Europe/Berlin", saved.Preferences.TimeZoneId);

        var reloaded = await sessions.GetPreferencesAsync(login.Session!);
        Assert.Equal(saved.Preferences, reloaded);

        // Culture header switch syncs UserPreference.Language too
        Assert.Equal("Culture.ProfileSaved", await sessions.SetPreferredCultureAsync(login.Session!, "en-US"));
        var afterHeaderSwitch = await sessions.GetPreferencesAsync(login.Session!);
        Assert.Equal("en-US", afterHeaderSwitch!.Language);

        await using var db = AuthSessionSqlTests.Auth(connection);
        Assert.Equal(1, await db.SecurityEvents.CountAsync(e => e.EventCode == "PreferencesUpdated"));
        var user = await db.Users.SingleAsync();
        Assert.Equal("en-US", user.PreferredCulture);
    }

    [OrganizationSqlServerFact]
    public async Task Profile_update_persists_contact_details_and_validated_avatar_roundtrip()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, _) = await AuthSessionSqlTests.Seed(database);
        var clock = new Clock();
        await using var provider = Provider(connection, new Source(), clock);
        var sessions = provider.GetRequiredService<RuntimeSessions>();

        var login = await sessions.LoginAsync("initial.admin", password);
        Assert.Equal("Auth.SignedIn", login.Code);

        var validPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01 };
        clock.Now = clock.Now.AddMinutes(2);

        var badEmail = await sessions.UpdateProfileAsync(login.Session!, new("Ebrahim", "invalid-email", null));
        Assert.Equal("Profile.EmailInvalid", badEmail.Code);

        var badAvatar = await sessions.UpdateProfileAsync(login.Session!, new("Ebrahim", "ebi@forgeflow.local", "+98218888", "<svg/>"u8.ToArray(), "image/svg+xml"));
        Assert.Equal("Profile.AvatarInvalid", badAvatar.Code);

        var updated = await sessions.UpdateProfileAsync(login.Session!, new("Ebrahim Dev", "ebi@forgeflow.local", "+98218888", validPng, "image/png"));
        Assert.Equal("Profile.Saved", updated.Code);
        Assert.NotNull(updated.Profile);
        Assert.Equal("Ebrahim Dev", updated.Profile!.DisplayName);
        Assert.Equal("ebi@forgeflow.local", updated.Profile.Email);
        Assert.Equal("+98218888", updated.Profile.PhoneNumber);
        Assert.True(updated.Profile.HasAvatar);

        var loaded = await sessions.GetProfileAsync(login.Session!);
        Assert.Equal(updated.Profile, loaded);

        var avatar = await sessions.GetAvatarAsync(login.Session!);
        Assert.NotNull(avatar);
        Assert.Equal("image/png", avatar!.Value.ContentType);
        Assert.Equal(validPng, avatar.Value.Data);

        var cleared = await sessions.UpdateProfileAsync(login.Session!, new("Ebrahim Dev", "ebi@forgeflow.local", "+98218888", RemoveAvatar: true));
        Assert.Equal("Profile.Saved", cleared.Code);
        Assert.False(cleared.Profile!.HasAvatar);
        Assert.Null(await sessions.GetAvatarAsync(login.Session!));

        await using var db = AuthSessionSqlTests.Auth(connection);
        Assert.Equal(2, await db.SecurityEvents.CountAsync(e => e.EventCode == "ProfileUpdated"));
        var pref = await db.UserPreferences.SingleAsync();
        db.UserPreferences.Remove(pref);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [OrganizationSqlServerFact]
    public async Task Menu_context_failed_attempts_site_switcher_and_session_revocation_work_in_sql()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var source = new Source();
        var clock = new Clock();
        await using var provider = Provider(connection, source, clock);
        var sessions = provider.GetRequiredService<RuntimeSessions>();

        // 1. First session (Desktop Chrome)
        var s1 = await sessions.LoginAsync(
            "initial.admin",
            password,
            clientIp: "192.168.1.24",
            userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0.0.0 Safari/537.36");
        Assert.Equal("Auth.SignedIn", s1.Code);

        // 2. Two failed sign-in attempts after s1
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("initial.admin", "wrong-pass-1", clientIp: "172.16.5.8")).Code);
        clock.Now = clock.Now.AddSeconds(10);
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("initial.admin", "wrong-pass-2", clientIp: "172.16.5.8")).Code);

        // 3. Second and third sessions (Android & Edge)
        clock.Now = clock.Now.AddMinutes(1);
        var s2 = await sessions.LoginAsync(
            "initial.admin",
            password,
            clientIp: "10.0.0.15",
            userAgent: "Mozilla/5.0 (Linux; Android 14) Chrome/130.0.0.0 Mobile Safari/537.36");
        clock.Now = clock.Now.AddMinutes(1);
        var s3 = await sessions.LoginAsync(
            "initial.admin",
            password,
            clientIp: "192.168.1.40",
            userAgent: "Mozilla/5.0 (Windows NT 10.0) Chrome/130.0.0.0 Edg/130.0.0.0");

        // Create a second active site under the user's organization to test multi-site switcher
        source.Current = s3.Session;
        long secondSiteId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var orgUseCases = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();
            var createdSite = await orgUseCases.CreateSite(new OrganizationScope.Organization(initial.OrganizationId!.Value), new(initial.OrganizationId!.Value, "PLANT2", "North Plant"));
            Assert.True(createdSite.Succeeded);
            secondSiteId = createdSite.Value!.Id;
        }

        var menu = await sessions.GetUserMenuContextAsync(s3.Session!);
        Assert.NotNull(menu);
        Assert.Equal("OrganizationAdministrator", menu!.ActiveRoleName);
        Assert.Equal(2, menu.AccessibleSites.Count);
        Assert.Equal(s2.Session!.SessionId, s2.Session.SessionId);
        Assert.Equal("10.0.0.15", menu.PreviousSignInIp);
        Assert.Equal(0, menu.FailedSignInsSincePrevious); // failures happened before s2

        // Check s2 sees the 2 failures that occurred after s1
        var menuFromS2 = await sessions.GetUserMenuContextAsync(s2.Session!);
        Assert.NotNull(menuFromS2);
        Assert.Equal("192.168.1.24", menuFromS2!.PreviousSignInIp);
        Assert.Equal(2, menuFromS2.FailedSignInsSincePrevious);
        Assert.Equal(3, menuFromS2.ActiveSessionCount);

        // Switch active site to PLANT2 & reject unauthorized org/site
        Assert.Equal("Site.Switched", await sessions.SwitchActiveSiteAsync(s3.Session!, initial.OrganizationId!.Value, secondSiteId));
        Assert.Equal("Site.AccessDenied", await sessions.SwitchActiveSiteAsync(s3.Session!, 999999L, 999999L));
        var menuAfterSwitch = await sessions.GetUserMenuContextAsync(s3.Session!);
        Assert.Equal("North Plant", menuAfterSwitch!.ActiveSiteName);

        // Active sessions list & single/other revocation
        var list = await sessions.ListActiveSessionsAsync(s3.Session!);
        Assert.Equal(3, list.Count);
        Assert.True(list[0].IsCurrent);
        Assert.Equal("Edge · Windows", list[0].DeviceSummary);
        Assert.Contains(list, s => s.DeviceSummary == "Chrome · Android" && s.IsMobileDevice);

        Assert.Equal("Session.CannotRevokeCurrent", (await sessions.RevokeSessionAsync(s3.Session!, s3.Session!.SessionId)).Code);
        Assert.Equal("Session.Revoked", (await sessions.RevokeSessionAsync(s3.Session!, s1.Session!.SessionId)).Code);
        Assert.Equal("Auth.SessionInvalid", await sessions.ValidateAsync(s1.Session!));

        var othersRevoked = await sessions.RevokeOtherSessionsAsync(s3.Session!);
        Assert.Equal("Session.OthersRevoked", othersRevoked.Code);
        Assert.Equal(1, othersRevoked.AffectedCount);
        Assert.Equal("Auth.SessionInvalid", await sessions.ValidateAsync(s2.Session!));
        Assert.Equal("Auth.Valid", await sessions.ValidateAsync(s3.Session!));
    }

    [OrganizationSqlServerFact]
    public async Task My_activity_enforces_strict_actor_isolation_and_enriches_ip_and_device()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var source = new Source();
        var clock = new Clock();
        await using var provider = Provider(connection, source, clock);
        var sessions = provider.GetRequiredService<RuntimeSessions>();

        // Sign in admin & create second user
        var adminLogin = await sessions.LoginAsync("initial.admin", password, clientIp: "192.168.1.24",
            userAgent: "Mozilla/5.0 (Windows NT 10.0) Chrome/130.0.0.0 Safari/537.36");
        source.Current = adminLogin.Session;

        long secondUserId;
        var secondPassword = AuthBootstrapTests.Password();
        await using (var scope = provider.CreateAsyncScope())
        {
            var userUseCases = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var created = await userUseCases.CreateOrganizationUser(
                new OrganizationScope.Organization(initial.OrganizationId!.Value),
                new(initial.OrganizationId!.Value, "second.operator", secondPassword, false,
                    initial.SiteId.HasValue ? [initial.SiteId.Value] : [], initial.SiteId.HasValue));
            Assert.True(created.Succeeded);
            secondUserId = created.Value!.UserId;
        }

        // Second user signs in and fails once
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("second.operator", "wrong-pass", clientIp: "172.16.5.8",
            userAgent: "Mozilla/5.0 (Linux; Android 14) Chrome/130.0.0.0 Mobile Safari/537.36")).Code);
        clock.Now = clock.Now.AddMinutes(1);
        var secondLogin = await sessions.LoginAsync("second.operator", secondPassword, clientIp: "10.0.0.15",
            userAgent: "Mozilla/5.0 (Linux; Android 14) Chrome/130.0.0.0 Mobile Safari/537.36");
        Assert.Equal("Auth.SignedIn", secondLogin.Code);

        await using var verifyScope = provider.CreateAsyncScope();
        var myActivity = verifyScope.ServiceProvider.GetRequiredService<IMyActivityStore>();

        var secondEntries = await myActivity.ListMyActivityAsync(secondUserId, new MyActivityQuery(30, null, null, 1, 20));
        Assert.Equal(2, secondEntries.Total); // 1 failed login + 1 SessionIssued
        Assert.Contains(secondEntries.Items, e => e.EventCode == "SignInFailed" && e.IpAddress == "172.16.5.8" && e.Outcome == 1);
        Assert.Contains(secondEntries.Items, e => e.EventCode == "SessionIssued" && e.IpAddress == "10.0.0.15" && e.DeviceSummary == "Chrome · Android");

        // Admin cannot read secondUserId's entries through GetMyActivityEntryAsync
        var targetEntry = secondEntries.Items.First();
        Assert.NotNull(await myActivity.GetMyActivityEntryAsync(secondUserId, targetEntry.Source, targetEntry.Id));
        Assert.Null(await myActivity.GetMyActivityEntryAsync(initial.UserId!.Value, targetEntry.Source, targetEntry.Id));

        // Admin's activity includes AccountCreated + SessionIssued, not secondUserId's SignInFailed
        var adminEntries = await myActivity.ListMyActivityAsync(initial.UserId!.Value, new MyActivityQuery(30, null, null, 1, 20));
        Assert.DoesNotContain(adminEntries.Items, e => e.EventCode == "SignInFailed");
        Assert.Contains(adminEntries.Items, e => e.EventCode == "SessionIssued" && e.DeviceSummary == "Chrome · Windows");
    }

    [OrganizationSqlServerFact]
    public async Task Notifications_honor_preferences_read_state_filters_live_channel_and_immutability()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var source = new Source();
        var clock = new Clock();
        await using var provider = Provider(connection, source, clock);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var liveChannel = provider.GetRequiredService<IUserNotificationLiveChannel>();

        var notifiedIds = new List<long>();
        liveChannel.UserNotificationsChanged += uid => notifiedIds.Add(uid);

        // First login: no prior session -> 0 notifications
        var s1 = await sessions.LoginAsync("initial.admin", password, clientIp: "192.168.1.24",
            userAgent: "Mozilla/5.0 (Windows NT 10.0) Chrome/130.0.0.0 Safari/537.36");
        Assert.Equal(0, await sessions.GetUnreadNotificationCountAsync(s1.Session!));

        // Failed login -> generates FailedSignInAttempts notification
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("initial.admin", "wrong-pass", clientIp: "172.16.5.8")).Code);

        // New device login -> generates NewDeviceSignIn notification
        clock.Now = clock.Now.AddMinutes(1);
        var s2 = await sessions.LoginAsync("initial.admin", password, clientIp: "10.0.0.15",
            userAgent: "Mozilla/5.0 (Linux; Android 14) Chrome/130.0.0.0 Mobile Safari/537.36");
        Assert.Equal(2, await sessions.GetUnreadNotificationCountAsync(s2.Session!));
        Assert.Contains(initial.UserId!.Value, notifiedIds);

        // Publish order alert
        await sessions.PublishAsync(
            initial.UserId!.Value,
            NotificationEventCatalog.MyProductionOrders,
            NotificationCategory.Orders,
            NotificationSeverity.Success,
            NotificationTemplateCatalog.MyProductionOrderStatusChanged,
            new { orderNumber = "PO-2026-001", fromStatus = "Draft", toStatus = "Released" },
            "/production-orders");

        var allPage = await sessions.ListNotificationsAsync(s2.Session!, NotificationListFilter.All, 1, 10);
        Assert.Equal(3, allPage.TotalCount);
        Assert.Equal(3, allPage.UnreadCount);

        var ordersPage = await sessions.ListNotificationsAsync(s2.Session!, NotificationListFilter.Orders, 1, 10);
        Assert.Single(ordersPage.Items);
        Assert.Equal("MyProductionOrderStatusChanged", ordersPage.Items[0].TemplateCode);

        // Disable NewDeviceSignIn in preferences -> next new device does NOT notify
        Assert.Equal("NotificationPrefs.Saved", await sessions.SaveNotificationPreferenceAsync(s2.Session!, NotificationEventCatalog.NewDeviceSignIn, false, false));
        clock.Now = clock.Now.AddMinutes(1);
        var s3 = await sessions.LoginAsync("initial.admin", password, clientIp: "10.99.99.99",
            userAgent: "Mozilla/5.0 (iPhone) Safari/604.1");
        Assert.Equal(3, await sessions.GetUnreadNotificationCountAsync(s3.Session!)); // unchanged!

        // Mark single read then mark all read
        var firstNotif = allPage.Items[0];
        Assert.True(await sessions.MarkNotificationReadAsync(s3.Session!, firstNotif.Id));
        Assert.Equal(2, await sessions.GetUnreadNotificationCountAsync(s3.Session!));
        Assert.Equal(2, await sessions.MarkAllNotificationsReadAsync(s3.Session!));
        Assert.Equal(0, await sessions.GetUnreadNotificationCountAsync(s3.Session!));

        // Guard prevents tampering or deletion of notifications
        await using var db = AuthSessionSqlTests.Auth(connection);
        var stored = await db.UserNotifications.FirstAsync();
        db.Entry(stored).Property(x => x.TemplateCode).CurrentValue = "PasswordChangedAlert";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(stored).State = EntityState.Unchanged;
        db.UserNotifications.Remove(stored);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [OrganizationSqlServerFact]
    public async Task Notifications_trigger_for_device_token_mismatch_failed_attempts_password_change_and_access_mutations()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var source = new Source();
        var clock = new Clock();
        await using var provider = Provider(connection, source, clock);
        var sessions = provider.GetRequiredService<RuntimeSessions>();

        const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0.0.0 Safari/537.36";
        const string tokenA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string tokenB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        // 1. First browser login + same browser re-login should NOT alert
        var sA1 = await sessions.LoginAsync("initial.admin", password, clientIp: "::1", userAgent: ua, deviceToken: tokenA);
        Assert.Equal("Auth.SignedIn", sA1.Code);
        Assert.Equal(0, await sessions.GetUnreadNotificationCountAsync(sA1.Session!));

        clock.Now = clock.Now.AddMinutes(1);
        var sA2 = await sessions.LoginAsync("initial.admin", password, clientIp: "::1", userAgent: ua, deviceToken: tokenA);
        Assert.Equal("Auth.SignedIn", sA2.Code);
        Assert.Equal(0, await sessions.GetUnreadNotificationCountAsync(sA2.Session!));

        // 2. Second browser on localhost (::1 + same Chrome UA, different device token) MUST trigger NewDeviceSignIn
        clock.Now = clock.Now.AddMinutes(1);
        var sB = await sessions.LoginAsync("initial.admin", password, clientIp: "::1", userAgent: ua, deviceToken: tokenB);
        Assert.Equal("Auth.SignedIn", sB.Code);
        Assert.Equal(1, await sessions.GetUnreadNotificationCountAsync(sB.Session!));

        // 3. Consecutive failed attempts (1st & 2nd) both notify
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("initial.admin", "wrong-1", clientIp: "::1", userAgent: ua)).Code);
        clock.Now = clock.Now.AddSeconds(10);
        Assert.Equal("Auth.LoginRejected", (await sessions.LoginAsync("initial.admin", "wrong-2", clientIp: "::1", userAgent: ua)).Code);
        Assert.Equal(3, await sessions.GetUnreadNotificationCountAsync(sB.Session!));

        // 4. Password change creates PasswordChangedAlert
        var nextPassword = AuthBootstrapTests.Password();
        clock.Now = clock.Now.AddMinutes(1);
        var pwRes = await sessions.ChangeOwnPasswordAsync(sB.Session!, password, nextPassword, clientIp: "::1", userAgent: ua);
        Assert.Equal("Auth.PasswordChanged", pwRes.Code);
        Assert.Equal(4, await sessions.GetUnreadNotificationCountAsync(pwRes.Session!));

        // 5. Role & access mutations notify target user
        source.Current = pwRes.Session;
        var orgId = initial.OrganizationId!.Value;
        var siteId = initial.SiteId!.Value;
        var targetPassword = AuthBootstrapTests.Password();
        long targetUserId;
        string siteRoleVersion;
        await using (var scope = provider.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var created = await users.CreateOrganizationUser(
                new OrganizationScope.Organization(orgId),
                new(orgId, "access.target", targetPassword, false, null, false));
            Assert.True(created.Succeeded);
            targetUserId = created.Value!.UserId;

            var siteAdded = await users.AssignSiteMembership(
                new OrganizationScope.Organization(orgId),
                new(orgId, targetUserId, siteId, AssignSiteOperator: true));
            Assert.True(siteAdded.Succeeded);
            siteRoleVersion = Assert.Single(siteAdded.Value!.Sites).SiteOperatorRoleRowVersion!;

            var deactivated = await users.SetTenantRoleActive(
                new OrganizationScope.Organization(orgId),
                new(orgId, targetUserId, TenantRoleKind.SiteOperator, siteId, false, siteRoleVersion));
            Assert.True(deactivated.Succeeded);

            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();
            var granted = await roles.SaveUserOperationGrants(
                new OrganizationScope.Organization(orgId),
                new(orgId, targetUserId, RoleScopeTarget.Organization, null,
                    [new(OrganizationPermissions.OrganizationsRead, true, null)]));
            Assert.True(granted.Succeeded);
        }

        clock.Now = clock.Now.AddMinutes(1);
        var targetLogin = await sessions.LoginAsync("access.target", targetPassword, clientIp: "::1", userAgent: ua, deviceToken: tokenA);
        Assert.Equal("Auth.SignedIn", targetLogin.Code);
        var targetPage = await sessions.ListNotificationsAsync(targetLogin.Session!, NotificationListFilter.Security, 1, 10);
        Assert.Equal(3, targetPage.Items.Count(n => n.TemplateCode == NotificationTemplateCatalog.RoleOrAccessChanged));
    }
}
