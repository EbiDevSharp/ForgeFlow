using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuthSessionSqlTests
{
    internal static AuthDbContext Auth(string connection)=>new(new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(connection,s=>s.MigrationsHistoryTable("__EFMigrationsHistory","auth")).Options);
    internal static async Task<(string Connection,string Password,BootstrapResult Initial)> Seed(OrganizationSqlServerTests.DisposableDatabase database)
    {
        using var business=database.Context(); var connection=business.Database.GetConnectionString()!;
        await using var auth=Auth(connection); await auth.Database.MigrateAsync(); var password=AuthBootstrapTests.Password();
        var initial=await new HostBootstrapper(connection).InitializeAsync(AuthBootstrapTests.Request(),password,HostOperatorConsent.Confirm("INITIALIZE"));
        Assert.Equal("Auth.Initialized",initial.Code); return(connection,password,initial);
    }
    private sealed class Source : ICurrentAuthenticatedSession
    { public SessionReference? Current; public ValueTask<SessionReference?> GetAsync(CancellationToken ct)=>ValueTask.FromResult(Current); }
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now=DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow()=>Now; }
    private static ServiceProvider Provider(string connection,Source source,Clock? clock=null)
    {
        var services=new ServiceCollection().AddLogging();
        services.AddRuntimeIdentity(o=>o.UseSqlServer(connection,s=>s.MigrationsHistoryTable("__EFMigrationsHistory","auth")));
        services.AddSingleton<TimeProvider>(clock??new Clock()); services.AddSingleton<RuntimeSessions>();
        services.AddSingleton<ICurrentAuthenticatedSession>(source); services.AddScoped<ITrustedActorAccessor,SessionActorAccessor>();services.AddScoped<IOrganizationPermissionEvaluator,SessionPermissionEvaluator>();
        services.AddOrganizationPersistence(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:ForgeFlow",connection}}).Build());
        return services.BuildServiceProvider();
    }
    [OrganizationSqlServerFact]
    public async Task Real_credentials_sessions_adapters_and_use_cases_then_current_session_logout()
    {
        await using var database=await OrganizationSqlServerTests.DisposableDatabase.Create(); var (connection,password,initial)=await Seed(database);
        var source=new Source(); await using var provider=Provider(connection,source); var sessions=provider.GetRequiredService<RuntimeSessions>();
        var first=await sessions.LoginAsync("initial.admin",password,clientIp:"192.168.1.24");
        Assert.Equal("Auth.SignedIn",first.Code); Assert.NotNull(first.Session);
        Assert.Null(await sessions.GetPreviousSignInAsync(first.Session!));
        var second=await sessions.LoginAsync("initial.admin",password,clientIp:"10.20.30.40");
        var previous=await sessions.GetPreviousSignInAsync(second.Session!);
        Assert.NotNull(previous); Assert.Equal("192.168.1.24",previous!.IpAddress);
        source.Current=first.Session;
        using(var scope=provider.CreateScope()) {
            var actor=await scope.ServiceProvider.GetRequiredService<ITrustedActorAccessor>().ResolveAsync(default); Assert.Equal(initial.UserId,actor!.ActorId); Assert.Equal(first.Session.SessionId,actor.ProvenanceReference);
            var useCases=scope.ServiceProvider.GetRequiredService<OrganizationUseCases>(); var owner=new OrganizationScope.Organization(initial.OrganizationId!.Value);
            Assert.True((await useCases.GetOrganization(owner,initial.OrganizationId.Value)).Succeeded);
            Assert.Equal(OrganizationAccessErrors.AccessDenied,(await useCases.CreateOrganization(new OrganizationScope.Platform(),new("EXTRA","Extra"))).Error!.Code);
            Assert.Equal(OrganizationAccessErrors.AccessDenied,(await useCases.GetOrganization(new OrganizationScope.Organization(long.MaxValue),long.MaxValue)).Error!.Code);
            var before=(await useCases.GetOrganization(owner,initial.OrganizationId.Value)).Value!;
            Assert.True((await useCases.RenameOrganization(owner,new(before.Id,"Renamed",before.RowVersion))).Succeeded);
        }
        Assert.Equal("Auth.SignedOut",await sessions.LogoutAsync(first.Session)); Assert.Equal("Auth.SessionInvalid",await sessions.ValidateAsync(first.Session));
        Assert.Equal("Auth.Valid",await sessions.ValidateAsync(second.Session!)); Assert.Equal("Auth.SignedOut",await sessions.LogoutAsync(first.Session));
        await using var verify=Auth(connection); Assert.Equal(1,await verify.SecurityEvents.CountAsync(e=>e.EventCode=="SessionRevoked"));
    }
    [OrganizationSqlServerFact]
    public async Task Failed_credentials_lock_account_and_revoke_existing_sessions_atomically()
    {
        await using var database=await OrganizationSqlServerTests.DisposableDatabase.Create(); var (connection,password,_)=await Seed(database);
        await using var provider=Provider(connection,new()); var sessions=provider.GetRequiredService<RuntimeSessions>(); var first=await sessions.LoginAsync("initial.admin",password);
        Assert.Equal("Auth.LoginRejected",(await sessions.LoginAsync("unknown.owner",AuthBootstrapTests.Password())).Code);
        for(var i=0;i<5;i++) Assert.Equal("Auth.LoginRejected",(await sessions.LoginAsync("initial.admin",AuthBootstrapTests.Password())).Code);
        Assert.Equal("Auth.LoginRejected",(await sessions.LoginAsync("initial.admin",password)).Code);
        await using(var db=Auth(connection)) { Assert.NotNull((await db.Users.SingleAsync()).LockoutEnd); Assert.Single(await db.SecurityEvents.Where(e=>e.EventCode=="AccountLocked").ToListAsync()); await db.Database.ExecuteSqlRawAsync("UPDATE [auth].[User] SET LockoutEnd=NULL"); }
        Assert.Equal("Auth.SessionInvalid",await sessions.ValidateAsync(first.Session!));
    }
    [AuthBootstrapSqlTheory]
    [InlineData("disabled")] [InlineData("stamp")] [InlineData("expired")]
    public async Task Current_database_state_and_fixed_session_expiry_override_a_stale_ticket(string change)
    {
        await using var database=await OrganizationSqlServerTests.DisposableDatabase.Create(); var(connection,password,_)=await Seed(database);
        var clock=new Clock(); await using var provider=Provider(connection,new(),clock); var sessions=provider.GetRequiredService<RuntimeSessions>(); var login=await sessions.LoginAsync("initial.admin",password);
        await using(var db=Auth(connection)) {
            if(change=="disabled") await db.Database.ExecuteSqlRawAsync("UPDATE [auth].[User] SET IsActive=0");
            if(change=="stamp") await db.Database.ExecuteSqlRawAsync("UPDATE [auth].[User] SET SecurityStamp=CONVERT(nvarchar(256),NEWID())");
        }
        if(change=="expired") clock.Now=clock.Now.AddHours(9);
        Assert.Equal("Auth.SessionInvalid",await sessions.ValidateAsync(login.Session!));
        if(change=="stamp") {
            var fresh=await sessions.LoginAsync("initial.admin",password); Assert.Equal("Auth.SignedIn",fresh.Code);
            Assert.Equal("Auth.SessionInvalid",await sessions.ValidateAsync(login.Session!)); Assert.Equal("Auth.Valid",await sessions.ValidateAsync(fresh.Session!));
        }
        await using var verify=Auth(connection); if(change!="expired") Assert.NotNull((await verify.Sessions.SingleAsync(s=>s.Id==login.Session!.SessionId)).RevokedAtUtc);
    }
    [OrganizationSqlServerFact]
    public async Task Membership_revocation_and_site_owner_checks_deny_next_operation_without_stale_claim_authority()
    {
        await using var database=await OrganizationSqlServerTests.DisposableDatabase.Create(); var(connection,password,initial)=await Seed(database);
        var source=new Source(); await using var provider=Provider(connection,source); var sessions=provider.GetRequiredService<RuntimeSessions>(); source.Current=(await sessions.LoginAsync("initial.admin",password)).Session;
        var actor=await new SessionActorAccessor(source,sessions).ResolveAsync(default); var permissions=new SessionPermissionEvaluator(source,sessions);
        Assert.False((await permissions.EvaluateAsync(actor!,new OrganizationScope.Site(initial.OrganizationId!.Value,long.MaxValue),OrganizationPermissions.SitesRead,default)).IsAllowed);
        await using(var db=Auth(connection)) await db.Database.ExecuteSqlRawAsync("UPDATE [auth].[OrganizationMembership] SET IsActive=0");
        Assert.False((await permissions.EvaluateAsync(actor!,new OrganizationScope.Organization(initial.OrganizationId!.Value),OrganizationPermissions.OrganizationsRead,default)).IsAllowed);
        Assert.Equal("Auth.Valid",await sessions.ValidateAsync(source.Current!)); // Membership loss does not prevent logout.
    }
    [AuthBootstrapSqlTheory]
    [InlineData("SessionIssued")] [InlineData("SessionRevoked")]
    public async Task Required_event_failure_rolls_back_session_creation_or_logout(string eventCode)
    {
        await using var database=await OrganizationSqlServerTests.DisposableDatabase.Create(); var(connection,password,_)=await Seed(database);
        await using var provider=Provider(connection,new()); var sessions=provider.GetRequiredService<RuntimeSessions>();
        var first=eventCode=="SessionRevoked" ? await sessions.LoginAsync("initial.admin",password) : null;
        await using(var db=Auth(connection)) {
            // Fixed theory strings; disposable database only. Constraint injects a real SQL rejection.
            if(eventCode=="SessionIssued") await db.Database.ExecuteSqlRawAsync("ALTER TABLE [auth].[SecurityEvent] ADD CONSTRAINT [SyntheticSessionAuditFailure] CHECK(EventCode<>'SessionIssued')");
            else await db.Database.ExecuteSqlRawAsync("ALTER TABLE [auth].[SecurityEvent] ADD CONSTRAINT [SyntheticSessionAuditFailure] CHECK(EventCode<>'SessionRevoked')");
        }
        if(first is null) { Assert.Equal("Auth.ServiceUnavailable",(await sessions.LoginAsync("initial.admin",password)).Code); await using var verify=Auth(connection); Assert.Empty(await verify.Sessions.ToListAsync()); }
        else { Assert.Equal("Auth.LogoutRevocationIncomplete",await sessions.LogoutAsync(first.Session!)); await using var verify=Auth(connection); Assert.Null((await verify.Sessions.SingleAsync()).RevokedAtUtc); }
    }

    [OrganizationSqlServerFact]
    public async Task Explicit_platform_grant_can_create_but_does_not_imply_membership_in_created_company()
    {
        await using var database=await OrganizationSqlServerTests.DisposableDatabase.Create(); var(connection,password,initial)=await Seed(database);
        var source=new Source(); await using var provider=Provider(connection,source); source.Current=(await provider.GetRequiredService<RuntimeSessions>().LoginAsync("initial.admin",password)).Session;
        await using(var db=Auth(connection)) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [auth].[ScopedRoleAssignment] ([UserId],[RoleId],[ScopeKind],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({initial.UserId},3,0,1,SYSUTCDATETIME(),{initial.UserId})");
        using var scope=provider.CreateScope(); var useCases=scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();
        var created=await useCases.CreateOrganization(new OrganizationScope.Platform(),new("SECOND","Second company")); Assert.True(created.Succeeded);
        Assert.Equal(OrganizationAccessErrors.AccessDenied,(await useCases.GetOrganization(new OrganizationScope.Organization(created.Value!.Id),created.Value.Id)).Error!.Code);
    }
    [OrganizationSqlServerFact]
    public async Task Site_operator_requires_both_memberships_and_cannot_access_sibling_or_organization_scope()
    {
        await using var database=await OrganizationSqlServerTests.DisposableDatabase.Create(); var(connection,password,initial)=await Seed(database);
        var source=new Source(); await using var provider=Provider(connection,source); var sessions=provider.GetRequiredService<RuntimeSessions>(); source.Current=(await sessions.LoginAsync("initial.admin",password)).Session;
        await using(var db=Auth(connection)) {
            await db.Database.ExecuteSqlRawAsync("UPDATE [auth].[ScopedRoleAssignment] SET IsActive=0");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [auth].[ScopedRoleAssignment] ([UserId],[RoleId],[ScopeKind],[OrganizationId],[SiteId],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({initial.UserId},2,2,{initial.OrganizationId},{initial.SiteId},1,SYSUTCDATETIME(),{initial.UserId})");
        }
        var actor=await new SessionActorAccessor(source,sessions).ResolveAsync(default); var permissions=new SessionPermissionEvaluator(source,sessions);
        Assert.True((await permissions.EvaluateAsync(actor!,new OrganizationScope.Site(initial.OrganizationId!.Value,initial.SiteId!.Value),OrganizationPermissions.SitesRead,default)).IsAllowed);
        Assert.False((await permissions.EvaluateAsync(actor!,new OrganizationScope.Organization(initial.OrganizationId.Value),OrganizationPermissions.SitesRead,default)).IsAllowed);
        Assert.False((await permissions.EvaluateAsync(actor!,new OrganizationScope.Site(initial.OrganizationId.Value,long.MaxValue),OrganizationPermissions.SitesRead,default)).IsAllowed);
        await using(var db=Auth(connection)) await db.Database.ExecuteSqlRawAsync("UPDATE [auth].[SiteMembership] SET IsActive=0");
        Assert.False((await permissions.EvaluateAsync(actor!,new OrganizationScope.Site(initial.OrganizationId.Value,initial.SiteId.Value),OrganizationPermissions.SitesRead,default)).IsAllowed);
    }
    [OrganizationSqlServerFact]
    public async Task Preferred_culture_updates_without_rotating_security_stamp_and_audits_change()
    {
        await using var database=await OrganizationSqlServerTests.DisposableDatabase.Create(); var(connection,password,_)=await Seed(database);
        await using var provider=Provider(connection,new()); var sessions=provider.GetRequiredService<RuntimeSessions>();
        var login=await sessions.LoginAsync("initial.admin",password); var session=login.Session!;
        Assert.Equal("Culture.ProfileSaved",await sessions.SetPreferredCultureAsync(session,"en-US"));
        Assert.Equal("en-US",await sessions.GetPreferredCultureAsync(session));
        Assert.Equal("Auth.Valid",await sessions.ValidateAsync(session));
        await using var verify=Auth(connection);
        var user=await verify.Users.SingleAsync();
        Assert.Equal("en-US",user.PreferredCulture);
        Assert.Equal(session.SecurityStamp,user.SecurityStamp);
        Assert.Equal(1,await verify.SecurityEvents.CountAsync(e=>e.EventCode=="CulturePreferenceChanged"));
    }
}

public sealed class AuthSessionHttpSqlTests
{
    [OrganizationSqlServerFact]
    public async Task Real_http_login_and_logout_use_protected_cookies_antiforgery_and_server_revocation()
    {
        await using var database=await OrganizationSqlServerTests.DisposableDatabase.Create(); var(connection,password,_)=await AuthSessionSqlTests.Seed(database);
        await using var host=await AuthHttpHost.Start(connection); var token=await host.Token();
        using var login=await host.Post("/account/sign-in",token,("username","initial.admin"),("password",password),("returnUrl","/"));
        Assert.Equal(System.Net.HttpStatusCode.Redirect,login.StatusCode);
        var oldCookie=login.Headers.GetValues("Set-Cookie").Single(c=>c.StartsWith("__Host-ForgeFlow.Auth=",StringComparison.Ordinal)).Split(';')[0];
        using var access=await host.Client.GetAsync("/test/protected"); Assert.Equal(System.Net.HttpStatusCode.NoContent,access.StatusCode);
        token=await host.Token(); using var logout=await host.Post("/account/logout",token,("returnUrl","/")); Assert.Equal(System.Net.HttpStatusCode.Redirect,logout.StatusCode);
        using var denied=await host.Client.GetAsync("/test/protected"); Assert.Equal(System.Net.HttpStatusCode.Unauthorized,denied.StatusCode);
        using var replay=new HttpRequestMessage(HttpMethod.Get,"/test/protected"); replay.Headers.Add("Cookie",oldCookie);
        using var replayed=await host.Client.SendAsync(replay); Assert.Equal(System.Net.HttpStatusCode.Unauthorized,replayed.StatusCode);
        await using var verify=AuthSessionSqlTests.Auth(connection); Assert.NotNull((await verify.Sessions.SingleAsync()).RevokedAtUtc);
    }
}
