using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ForgeFlow.Web.Hosting;
using ForgeFlow.Web.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task AuthRateLimiting_RejectsRequestsAfterPermitLimit_With429_AndRetryAfter()
    {
        var config = new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:Auth:PermitLimit"] = "2",
            ["RateLimiting:Auth:WindowSeconds"] = "30",
            ["RateLimiting:Auth:SegmentsPerWindow"] = "2",
            ["RateLimiting:Auth:QueueLimit"] = "0"
        };

        await using var host = await RateLimitTestHost.StartAsync(config, app =>
        {
            app.MapPost("/account/test-auth", () => Results.Ok(new { status = "authorized" }))
                .RequireRateLimiting(RateLimitingPolicyNames.Auth);
        });

        using var client = host.Client;
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");

        // 1. First request -> Success (200 OK)
        var res1 = await host.Client.PostAsync("/account/test-auth", null);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2. Second request -> Success (200 OK)
        var res2 = await host.Client.PostAsync("/account/test-auth", null);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // 3. Third request -> Rejected (429 Too Many Requests)
        var res3 = await host.Client.PostAsync("/account/test-auth", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, res3.StatusCode);

        // Assert Retry-After header
        Assert.True(res3.Headers.Contains("Retry-After"));
        var retryAfterValue = res3.Headers.GetValues("Retry-After").FirstOrDefault();
        Assert.NotNull(retryAfterValue);
        Assert.True(int.TryParse(retryAfterValue, out var retrySec) && retrySec > 0);

        // Assert JSON body content
        var json = await res3.Content.ReadFromJsonAsync<RateLimitErrorResponse>();
        Assert.NotNull(json);
        Assert.Equal("Auth.RateLimitExceeded", json.Code);
        Assert.True(json.RetryAfterSeconds > 0);
        Assert.Contains("sign-in", json.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthRateLimiting_ReturnsPersianMessage_WhenCultureIsFa()
    {
        var config = new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:Auth:PermitLimit"] = "1",
            ["RateLimiting:Auth:WindowSeconds"] = "45",
            ["RateLimiting:Auth:QueueLimit"] = "0"
        };

        await using var host = await RateLimitTestHost.StartAsync(config, app =>
        {
            app.MapPost("/account/test-auth", () => Results.Ok(new { status = "authorized" }))
                .RequireRateLimiting(RateLimitingPolicyNames.Auth);
        });

        host.Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fa-IR");

        // First request allowed
        var res1 = await host.Client.PostAsync("/account/test-auth", null);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Second request throttled
        var res2 = await host.Client.PostAsync("/account/test-auth", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, res2.StatusCode);

        var json = await res2.Content.ReadFromJsonAsync<RateLimitErrorResponse>();
        Assert.NotNull(json);
        Assert.Equal("Auth.RateLimitExceeded", json.Code);
        Assert.Contains("تلاش‌های ورود", json.Message);
    }

    [Fact]
    public async Task ExportRateLimiting_ThrottlesBursts_ForExportEndpoints()
    {
        var config = new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:Export:PermitLimit"] = "1",
            ["RateLimiting:Export:WindowSeconds"] = "60",
            ["RateLimiting:Export:QueueLimit"] = "0"
        };

        await using var host = await RateLimitTestHost.StartAsync(config, app =>
        {
            app.MapGet("/api/export/test", () => Results.Ok("exported"))
                .RequireRateLimiting(RateLimitingPolicyNames.Export);
        });

        using var client = host.Client;
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");

        // 1st request -> OK
        var res1 = await host.Client.GetAsync("/api/export/test");
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2nd request -> 429 Too Many Requests
        var res2 = await host.Client.GetAsync("/api/export/test");
        Assert.Equal(HttpStatusCode.TooManyRequests, res2.StatusCode);

        var json = await res2.Content.ReadFromJsonAsync<RateLimitErrorResponse>();
        Assert.NotNull(json);
        Assert.Equal("Export.RateLimitExceeded", json.Code);
        Assert.Contains("export", json.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnprotectedEndpoints_And_BlazorHub_RemainUnhindered()
    {
        var config = new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:Auth:PermitLimit"] = "1",
            ["RateLimiting:Export:PermitLimit"] = "1"
        };

        await using var host = await RateLimitTestHost.StartAsync(config, app =>
        {
            app.MapGet("/_blazor/negotiate", () => Results.Ok(new { connectionId = "xyz" }));
            app.MapGet("/health", () => Results.Ok("healthy"));
        });

        for (var i = 0; i < 10; i++)
        {
            var blazorRes = await host.Client.GetAsync("/_blazor/negotiate");
            Assert.Equal(HttpStatusCode.OK, blazorRes.StatusCode);

            var healthRes = await host.Client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);
        }
    }

    private sealed class RateLimitTestHost : IAsyncDisposable
    {
        private readonly WebApplication app;
        public HttpClient Client { get; }

        private RateLimitTestHost(WebApplication app, HttpClient client)
        {
            this.app = app;
            Client = client;
        }

        public static async Task<RateLimitTestHost> StartAsync(
            Dictionary<string, string?> config,
            Action<IEndpointRouteBuilder> configureEndpoints)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
            builder.Configuration.AddInMemoryCollection(config);
            builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
            builder.Services.AddForgeFlowRateLimiting(builder.Configuration);

            var app = builder.Build();
            app.UseRequestLocalization(CulturePolicy.Configure);
            app.UseRouting();
            app.UseRateLimiter();
            configureEndpoints(app);

            await app.StartAsync();
            var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            return new RateLimitTestHost(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed record RateLimitErrorResponse(string? Code, string? Message, int RetryAfterSeconds);
}
