using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ForgeFlow.Web.Authentication;
using ForgeFlow.Web.Localization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ForgeFlow.Web.Tests;

// Real loopback TLS/HTTP, cookies and antiforgery; no test endpoints are added to production.
public sealed class AuthHttpHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly X509Certificate2 certificate;
    public HttpClient Client { get; }
    private AuthHttpHost(WebApplication app,X509Certificate2 certificate,HttpClient client) { this.app=app; this.certificate=certificate; Client=client; }
    public static async Task<AuthHttpHost> Start(string? connection=null)
    {
        using var rsa=RSA.Create(2048);
        var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        var builder=WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0,l=>l.UseHttps(certificate)));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:ForgeFlow"]=connection });
        builder.Services.AddLocalization(o=>o.ResourcesPath="Resources"); builder.Services.AddAntiforgery();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthenticationBackend(builder.Configuration);
        var app=builder.Build(); app.UseRequestLocalization(CulturePolicy.Configure); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.UseAntiforgery();
        app.MapAuthenticationBackend();
        app.MapGet("/test/token",(HttpContext context,IAntiforgery antiforgery)=>new { token=antiforgery.GetAndStoreTokens(context).RequestToken });
        app.MapGet("/test/ticket",async(HttpContext context)=> {
            await context.SignInAsync(IdentityConstants.ApplicationScheme,SessionClaims.Create(AuthBackendTests.Reference()),new AuthenticationProperties { IsPersistent=false });
            return Results.NoContent();
        });
        app.MapGet("/test/protected",()=>Results.NoContent()).RequireAuthorization();
        try {
            await app.StartAsync();
            var handler=new HttpClientHandler { AllowAutoRedirect=false,ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert?.Thumbprint==certificate.Thumbprint };
            return new(app,certificate,new HttpClient(handler){BaseAddress=new Uri(app.Urls.Single())});
        } catch { await app.DisposeAsync(); certificate.Dispose(); throw; }
    }
    public async Task<string> Token() => (await Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/test/token")).GetProperty("token").GetString()!;
    public Task<HttpResponseMessage> Post(string path,string token,params (string Key,string Value)[] fields)
        => Client.PostAsync(path,new FormUrlEncodedContent(fields.Select(f=>new KeyValuePair<string,string>(f.Key,f.Value)).Append(new("__RequestVerificationToken",token))));
    public async ValueTask DisposeAsync(){Client.Dispose();await app.StopAsync();await app.DisposeAsync();certificate.Dispose();}
}

public sealed class AuthHttpTests
{
    [AuthHttpFact]
    public async Task Real_http_enforces_antiforgery_local_targets_cookie_security_and_outage_denial()
    {
        await using var host=await AuthHttpHost.Start();
        using var missing=await host.Client.PostAsync("/account/sign-in",new FormUrlEncodedContent(new Dictionary<string,string>{{"username","synthetic"},{"password",AuthBootstrapTests.Password()}}));
        Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
        var token=await host.Token();
        using var external=await host.Post("/account/sign-in",token,("username","synthetic"),("password",AuthBootstrapTests.Password()),("returnUrl","//evil.example"));
        Assert.Equal(HttpStatusCode.BadRequest,external.StatusCode); Assert.Null(external.Headers.Location);
        using var outage=await host.Post("/account/sign-in",token,("username","synthetic"),("password",AuthBootstrapTests.Password()),("returnUrl","/"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,outage.StatusCode);
        Assert.Contains("Auth.ServiceUnavailable",await outage.Content.ReadAsStringAsync());
        Assert.False(outage.Headers.TryGetValues("Set-Cookie",out _));
        using var ticket=await host.Client.GetAsync("/test/ticket");
        var cookie=Assert.Single(ticket.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-ForgeFlow.Auth=",cookie); Assert.Contains("secure",cookie); Assert.Contains("httponly",cookie); Assert.Contains("samesite=lax",cookie); Assert.DoesNotContain("expires=",cookie);
        using var protectedResponse=await host.Client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,protectedResponse.StatusCode);
        using var getLogout=await host.Client.GetAsync("/account/logout"); Assert.Equal(HttpStatusCode.MethodNotAllowed,getLogout.StatusCode);
        token=await host.Token(); using var logout=await host.Post("/account/logout",token,("returnUrl","/"));
        Assert.Equal(HttpStatusCode.Redirect,logout.StatusCode); Assert.Equal("/",logout.Headers.Location!.OriginalString);
    }
}

public sealed class AuthHttpFactAttribute : FactAttribute
{
    public AuthHttpFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("FORGEFLOW_AUTH_HTTP_TESTS")!="1")
            Skip="Deferred TLS integration; set FORGEFLOW_AUTH_HTTP_TESTS=1 under normal Windows identity. Agent Schannel could not acquire credentials.";
    }
}
