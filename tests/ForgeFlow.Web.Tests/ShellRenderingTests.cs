using System.Globalization;
using System.Security.Claims;
using Bit.BlazorUI;
using ForgeFlow.Application.Dashboard;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Components.Layout;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Localization;
using ForgeFlow.Web.Organizations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ShellRenderingTests
{
    [Theory]
    [InlineData("fa-IR", "دسترسی غیرمجاز")]
    [InlineData("en-US", "Access denied")]
    public async Task Access_denied_renders_localized_heading_and_safe_home_link(string culture, string expectedTitle)
    {
        var html = await Render<AccessDenied>(culture, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ebrahim")], "Test")));
        Assert.Contains(expectedTitle, html);
        Assert.Contains("href=\"/\"", html);
        Assert.DoesNotContain("ORG", html);
    }

    [Theory]
    [InlineData("fa-IR", "خروج")]
    [InlineData("en-US", "Sign out")]
    public async Task User_menu_renders_initial_only_on_trigger_and_full_username_in_panel(string culture, string expectedLogout)
    {
        var html = await Render<UserMenu>(culture, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "very.long.operator.name")], "Test")));
        Assert.Contains(">V</span>", html);
        Assert.Contains("very.long.operator.name", html);
        Assert.Contains(expectedLogout, html);
        Assert.Contains("action=\"/account/logout\"", html);
    }

    [Theory]
    [InlineData("fa-IR", "داشبورد عملیات")]
    [InlineData("en-US", "Operations Dashboard")]
    public async Task Home_page_renders_operations_dashboard_with_localized_heading(string culture, string expectedTitle)
    {
        var html = await Render<Home>(culture, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ebrahim")], "Test")));
        Assert.Contains($"<h1 id=\"dash-title\">{expectedTitle}</h1>", html);
    }

    private static async Task<string> Render<TComponent>(string culture, ClaimsPrincipal principal) where TComponent : IComponent
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            var info = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = info;
            CultureInfo.CurrentUICulture = info;
            var services = new ServiceCollection().AddLogging();
            services.AddLocalization(o => o.ResourcesPath = "Resources");
            services.AddSingleton<IJSRuntime, NoBrowserJs>();
            services.AddBitBlazorUIServices();
            services.AddSingleton<AuthenticationStateProvider>(new StaticAuthProvider(principal));
            services.AddAuthorizationCore();
            services.AddSingleton<CulturePolicy>();
            services.AddSingleton<DisplayFormatter>();
            services.AddDbContext<AuthDbContext>(o => o.UseSqlServer("Server=invalid.invalid;Database=missing;Integrated Security=True;Encrypt=True;Connect Timeout=1"));
            services.AddSingleton<ITrustedActorAccessor>(new NullActorAccessor());
            services.AddSingleton<IOrganizationPermissionEvaluator>(new DenyAllPermissions());
            services.AddSingleton<IDashboardQueryStore>(new EmptyDashboardStore());
            services.AddSingleton<IOrganizationOperationsStore>(new EmptyOrganizationStore());
            services.AddScoped<DashboardUseCases>();
            services.AddScoped<OrganizationUseCases>();
            services.AddScoped<ReadScopes>();
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                CultureInfo.CurrentCulture = info;
                CultureInfo.CurrentUICulture = info;
                var output = await renderer.RenderComponentAsync<CascadingAuthenticationState>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["ChildContent"] = (RenderFragment)(builder => { builder.OpenComponent<TComponent>(0); builder.CloseComponent(); })
                }));
                return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private sealed class StaticAuthProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }

    private sealed class NoBrowserJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    private sealed class NullActorAccessor : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(null);
    }

    private sealed class DenyAllPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default) =>
            ValueTask.FromResult(new PermissionDecision(false));
    }

    private sealed class EmptyDashboardStore : IDashboardQueryStore
    {
        public Task<DashboardSnapshot> GetSnapshot(long organizationId, long? siteId, DateOnly today, DashboardCapabilities capabilities, CancellationToken ct) =>
            Task.FromResult(new DashboardSnapshot(capabilities, null, [], null, [], null, [], null, [], []));
    }

    private sealed class EmptyOrganizationStore : IOrganizationOperationsStore
    {
        public Task<OrganizationPage<OrganizationDetails>> ListOrganizations(long organizationId, OrganizationListQuery query, CancellationToken ct) => Task.FromResult(new OrganizationPage<OrganizationDetails>([], 0, 1, 25));
        public Task<OrganizationDetails?> GetOrganization(long organizationId, CancellationToken ct) => Task.FromResult<OrganizationDetails?>(null);
        public Task<OrganizationPage<SiteDetails>> ListSites(long organizationId, long? siteId, OrganizationListQuery query, CancellationToken ct) => Task.FromResult(new OrganizationPage<SiteDetails>([], 0, 1, 25));
        public Task<SiteDetails?> GetSite(long organizationId, long siteId, CancellationToken ct) => Task.FromResult<SiteDetails?>(null);
        public Task<OrganizationResult<OrganizationDetails>> CreateOrganization(CreateOrganization request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SiteDetails>> CreateSite(CreateSite request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<OrganizationDetails>> UpdateOrganization(long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SiteDetails>> UpdateSite(long organizationId, long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }
}
