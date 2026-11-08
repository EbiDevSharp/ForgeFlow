using System.Globalization;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class SampleFormattingRoundTripTests
{
    [Theory]
    [InlineData("fa-IR", "1404/01/01", "2025-03-21")]
    [InlineData("fa-IR", "۱۴۰۴/۰۱/۰۱", "2025-03-21")]
    [InlineData("en-US", "03/21/2025", "2025-03-21")]
    public void Date_input_round_trips_to_an_invariant_date_only(string inputCulture, string raw, string canonical)
    {
        var formatter = new DisplayFormatter(new CulturePolicy());
        Assert.True(formatter.TryDateOnly(raw, inputCulture, out var date));
        Assert.Equal(canonical, DisplayFormatter.CanonicalDate(date));
    }

    [Theory]
    [InlineData("fa-IR", "۱۴۰۴/۱۳/۰۱")]
    [InlineData("fa-IR", "۱۴۰۴/۰۱/۳۲")]
    [InlineData("fa-IR", "03/21/2025")]
    [InlineData("en-US", "02/29/2025")]
    [InlineData("en-US", "2025-03-21")]
    [InlineData("xx-XX", "03/21/2025")]
    public void Invalid_or_non_policy_dates_are_rejected_without_coercion(string inputCulture, string raw)
        => Assert.False(new DisplayFormatter(new CulturePolicy()).TryDateOnly(raw, inputCulture, out _));

    [Theory]
    [InlineData("fa-IR", "۱۲۳۴٫۵۰", "1234.5")]
    [InlineData("fa-IR", "-۱۲۳۴٫۵۰", "-1234.5")]
    [InlineData("en-US", "0", "0")]
    [InlineData("en-US", "-0.25", "-0.25")]
    public void Numeric_values_parse_to_the_same_decimal_magnitude(string inputCulture, string raw, string canonical)
    {
        var formatter = new DisplayFormatter(new CulturePolicy());
        Assert.True(formatter.TryNumber(raw, inputCulture, out var value));
        Assert.Equal(decimal.Parse(canonical, CultureInfo.InvariantCulture), value);
        Assert.Equal(canonical, DisplayFormatter.CanonicalAmount(value));
    }

    [Theory]
    [InlineData("fa-IR", "1,234.50")]
    [InlineData("fa-IR", "۱۲۳۴.۵۰")]
    [InlineData("fa-IR", "۱٬۲۳۴٫۵۰")]
    [InlineData("en-US", "1,234.50")]
    [InlineData("en-US", "1.234,50")]
    [InlineData("en-US", "12..50")]
    [InlineData("en-US", "1.234")]
    public void Grouped_foreign_and_excess_precision_numbers_are_not_reinterpreted(string inputCulture, string raw)
    {
        var formatter = new DisplayFormatter(new CulturePolicy());
        if (formatter.TryNumber(raw, inputCulture, out var value)) Assert.NotEqual(decimal.Round(value, 2), value);
        else Assert.False(formatter.TryNumber(raw, inputCulture, out _));
    }

    [Fact]
    public void Currency_and_enum_identifiers_are_exact_stable_codes_without_conversion()
    {
        var formatter = new DisplayFormatter(new CulturePolicy());
        Assert.True(formatter.TryCurrencyCode("EUR", out var code));
        Assert.Equal("EUR", code);
        Assert.False(formatter.TryCurrencyCode("eur", out _));
        Assert.False(formatter.TryCurrencyCode("USD/EUR", out _));
        Assert.True(DisplayFormatter.TrySampleMode("detailed", out var mode));
        Assert.Equal(SampleMode.Detailed, mode);
        Assert.Equal("detailed", DisplayFormatter.SampleModeValue(mode));
        Assert.False(DisplayFormatter.TrySampleMode("Detailed", out _));
        Assert.Equal(formatter.Number(12.5m) + " EUR", formatter.Money(12.5m, "EUR"));
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void Validation_keeps_invalid_raw_inputs_and_valid_submission_is_culture_independent(string uiCulture)
    {
        using var culture = new CultureScope(uiCulture);
        using var services = new ServiceCollection().AddLogging().AddLocalization(options => options.ResourcesPath = "Resources").BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<SampleResources>>();
        var formatter = new DisplayFormatter(new CulturePolicy());
        var form = new SampleFormState
        {
            Name = "Synthetic sample",
            DateRaw = "1404/13/01", DateCulture = "fa-IR",
            AmountRaw = "1,000.00", AmountCulture = "en-US",
            CurrencyCode = "EUR", ModeValue = "detailed"
        };
        form.Validate(text["NameRequired"], text["DateInvalid"], text["AmountInvalid"], text["CurrencyInvalid"], text["SampleModeInvalid"], formatter);
        Assert.False(form.Submitted);
        Assert.Equal("1404/13/01", form.DateRaw);
        Assert.Equal("1,000.00", form.AmountRaw);
        Assert.Contains(text["DateInvalid"].Value, form.EditContext.GetValidationMessages());
        Assert.Contains(text["AmountInvalid"].Value, form.EditContext.GetValidationMessages());

        form.DateRaw = uiCulture == "fa-IR" ? "۱۴۰۴/۰۱/۰۱" : "03/21/2025";
        form.DateCulture = uiCulture;
        form.AmountRaw = uiCulture == "fa-IR" ? "۱۲۳۴٫۵۰" : "1234.50";
        form.AmountCulture = uiCulture;
        form.Validate(text["NameRequired"], text["DateInvalid"], text["AmountInvalid"], text["CurrencyInvalid"], text["SampleModeInvalid"], formatter);
        Assert.True(form.Submitted);
        var canonical = form.CreateCanonicalValues();
        Assert.Equal(new DateOnly(2025, 3, 21), canonical.Date);
        Assert.Equal(1234.50m, canonical.Amount);
        Assert.Equal("EUR", canonical.CurrencyCode);
        Assert.Equal(SampleMode.Detailed, canonical.Mode);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous;
        private readonly CultureInfo previousUi;
        public CultureScope(string name)
        {
            previous = CultureInfo.CurrentCulture;
            previousUi = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }
        public void Dispose() { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
}
