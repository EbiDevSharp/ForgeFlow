using System.Globalization;
using System.Reflection;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Hosting;
using ForgeFlow.Web.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class Phase1RemediationTests
{
    [Fact]
    public void ExportEndpoints_audit_route_requires_authorization()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter();
        using var app = builder.Build();

        app.MapExportEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var auditEndpoint = Assert.Single(endpoints, e => e.RoutePattern.RawText == "/api/export/audit");
        Assert.Contains(auditEndpoint.Metadata, m => m is IAuthorizeData);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    public void ExportEndpoints_localized_csv_headers_resolve_non_empty_in_both_cultures(string cultureName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        using var sp = services.BuildServiceProvider();
        var localizer = sp.GetRequiredService<IStringLocalizer<SharedResources>>();

        var prevCulture = CultureInfo.CurrentCulture;
        var prevUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            string[] keys =
            [
                "AuditTimeColumn",
                "AuditCategoryLabel",
                "AuditEventColumn",
                "AuditActorColumn",
                "AuditSubjectColumn",
                "AuditOutcomeColumn",
                "AuditColSite",
                "AuditColCorrelation"
            ];

            foreach (var key in keys)
            {
                var localized = localizer[key];
                Assert.False(localized.ResourceNotFound, $"Resource key '{key}' was not found for {cultureName}.");
                Assert.False(string.IsNullOrWhiteSpace(localized.Value), $"Resource key '{key}' was empty for {cultureName}.");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
            CultureInfo.CurrentUICulture = prevUiCulture;
        }
    }

    [Fact]
    public void RuntimeSessions_login_throttle_store_is_bounded_MemoryCache()
    {
        var field = typeof(RuntimeSessions).GetField("s_loginThrottles", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var cache = Assert.IsType<MemoryCache>(field!.GetValue(null));

        var recordMethod = typeof(RuntimeSessions).GetMethod("RecordLoginFailure", BindingFlags.Static | BindingFlags.NonPublic);
        var clearMethod = typeof(RuntimeSessions).GetMethod("ClearLoginFailure", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(recordMethod);
        Assert.NotNull(clearMethod);

        recordMethod!.Invoke(null, ["phase1-test-user", "127.0.0.1"]);
        Assert.True(cache.Count >= 1);
        clearMethod!.Invoke(null, ["phase1-test-user", "127.0.0.1"]);
    }
}
