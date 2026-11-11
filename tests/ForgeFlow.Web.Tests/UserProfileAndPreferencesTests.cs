using System.Globalization;
using ForgeFlow.Application.Users;
using ForgeFlow.Web.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class UserProfileAndPreferencesTests
{
    [Fact]
    public void Preference_catalog_normalizes_valid_inputs_and_rejects_invalid_values()
    {
        Assert.Equal("dark", UserPreferenceCatalog.NormalizeTheme(" DARK "));
        Assert.Equal("system", UserPreferenceCatalog.NormalizeTheme("System"));
        Assert.Null(UserPreferenceCatalog.NormalizeTheme("midnight"));

        Assert.Equal("persian", UserPreferenceCatalog.NormalizeCalendar("Persian"));
        Assert.Equal("gregorian", UserPreferenceCatalog.NormalizeCalendar("gregorian"));
        Assert.Null(UserPreferenceCatalog.NormalizeCalendar("lunar"));

        Assert.Equal("Asia/Tehran", UserPreferenceCatalog.NormalizeTimeZoneId("Asia/Tehran"));
        Assert.Equal("UTC", UserPreferenceCatalog.NormalizeTimeZoneId("UTC"));
        Assert.Null(UserPreferenceCatalog.NormalizeTimeZoneId("Mars/Olympus"));
        Assert.Contains(UserPreferenceCatalog.CuratedTimeZones, tz => tz.Id == "Asia/Tehran");
    }

    [Fact]
    public void Profile_catalog_validates_email_phone_and_magic_bytes_avatar()
    {
        Assert.True(UserPreferenceCatalog.IsValidEmailOrEmpty(" ", out var emptyEmail));
        Assert.Null(emptyEmail);
        Assert.True(UserPreferenceCatalog.IsValidEmailOrEmpty("  ops@forgeflow.local ", out var parsedEmail));
        Assert.Equal("ops@forgeflow.local", parsedEmail);
        Assert.False(UserPreferenceCatalog.IsValidEmailOrEmpty("not-an-email", out _));

        Assert.True(UserPreferenceCatalog.IsValidPhoneOrEmpty("+98 21 8888-1234", out var phone));
        Assert.Equal("+98 21 8888-1234", phone);
        Assert.False(UserPreferenceCatalog.IsValidPhoneOrEmpty("abc<script>", out _));

        var validPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01 };
        Assert.True(UserPreferenceCatalog.IsValidAvatar(validPng, "image/png"));
        Assert.False(UserPreferenceCatalog.IsValidAvatar(validPng, "image/jpeg"));
        Assert.False(UserPreferenceCatalog.IsValidAvatar("<svg/>"u8.ToArray(), "image/svg+xml"));
        Assert.False(UserPreferenceCatalog.IsValidAvatar(new byte[UserPreferenceCatalog.MaxAvatarBytes + 1], "image/png"));
    }

    [Fact]
    public void Display_formatter_and_culture_policy_apply_independent_calendar_and_timezone()
    {
        var prev = CultureInfo.CurrentCulture;
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            var prefs = new UserDisplayPreferences();
            var policy = new CulturePolicy(prefs);
            var formatter = new DisplayFormatter(policy, displayPreferences: prefs);

            // Persian UI language + Gregorian calendar + UTC timezone
            var fa = CultureInfo.GetCultureInfo("fa-IR");
            CultureInfo.CurrentCulture = fa;
            CultureInfo.CurrentUICulture = fa;
            prefs.Apply(new UserPreferencesSnapshot("fa-IR", "dark", "gregorian", "UTC"));

            var utcInstant = new DateTime(2026, 11, 11, 10, 30, 0, DateTimeKind.Utc);
            Assert.Equal("2026/11/11 - 10:30:00", formatter.Timestamp(utcInstant, " - "));
            Assert.IsType<GregorianCalendar>(policy.PickerCulture.DateTimeFormat.Calendar);

            // English UI language + Persian calendar + Asia/Tehran (+03:30)
            var en = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = en;
            CultureInfo.CurrentUICulture = en;
            prefs.Apply(new UserPreferencesSnapshot("en-US", "system", "persian", "Asia/Tehran"));

            Assert.Equal("1405/08/20 - 14:00:00", formatter.Timestamp(utcInstant, " - "));
            Assert.IsType<PersianCalendar>(policy.PickerCulture.DateTimeFormat.Calendar);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36", "Chrome · Windows", false)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0", "Edge · Windows", false)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/130.0.0.0 Mobile Safari/537.36", "Chrome · Android", true)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1", "Safari · iPhone", true)]
    [InlineData(null, "Unknown", false)]
    public void User_agent_parser_extracts_clean_device_summary_and_mobile_flag(string? ua, string expectedSummary, bool expectedMobile)
    {
        var (summary, isMobile) = UserAgentDeviceParser.Parse(ua);
        Assert.Equal(expectedSummary, summary);
        Assert.Equal(expectedMobile, isMobile);
    }

    [Theory]
    [InlineData("fa-IR", "SessionIssued", "ورود موفق (صدور نشست)")]
    [InlineData("en-US", "SessionIssued", "Session Issued (Sign-In)")]
    public void Notification_text_helper_localizes_events_order_statuses_and_roles(string culture, string eventCode, string expectedEventLabel)
    {
        var prev = CultureInfo.CurrentCulture;
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            var info = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = info;
            CultureInfo.CurrentUICulture = info;

            var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources");
            using var provider = services.BuildServiceProvider();
            var localizer = provider.GetRequiredService<IStringLocalizer<SharedResources>>();

            Assert.Equal(expectedEventLabel, NotificationTextHelper.ResolveEventLabel(eventCode, localizer));
            Assert.NotEqual("Draft", culture == "fa-IR" ? NotificationTextHelper.ResolveOrderStatusLabel("Draft", localizer) : string.Empty);
            Assert.NotEqual("SiteOperator", NotificationTextHelper.ResolveRoleLabel("SiteOperator", localizer));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }
}
