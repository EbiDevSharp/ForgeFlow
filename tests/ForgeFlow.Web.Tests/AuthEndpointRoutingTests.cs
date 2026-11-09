using System.Net;
using ForgeFlow.Web.Authentication;
using ForgeFlow.Web.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuthEndpointRoutingTests
{
    [Fact]
    public async Task Real_router_selects_distinct_login_page_sign_in_and_logout_endpoints()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(App).Assembly.FullName });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddLocalization();
        builder.Services.AddAuthenticationBackend(builder.Configuration);
        await using var app = builder.Build();
        app.UseRouting();
        // Observe actual endpoint selection, before executing credentials, antiforgery or any SQL.
        app.Use((HttpContext context, RequestDelegate _) =>
        {
            var endpoint = context.GetEndpoint();
            context.Response.StatusCode = endpoint is null ? 404 : 204;
            context.Response.Headers["Selected-Endpoint"] = endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "RazorPage";
            return Task.CompletedTask;
        });
        app.MapAuthenticationBackend();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        await app.StartAsync();
        try
        {
            var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
            foreach (var path in new[] { "/account/login", "/account/sign-in", "/account/logout" })
                Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == path);
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var page = await client.GetAsync("/account/login");
            Assert.Equal(HttpStatusCode.NoContent, page.StatusCode);
            Assert.Equal("RazorPage", Assert.Single(page.Headers.GetValues("Selected-Endpoint")));
            using var login = await client.PostAsync("/account/sign-in", new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            Assert.Equal("Auth.Login", Assert.Single(login.Headers.GetValues("Selected-Endpoint")));
            using var logout = await client.PostAsync("/account/logout", new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Equal("Auth.Logout", Assert.Single(logout.Headers.GetValues("Selected-Endpoint")));
        }
        finally { await app.StopAsync(); }
    }
}
