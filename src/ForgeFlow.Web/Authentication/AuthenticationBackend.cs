using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
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
public sealed class SessionAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly RuntimeSessions _sessions;
    private SessionReference? _currentReference;
    private readonly Guid _circuitId = Guid.NewGuid();
    private bool _subscribed;
    private readonly object _sync = new();

    public SessionAuthenticationStateProvider(ILoggerFactory logging, RuntimeSessions sessions) 
        : base(logging)
    {
        _sessions = sessions;
    }

    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var state = await base.GetAuthenticationStateAsync();
        AttachSession(state);
        return state;
    }

    public void AttachSession(AuthenticationState state)
    {
        var reference = SessionClaims.Read(state.User);
        lock (_sync)
        {
            if (reference is null)
            {
                DetachSession();
                return;
            }

            if (_currentReference?.SessionId != reference.SessionId)
            {
                DetachSession();
                _currentReference = reference;
                _sessions.RegisterCircuit(reference.SessionId, _circuitId);
                _sessions.SessionRevoked += OnSessionRevoked;
                _sessions.UserSessionsRevoked += OnUserSessionsRevoked;
                _subscribed = true;
            }
        }
    }

    public Task<bool> ValidateStateAsync(AuthenticationState state, CancellationToken cancellationToken)
        => ValidateAuthenticationStateAsync(state, cancellationToken);

    private void DetachSession()
    {
        lock (_sync)
        {
            if (_subscribed && _currentReference is not null)
            {
                _sessions.SessionRevoked -= OnSessionRevoked;
                _sessions.UserSessionsRevoked -= OnUserSessionsRevoked;
                _sessions.UnregisterCircuit(_currentReference.SessionId, _circuitId);
                _subscribed = false;
            }
            _currentReference = null;
        }
    }

    private void OnSessionRevoked(Guid sessionId, string reason)
    {
        bool shouldSignOut = false;
        lock (_sync)
        {
            if (_currentReference?.SessionId == sessionId)
            {
                shouldSignOut = true;
            }
        }
        if (shouldSignOut)
        {
            ForceSignOut();
        }
    }

    private void OnUserSessionsRevoked(long userId, string reason)
    {
        bool shouldSignOut = false;
        lock (_sync)
        {
            if (_currentReference?.UserId == userId)
            {
                shouldSignOut = true;
            }
        }
        if (shouldSignOut)
        {
            ForceSignOut();
        }
    }

    public void ForceSignOut()
    {
        DetachSession();
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
    }

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        var reference = SessionClaims.Read(state.User);
        if (reference is null)
        {
            DetachSession();
            return false;
        }

        AttachSession(state);
        var code = await _sessions.ValidateAsync(reference, cancellationToken);
        if (code != "Auth.Valid")
        {
            DetachSession();
            return false;
        }
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachSession();
        }
        base.Dispose(disposing);
    }
}

public static class AuthenticationBackend
{
    public const string UnavailableItem = "ForgeFlow.AuthUnavailable";
    public const string RejectedPrincipalItem = "ForgeFlow.RejectedCookiePrincipal";
    public const string DeviceCookieName = "__Host-ForgeFlow.Device";
    public const string TwoFactorPendingCookieName = "__Host-ForgeFlow.TwoFactor";
    public const string RememberTwoFactorCookieName = "__Host-ForgeFlow.Remember2FA";
    public const string PasskeyChallengeCookieName = "__Host-ForgeFlow.PasskeyChallenge";
    private const string TwoFactorPendingPurpose = "ForgeFlow.TwoFactorPending.v1";
    private const string RememberTwoFactorPurpose = "ForgeFlow.Remember2FA.v1";
    private const string PasskeyChallengePurpose = "ForgeFlow.PasskeyChallenge.v1";
    public static bool IsStaticAssetPath(string? path) =>
        path is not null && (
            path.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/fonts/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase));

    public static IServiceCollection AddAuthenticationBackend(this IServiceCollection services,IConfiguration configuration)
    {
        // Identity contexts live only in RuntimeSessions' fresh operation scopes, not circuit services.
        services.AddRuntimeIdentity(options => {
            var connection=configuration.GetConnectionString("ForgeFlow");
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Auth.ConnectionRequired");
            options.UseSqlServer(connection,sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory","auth"));
        });
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.Configure<RuntimeSessionOptions>(configuration.GetSection("ForgeFlow:Auth:Sessions"));
        services.AddSingleton<PasskeyChallengeStore>();
        services.Configure<WebAuthnOptions>(configuration.GetSection(WebAuthnOptions.SectionName));
        services.Configure<WebAuthnOptions>(configuration.GetSection("ForgeFlow:WebAuthn"));
        services.AddSingleton<RuntimeSessions>();
        services.AddSingleton<ForgeFlow.Application.Users.IUserNotificationPublisher>(sp => sp.GetRequiredService<RuntimeSessions>());
        services.AddSingleton<ForgeFlow.Application.Security.ISessionInvalidator>(sp => sp.GetRequiredService<RuntimeSessions>());
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
                if (IsStaticAssetPath(context.HttpContext.Request.Path.Value))
                    return;

                var reference=SessionClaims.Read(context.Principal!);
                var code=reference is null ? "Auth.SessionInvalid" : await context.HttpContext.RequestServices.GetRequiredService<RuntimeSessions>().ValidateAsync(reference,context.HttpContext.RequestAborted);
                if (code!="Auth.Valid") {
                    if (code=="Auth.ServiceUnavailable") context.HttpContext.Items[UnavailableItem]=true;
                    if (code=="Auth.SessionRevoked") context.HttpContext.Items["ForgeFlow.SessionRevoked"]=true;
                    if (reference is not null) context.HttpContext.Items[RejectedPrincipalItem]=context.Principal;
                    context.RejectPrincipal(); await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                }
            };
            options.Events.OnRedirectToLogin=context=>WriteError(context.HttpContext,
                context.HttpContext.Items.ContainsKey(UnavailableItem) ? "Auth.ServiceUnavailable" :
                context.HttpContext.Items.ContainsKey("ForgeFlow.SessionRevoked") ? "Auth.SessionRevoked" :
                "Auth.SessionInvalid",
                context.HttpContext.Items.ContainsKey(UnavailableItem) ? 503 : 401);
            options.Events.OnRedirectToAccessDenied=context=>WriteError(context.HttpContext,"Auth.AccessDenied",403);
        });
        return services;
    }
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
                context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
                context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
                context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
                var host = context.Request.Host.Value;
                var wsScheme = string.Equals(context.Request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
                var sameOriginWs = string.IsNullOrEmpty(host) ? string.Empty : $" {wsScheme}://{host}";
                var cspPolicy = $"default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'self'; form-action 'self'; img-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'{sameOriginWs};";
                if (!context.Response.Headers.TryGetValue("Content-Security-Policy", out var existingCsp) ||
                    string.Equals(existingCsp.ToString(), "frame-ancestors 'self'", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Headers["Content-Security-Policy"] = cspPolicy;
                }
                return Task.CompletedTask;
            });
            await next(context);
        });
    // Existing destination allowlist. UI routes may be added only when they exist and reauthorize access.
    public static string? LocalReturnUrl(string? raw) => string.IsNullOrEmpty(raw) || raw=="/" ? "/" : null;
    public static void MapAuthenticationBackend(this WebApplication app)
    {
        app.MapGet("/account/antiforgery",AntiforgeryToken);
        app.MapGet("/account/avatar", Avatar);
        app.MapPost("/account/sign-in",Login).WithName("Auth.Login").RequireRateLimiting(RateLimitingPolicyNames.Auth);
        app.MapPost("/account/sign-in-2fa",LoginTwoFactor).WithName("Auth.LoginTwoFactor").RequireRateLimiting(RateLimitingPolicyNames.Auth);
        app.MapPost("/account/passkey/register-options", PasskeyRegisterOptions).RequireRateLimiting(RateLimitingPolicyNames.Auth);
        app.MapPost("/account/passkey/register-verify", PasskeyRegisterVerify).RequireRateLimiting(RateLimitingPolicyNames.Auth);
        app.MapPost("/account/passkey/login-options", PasskeyLoginOptions).RequireRateLimiting(RateLimitingPolicyNames.Auth);
        app.MapPost("/account/passkey/login-verify", PasskeyLoginVerify).RequireRateLimiting(RateLimitingPolicyNames.Auth);
        app.MapPost("/account/logout",Logout).WithName("Auth.Logout");
        app.MapPost("/account/change-password-submit",ChangePasswordSubmit).WithName("Auth.ChangePassword").RequireRateLimiting(RateLimitingPolicyNames.Auth);
    }

    public static async Task<IResult> Avatar(HttpContext context, RuntimeSessions sessions)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var reference = SessionClaims.Read(context.User);
        if (reference is null)
            return Results.Unauthorized();

        var avatar = await sessions.GetAvatarAsync(reference, context.RequestAborted);
        return avatar is null
            ? Results.NotFound()
            : Results.File(avatar.Value.Data, avatar.Value.ContentType);
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
    public static string? ResolveClientIp(HttpContext context)
        => context.Connection.RemoteIpAddress is { } remote
            ? NormalizeIp(remote)
            : null;

    private static string NormalizeIp(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();

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
        var initialCulture = ForgeFlow.Web.Localization.CulturePolicy.Normalize(CultureInfo.CurrentUICulture.Name);
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var existingDeviceToken = context.Request.Cookies.TryGetValue(DeviceCookieName, out var rawDevice) && IsValidDeviceToken(rawDevice)
            ? rawDevice
            : null;
        var deviceToken = existingDeviceToken ?? Guid.NewGuid().ToString("N");
        var dp = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        var hasRememberCookie = context.Request.Cookies.TryGetValue(RememberTwoFactorCookieName, out var remRaw)
            && TryReadProtectedTwoFactorToken(dp, RememberTwoFactorPurpose, remRaw, out _, out _);

        var result = await sessions.LoginAsync(
            form["username"].ToString(),
            form["password"].ToString(),
            context.RequestAborted,
            ResolveClientIp(context),
            initialCulture,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            deviceToken,
            twoFactorDeviceTrusted: hasRememberCookie);

        if (result.Code == "Auth.TwoFactorRequired" && result.PendingUserId is { } pendingUserId && !string.IsNullOrEmpty(result.PendingSecurityStamp))
        {
            var pendingToken = CreateProtectedTwoFactorToken(dp, TwoFactorPendingPurpose, pendingUserId, result.PendingSecurityStamp, TimeSpan.FromMinutes(5));
            context.Response.Cookies.Append(TwoFactorPendingCookieName, pendingToken, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                MaxAge = TimeSpan.FromMinutes(5)
            });
            return Results.LocalRedirect("/account/login-2fa");
        }
        if (result.Session is null) return Error(context,result.Code,result.Code=="Auth.ServiceUnavailable" ? 503 : 401);
        try {
            if (existingDeviceToken is null && context.Request.IsHttps)
            {
                context.Response.Cookies.Append(DeviceCookieName, deviceToken, new CookieOptions
                {
                    Path = "/",
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(365)
                });
            }
            await context.SignInAsync(IdentityConstants.ApplicationScheme,SessionClaims.Create(result.Session),
                new AuthenticationProperties { IsPersistent=false,AllowRefresh=false,IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=result.Expires });
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ForgeFlow.AuthBoundary")
                .LogInformation("AUTH-HTTP stage=cookie-issued outcome=redirect-root");
            return Results.LocalRedirect(
                result.RequirePasswordChange ? "/account/change-password" :
                result.RequireTwoFactorSetup ? "/account/security?setup2fa=1" :
                target);
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
    private static bool IsValidDeviceToken(string? raw)
        => !string.IsNullOrWhiteSpace(raw) && raw.Length == 32 && raw.All(Uri.IsHexDigit);

    private static string CreateProtectedTwoFactorToken(
        Microsoft.AspNetCore.DataProtection.IDataProtectionProvider provider,
        string purpose,
        long userId,
        string securityStamp,
        TimeSpan lifetime)
    {
        var protector = provider.CreateProtector(purpose);
        var expiresTicks = DateTimeOffset.UtcNow.Add(lifetime).UtcTicks;
        return protector.Protect($"{userId.ToString(CultureInfo.InvariantCulture)}|{expiresTicks.ToString(CultureInfo.InvariantCulture)}|{securityStamp}");
    }

    private static bool TryReadProtectedTwoFactorToken(
        Microsoft.AspNetCore.DataProtection.IDataProtectionProvider provider,
        string purpose,
        string? protectedValue,
        out long userId,
        out string securityStamp)
    {
        userId = 0;
        securityStamp = string.Empty;
        if (string.IsNullOrWhiteSpace(protectedValue)) return false;
        try
        {
            var raw = provider.CreateProtector(purpose).Unprotect(protectedValue);
            var firstPipe = raw.IndexOf('|');
            if (firstPipe <= 0) return false;
            var secondPipe = raw.IndexOf('|', firstPipe + 1);
            if (secondPipe <= firstPipe + 1) return false;
            if (!long.TryParse(raw[..firstPipe], NumberStyles.None, CultureInfo.InvariantCulture, out userId) || userId <= 0) return false;
            if (!long.TryParse(raw[(firstPipe + 1)..secondPipe], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresTicks)) return false;
            if (DateTimeOffset.UtcNow.UtcTicks > expiresTicks) return false;
            securityStamp = raw[(secondPipe + 1)..];
            return !string.IsNullOrEmpty(securityStamp) && securityStamp.Length <= 256;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasValidPendingTwoFactor(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(TwoFactorPendingCookieName, out var raw)) return false;
        var dp = context.RequestServices.GetService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        return dp is not null && TryReadProtectedTwoFactorToken(dp, TwoFactorPendingPurpose, raw, out _, out _);
    }

    public static async Task<IResult> LoginTwoFactor(HttpContext context, IAntiforgery antiforgery, RuntimeSessions sessions)
    {
        context.Response.Headers.CacheControl = "no-store";
        var form = await Form(context, antiforgery);
        if (form is null) return Error(context, "Auth.RequestRejected", 400);
        if (context.User.Identity?.IsAuthenticated == true) return Error(context, "Auth.RequestRejected", 400);

        var dp = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        if (!context.Request.Cookies.TryGetValue(TwoFactorPendingCookieName, out var pendingRaw)
            || !TryReadProtectedTwoFactorToken(dp, TwoFactorPendingPurpose, pendingRaw, out var pendingUserId, out var pendingStamp))
        {
            context.Response.Cookies.Delete(TwoFactorPendingCookieName, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
            return Error(context, "Auth.TwoFactorPendingExpired", 401);
        }

        if (form["code"].Count != 1)
            return Error(context, "Auth.RequestRejected", 400);

        var code = form["code"].ToString();
        var isRecovery = string.Equals(form["isRecovery"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
        var rememberDevice = string.Equals(form["rememberDevice"].ToString(), "true", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(form["rememberDevice"].ToString(), "on", StringComparison.OrdinalIgnoreCase);

        var initialCulture = ForgeFlow.Web.Localization.CulturePolicy.Normalize(CultureInfo.CurrentUICulture.Name);
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var existingDeviceToken = context.Request.Cookies.TryGetValue(DeviceCookieName, out var rawDevice) && IsValidDeviceToken(rawDevice)
            ? rawDevice
            : null;
        var deviceToken = existingDeviceToken ?? Guid.NewGuid().ToString("N");

        var result = await sessions.CompleteTwoFactorLoginAsync(
            pendingUserId,
            pendingStamp,
            code,
            isRecovery,
            rememberDevice,
            context.RequestAborted,
            ResolveClientIp(context),
            initialCulture,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            deviceToken);

        if (result.Session is null)
        {
            if (result.Code == "Auth.TwoFactorPendingExpired")
                context.Response.Cookies.Delete(TwoFactorPendingCookieName, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
            return Error(context, result.Code, result.Code == "Auth.ServiceUnavailable" ? 503 : 401);
        }

        try
        {
            context.Response.Cookies.Delete(TwoFactorPendingCookieName, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
            if (existingDeviceToken is null && context.Request.IsHttps)
            {
                context.Response.Cookies.Append(DeviceCookieName, deviceToken, new CookieOptions
                {
                    Path = "/",
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(365)
                });
            }
            if (rememberDevice && context.Request.IsHttps)
            {
                var rememberToken = CreateProtectedTwoFactorToken(dp, RememberTwoFactorPurpose, result.Session.UserId, result.Session.SecurityStamp, TimeSpan.FromDays(30));
                context.Response.Cookies.Append(RememberTwoFactorCookieName, rememberToken, new CookieOptions
                {
                    Path = "/",
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(30)
                });
            }
            await context.SignInAsync(IdentityConstants.ApplicationScheme, SessionClaims.Create(result.Session),
                new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = result.Expires });
            return Results.LocalRedirect("/");
        }
        catch (Exception ex)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ForgeFlow.AuthBoundary")
                .LogWarning(ex, "Failed to refresh authentication cookie after password change; rolling back session.");
            await sessions.LogoutAsync(result.Session, CancellationToken.None);
            if (!context.Response.HasStarted)
            {
                try { await context.SignOutAsync(IdentityConstants.ApplicationScheme); }
                catch (Exception signOutEx)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ForgeFlow.AuthBoundary")
                        .LogDebug(signOutEx, "Failed to clear auth cookie during password change rollback.");
                }
            }
            return Error(context, "Auth.ServiceUnavailable", 503);
        }
    }

    public sealed record PasskeyRegisterOptionsRequest(string? CurrentPassword);
    private sealed record PasskeyRegisterVerifyRequest(string? DisplayName, string? ClientDataJson, string? AttestationObject);
    private sealed record PasskeyLoginVerifyRequest(string? CredentialId, string? ClientDataJson, string? AuthenticatorData, string? Signature);

    private static async Task<bool> ValidateJsonAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!context.Request.IsHttps || (context.Request.ContentLength is > 32768))
            return false;
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (Exception e) when (e is AntiforgeryValidationException or InvalidDataException or BadHttpRequestException)
        {
            return false;
        }
    }

    public static (string RpId, string ExpectedOrigin, string RpName) ResolveWebAuthnConfiguration(HttpContext context)
    {
        var options = context.RequestServices.GetService<Microsoft.Extensions.Options.IOptions<WebAuthnOptions>>()?.Value
            ?? new WebAuthnOptions();

        var rpName = string.IsNullOrWhiteSpace(options.RpName) ? "ForgeFlow" : options.RpName;
        var rpId = !string.IsNullOrWhiteSpace(options.RpId) ? options.RpId : context.Request.Host.Host;
        var expectedOrigin = !string.IsNullOrWhiteSpace(options.Origin)
            ? options.Origin
            : $"{context.Request.Scheme}://{context.Request.Host}";

        return (rpId, expectedOrigin, rpName);
    }

    private static string IssuePasskeyChallenge(HttpContext context, long userId, PasskeyChallengeStore challengeStore, bool stepUpVerified = false)
    {
        var dp = context.RequestServices.GetRequiredService<IDataProtectionProvider>();
        var challengeBytes = RandomNumberGenerator.GetBytes(32);
        var challengeB64Url = WebAuthnVerifier.ToBase64Url(challengeBytes);
        var challengeKey = Guid.NewGuid().ToString("N");
        var expiresTicks = DateTimeOffset.UtcNow.AddMinutes(2).UtcTicks;

        challengeStore.StoreChallenge(challengeKey, userId, challengeB64Url, TimeSpan.FromMinutes(2), stepUpVerified);

        var token = dp.CreateProtector(PasskeyChallengePurpose)
            .Protect($"{userId.ToString(CultureInfo.InvariantCulture)}|{expiresTicks.ToString(CultureInfo.InvariantCulture)}|{challengeKey}");

        context.Response.Cookies.Append(PasskeyChallengeCookieName, token, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = TimeSpan.FromMinutes(2)
        });
        return challengeB64Url;
    }

    private static bool TryConsumePasskeyChallenge(HttpContext context, long expectedUserId, PasskeyChallengeStore challengeStore, out string challengeB64Url, out bool stepUpVerified)
    {
        challengeB64Url = string.Empty;
        stepUpVerified = false;
        if (!context.Request.Cookies.TryGetValue(PasskeyChallengeCookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        context.Response.Cookies.Delete(PasskeyChallengeCookieName, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
        try
        {
            var dp = context.RequestServices.GetRequiredService<IDataProtectionProvider>();
            var plain = dp.CreateProtector(PasskeyChallengePurpose).Unprotect(raw);
            var first = plain.IndexOf('|');
            if (first <= 0) return false;
            var second = plain.IndexOf('|', first + 1);
            if (second <= first + 1) return false;
            if (!long.TryParse(plain[..first], NumberStyles.None, CultureInfo.InvariantCulture, out var uid) || uid != expectedUserId) return false;
            if (!long.TryParse(plain[(first + 1)..second], NumberStyles.None, CultureInfo.InvariantCulture, out var exp) || DateTimeOffset.UtcNow.UtcTicks > exp) return false;
            var challengeKey = plain[(second + 1)..];
            if (string.IsNullOrWhiteSpace(challengeKey)) return false;

            return challengeStore.TryConsume(challengeKey, expectedUserId, out challengeB64Url, out stepUpVerified);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<IResult> PasskeyRegisterOptions(
        HttpContext context,
        IAntiforgery antiforgery,
        RuntimeSessions sessions,
        UserManager<ApplicationAccount> userManager,
        PasskeyChallengeStore challengeStore)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await ValidateJsonAntiforgeryAsync(context, antiforgery))
            return Error(context, "Auth.RequestRejected", 400);

        var reference = SessionClaims.Read(context.User);
        if (reference is null)
            return Error(context, "Auth.SessionInvalid", 401);

        PasskeyRegisterOptionsRequest? payload = null;
        try
        {
            payload = await context.Request.ReadFromJsonAsync<PasskeyRegisterOptionsRequest>(context.RequestAborted);
        }
        catch
        {
            return Error(context, "Auth.RequestRejected", 400);
        }

        if (string.IsNullOrWhiteSpace(payload?.CurrentPassword))
            return Error(context, "Auth.CurrentPasswordInvalid", 400);

        var user = await userManager.FindByIdAsync(reference.UserId.ToString(CultureInfo.InvariantCulture));
        if (user is null || !user.IsActive || !await userManager.CheckPasswordAsync(user, payload.CurrentPassword))
            return Error(context, "Auth.CurrentPasswordInvalid", 400);

        var existingIds = await sessions.GetActivePasskeyCredentialIdsAsync(reference, context.RequestAborted);
        var challenge = IssuePasskeyChallenge(context, reference.UserId, challengeStore, stepUpVerified: true);

        var dp = context.RequestServices.GetRequiredService<IDataProtectionProvider>();
        var userHandleProtector = dp.CreateProtector("ForgeFlow.WebAuthn.UserHandle.v1");
        var userHandleBytes = SHA256.HashData(userHandleProtector.Protect(BitConverter.GetBytes(reference.UserId)));
        var userHandleB64Url = WebAuthnVerifier.ToBase64Url(userHandleBytes);

        var displayName = string.IsNullOrWhiteSpace(reference.DisplayName) ? reference.UserId.ToString(CultureInfo.InvariantCulture) : reference.DisplayName!;
        var (rpId, _, rpName) = ResolveWebAuthnConfiguration(context);

        return Results.Json(new
        {
            challenge,
            rp = new { id = rpId, name = rpName },
            user = new { id = userHandleB64Url, name = displayName, displayName },
            pubKeyCredParams = new object[]
            {
                new { type = "public-key", alg = WebAuthnVerifier.AlgEs256 },
                new { type = "public-key", alg = WebAuthnVerifier.AlgRs256 }
            },
            timeout = 60000,
            attestation = "none",
            excludeCredentials = existingIds.Select(id => new { type = "public-key", id = WebAuthnVerifier.ToBase64Url(id) }),
            authenticatorSelection = new { residentKey = "preferred", userVerification = "required" }
        });
    }

    public static async Task<IResult> PasskeyRegisterVerify(
        HttpContext context,
        IAntiforgery antiforgery,
        RuntimeSessions sessions,
        PasskeyChallengeStore challengeStore)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await ValidateJsonAntiforgeryAsync(context, antiforgery))
            return Error(context, "Auth.RequestRejected", 400);

        var reference = SessionClaims.Read(context.User);
        if (reference is null)
            return Error(context, "Auth.SessionInvalid", 401);

        if (!TryConsumePasskeyChallenge(context, reference.UserId, challengeStore, out var expectedChallenge, out var stepUpVerified) || !stepUpVerified)
            return Error(context, "Auth.RequestRejected", 400);

        PasskeyRegisterVerifyRequest? payload;
        try { payload = await context.Request.ReadFromJsonAsync<PasskeyRegisterVerifyRequest>(context.RequestAborted); }
        catch (System.Text.Json.JsonException) { return Error(context, "Auth.RequestRejected", 400); }
        catch (BadHttpRequestException) { return Error(context, "Auth.RequestRejected", 400); }
        if (payload is null || string.IsNullOrWhiteSpace(payload.ClientDataJson) || string.IsNullOrWhiteSpace(payload.AttestationObject))
            return Error(context, "Auth.RequestRejected", 400);

        byte[] clientDataBytes, attestationBytes;
        try
        {
            clientDataBytes = WebAuthnVerifier.FromBase64Url(payload.ClientDataJson);
            attestationBytes = WebAuthnVerifier.FromBase64Url(payload.AttestationObject);
        }
        catch (FormatException) { return Error(context, "Auth.PasskeyInvalid", 400); }

        var (expectedRpId, expectedOrigin, _) = ResolveWebAuthnConfiguration(context);
        var verified = WebAuthnVerifier.VerifyRegistration(clientDataBytes, attestationBytes, expectedChallenge, expectedOrigin, expectedRpId);
        if (verified is null)
            return Error(context, "Auth.PasskeyInvalid", 400);

        var code = await sessions.RegisterPasskeyAsync(reference, payload.DisplayName ?? "Passkey", verified, context.RequestAborted);
        if (code != "Auth.PasskeyRegistered")
            return Error(context, code, code == "Auth.ServiceUnavailable" ? 503 : code == "Auth.SessionInvalid" ? 401 : 400);

        var localizer = context.RequestServices.GetRequiredService<IStringLocalizer<AuthenticationResources>>();
        return Results.Json(new { code, message = localizer[code].Value });
    }

    public static async Task<IResult> PasskeyLoginOptions(
        HttpContext context,
        IAntiforgery antiforgery,
        PasskeyChallengeStore challengeStore)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await ValidateJsonAntiforgeryAsync(context, antiforgery))
            return Error(context, "Auth.RequestRejected", 400);

        var challenge = IssuePasskeyChallenge(context, 0, challengeStore, stepUpVerified: false);
        var (rpId, _, _) = ResolveWebAuthnConfiguration(context);
        return Results.Json(new
        {
            challenge,
            rpId = rpId,
            timeout = 60000,
            userVerification = "required"
        });
    }

    public static async Task<IResult> PasskeyLoginVerify(
        HttpContext context,
        IAntiforgery antiforgery,
        RuntimeSessions sessions,
        PasskeyChallengeStore challengeStore)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await ValidateJsonAntiforgeryAsync(context, antiforgery))
            return Error(context, "Auth.RequestRejected", 400);
        if (context.User.Identity?.IsAuthenticated == true)
            return Error(context, "Auth.RequestRejected", 400);

        if (!TryConsumePasskeyChallenge(context, 0, challengeStore, out var expectedChallenge, out _))
            return Error(context, "Auth.RequestRejected", 400);

        PasskeyLoginVerifyRequest? payload;
        try { payload = await context.Request.ReadFromJsonAsync<PasskeyLoginVerifyRequest>(context.RequestAborted); }
        catch (System.Text.Json.JsonException) { return Error(context, "Auth.RequestRejected", 400); }
        catch (BadHttpRequestException) { return Error(context, "Auth.RequestRejected", 400); }
        if (payload is null || string.IsNullOrWhiteSpace(payload.CredentialId) || string.IsNullOrWhiteSpace(payload.ClientDataJson)
            || string.IsNullOrWhiteSpace(payload.AuthenticatorData) || string.IsNullOrWhiteSpace(payload.Signature))
            return Error(context, "Auth.RequestRejected", 400);

        byte[] credIdBytes, clientDataBytes, authDataBytes, sigBytes;
        try
        {
            credIdBytes = WebAuthnVerifier.FromBase64Url(payload.CredentialId);
            clientDataBytes = WebAuthnVerifier.FromBase64Url(payload.ClientDataJson);
            authDataBytes = WebAuthnVerifier.FromBase64Url(payload.AuthenticatorData);
            sigBytes = WebAuthnVerifier.FromBase64Url(payload.Signature);
        }
        catch (FormatException) { return Error(context, "Auth.LoginRejected", 401); }

        var initialCulture = ForgeFlow.Web.Localization.CulturePolicy.Normalize(CultureInfo.CurrentUICulture.Name);
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var existingDeviceToken = context.Request.Cookies.TryGetValue(DeviceCookieName, out var rawDevice) && IsValidDeviceToken(rawDevice)
            ? rawDevice
            : null;
        var deviceToken = existingDeviceToken ?? Guid.NewGuid().ToString("N");

        var (expectedRpId, expectedOrigin, _) = ResolveWebAuthnConfiguration(context);

        var result = await sessions.LoginWithPasskeyAsync(
            credIdBytes,
            clientDataBytes,
            authDataBytes,
            sigBytes,
            expectedChallenge,
            expectedOrigin,
            expectedRpId,
            context.RequestAborted,
            ResolveClientIp(context),
            initialCulture,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            deviceToken);

        if (result.Session is null)
            return Error(context, result.Code, result.Code == "Auth.ServiceUnavailable" ? 503 : 401);

        try
        {
            if (existingDeviceToken is null && context.Request.IsHttps)
            {
                context.Response.Cookies.Append(DeviceCookieName, deviceToken, new CookieOptions
                {
                    Path = "/",
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(365)
                });
            }
            await context.SignInAsync(IdentityConstants.ApplicationScheme, SessionClaims.Create(result.Session),
                new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = result.Expires });
            return Results.Json(new { redirectUrl = "/" });
        }
        catch (Exception ex)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ForgeFlow.AuthBoundary")
                .LogWarning(ex, "Failed to refresh authentication cookie after password change; rolling back session.");
            await sessions.LogoutAsync(result.Session, CancellationToken.None);
            if (!context.Response.HasStarted)
            {
                try { await context.SignOutAsync(IdentityConstants.ApplicationScheme); }
                catch (Exception signOutEx)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ForgeFlow.AuthBoundary")
                        .LogDebug(signOutEx, "Failed to clear auth cookie during password change rollback.");
                }
            }
            return Error(context, "Auth.ServiceUnavailable", 503);
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

    public static async Task<IResult> ChangePasswordSubmit(HttpContext context, IAntiforgery antiforgery, RuntimeSessions sessions)
    {
        context.Response.Headers.CacheControl = "no-store";
        var form = await Form(context, antiforgery);
        if (form is null) return Error(context, "Auth.RequestRejected", 400);
        if (form["currentPassword"].Count != 1 || form["newPassword"].Count != 1 || form["confirmPassword"].Count != 1)
            return Error(context, "Auth.RequestRejected", 400);

        var reference = SessionClaims.Read(context.User);
        if (reference is null) return Error(context, "Auth.SessionInvalid", 401);

        var currentPassword = form["currentPassword"].ToString();
        var newPassword = form["newPassword"].ToString();
        var confirmPassword = form["confirmPassword"].ToString();
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            return Error(context, "Auth.PasswordMismatch", 400);

        var userAgent = context.Request.Headers.UserAgent.ToString();
        var result = await sessions.ChangeOwnPasswordAsync(reference, currentPassword, newPassword, context.RequestAborted, ResolveClientIp(context), string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
        if (result.Session is null)
        {
            if (result.Code == "Auth.SessionInvalid")
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                return Error(context, "Auth.SessionInvalid", 401);
            }
            return Error(context, result.Code, result.Code == "Auth.ServiceUnavailable" ? 503 : 400);
        }

        try
        {
            await context.SignInAsync(IdentityConstants.ApplicationScheme, SessionClaims.Create(result.Session),
                new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = result.Expires });
            var localizer = context.RequestServices.GetRequiredService<IStringLocalizer<AuthenticationResources>>();
            return Results.Json(new { code = result.Code, message = localizer[result.Code].Value });
        }
        catch (Exception ex)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ForgeFlow.AuthBoundary")
                .LogWarning(ex, "Failed to refresh authentication cookie after password change; rolling back session.");
            await sessions.LogoutAsync(result.Session, CancellationToken.None);
            if (!context.Response.HasStarted)
            {
                try { await context.SignOutAsync(IdentityConstants.ApplicationScheme); }
                catch (Exception signOutEx)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ForgeFlow.AuthBoundary")
                        .LogDebug(signOutEx, "Failed to clear auth cookie during password change rollback.");
                }
            }
            return Error(context, "Auth.ServiceUnavailable", 503);
        }
    }
}
