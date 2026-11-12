using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class SecurityAuditTests
{
    private static ServiceProvider BuildAuthServices(IConfiguration? config = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAntiforgery();
        services.AddAuthenticationBackend(config ?? new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static (DefaultHttpContext Context, string Token, string Cookie) CreateSignedFormPost(
        IServiceProvider sp,
        string body,
        bool isHttps = true,
        ClaimsPrincipal? user = null,
        long? declaredContentLength = null)
    {
        var csrf = sp.GetRequiredService<IAntiforgery>();
        var effectiveUser = user ?? new ClaimsPrincipal(new ClaimsIdentity());
        var issue = new DefaultHttpContext { RequestServices = sp, User = effectiveUser };
        issue.Request.Scheme = "https";
        var tokens = csrf.GetAndStoreTokens(issue);
        var cookie = issue.Response.Headers.SetCookie.Single()!.Split(';')[0];

        var fullBody = string.IsNullOrEmpty(body)
            ? "__RequestVerificationToken=" + Uri.EscapeDataString(tokens.RequestToken!)
            : body + "&__RequestVerificationToken=" + Uri.EscapeDataString(tokens.RequestToken!);
        var bytes = Encoding.UTF8.GetBytes(fullBody);

        var ctx = new DefaultHttpContext { RequestServices = sp, User = effectiveUser };
        ctx.Request.Scheme = isHttps ? "https" : "http";
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Headers.Cookie = cookie;
        ctx.Request.ContentLength = declaredContentLength ?? bytes.Length;
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Response.Body = new MemoryStream();
        return (ctx, tokens.RequestToken!, cookie);
    }

    [Fact]
    public void Cookie_and_device_cookie_enforce_host_prefix_and_secure_defaults()
    {
        using var sp = BuildAuthServices();
        var cookie = sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);

        Assert.StartsWith("__Host-", cookie.Cookie.Name, StringComparison.Ordinal);
        Assert.Equal("__Host-ForgeFlow.Auth", cookie.Cookie.Name);
        Assert.StartsWith("__Host-", AuthenticationBackend.DeviceCookieName, StringComparison.Ordinal);
        Assert.Null(cookie.Cookie.Domain);
        Assert.Equal("/", cookie.Cookie.Path);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite);
        Assert.False(cookie.SlidingExpiration);
        Assert.Equal(TimeSpan.FromHours(8), cookie.ExpireTimeSpan);
        Assert.Equal(TimeSpan.FromHours(8), RuntimeSessions.Lifetime);
    }

    [Fact]
    public async Task Login_rejects_non_https_requests_and_sets_no_store_cache_header()
    {
        using var sp = BuildAuthServices();
        using var scope = sp.CreateScope();
        var csrf = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var sessions = scope.ServiceProvider.GetRequiredService<RuntimeSessions>();
        var (ctx, _, _) = CreateSignedFormPost(scope.ServiceProvider, "username=operator&password=CorrectHorseBattery1!", isHttps: false);

        var result = await AuthenticationBackend.Login(ctx, csrf, sessions);
        await result.ExecuteAsync(ctx);

        Assert.Equal(400, ctx.Response.StatusCode);
        Assert.Contains("no-store", ctx.Response.Headers.CacheControl.ToString(), StringComparison.OrdinalIgnoreCase);
        ctx.Response.Body.Position = 0;
        var json = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        Assert.Contains("Auth.RequestRejected", json);
    }

    [Fact]
    public async Task Login_rejects_oversized_body_over_16kb()
    {
        using var sp = BuildAuthServices();
        using var scope = sp.CreateScope();
        var csrf = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var sessions = scope.ServiceProvider.GetRequiredService<RuntimeSessions>();
        var (ctx, _, _) = CreateSignedFormPost(scope.ServiceProvider, "username=operator&password=CorrectHorseBattery1!", declaredContentLength: 16385);

        var result = await AuthenticationBackend.Login(ctx, csrf, sessions);
        await result.ExecuteAsync(ctx);

        Assert.Equal(400, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        Assert.Contains("Auth.RequestRejected", await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Login_rejects_duplicate_credential_fields()
    {
        using var sp = BuildAuthServices();
        using var scope = sp.CreateScope();
        var csrf = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var sessions = scope.ServiceProvider.GetRequiredService<RuntimeSessions>();
        var (ctx, _, _) = CreateSignedFormPost(scope.ServiceProvider, "username=user1&username=user2&password=CorrectHorseBattery1!");

        var result = await AuthenticationBackend.Login(ctx, csrf, sessions);
        await result.ExecuteAsync(ctx);

        Assert.Equal(400, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        Assert.Contains("Auth.RequestRejected", await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Login_rejects_already_authenticated_user_without_explicit_logout()
    {
        using var sp = BuildAuthServices();
        using var scope = sp.CreateScope();
        var csrf = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var sessions = scope.ServiceProvider.GetRequiredService<RuntimeSessions>();
        var principal = SessionClaims.Create(new SessionReference(10, Guid.NewGuid(), "stamp-123"));
        var (ctx, _, _) = CreateSignedFormPost(scope.ServiceProvider, "username=other&password=CorrectHorseBattery1!", user: principal);

        var result = await AuthenticationBackend.Login(ctx, csrf, sessions);
        await result.ExecuteAsync(ctx);

        Assert.Equal(400, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        Assert.Contains("Auth.RequestRejected", await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:5001/")]
    [InlineData("/\\evil.example")]
    [InlineData("/%2f%2fevil.example")]
    [InlineData("/account/login")]
    [InlineData("/?next=https://evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void LocalReturnUrl_blocks_all_open_redirect_and_non_allowlisted_payloads(string payload)
    {
        Assert.Null(AuthenticationBackend.LocalReturnUrl(payload));
    }

    [Fact]
    public async Task ChangePassword_rejects_unauthenticated_and_mismatched_requests()
    {
        using var sp = BuildAuthServices();
        using var scope = sp.CreateScope();
        var csrf = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var sessions = scope.ServiceProvider.GetRequiredService<RuntimeSessions>();

        var (anonCtx, _, _) = CreateSignedFormPost(scope.ServiceProvider, "currentPassword=OldPass123456&newPassword=NewPass123456&confirmPassword=NewPass123456");
        var anonResult = await AuthenticationBackend.ChangePasswordSubmit(anonCtx, csrf, sessions);
        await anonResult.ExecuteAsync(anonCtx);
        Assert.Equal(401, anonCtx.Response.StatusCode);

        var principal = SessionClaims.Create(new SessionReference(10, Guid.NewGuid(), "stamp-123"));
        var (mismatchCtx, _, _) = CreateSignedFormPost(scope.ServiceProvider, "currentPassword=OldPass123456&newPassword=NewPass123456&confirmPassword=Different123456", user: principal);
        var mismatchResult = await AuthenticationBackend.ChangePasswordSubmit(mismatchCtx, csrf, sessions);
        await mismatchResult.ExecuteAsync(mismatchCtx);
        Assert.Equal(400, mismatchCtx.Response.StatusCode);
        mismatchCtx.Response.Body.Position = 0;
        Assert.Contains("Auth.PasswordMismatch", await new StreamReader(mismatchCtx.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public void Identity_password_and_lockout_policies_match_security_baseline()
    {
        using var sp = BuildAuthServices();
        var identity = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal(12, identity.Password.RequiredLength);
        Assert.Equal(5, identity.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), identity.Lockout.DefaultLockoutTimeSpan);
        Assert.DoesNotContain("'", identity.User.AllowedUserNameCharacters);
        Assert.DoesNotContain("\"", identity.User.AllowedUserNameCharacters);
        Assert.DoesNotContain("<", identity.User.AllowedUserNameCharacters);
        Assert.DoesNotContain(">", identity.User.AllowedUserNameCharacters);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", true)] // Handled by Identity RequiredLength (12); BootstrapPasswordValidator checks <= 128
    [InlineData("ValidPassword1234", true)]
    public async Task BootstrapPasswordValidator_rejects_null_and_overlong_passwords(string? password, bool expectedValid)
    {
        var validator = new BootstrapPasswordValidator();
        var res = await validator.ValidateAsync(null!, new ApplicationAccount(), password);
        Assert.Equal(expectedValid, res.Succeeded);

        var overlong = new string('a', 129);
        var overlongRes = await validator.ValidateAsync(null!, new ApplicationAccount(), overlong);
        Assert.False(overlongRes.Succeeded);
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_rejects_short_control_char_and_overlong_passwords_before_db()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();
        var reference = new SessionReference(1, Guid.NewGuid(), "stamp");

        Assert.Equal("Auth.PasswordInvalid", (await sessions.ChangeOwnPasswordAsync(reference, "OldPassword123", "short11char")).Code);
        Assert.Equal("Auth.PasswordInvalid", (await sessions.ChangeOwnPasswordAsync(reference, "OldPassword123", new string('a', 129))).Code);
        Assert.Equal("Auth.PasswordInvalid", (await sessions.ChangeOwnPasswordAsync(reference, "OldPassword123", "ValidPass\0Word123")).Code);
        Assert.Equal("Auth.CurrentPasswordInvalid", (await sessions.ChangeOwnPasswordAsync(reference, "", "ValidPassword123")).Code);
    }

    [Fact]
    public void SessionClaims_rejects_oversized_stamp_and_zero_user_or_empty_session()
    {
        var longStamp = new string('s', 257);
        var longPrincipal = SessionClaims.Create(new SessionReference(10, Guid.NewGuid(), longStamp));
        Assert.Null(SessionClaims.Read(longPrincipal));

        var zeroUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "0"),
            new Claim(SessionClaims.SessionId, Guid.NewGuid().ToString("D")),
            new Claim(SessionClaims.Stamp, "stamp")
        ], IdentityConstants.ApplicationScheme));
        Assert.Null(SessionClaims.Read(zeroUser));

        var emptySession = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "10"),
            new Claim(SessionClaims.SessionId, Guid.Empty.ToString("D")),
            new Claim(SessionClaims.Stamp, "stamp")
        ], IdentityConstants.ApplicationScheme));
        Assert.Null(SessionClaims.Read(emptySession));
    }

    [Fact]
    public async Task Security_headers_middleware_emits_required_hardening_headers()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.UseSecurityHeaders();
        app.MapGet("/ping", () => Results.Ok("ok"));

        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var response = await client.GetAsync("/ping");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
            Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            Assert.Equal("camera=(), microphone=(), geolocation=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")));
            var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("default-src 'self'", csp);
            Assert.Contains("object-src 'none'", csp);
            Assert.Contains("frame-ancestors 'self'", csp);
            Assert.Contains("base-uri 'self'", csp);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Auth_login_rate_limiter_partitions_by_remote_ip_and_returns_429_when_exceeded()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthenticationBackend(new ConfigurationBuilder().Build());
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapPost("/test-login", () => Results.Ok()).RequireRateLimiting("auth-login");

        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            for (var i = 0; i < 30; i++)
            {
                using var ok = await client.PostAsync("/test-login", new StringContent(string.Empty));
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            }

            using var throttled = await client.PostAsync("/test-login", new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
            var body = await throttled.Content.ReadAsStringAsync();
            Assert.Contains("Auth.LoginThrottled", body);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void Auth_ui_script_has_no_innerHTML_eval_or_credential_persistence()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "ApplicationSources", "wwwroot", "auth-ui.mjs");
        var script = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("innerHTML", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outerHTML", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eval(", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new URL(response.url).origin === location.origin", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Avatar_endpoint_requires_valid_session_and_disables_caching()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();
        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Response.Body = new MemoryStream();

        var result = await AuthenticationBackend.Avatar(ctx, sessions);
        await result.ExecuteAsync(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
        Assert.Contains("no-store", ctx.Response.Headers.CacheControl.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Antiforgery_token_from_anonymous_user_cannot_be_replayed_for_authenticated_change_password()
    {
        using var sp = BuildAuthServices();
        using var scope = sp.CreateScope();
        var csrf = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var sessions = scope.ServiceProvider.GetRequiredService<RuntimeSessions>();

        var (ctx, _, _) = CreateSignedFormPost(scope.ServiceProvider, "currentPassword=OldPass123456&newPassword=NewPass123456&confirmPassword=NewPass123456", user: null);
        ctx.User = SessionClaims.Create(new SessionReference(10, Guid.NewGuid(), "stamp-123"));

        var result = await AuthenticationBackend.ChangePasswordSubmit(ctx, csrf, sessions);
        await result.ExecuteAsync(ctx);

        Assert.Equal(400, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        Assert.Contains("Auth.RequestRejected", await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }
}
