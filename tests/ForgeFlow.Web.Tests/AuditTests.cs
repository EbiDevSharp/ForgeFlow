using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Bit.BlazorUI;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Localization;
using ForgeFlow.Web.Organizations;
using ForgeFlow.Web.Roles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuditTests
{
    [Fact]
    public void U01_permission_catalog_and_role_localization_register_audit_read()
    {
        Assert.True(PermissionCatalog.TryGet(AuditPermissions.AuditRead, out var descriptor));
        Assert.Equal(PermissionModule.Audit, descriptor.Module);
        Assert.Equal("Read", descriptor.ActionKey);
        Assert.Equal(PermissionScopeApplicability.OrganizationOrSite, descriptor.Applicability);

        Assert.Contains(PermissionModule.Audit, RolePermissionLocalization.TenantModules);
        Assert.Equal("PermPageAudit", RolePermissionLocalization.PageTitleKey(PermissionModule.Audit));
        Assert.Equal("PermOpRead", RolePermissionLocalization.OperationLabelKey(AuditPermissions.AuditRead));

        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Organization(1), AuditPermissions.AuditRead));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Site(1, 10), AuditPermissions.AuditRead));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.DeploymentProvisioner, new OrganizationScope.Platform(), AuditPermissions.AuditRead));
        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.SiteOperator, new OrganizationScope.Site(1, 10), AuditPermissions.AuditRead));
    }

    [Fact]
    public void U04_payload_parser_flattens_nested_json_and_handles_invalid_json_gracefully()
    {
        var parsed = AuditPayloadParser.ParseFields("{\"Before\":{\"Name\":\"Old\"},\"After\":{\"Name\":\"New\",\"IsActive\":true},\"Tags\":[\"a\",\"b\"]}");
        Assert.Contains(parsed, kv => kv.Key == "Before.Name" && kv.Value == "Old");
        Assert.Contains(parsed, kv => kv.Key == "After.Name" && kv.Value == "New");
        Assert.Contains(parsed, kv => kv.Key == "After.IsActive" && kv.Value == "true");
        Assert.Contains(parsed, kv => kv.Key == "Tags" && kv.Value == "a, b");

        Assert.Empty(AuditPayloadParser.ParseFields(null));
        Assert.Empty(AuditPayloadParser.ParseFields(""));
        Assert.Empty(AuditPayloadParser.ParseFields("{not-valid-json"));
    }

    [Fact]
    public async Task U02_and_U03_audit_use_cases_fail_closed_and_validate_query_bounds()
    {
        var store = new FakeAuditStore();
        var missingAdapters = new AuditUseCases(store);
        var noActorRes = await missingAdapters.ListAuditEntries(new OrganizationScope.Organization(1), new AuditListQuery());
        Assert.False(noActorRes.Succeeded);
        Assert.Equal(AuditErrors.ActorRequired, noActorRes.Error!.Code);

        var deniedUseCases = new AuditUseCases(
            store,
            new StubActor(new TrustedActor(5, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid())),
            new StubPermissions(false));
        var deniedRes = await deniedUseCases.ListAuditEntries(new OrganizationScope.Organization(1), new AuditListQuery());
        Assert.False(deniedRes.Succeeded);
        Assert.Equal(AuditErrors.AccessDenied, deniedRes.Error!.Code);

        var allowedUseCases = new AuditUseCases(
            store,
            new StubActor(new TrustedActor(5, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid())),
            new StubPermissions(true));

        Assert.Equal(
            AuditErrors.InvalidQuery,
            (await allowedUseCases.ListAuditEntries(new OrganizationScope.Organization(1), new AuditListQuery(Page: 0))).Error!.Code);
        Assert.Equal(
            AuditErrors.InvalidQuery,
            (await allowedUseCases.ListAuditEntries(new OrganizationScope.Organization(1), new AuditListQuery(PageSize: 101))).Error!.Code);
        Assert.Equal(
            AuditErrors.InvalidQuery,
            (await allowedUseCases.ListAuditEntries(new OrganizationScope.Organization(1), new AuditListQuery(Search: new string('a', 101)))).Error!.Code);

        var okList = await allowedUseCases.ListAuditEntries(
            new OrganizationScope.Organization(1),
            new AuditListQuery(AuditCategory.Business, " Organization.Created ", " ALPHA ", 1, 20));
        Assert.True(okList.Succeeded);
        Assert.Equal("Organization.Created", store.LastQuery!.EventCode);
        Assert.Equal("ALPHA", store.LastQuery!.Search);

        Assert.Equal(
            AuditErrors.NotFound,
            (await allowedUseCases.GetAuditEntry(new OrganizationScope.Organization(1), AuditSourceKind.Business, 0)).Error!.Code);
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public async Task B01_to_B04_audit_page_renders_bilingual_entries_and_denied_state(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var catalog = LoadCatalog(cultureName);
            var okHtml = await RenderAuditPageAsync(cultureName, catalog, allowAccess: true);
            Assert.Contains(catalog["Audit"], okHtml, StringComparison.Ordinal);
            Assert.Contains(catalog["AuditCaption"], okHtml, StringComparison.Ordinal);
            Assert.Contains(catalog["AuditActorColumn"], okHtml, StringComparison.Ordinal);
            Assert.Contains(catalog["AuditTimeColumn"], okHtml, StringComparison.Ordinal);
            Assert.Contains(catalog["AuditOutcomeColumn"], okHtml, StringComparison.Ordinal);

            var deniedHtml = await RenderAuditPageAsync(cultureName, catalog, allowAccess: false);
            Assert.Contains(catalog["ReadDenied"], deniedHtml, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static async Task<string> RenderAuditPageAsync(
        string cultureName,
        IReadOnlyDictionary<string, string> catalog,
        bool allowAccess)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime>(new NoOpJs());
        services.AddBitBlazorUIServices();

        services.AddSingleton(new CulturePolicy());
        services.AddSingleton<DisplayFormatter>();
        services.AddSingleton<IStringLocalizer<SharedResources>>(new CatalogLocalizer(catalog));
        services.AddSingleton<NavigationManager>(new FakeNav("https://localhost:5001/organizations/1/audit"));

        services.AddDbContext<AuthDbContext>(o =>
            o.UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=true;Encrypt=true;Connect Timeout=1"));
        var actor = new TrustedActor(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        services.AddSingleton<ITrustedActorAccessor>(new ScopesSkipActor(actor));
        services.AddSingleton<IOrganizationPermissionEvaluator>(new StubPermissions(allowAccess));
        services.AddScoped<ReadScopes>();
        services.AddSingleton<IAuditOperationsStore>(new FakeAuditStore());
        services.AddScoped<AuditUseCases>();

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var info = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = info;
            CultureInfo.CurrentUICulture = info;
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Audit.OrganizationId)] = 1L
            });
            var output = await renderer.RenderComponentAsync<Audit>(parameters);
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    private static Dictionary<string, string> LoadCatalog(string culture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Catalogs", "Localization", $"SharedResources.{culture}.resx");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(x => x.Attribute("name")!.Value, x => x.Element("value")!.Value);
    }

    private sealed class StubActor(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult(actor);
    }

    private sealed class ScopesSkipActor(TrustedActor actor) : ITrustedActorAccessor
    {
        private int calls;
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Interlocked.Increment(ref calls) == 1 ? null : actor);
    }

    private sealed class StubPermissions(bool allowed) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor,
            OrganizationScope scope,
            string permission,
            CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(allowed));
    }

    private sealed class FakeAuditStore : IAuditOperationsStore
    {
        public AuditListQuery? LastQuery { get; private set; }

        private static readonly AuditEntrySummary[] SampleEntries =
        [
            new(
                AuditSourceKind.Business,
                10,
                new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc),
                "Organization.Created",
                1,
                null,
                1,
                "admin@forgeflow.local",
                null,
                null,
                null,
                null,
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "{\"Before\":null,\"After\":{\"Code\":\"ALPHA\",\"Name\":\"Alpha Corp\",\"IsActive\":true}}"),
            new(
                AuditSourceKind.Security,
                25,
                new DateTime(2026, 11, 10, 12, 5, 0, DateTimeKind.Utc),
                "RoleDefinitionChanged",
                1,
                null,
                1,
                "admin@forgeflow.local",
                null,
                null,
                null,
                1,
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "{\"action\":\"Created\",\"roleId\":100,\"name\":\"Auditor\"}")
        ];

        public Task<OrganizationPage<AuditEntrySummary>> ListAuditEntries(
            OrganizationScope scope,
            AuditListQuery query,
            CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(new OrganizationPage<AuditEntrySummary>(SampleEntries, SampleEntries.Length, query.Page, query.PageSize));
        }

        public Task<AuditEntryDetails?> GetAuditEntry(
            OrganizationScope scope,
            AuditSourceKind source,
            long entryId,
            CancellationToken ct = default)
        {
            var match = SampleEntries.FirstOrDefault(e => e.Source == source && e.Id == entryId);
            return Task.FromResult(match is null
                ? null
                : new AuditEntryDetails(match, AuditPayloadParser.ParseFields(match.RawJson)));
        }
    }

    private sealed class CatalogLocalizer(IReadOnlyDictionary<string, string> catalog) : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name]
            => catalog.TryGetValue(name, out var value) ? new(name, value, false) : new(name, name, true);

        public LocalizedString this[string name, params object[] arguments]
            => catalog.TryGetValue(name, out var value)
                ? new(name, string.Format(CultureInfo.CurrentCulture, value, arguments), false)
                : new(name, name, true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => catalog.Select(kv => new LocalizedString(kv.Key, kv.Value, false));
    }

    private sealed class FakeNav : NavigationManager
    {
        public FakeNav(string uri) => Initialize("https://localhost:5001/", uri);
        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
    }

    private sealed class NoOpJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
