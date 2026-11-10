using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuditSqlTests
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
        services.AddAuditManagement();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddAuditManagement_fails_closed_when_trusted_adapters_are_missing()
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
        services.AddAuditManagement();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditUseCases>();

        var result = await audit.ListAuditEntries(new OrganizationScope.Organization(1), new AuditListQuery());
        Assert.False(result.Succeeded);
        Assert.Equal(AuditErrors.ActorRequired, result.Error!.Code);
    }

    [OrganizationSqlServerFact]
    public async Task S01_to_S05_organization_site_and_platform_audit_isolation_and_filtering()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgAlphaId = initial.OrganizationId!.Value;
        var siteAlpha1Id = initial.SiteId!.Value;

        await using (var auth = AuthSessionSqlTests.Auth(connection))
        {
            await auth.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT [auth].[ScopedRoleAssignment] ([UserId],[RoleId],[ScopeKind],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({initial.UserId},{FixedAccessRoles.DeploymentProvisioner},{(byte)0},{true},{DateTime.UtcNow},{initial.UserId})");
        }

        var clock = new DeterministicClock();
        var source = new SessionSource();

        await using var provider = BuildProvider(connection, source, clock);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var adminSignIn = await sessions.LoginAsync("initial.admin", password);
        Assert.Equal("Auth.SignedIn", adminSignIn.Code);
        source.Current = adminSignIn.Session;

        long siteAlpha2Id, orgBetaId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var orgs = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();
            var s2 = await orgs.CreateSite(new OrganizationScope.Organization(orgAlphaId), new CreateSite(orgAlphaId, "S2", "Alpha Site 2"));
            Assert.True(s2.Succeeded);
            siteAlpha2Id = s2.Value!.Id;

            var beta = await orgs.CreateOrganization(new OrganizationScope.Platform(), new CreateOrganization("BETA", "Beta Organization"));
            Assert.True(beta.Succeeded);
            orgBetaId = beta.Value!.Id;

            var users = scope.ServiceProvider.GetRequiredService<UserUseCases>();
            var siteAuditor = await users.CreateOrganizationUser(
                new OrganizationScope.Organization(orgAlphaId),
                new CreateOrganizationUser(
                    orgAlphaId,
                    "siteauditor",
                    password,
                    AssignOrganizationAdmin: false,
                    SiteIds: [siteAlpha1Id],
                    AssignSiteOperatorToSites: false));
            Assert.True(siteAuditor.Succeeded);

            var roles = scope.ServiceProvider.GetRequiredService<RoleUseCases>();
            var grantRes = await roles.SaveUserOperationGrants(
                new OrganizationScope.Organization(orgAlphaId),
                new SaveUserOperationGrants(
                    orgAlphaId,
                    siteAuditor.Value!.UserId,
                    RoleScopeTarget.Site,
                    siteAlpha1Id,
                    [new OperationGrantChange(AuditPermissions.AuditRead, IsActive: true)]));
            Assert.True(grantRes.Succeeded);
        }

        // S01 & S04 & S05: Admin queries Organization Alpha (All, Business, Security, EventCode filter, and GetAuditEntry details)
        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditUseCases>();
            var alphaScope = new OrganizationScope.Organization(orgAlphaId);

            var allAlpha = await audit.ListAuditEntries(alphaScope, new AuditListQuery(AuditCategory.All, Page: 1, PageSize: 50));
            Assert.True(allAlpha.Succeeded);
            Assert.NotEmpty(allAlpha.Value!.Items);
            Assert.All(allAlpha.Value.Items, item => Assert.Equal(orgAlphaId, item.OrganizationId));

            var businessOnly = await audit.ListAuditEntries(alphaScope, new AuditListQuery(AuditCategory.Business, Page: 1, PageSize: 50));
            Assert.True(businessOnly.Succeeded);
            Assert.All(businessOnly.Value!.Items, item => Assert.Equal(AuditSourceKind.Business, item.Source));

            var securityOnly = await audit.ListAuditEntries(alphaScope, new AuditListQuery(AuditCategory.Security, Page: 1, PageSize: 50));
            Assert.True(securityOnly.Succeeded);
            Assert.All(securityOnly.Value!.Items, item => Assert.Equal(AuditSourceKind.Security, item.Source));
            Assert.Contains(securityOnly.Value.Items, item => item.EventCode == "SessionIssued" && item.SubjectUserName == "initial.admin");

            var filteredByCode = await audit.ListAuditEntries(alphaScope, new AuditListQuery(AuditCategory.Security, EventCode: "DirectPermissionChanged"));
            Assert.True(filteredByCode.Succeeded);
            var directPermEvent = Assert.Single(filteredByCode.Value!.Items);
            Assert.Equal("initial.admin", directPermEvent.ActorUserName);

            var details = await audit.GetAuditEntry(alphaScope, AuditSourceKind.Security, directPermEvent.Id);
            Assert.True(details.Succeeded);
            Assert.NotEmpty(details.Value!.ParsedFields);

            // S03: Platform scope only returns OrganizationId == null security events (e.g. LoginSucceeded)
            var platformEvents = await audit.ListAuditEntries(new OrganizationScope.Platform(), new AuditListQuery());
            Assert.True(platformEvents.Succeeded);
            Assert.NotEmpty(platformEvents.Value!.Items);
            Assert.All(platformEvents.Value.Items, item =>
            {
                Assert.Equal(AuditSourceKind.Security, item.Source);
                Assert.Null(item.OrganizationId);
            });
        }

        // S02: Site-scoped auditor can read Site 1 audit entries, is denied Organization Alpha and Site 2 and Beta
        var auditorSignIn = await sessions.LoginAsync("siteauditor", password);
        Assert.Equal("Auth.SignedIn", auditorSignIn.Code);
        source.Current = auditorSignIn.Session;

        await using (var scope = provider.CreateAsyncScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditUseCases>();
            var site1Res = await audit.ListAuditEntries(new OrganizationScope.Site(orgAlphaId, siteAlpha1Id), new AuditListQuery());
            Assert.True(site1Res.Succeeded);
            Assert.NotEmpty(site1Res.Value!.Items);
            Assert.All(site1Res.Value.Items, item =>
            {
                Assert.Equal(orgAlphaId, item.OrganizationId);
                Assert.Equal(siteAlpha1Id, item.SiteId);
            });

            var orgDenied = await audit.ListAuditEntries(new OrganizationScope.Organization(orgAlphaId), new AuditListQuery());
            Assert.False(orgDenied.Succeeded);
            Assert.Equal(AuditErrors.AccessDenied, orgDenied.Error!.Code);

            var site2Denied = await audit.ListAuditEntries(new OrganizationScope.Site(orgAlphaId, siteAlpha2Id), new AuditListQuery());
            Assert.False(site2Denied.Succeeded);
            Assert.Equal(AuditErrors.AccessDenied, site2Denied.Error!.Code);

            var betaDenied = await audit.ListAuditEntries(new OrganizationScope.Organization(orgBetaId), new AuditListQuery());
            Assert.False(betaDenied.Succeeded);
            Assert.Equal(AuditErrors.AccessDenied, betaDenied.Error!.Code);
        }
    }
}
