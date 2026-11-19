using System.Globalization;
using ForgeFlow.Web.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class Phase4UiHygieneTests
{
    [Fact]
    public void DatePickerHelper_guards_default_DateOnly_for_PersianCalendar_and_uses_Utc_zero_offset()
    {
        Assert.Null(DatePickerHelper.ToDateTimeOffset(default(DateOnly)));
        Assert.Null(DatePickerHelper.ToDateTimeOffset((DateOnly?)null));
        Assert.Null(DatePickerHelper.FromDateTimeOffset(null));

        var sample = new DateOnly(2026, 11, 19);
        var dto = DatePickerHelper.ToDateTimeOffset(sample);
        Assert.NotNull(dto);
        Assert.Equal(TimeSpan.Zero, dto!.Value.Offset);
        Assert.Equal(sample, DatePickerHelper.FromDateTimeOffset(dto));

        // Verify PersianCalendar accepts the returned DateTimeOffset without throwing
        var persian = new PersianCalendar();
        Assert.Equal(1405, persian.GetYear(dto.Value.UtcDateTime));
    }

    [Theory]
    [InlineData("en-US", ", ")]
    [InlineData("fa-IR", "، ")]
    public void SharedResources_ListSeparator_resolves_per_culture(string cultureName, string expected)
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

            var value = localizer["ListSeparator"];
            Assert.False(value.ResourceNotFound);
            Assert.Equal(expected, value.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
            CultureInfo.CurrentUICulture = prevUiCulture;
        }
    }
}
