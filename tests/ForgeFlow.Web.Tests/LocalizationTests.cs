using System.Globalization;
using ForgeFlow.Web.Localization;
using ForgeFlow.Web.Components.Pages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("fa-IR", "rtl", "1405/01/01", "نام را وارد کنید.")]
    [InlineData("en-US", "ltr", "2026/03/21", "Enter a name.")]
    public void Localizer_direction_and_calendar_use_the_request_culture(string name, string direction, string date, string required)
    {
        using var scope = new CultureScope(name);
        using var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<SampleResources>>();
        Assert.False(text["Title"].ResourceNotFound);
        Assert.False(text["NameRequired"].ResourceNotFound);
        Assert.Equal(required, text["NameRequired"].Value);
        Assert.DoesNotContain("{0}", text["ValidationSucceeded", "Fixture name"].Value);
        var policy = new CulturePolicy();
        Assert.Equal(name, policy.Language);
        Assert.Equal(direction, policy.Direction);
        var pickerCulture = policy.PickerCulture;
        Assert.NotNull(pickerCulture);
        var formatter = new DisplayFormatter(policy);
        Assert.Equal(date, formatter.Date(new DateOnly(2026, 3, 21)));
        foreach (var number in new[] { 0m, -1234.5m, 12345.67m })
            Assert.Equal(number.ToString("N2", CultureInfo.GetCultureInfo(name)), formatter.Number(number));
    }

    [Fact]
    public void Request_policy_defaults_to_Persian_and_disables_query_overrides()
    {
        var options = new RequestLocalizationOptions();
        CulturePolicy.Configure(options);
        Assert.Equal("fa-IR", options.DefaultRequestCulture.Culture.Name);
        Assert.Equal(CulturePolicy.Supported, options.SupportedCultures!.Select(x => x.Name));
        Assert.Equal(CulturePolicy.Supported, options.SupportedUICultures!.Select(x => x.Name));
        Assert.Collection(options.RequestCultureProviders,
            provider => Assert.IsAssignableFrom<CookieRequestCultureProvider>(provider),
            provider => Assert.IsType<AcceptLanguageHeaderRequestCultureProvider>(provider));
    }

    [Theory]
    [InlineData("c=en-US|uic=fa-IR", "en-US")]
    [InlineData("c=fa-IR|uic=en-US", "fa-IR")]
    [InlineData("c=xx-XX|uic=en-US", null)]
    [InlineData("invalid", null)]
    public async Task Cookie_culture_is_supported_and_consistent_for_formatting_and_messages(string cookie, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $".AspNetCore.Culture={cookie}";
        var provider = new CulturePolicy.ConsistentCookieCultureProvider();
        var result = await provider.DetermineProviderCultureResult(context);
        if (expected is null)
            Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Equal(expected, Assert.Single(result.Cultures).Value);
            Assert.Equal(expected, Assert.Single(result.UICultures).Value);
        }
    }

    [Theory]
    [InlineData("fa-IR", 2025, 3, 20, "1403/12/30")]
    [InlineData("fa-IR", 2025, 3, 21, "1404/01/01")]
    [InlineData("en-US", 2025, 3, 20, "2025/03/20")]
    public void Date_formatter_covers_calendar_year_boundaries(string name, int year, int month, int day, string expected)
    {
        using var scope = new CultureScope(name);
        Assert.Equal(expected, new DisplayFormatter(new CulturePolicy()).Date(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void Form_validation_uses_localized_message_and_preserves_entered_data(string name)
    {
        using var scope = new CultureScope(name);
        using var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<SampleResources>>();
        var form = new SampleFormState { Name = "  " };
        form.Validate(text["NameRequired"]);
        Assert.False(form.Submitted);
        Assert.True(form.HasNameError);
        Assert.Equal(text["NameRequired"].Value, Assert.Single(form.EditContext.GetValidationMessages()));

        const string entered = "  نام Example <script>  ";
        form.Name = entered;
        form.Validate(text["NameRequired"]);
        Assert.True(form.Submitted);
        Assert.Empty(form.EditContext.GetValidationMessages());
        Assert.Equal(entered, form.Name);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUi = CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
