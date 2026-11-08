using System.Globalization;
using System.Text;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Drafts;
using ForgeFlow.Web.Localization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class SwitchAndDraftTests
{
    [Theory]
    [InlineData("fa-IR", "۱۲۳۴٫۵۰", "fa-IR", "1234.50")]
    [InlineData("en-US", "۱۲۳۴٫۵۰", "fa-IR", "1234.50")]
    [InlineData("fa-IR", "1234.50", "en-US", "1234.50")]
    [InlineData("en-US", "-12.25", "en-US", "-12.25")]
    public void Numeric_drafts_retain_source_culture_and_canonical_value(string displayCulture, string raw, string inputCulture, string canonical)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(displayCulture);
            var formatter = new DisplayFormatter(new CulturePolicy());
            Assert.True(formatter.TryNumber(raw, inputCulture, out var value));
            Assert.Equal(decimal.Parse(canonical, CultureInfo.InvariantCulture), value);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("12..")]
    [InlineData("1,000")]
    [InlineData("")]
    [InlineData("-")]
    public void Invalid_raw_numbers_are_not_silently_reinterpreted(string raw)
        => Assert.False(new DisplayFormatter(new CulturePolicy()).TryNumber(raw, "en-US", out _));

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void Restore_rebuilds_validation_and_modified_state_without_changing_raw_values(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var services = new ServiceCollection().AddLogging().AddLocalization(options => options.ResourcesPath = "Resources").BuildServiceProvider();
            var text = services.GetRequiredService<IStringLocalizer<SampleResources>>();
            var form = new SampleFormState();
            form.Restore(new DraftSnapshot
            {
                Fields = new()
                {
                    ["Name"] = new("", null), ["DateRaw"] = new("۱۴۰۴/۱۳/۰۱", "fa-IR"),
                    ["Notes"] = new("Synthetic notes\nساختگی", null), ["AmountRaw"] = new("12..", "en-US"),
                    ["CurrencyCode"] = new("USD", null), ["ModeValue"] = new("summary", null)
                },
                Dirty = ["Name", "DateRaw", "AmountRaw"], Touched = ["Name"], ValidationAttempted = true
            });
            Assert.True(form.ValidationAttempted);
            Assert.True(form.EditContext.IsModified());
            form.Validate(text["NameRequired"], text["AmountInvalid"], new DisplayFormatter(new CulturePolicy()));
            Assert.False(form.Submitted);
            Assert.Contains(text["NameRequired"].Value, form.EditContext.GetValidationMessages());
            Assert.Contains(text["AmountInvalid"].Value, form.EditContext.GetValidationMessages());
            Assert.Equal("12..", form.AmountRaw);
            Assert.Equal("۱۴۰۴/۱۳/۰۱", form.DateRaw);
            Assert.Equal("fa-IR", form.DateCulture);
            Assert.Equal("Synthetic notes\nساختگی", form.Notes);
            form.Reset();
            Assert.False(form.EditContext.IsModified());
            Assert.False(form.ValidationAttempted);
            Assert.Empty(form.EditContext.GetValidationMessages());
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("fa-IR", "/", true, 204)]
    [InlineData("en-US", "/", true, 204)]
    [InlineData("xx-XX", "/", true, 400)]
    [InlineData("en-US", "https://example.invalid/", true, 400)]
    [InlineData("en-US", "//example.invalid/", true, 400)]
    [InlineData("en-US", "/", false, 400)]
    public async Task Culture_endpoint_requires_antiforgery_supported_culture_and_allowlisted_return(string culture, string returnUrl, bool validToken, int expected)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes($"culture={Uri.EscapeDataString(culture)}&returnUrl={Uri.EscapeDataString(returnUrl)}"));
        var result = await CultureEndpoint.SwitchAsync(context, new AntiforgeryFixture(validToken));
        Assert.Equal(expected, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        if (expected == 204)
        {
            var cookie = context.Response.Headers.SetCookie.ToString();
            Assert.Contains(".AspNetCore.Culture=", cookie);
            Assert.Contains(culture, cookie);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("expires=", cookie);
        }
        else Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    private sealed class AntiforgeryFixture(bool valid) : IAntiforgery
    {
        public Task ValidateRequestAsync(HttpContext context) => valid ? Task.CompletedTask : Task.FromException(new AntiforgeryValidationException("fixture"));
        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(valid);
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => throw new NotSupportedException();
        public AntiforgeryTokenSet GetTokens(HttpContext context) => throw new NotSupportedException();
        public void SetCookieTokenAndHeader(HttpContext context) => throw new NotSupportedException();
    }
}
