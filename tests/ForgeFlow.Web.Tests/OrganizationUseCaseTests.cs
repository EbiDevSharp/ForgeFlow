using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class OrganizationUseCaseTests
{
    internal sealed class ActorSource : ITrustedActorAccessor
    {
        internal TrustedActor? Actor = new(42, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(Actor);
    }
    internal sealed class Grants(string permission, OrganizationScope scope) : IOrganizationPermissionEvaluator
    {
        internal bool Revoked;
        internal Action? AfterEvaluation;
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope requested, string operation, CancellationToken ct)
        {
            var matches = (requested, scope) switch {
                (OrganizationScope.Platform, OrganizationScope.Platform) => true,
                (OrganizationScope.Organization a, OrganizationScope.Organization b) => a.OrganizationId == b.OrganizationId,
                (OrganizationScope.Site a, OrganizationScope.Site b) => a.OrganizationId == b.OrganizationId && a.SiteId == b.SiteId,
                _ => false };
            var allowed = !Revoked && actor.ActorId == 42 && permission == operation && matches;
            var action = AfterEvaluation; AfterEvaluation = null; action?.Invoke();
            return ValueTask.FromResult(new PermissionDecision(allowed));
        }
    }
    private sealed class Port : IOrganizationOperationsStore
    {
        internal int Calls;
        internal long? OrgId, SiteId;
        internal OrganizationListQuery? Query;
        internal TrustedActor? Actor;
        private static OrganizationDetails Org(long id) => new(id, "ORG", "Company", true, Convert.ToBase64String(new byte[8]));
        private static SiteDetails Site(long org, long id) => new(id, org, "SITE", "Plant", true, Convert.ToBase64String(new byte[8]));
        public Task<OrganizationPage<OrganizationDetails>> ListOrganizations(long id, OrganizationListQuery query, CancellationToken ct)
        { Calls++; OrgId = id; Query = query; return Task.FromResult(new OrganizationPage<OrganizationDetails>([Org(id)], 1, query.Page, query.PageSize)); }
        public Task<OrganizationDetails?> GetOrganization(long id, CancellationToken ct)
        { Calls++; OrgId = id; return Task.FromResult<OrganizationDetails?>(id == 999 ? null : Org(id)); }
        public Task<OrganizationPage<SiteDetails>> ListSites(long org, long? site, OrganizationListQuery query, CancellationToken ct)
        { Calls++; OrgId = org; SiteId = site; Query = query; return Task.FromResult(new OrganizationPage<SiteDetails>([], 0, query.Page, query.PageSize)); }
        public Task<SiteDetails?> GetSite(long org, long id, CancellationToken ct)
        { Calls++; OrgId = org; SiteId = id; return Task.FromResult<SiteDetails?>(id == 999 ? null : Site(org, id)); }
        private async Task<OrganizationResult<T>> Write<T>(T value, OrganizationWriteAccess access, CancellationToken ct)
        { Calls++; Actor = access.Actor; return await access.Revalidate(ct) ? OrganizationResult<T>.Success(value) : OrganizationResult<T>.Failure(OrganizationAccessErrors.AccessDenied); }
        public Task<OrganizationResult<OrganizationDetails>> CreateOrganization(CreateOrganization request, OrganizationWriteAccess access, CancellationToken ct) => Write(Org(1), access, ct);
        public Task<OrganizationResult<SiteDetails>> CreateSite(CreateSite request, OrganizationWriteAccess access, CancellationToken ct) => Write(Site(request.OrganizationId, 1), access, ct);
        public Task<OrganizationResult<OrganizationDetails>> UpdateOrganization(long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct) => Write(Org(id), access, ct);
        public Task<OrganizationResult<SiteDetails>> UpdateSite(long org, long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct) => Write(Site(org, id), access, ct);
    }

    [Fact]
    public async Task Runtime_registration_denies_without_adapters_or_database_configuration()
    {
        using var provider = new ServiceCollection().AddOrganizationPersistence(new ConfigurationBuilder().Build()).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<OrganizationUseCases>();
        Assert.Equal(OrganizationAccessErrors.ActorRequired, (await service.GetOrganization(new OrganizationScope.Organization(1), 1)).Error!.Code);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("scope")]
    [InlineData("permission")]
    [InlineData("revoked")]
    public async Task Missing_context_and_permissions_fail_before_store_access(string missing)
    {
        var port = new Port(); var actor = new ActorSource(); var scope = new OrganizationScope.Organization(1);
        var grant = new Grants(OrganizationPermissions.OrganizationsRead, scope) { Revoked = missing == "revoked" };
        if (missing == "actor") actor.Actor = null;
        var service = new OrganizationUseCases(port, actor, missing == "permission" ? null : grant);
        var result = await service.GetOrganization(missing == "scope" ? null : scope, 1);
        Assert.False(result.Succeeded); Assert.Equal(0, port.Calls);
        Assert.Equal(missing == "actor" ? OrganizationAccessErrors.ActorRequired : missing == "scope" ? OrganizationAccessErrors.ScopeRequired : OrganizationAccessErrors.AccessDenied, result.Error!.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guessed_ids_and_missing_ids_return_the_same_not_found_without_sensitive_parameters(bool site)
    {
        var port = new Port(); var actor = new ActorSource();
        var scope = new OrganizationScope.Organization(1);
        var service = new OrganizationUseCases(port, actor, new Grants(site ? OrganizationPermissions.SitesRead : OrganizationPermissions.OrganizationsRead, scope));
        var wrong = site ? (await service.GetSite(scope, 2, 1)).Error : (await service.GetOrganization(scope, 2)).Error;
        Assert.Equal(0, port.Calls);
        var missingScope = new OrganizationScope.Organization(999);
        service = new OrganizationUseCases(port, actor, new Grants(site ? OrganizationPermissions.SitesRead : OrganizationPermissions.OrganizationsRead, missingScope));
        var absent = site ? (await service.GetSite(missingScope, 999, 999)).Error : (await service.GetOrganization(missingScope, 999)).Error;
        Assert.Equal(wrong!.Code, absent!.Code); Assert.Empty(wrong.Parameters); Assert.Empty(absent.Parameters);
    }

    [Fact]
    public async Task Site_scoped_lists_preserve_owner_and_site_predicates_and_bound_paging()
    {
        var port = new Port(); var scope = new OrganizationScope.Site(10, 20);
        var service = new OrganizationUseCases(port, new ActorSource(), new Grants(OrganizationPermissions.SitesRead, scope));
        Assert.True((await service.ListSites(scope, 10, new OrganizationListQuery(Page: -1, PageSize: 1000))).Succeeded);
        Assert.Equal(10, port.OrgId); Assert.Equal(20, port.SiteId);
        Assert.Equal(1, port.Query!.Page); Assert.Equal(100, port.Query.PageSize);
        Assert.Equal("Site.NotFound", (await service.GetSite(scope, 10, 21)).Error!.Code);
        Assert.Equal(1, port.Calls);
    }

    [Fact]
    public async Task Organization_creation_requires_platform_authority_and_uses_resolved_actor()
    {
        var port = new Port(); var actor = new ActorSource(); var org = new OrganizationScope.Organization(1);
        var service = new OrganizationUseCases(port, actor, new Grants(OrganizationPermissions.OrganizationsCreate, org));
        Assert.Equal(OrganizationAccessErrors.AccessDenied, (await service.CreateOrganization(org, new("ORG", "Company"))).Error!.Code);
        Assert.Equal(0, port.Calls);
        var platform = new OrganizationScope.Platform();
        service = new OrganizationUseCases(port, actor, new Grants(OrganizationPermissions.OrganizationsCreate, platform));
        Assert.True((await service.CreateOrganization(platform, new("ORG", "Company"))).Succeeded);
        Assert.Same(actor.Actor, port.Actor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("AA==")]
    public async Task Updates_require_an_eight_byte_expected_version_before_store_access(string? version)
    {
        var port = new Port(); var scope = new OrganizationScope.Organization(1);
        var service = new OrganizationUseCases(port, new ActorSource(), new Grants(OrganizationPermissions.OrganizationsUpdate, scope));
        Assert.Equal(OrganizationAccessErrors.ConcurrencyConflict, (await service.RenameOrganization(scope, new(1, "Name", version))).Error!.Code);
        Assert.Equal(0, port.Calls);
    }

    [Fact]
    public async Task Management_operations_require_their_exact_permission_and_matching_owner()
    {
        var port = new Port(); var actor = new ActorSource(); var org = new OrganizationScope.Organization(1);
        var version = Convert.ToBase64String(new byte[8]);
        var readOnly = new OrganizationUseCases(port, actor, new Grants(OrganizationPermissions.OrganizationsRead, org));
        Assert.False((await readOnly.CreateSite(org, new(1, "SITE", "Plant"))).Succeeded);
        Assert.False((await readOnly.RenameOrganization(org, new(1, "New", version))).Succeeded);
        Assert.False((await readOnly.SetOrganizationActive(org, new(1, false, version))).Succeeded);
        Assert.False((await readOnly.RenameSite(org, new(1, 2, "New", version))).Succeeded);
        Assert.False((await readOnly.SetSiteActive(org, new(1, 2, false, version))).Succeeded);
        Assert.Equal(0, port.Calls);
        var create = new OrganizationUseCases(port, actor, new Grants(OrganizationPermissions.SitesCreate, org));
        Assert.False((await create.CreateSite(org, new(2, "SITE", "Plant"))).Succeeded);
        Assert.True((await create.CreateSite(org, new(1, "SITE", "Plant"))).Succeeded);
        var update = new OrganizationUseCases(port, actor, new Grants(OrganizationPermissions.OrganizationsUpdate, org));
        Assert.True((await update.RenameOrganization(org, new(1, "New", version))).Succeeded);
        var lifecycle = new OrganizationUseCases(port, actor, new Grants(OrganizationPermissions.OrganizationsSetActive, org));
        Assert.True((await lifecycle.SetOrganizationActive(org, new(1, true, version))).Succeeded);
        var siteUpdate = new OrganizationUseCases(port, actor, new Grants(OrganizationPermissions.SitesUpdate, org));
        Assert.True((await siteUpdate.RenameSite(org, new(1, 2, "New", version))).Succeeded);
        var siteStatus = new OrganizationUseCases(port, actor, new Grants(OrganizationPermissions.SitesSetActive, org));
        Assert.True((await siteStatus.SetSiteActive(org, new(1, 2, false, version))).Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_revalidation_rejects_revoked_permission_or_changed_session(bool changeSession)
    {
        var port = new Port(); var actor = new ActorSource(); var scope = new OrganizationScope.Platform();
        var grants = new Grants(OrganizationPermissions.OrganizationsCreate, scope);
        grants.AfterEvaluation = () => {
            if (changeSession) actor.Actor = new TrustedActor(42, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
            else grants.Revoked = true;
        };
        var result = await new OrganizationUseCases(port, actor, grants).CreateOrganization(scope, new("ORG", "Company"));
        Assert.Equal(OrganizationAccessErrors.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Authorized_requests_without_a_connection_return_stable_persistence_failure()
    {
        var scope = new OrganizationScope.Organization(1);
        var services = new ServiceCollection().AddOrganizationPersistence(new ConfigurationBuilder().Build());
        services.AddSingleton<ITrustedActorAccessor>(new ActorSource());
        services.AddSingleton<IOrganizationPermissionEvaluator>(new Grants(OrganizationPermissions.OrganizationsRead, scope));
        using var provider = services.BuildServiceProvider();
        using var operation = provider.CreateScope();
        var result = await operation.ServiceProvider.GetRequiredService<OrganizationUseCases>().GetOrganization(scope, 1);
        Assert.Equal(OrganizationAccessErrors.PersistenceUnavailable, result.Error!.Code);
    }
}
