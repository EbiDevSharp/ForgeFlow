using Bit.BlazorUI;
using ForgeFlow.Web.Components;
using ForgeFlow.Web.Localization;
using ForgeFlow.Web.Hosting;
using ForgeFlow.Web.Drafts;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using ForgeFlow.Web.Authentication;

var builder = WebApplication.CreateBuilder(args);
using var localRuntime = LocalRuntimeConfiguration.Configure(builder);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddBitBlazorUIServices();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(CulturePolicy.Configure);
builder.Services.AddScoped<CulturePolicy>();
builder.Services.AddScoped<ForgeFlow.Web.Organizations.ReadScopes>();
builder.Services.AddScoped<DisplayFormatter>();
builder.Services.AddScoped<DraftCoordinator>();
builder.Services.AddOrganizationPersistence(builder.Configuration);
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
app.UseRequestLocalization();
app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAuthentication();
app.Use(async (context, next) =>
{
    await CulturePolicy.ApplyProfileFallbackAsync(context);
    if (EntryRouting.RedirectAnonymousRoot(context)) return;
    await next(context);
});
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapPost("/culture", CultureEndpoint.SwitchAsync);
app.MapAuthenticationBackend();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await ForgeFlow.Infrastructure.Identity.AuthFailureDiagnostics.InspectStoreAsync(app.Services);
app.Run();

public partial class Program;

