using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ForgeFlow.Web.Authentication;

public sealed class AuthenticationResources;

public static class SessionClaims
{
    public const string SessionId = "ForgeFlow.Session";
    public const string Stamp = "AspNet.Identity.SecurityStamp";
    public static ClaimsPrincipal Create(SessionReference session)
    {
        var claims=new List<Claim>
        {
            new(ClaimTypes.NameIdentifier,session.UserId.ToString(CultureInfo.InvariantCulture)),
            new(SessionId,session.SessionId.ToString("D")), new(Stamp,session.SecurityStamp)
        };
        if (!string.IsNullOrWhiteSpace(session.DisplayName)) claims.Add(new(ClaimTypes.Name,session.DisplayName));
        return new(new ClaimsIdentity(claims,IdentityConstants.ApplicationScheme));
    }
    public static SessionReference? Read(ClaimsPrincipal principal)
    {
        var identities=principal.Identities.ToArray();
        if (identities.Length!=1 || !identities[0].IsAuthenticated || identities[0].AuthenticationType!=IdentityConstants.ApplicationScheme) return null;
        string? Single(string type) { var values=principal.FindAll(type).ToArray(); return values.Length==1 ? values[0].Value : null; }
        var stamp=Single(Stamp);
        return long.TryParse(Single(ClaimTypes.NameIdentifier),NumberStyles.None,CultureInfo.InvariantCulture,out var userId) && userId>0
            && Guid.TryParseExact(Single(SessionId),"D",out var sessionId) && sessionId!=Guid.Empty && !string.IsNullOrEmpty(stamp) && stamp.Length<=256
            ? new(userId,sessionId,stamp,Single(ClaimTypes.Name)) : null;
    }
}

public sealed class CircuitSessionSource(AuthenticationStateProvider authentication) : ICurrentAuthenticatedSession
{
    public async ValueTask<SessionReference?> GetAsync(CancellationToken ct)
    {
        try { return SessionClaims.Read((await authentication.GetAuthenticationStateAsync().WaitAsync(ct)).User); }
        catch (InvalidOperationException) { return null; }
    }
}
public sealed class SessionAuthenticationStateProvider(ILoggerFactory logging,RuntimeSessions sessions) : RevalidatingServerAuthenticationStateProvider(logging)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state,CancellationToken cancellationToken)
    {
        var reference=SessionClaims.Read(state.User);
        return reference is not null && await sessions.ValidateAsync(reference,cancellationToken)=="Auth.Valid";
    }
}

public static class AuthenticationBackend
{
    public const string UnavailableItem = "ForgeFlow.AuthUnavailable";
    public const string RejectedPrincipalItem = "ForgeFlow.RejectedCookiePrincipal";
    public static IServiceCollection AddAuthenticationBackend(this IServiceCollection services,IConfiguration configuration)
    {
        // Identity contexts live only in RuntimeSessions' fresh operation scopes, not circuit services.
        services.AddRuntimeIdentity(options => {
            var connection=configuration.GetConnectionString("ForgeFlow");
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Auth.ConnectionRequired");
            options.UseSqlServer(connection,sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory","auth"));
        });
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<RuntimeSessions>();
        services.AddScoped<AuthenticationStateProvider,SessionAuthenticationStateProvider>();
        services.AddCascadingAuthenticationState();
        services.AddScoped<ICurrentAuthenticatedSession,CircuitSessionSource>();
        services.AddScoped<ITrustedActorAccessor,SessionActorAccessor>();
        services.AddScoped<IOrganizationPermissionEvaluator,SessionPermissionEvaluator>();
        services.AddAuthorization();
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme,options => {
            options.Cookie.Name="__Host-ForgeFlow.Auth"; options.Cookie.Path="/"; options.Cookie.Domain=null;
            options.Cookie.HttpOnly=true; options.Cookie.SecurePolicy=CookieSecurePolicy.Always; options.Cookie.SameSite=SameSiteMode.Lax;
            options.ExpireTimeSpan=RuntimeSessions.Lifetime; options.SlidingExpiration=false;
            options.Events.OnValidatePrincipal=async context => {
                var reference=SessionClaims.Read(context.Principal!);
                var code=reference is null ? "Auth.SessionInvalid" : await context.HttpContext.RequestServices.GetRequiredService<RuntimeSessions>().ValidateAsync(reference,context.HttpContext.RequestAborted);
                if (code!="Auth.Valid") {
                    if (code=="Auth.ServiceUnavailable") context.HttpContext.Items[UnavailableItem]=true;
                    if (reference is not null) context.HttpContext.Items[RejectedPrincipalItem]=context.Principal;
                    context.RejectPrincipal(); await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                }
            };
            options.Events.OnRedirectToLogin=context=>WriteError(context.HttpContext,context.HttpContext.Items.ContainsKey(UnavailableItem) ? "Auth.ServiceUnavailable" : "Auth.SessionInvalid",context.HttpContext.Items.ContainsKey(UnavailableItem) ? 503 : 401);
            options.Events.OnRedirectToAccessDenied=context=>WriteError(context.HttpContext,"Auth.AccessDenied",403);
        });
        services.AddRateLimiter(options => {
            options.AddPolicy("auth-login",_=>RateLimitPartition.GetFixedWindowLimiter("auth-login",_=>new FixedWindowRateLimiterOptions { PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true }));
            options.OnRejected=(context,_)=>new ValueTask(WriteError(context.HttpContext,"Auth.LoginThrottled",429));
        });
        return services;
    }
    // Existing destination allowlist. UI routes may be added only when they exist and reauthorize access.
    public static string? LocalReturnUrl(string? raw) => string.IsNullOrEmpty(raw) || raw=="/" ? "/" : null;
    public static void MapAuthenticationBackend(this WebApplication app)
    {
        app.MapGet("/account/antiforgery",AntiforgeryToken);
        app.MapPost("/account/sign-in",Login).WithName("Auth.Login").RequireRateLimiting("auth-login");
        app.MapPost("/account/logout",Logout).WithName("Auth.Logout");
    }
    public static IResult AntiforgeryToken(HttpContext context,IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl="no-store";
        var tokens=antiforgery.GetAndStoreTokens(context);
        return Results.Json(new { requestToken=tokens.RequestToken });
    }
    private static IResult Error(HttpContext context,string code,int status)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ForgeFlow.AuthBoundary")
            .LogWarning("AUTH-HTTP outcome={Code} status={Status}", code, status);
        return Results.Json(new { code,message=context.RequestServices.GetRequiredService<IStringLocalizer<AuthenticationResources>>()[code].Value },statusCode:status);
    }
    private static Task WriteError(HttpContext context,string code,int status) => Error(context,code,status).ExecuteAsync(context);
    private static async Task<IFormCollection?> Form(HttpContext context,IAntiforgery antiforgery,bool logout=false)
    {
        if (!context.Request.IsHttps || !context.Request.HasFormContentType || context.Request.ContentLength>16384)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ForgeFlow.AuthBoundary")
                .LogWarning("AUTH-HTTP stage=transport-validation outcome=rejected");
            return null;
        }
        var size=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (size is { IsReadOnly:false }) size.MaxRequestBodySize=16384;
        var current=context.User;
        try {
            // A cryptographically verified cookie rejected by DB eligibility may still validate
            // its logout CSRF token. This principal is never restored for authorization.
            if (logout && context.Items[RejectedPrincipalItem] is ClaimsPrincipal rejected) context.User=rejected;
            await antiforgery.ValidateRequestAsync(context); return await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (Exception e) when (e is AntiforgeryValidationException or InvalidDataException or BadHttpRequestException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ForgeFlow.AuthBoundary")
                .LogWarning("AUTH-HTTP stage=form-validation exceptionType={ExceptionType}", e.GetType().Name);
            return null;
        }
        finally { context.User=current; }
    }
    public static async Task<IResult> Login(HttpContext context,IAntiforgery antiforgery,RuntimeSessions sessions)
    {
        context.Response.Headers.CacheControl="no-store";
        var form=await Form(context,antiforgery);
        if (form is null) return Error(context,"Auth.RequestRejected",400);
        if (form["username"].Count!=1 || form["password"].Count!=1 || form["returnUrl"].Count>1)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ForgeFlow.AuthBoundary")
                .LogWarning("AUTH-HTTP stage=field-binding outcome=rejected");
            return Error(context,"Auth.RequestRejected",400);
        }
        var target=LocalReturnUrl(form["returnUrl"].ToString()); if (target is null) return Error(context,"Auth.RequestRejected",400);
        // An identity change must cross an explicit logout boundary before another sign-in.
        if (context.User.Identity?.IsAuthenticated==true) return Error(context,"Auth.RequestRejected",400);
        var result=await sessions.LoginAsync(form["username"].ToString(),form["password"].ToString(),context.RequestAborted);
        if (result.Session is null) return Error(context,result.Code,result.Code=="Auth.ServiceUnavailable" ? 503 : 401);
        try {
            await context.SignInAsync(IdentityConstants.ApplicationScheme,SessionClaims.Create(result.Session),
                new AuthenticationProperties { IsPersistent=false,AllowRefresh=false,IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=result.Expires });
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ForgeFlow.AuthBoundary")
                .LogInformation("AUTH-HTTP stage=cookie-issued outcome=redirect-root");
            return Results.LocalRedirect(target);
        }
        catch (Exception exception) {
            if (context.RequestServices.GetService<AuthDiagnosticsOptions>()?.Enabled == true)
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ForgeFlow.AuthBoundary")
                    .LogError("AUTH-DIAGNOSTIC stage=cookie-issuance details={Details}", AuthFailureDiagnostics.Describe(exception, result.Session.SecurityStamp, form["password"].ToString()));
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ForgeFlow.AuthBoundary")
                .LogWarning("AUTH-HTTP stage=cookie-issuance outcome=failed");
            // SQL has committed; a failed cookie write must not claim success. Best-effort revoke.
            await sessions.LogoutAsync(result.Session,CancellationToken.None);
            if (!context.Response.HasStarted)
            {
                try { await context.SignOutAsync(IdentityConstants.ApplicationScheme); }
                catch (Exception) { /* The response below still reports unavailability, never success. */ }
            }
            return Error(context,"Auth.ServiceUnavailable",503);
        }
    }
    public static async Task<IResult> Logout(HttpContext context,IAntiforgery antiforgery,RuntimeSessions sessions)
    {
        context.Response.Headers.CacheControl="no-store";
        var form=await Form(context,antiforgery,logout:true); if (form is null || form["returnUrl"].Count>1) return Error(context,"Auth.RequestRejected",400);
        var target=LocalReturnUrl(form["returnUrl"].ToString()); if (target is null) return Error(context,"Auth.RequestRejected",400);
        var reference=SessionClaims.Read(context.User);
        string code;
        try { code=context.Items.ContainsKey(UnavailableItem) ? "Auth.LogoutRevocationIncomplete" : reference is null ? "Auth.SignedOut" : await sessions.LogoutAsync(reference,context.RequestAborted); }
        catch (OperationCanceledException) { code="Auth.LogoutRevocationIncomplete"; }
        finally { await context.SignOutAsync(IdentityConstants.ApplicationScheme); }
        return code=="Auth.SignedOut" ? Results.LocalRedirect(target) : Error(context,"Auth.LogoutRevocationIncomplete",503);
    }
}
