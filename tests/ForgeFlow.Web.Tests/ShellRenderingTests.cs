using System.Globalization;
using System.Security.Claims;
using Bit.BlazorUI;
using ForgeFlow.Web.Components.Layout;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
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
    [InlineData("fa-IR", "خانه")]
    [InlineData("en-US", "Home")]
    public async Task Home_page_renders_empty_shell_with_hidden_accessible_heading(string culture, string expectedTitle)
    {
        var html = await Render<Home>(culture, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ebrahim")], "Test")));
        Assert.Contains($"<h1 class=\"visually-hidden\">{expectedTitle}</h1>", html);
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
}
