using System.Globalization;
using System.Security.Claims;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuthBackendTests
{
    [Fact]
    public async Task Outage_logout_validates_original_cookie_csrf_but_never_restores_authority_and_expires_cookie()
    {
        var services=new ServiceCollection().AddLogging().AddLocalization(o=>o.ResourcesPath="Resources");
        services.AddDataProtection().UseEphemeralDataProtectionProvider(); services.AddAntiforgery(); services.AddAuthenticationBackend(new ConfigurationBuilder().Build());
        using var provider=services.BuildServiceProvider(); using var scope=provider.CreateScope();
        var csrf=scope.ServiceProvider.GetRequiredService<IAntiforgery>(); var principal=SessionClaims.Create(Reference());
        var issue=new DefaultHttpContext{RequestServices=scope.ServiceProvider,User=principal}; issue.Request.Scheme="https";
        var tokens=csrf.GetAndStoreTokens(issue); var cookie=issue.Response.Headers.SetCookie.Single()!.Split(';')[0];
        var logout=new DefaultHttpContext{RequestServices=scope.ServiceProvider}; logout.Request.Scheme="https"; logout.Request.Method="POST";
        logout.Request.ContentType="application/x-www-form-urlencoded"; logout.Request.Headers.Cookie=cookie;
        logout.Request.Body=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("returnUrl=%2F&__RequestVerificationToken="+Uri.EscapeDataString(tokens.RequestToken!)));
        logout.Response.Body=new MemoryStream(); logout.Items[AuthenticationBackend.UnavailableItem]=true; logout.Items[AuthenticationBackend.RejectedPrincipalItem]=principal;
        await (await AuthenticationBackend.Logout(logout,csrf,provider.GetRequiredService<RuntimeSessions>())).ExecuteAsync(logout);
        Assert.Equal(503,logout.Response.StatusCode); Assert.False(logout.User.Identity?.IsAuthenticated ?? false);
        Assert.Contains(logout.Response.Headers.SetCookie,c=>c!.StartsWith("__Host-ForgeFlow.Auth=;",StringComparison.Ordinal));
        logout.Response.Body.Position=0; Assert.Contains("Auth.LogoutRevocationIncomplete",await new StreamReader(logout.Response.Body).ReadToEndAsync());
    }
    [Fact]
    public async Task Real_antiforgery_rejects_untrusted_posts_and_allows_verified_request_to_fail_closed_on_missing_store()
    {
        var services=new ServiceCollection().AddLogging().AddLocalization(o=>o.ResourcesPath="Resources");
        services.AddDataProtection().UseEphemeralDataProtectionProvider(); services.AddAntiforgery(); services.AddAuthenticationBackend(new ConfigurationBuilder().Build());
        using var provider=services.BuildServiceProvider(); using var scope=provider.CreateScope();
        var csrf=scope.ServiceProvider.GetRequiredService<IAntiforgery>(); var sessions=scope.ServiceProvider.GetRequiredService<RuntimeSessions>();
        DefaultHttpContext Context(string body,string? cookie=null) {
            var ctx=new DefaultHttpContext{RequestServices=scope.ServiceProvider}; ctx.Request.Scheme="https"; ctx.Request.Method="POST"; ctx.Request.ContentType="application/x-www-form-urlencoded";
            ctx.Request.Body=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)); ctx.Response.Body=new MemoryStream();
            if(cookie is not null)ctx.Request.Headers.Cookie=cookie; return ctx;
        }
        var rejected=Context("username=synthetic&password=synthetic-invalid");
        await (await AuthenticationBackend.Login(rejected,csrf,sessions)).ExecuteAsync(rejected); Assert.Equal(400,rejected.Response.StatusCode);
        var issue=Context(""); var tokens=csrf.GetAndStoreTokens(issue); var cookie=issue.Response.Headers.SetCookie.Single()!.Split(';')[0];
        var accepted=Context("username=synthetic&password=synthetic-invalid&__RequestVerificationToken="+Uri.EscapeDataString(tokens.RequestToken!),cookie);
        await (await AuthenticationBackend.Login(accepted,csrf,sessions)).ExecuteAsync(accepted); Assert.Equal(503,accepted.Response.StatusCode);
        accepted.Response.Body.Position=0; Assert.Contains("Auth.ServiceUnavailable",await new StreamReader(accepted.Response.Body).ReadToEndAsync());
    }
    [Fact]
    public async Task Login_ui_can_fetch_a_no_store_antiforgery_form_token()
    {
        var services=new ServiceCollection().AddLogging().AddLocalization(o=>o.ResourcesPath="Resources");
        services.AddDataProtection().UseEphemeralDataProtectionProvider(); services.AddAntiforgery();
        using var provider=services.BuildServiceProvider(); using var scope=provider.CreateScope();
        var context=new DefaultHttpContext { RequestServices=scope.ServiceProvider }; context.Request.Scheme="https"; context.Response.Body=new MemoryStream();
        await AuthenticationBackend.AntiforgeryToken(context,scope.ServiceProvider.GetRequiredService<IAntiforgery>()).ExecuteAsync(context);
        Assert.Contains("no-store",context.Response.Headers.CacheControl.ToString(),StringComparison.OrdinalIgnoreCase);
        Assert.Contains(context.Response.Headers.SetCookie,c=>c!.StartsWith(".AspNetCore.Antiforgery",StringComparison.Ordinal));
        context.Response.Body.Position=0;
        var json=await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("requestToken").GetString()));
    }
    internal static SessionReference Reference() => new(42,Guid.NewGuid(),Guid.NewGuid().ToString());
    [Theory]
    [InlineData(null,true)] [InlineData("",true)] [InlineData("/",true)]
    [InlineData("//evil.example",false)] [InlineData("https://evil.example",false)] [InlineData("/\\evil.example",false)]
    [InlineData("/%2f%2fevil.example",false)] [InlineData("/account/login",false)] [InlineData("/?next=https://evil.example",false)]
    public void Return_targets_use_only_existing_allowlisted_local_routes(string? raw,bool allowed)
        => Assert.Equal(allowed,AuthenticationBackend.LocalReturnUrl(raw)=="/");

    [Theory]
    [InlineData("user")] [InlineData("session")] [InlineData("stamp")] [InlineData("duplicate")] [InlineData("scheme")] [InlineData("identities")]
    public void Missing_malformed_or_duplicate_reserved_claims_are_rejected(string invalid)
    {
        var principal=SessionClaims.Create(Reference()); var identity=(ClaimsIdentity)principal.Identity!;
        var type=invalid=="user" ? ClaimTypes.NameIdentifier : invalid=="session" ? SessionClaims.SessionId : SessionClaims.Stamp;
        if(invalid=="duplicate") identity.AddClaim(new(SessionClaims.SessionId,Guid.NewGuid().ToString()));
        else if(invalid=="scheme") principal=new(new ClaimsIdentity(identity.Claims,"untrusted"));
        else if(invalid=="identities") principal.AddIdentity(new ClaimsIdentity());
        else { identity.RemoveClaim(identity.FindFirst(type)!); if(invalid!="stamp") identity.AddClaim(new(type,"invalid")); }
        Assert.Null(SessionClaims.Read(principal));
    }
    [Fact]
    public void Ticket_contains_references_only_and_stamp_is_not_in_diagnostic_representation()
    {
        var reference=Reference(); var ticket=SessionClaims.Create(reference);
        Assert.Equal(3,ticket.Claims.Count()); Assert.DoesNotContain(ticket.Claims,c=>c.Type==ClaimTypes.Role);
        Assert.Equal(reference.SessionId,SessionClaims.Read(ticket)!.SessionId); Assert.DoesNotContain(reference.SecurityStamp,reference.ToString());
    }
    [Fact]
    public void Authenticated_display_name_is_server_minted_and_does_not_grant_authority()
    {
        var reference=new SessionReference(42,Guid.NewGuid(),Guid.NewGuid().ToString(),"sample.operator");
        var ticket=SessionClaims.Create(reference);
        Assert.Equal("sample.operator",ticket.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal("sample.operator",SessionClaims.Read(ticket)?.DisplayName);
        Assert.DoesNotContain(ticket.Claims,c=>c.Type==ClaimTypes.Role);
    }
    [Theory]
    [InlineData(1,"organization","Organizations.Read",true)] [InlineData(1,"platform","Organizations.Create",false)]
    [InlineData(3,"platform","Organizations.Create",true)] [InlineData(3,"organization","Organizations.Read",false)]
    [InlineData(2,"site","Sites.Update",true)] [InlineData(2,"organization","Sites.Create",false)]
    [InlineData(2,"site","Organizations.Update",false)] [InlineData(1,"site","Sites.Read",true)]
    [InlineData(1,"organization","unknown",false)] [InlineData(99,"platform","Organizations.Create",false)]
    public void Fixed_roles_do_not_expand_authority(byte role,string kind,string permission,bool expected)
    {
        OrganizationScope scope=kind=="platform" ? new OrganizationScope.Platform() : kind=="organization" ? new OrganizationScope.Organization(1) : new OrganizationScope.Site(1,2);
        Assert.Equal(expected,FixedRolePolicy.Allows(role,scope,permission));
    }
    [Fact]
    public async Task Missing_database_configuration_fails_closed_and_never_creates_a_fallback_actor()
    {
        using var provider=new ServiceCollection().AddLogging().AddAuthenticationBackend(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var sessions=provider.GetRequiredService<RuntimeSessions>(); var reference=Reference();
        Assert.Equal("Auth.ServiceUnavailable",(await sessions.LoginAsync("synthetic",AuthBootstrapTests.Password())).Code);
        Assert.Equal("Auth.ServiceUnavailable",await sessions.ValidateAsync(reference));
        Assert.Equal("Auth.LogoutRevocationIncomplete",await sessions.LogoutAsync(reference));
        var source=new Source(reference);
        Assert.Null(await new SessionActorAccessor(source,sessions).ResolveAsync(default));
        Assert.False((await new SessionPermissionEvaluator(source,sessions).EvaluateAsync(new(42,ActorProvenance.ControlledBootstrap,reference.SessionId,Guid.NewGuid()),new OrganizationScope.Platform(),OrganizationPermissions.OrganizationsCreate,default)).IsAllowed);
    }
    private sealed class Source(SessionReference reference) : ICurrentAuthenticatedSession
    { public ValueTask<SessionReference?> GetAsync(CancellationToken ct)=>ValueTask.FromResult<SessionReference?>(reference); }
    [Fact]
    public void Cookies_have_required_server_security_and_fixed_expiry()
    {
        using var provider=new ServiceCollection().AddLogging().AddAuthenticationBackend(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var cookie=provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        Assert.Equal("__Host-ForgeFlow.Auth",cookie.Cookie.Name); Assert.Null(cookie.Cookie.Domain); Assert.Equal("/",cookie.Cookie.Path);
        Assert.True(cookie.Cookie.HttpOnly); Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always,cookie.Cookie.SecurePolicy);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax,cookie.Cookie.SameSite); Assert.False(cookie.SlidingExpiration); Assert.Equal(TimeSpan.FromHours(8),cookie.ExpireTimeSpan);
    }
    [Theory] [InlineData("fa-IR")] [InlineData("en-US")]
    public void Every_public_authentication_error_has_a_localized_resource(string culture)
    {
        var previous=CultureInfo.CurrentUICulture;
        try {
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(culture);
            using var provider=new ServiceCollection().AddLogging().AddLocalization(o=>o.ResourcesPath="Resources").BuildServiceProvider();
            var text=provider.GetRequiredService<IStringLocalizer<AuthenticationResources>>();
            foreach(var code in new[]{"Auth.LoginRejected","Auth.SessionInvalid","Auth.AccessDenied","Auth.ServiceUnavailable","Auth.LogoutRevocationIncomplete","Auth.RequestRejected","Auth.LoginThrottled"})
            { Assert.False(text[code].ResourceNotFound); Assert.False(string.IsNullOrWhiteSpace(text[code])); }
        } finally { CultureInfo.CurrentUICulture=previous; }
    }
}
