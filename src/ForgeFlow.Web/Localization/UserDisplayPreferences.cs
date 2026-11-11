using ForgeFlow.Application.Users;

namespace ForgeFlow.Web.Localization;

public sealed class UserDisplayPreferences
{
    public event Action? Changed;

    public string? Language { get; private set; }
    public string Theme { get; private set; } = UserPreferenceCatalog.LightTheme;
    public string? CalendarSystem { get; private set; }
    public string TimeZoneId { get; private set; } = UserPreferenceCatalog.DefaultTimeZoneId;
    public TimeZoneInfo TimeZoneInfo { get; private set; } = UserPreferenceCatalog.ResolveTimeZoneOrDefault(UserPreferenceCatalog.DefaultTimeZoneId);
    public bool IsLoadedFromDatabase { get; private set; }
    public int AvatarVersion { get; private set; }
    public string? ProfileDisplayName { get; private set; }
    public bool? ProfileHasAvatar { get; private set; }
    public int MenuRefreshVersion { get; private set; }

    public void NotifyProfileUpdated(string? displayName, bool hasAvatar)
    {
        ProfileDisplayName = displayName;
        ProfileHasAvatar = hasAvatar;
        AvatarVersion++;
        MenuRefreshVersion++;
        Changed?.Invoke();
    }

    public void NotifyMenuContextChanged()
    {
        MenuRefreshVersion++;
        Changed?.Invoke();
    }

    public void Apply(UserPreferencesSnapshot snapshot, bool fromDatabase = true)
    {
        var language = UserPreferenceCatalog.NormalizeLanguage(snapshot.Language) ?? UserPreferenceCatalog.PersianLanguage;
        var theme = UserPreferenceCatalog.NormalizeTheme(snapshot.Theme) ?? UserPreferenceCatalog.LightTheme;
        var calendar = UserPreferenceCatalog.NormalizeCalendar(snapshot.CalendarSystem) ?? UserPreferenceCatalog.DefaultCalendarForLanguage(language);
        var timeZoneId = UserPreferenceCatalog.NormalizeTimeZoneId(snapshot.TimeZoneId) ?? UserPreferenceCatalog.DefaultTimeZoneId;
        var resolvedZone = UserPreferenceCatalog.ResolveTimeZoneOrDefault(timeZoneId);

        var updated = !string.Equals(Language, language, StringComparison.Ordinal)
                      || !string.Equals(Theme, theme, StringComparison.Ordinal)
                      || !string.Equals(CalendarSystem, calendar, StringComparison.Ordinal)
                      || !string.Equals(TimeZoneId, timeZoneId, StringComparison.Ordinal)
                      || (fromDatabase && !IsLoadedFromDatabase);

        Language = language;
        Theme = theme;
        CalendarSystem = calendar;
        TimeZoneId = timeZoneId;
        TimeZoneInfo = resolvedZone;
        if (fromDatabase)
            IsLoadedFromDatabase = true;

        if (updated)
            Changed?.Invoke();
    }

    public string EffectiveCalendar(string? currentCultureLanguage)
    {
        if (CalendarSystem is UserPreferenceCatalog.PersianCalendar or UserPreferenceCatalog.GregorianCalendar)
            return CalendarSystem;
        return UserPreferenceCatalog.DefaultCalendarForLanguage(currentCultureLanguage);
    }
}
