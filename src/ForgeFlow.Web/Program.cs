using Bit.BlazorUI;
using ForgeFlow.Web.Components;
using ForgeFlow.Web.Localization;
using ForgeFlow.Web.Hosting;
using ForgeFlow.Web.Drafts;

var builder = WebApplication.CreateBuilder(args);
using var localRuntime = LocalRuntimeConfiguration.Configure(builder);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddBitBlazorUIServices();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(CulturePolicy.Configure);
builder.Services.AddScoped<CulturePolicy>();
builder.Services.AddScoped<DisplayFormatter>();
builder.Services.AddScoped<DraftCoordinator>();

var app = builder.Build();
app.UseRequestLocalization();
app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAntiforgery();
app.MapStaticAssets();
app.MapPost("/culture", CultureEndpoint.SwitchAsync);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program;
