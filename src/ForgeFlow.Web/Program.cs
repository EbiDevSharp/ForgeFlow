using Bit.BlazorUI;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Infrastructure.Diagnostics;
using ForgeFlow.Web.Components;
using ForgeFlow.Web.Diagnostics;
using ForgeFlow.Web.Localization;
using ForgeFlow.Web.Hosting;
using ForgeFlow.Web.Drafts;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using ForgeFlow.Infrastructure.Retention;
using ForgeFlow.Web.Authentication;
using Microsoft.AspNetCore.Components.Server.Circuits;

var builder = WebApplication.CreateBuilder(args);
using var localRuntime = LocalRuntimeConfiguration.Configure(builder);
builder.Services.AddRazorComponents().AddInteractiveServerComponents(options =>
{
    options.DisconnectedCircuitMaxRetained = 100;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.MaxBufferedUnacknowledgedRenderBatches = 10;
});
builder.Services.AddHealthChecks()
    .AddCheck<OrganizationDbContextHealthCheck>("OrganizationDbContext")
    .AddCheck<AuthDbContextHealthCheck>("AuthDbContext")
    .AddCheck<DiskSpaceHealthCheck>("DiskSpace");
builder.Services.AddBitBlazorUIServices();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(CulturePolicy.Configure);
builder.Services.AddScoped<UserDisplayPreferences>();
builder.Services.AddScoped<CulturePolicy>();
builder.Services.AddScoped<ForgeFlow.Web.Organizations.ReadScopes>();
builder.Services.AddScoped<DisplayFormatter>();
builder.Services.AddScoped<DraftCoordinator>();
builder.Services.AddOrganizationPersistence(builder.Configuration);
builder.Services.AddDiagnostics(builder.Configuration);
builder.Services.AddRetentionServices(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IOperationContextAccessor, HttpOperationContextAccessor>();
builder.Services.AddScoped<CircuitHandler, OperationCircuitHandler>();
builder.Services.AddScoped<UiErrorReporter>();
builder.Services.AddExceptionHandler<GlobalExceptionLoggingHandler>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;

    var section = builder.Configuration.GetSection("ForwardedHeaders");
    var knownProxies = section.GetSection("KnownProxies").Get<string[]>();
    if (knownProxies is { Length: > 0 })
    {
        options.KnownProxies.Clear();
        foreach (var proxy in knownProxies)
        {
            if (System.Net.IPAddress.TryParse(proxy, out var ip))
                options.KnownProxies.Add(ip);
        }
    }
    var knownNetworks = section.GetSection("KnownNetworks").Get<string[]>();
    if (knownNetworks is { Length: > 0 })
    {
        options.KnownIPNetworks.Clear();
        foreach (var net in knownNetworks)
        {
            if (System.Net.IPNetwork.TryParse(net, out var ipNet))
            {
                options.KnownIPNetworks.Add(ipNet);
            }
        }
    }
});
builder.Services.AddForgeFlowRateLimiting(builder.Configuration);
builder.Services.AddAuthenticationBackend(builder.Configuration);
builder.Services.AddUserManagement();
builder.Services.AddRoleManagement();
builder.Services.AddAuditManagement();
var authDiagnostics = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("ForgeFlow:Auth:Diagnostics");
builder.Services.AddSingleton(new ForgeFlow.Infrastructure.Identity.AuthDiagnosticsOptions
{
    Enabled = authDiagnostics
});
if (authDiagnostics)
{
    // Only sanitized auth diagnostics are emitted; don't duplicate raw EF exceptions/SQL or circuit URLs.
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
}

var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment(LocalRuntimeConfiguration.SmokeEnvironment))
    app.UseHsts();
app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.Use(async (context, next) =>
{
    var corr = Guid.TryParse(context.Request.Headers[HttpOperationContextAccessor.CorrelationHeader].FirstOrDefault(), out var headerGuid)
        ? headerGuid
        : Guid.NewGuid();
    context.Items[HttpOperationContextAccessor.CorrelationItemKey] = corr;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.TryAdd(HttpOperationContextAccessor.CorrelationHeader, corr.ToString("N"));
        return Task.CompletedTask;
    });
    await next(context);
});
app.UseRequestLocalization();
app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found");

// Serve static assets before authentication/session middleware so asset requests do not hit session validation (F-02)
app.MapStaticAssets();

app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (AuthenticationBackend.IsStaticAssetPath(context.Request.Path.Value))
    {
        await next(context);
        return;
    }

    var reference = SessionClaims.Read(context.User);
    if (reference is not null)
    {
        var sessions = context.RequestServices.GetService<RuntimeSessions>();
        var displayPrefs = context.RequestServices.GetService<UserDisplayPreferences>();
        if (sessions is not null && displayPrefs is not null)
        {
            var snapshot = await sessions.GetPreferencesAsync(reference, context.RequestAborted);
            if (snapshot is not null)
                displayPrefs.Apply(snapshot, fromDatabase: true);
        }
    }
    await CulturePolicy.ApplyProfileFallbackAsync(context);
    if (EntryRouting.RedirectAnonymousRoot(context)) return;
    await next(context);
});
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapPost("/culture", CultureEndpoint.SwitchAsync);
app.MapAuthenticationBackend();
app.MapExportEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await ForgeFlow.Infrastructure.Identity.AuthFailureDiagnostics.InspectStoreAsync(app.Services);
app.Run();

public partial class Program;

