using System.Net.Mail;

namespace ForgeFlow.Application.Users;

public static class UserPreferenceCatalog
{
    public const string LightTheme = "light";
    public const string DarkTheme = "dark";
    public const string SystemTheme = "system";

    public const string PersianCalendar = "persian";
    public const string GregorianCalendar = "gregorian";

    public const string PersianLanguage = "fa-IR";
    public const string EnglishLanguage = "en-US";

    public const string DefaultTimeZoneId = "Asia/Tehran";
    public const int MaxAvatarBytes = 262_144;

    public static IReadOnlyList<string> SupportedThemes { get; } = [LightTheme, DarkTheme, SystemTheme];
    public static IReadOnlyList<string> SupportedCalendars { get; } = [PersianCalendar, GregorianCalendar];
    public static IReadOnlyList<string> SupportedLanguages { get; } = [PersianLanguage, EnglishLanguage];

    private static readonly string[] CuratedTimeZoneIds =
    [
        "Asia/Tehran",
        "UTC",
        "Asia/Dubai",
        "Asia/Istanbul",
        "Asia/Riyadh",
        "Europe/London",
        "Europe/Berlin",
        "Europe/Paris",
        "America/New_York",
        "America/Los_Angeles",
        "Asia/Tokyo",
        "Asia/Shanghai",
        "Australia/Sydney"
    ];

    public static IReadOnlyList<TimeZoneOption> CuratedTimeZones { get; } = BuildCuratedTimeZones();

    public static string? NormalizeTheme(string? raw)
        => SupportedThemes.FirstOrDefault(v => string.Equals(v, raw?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? NormalizeCalendar(string? raw)
        => SupportedCalendars.FirstOrDefault(v => string.Equals(v, raw?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? NormalizeLanguage(string? raw)
        => SupportedLanguages.FirstOrDefault(v => string.Equals(v, raw?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<int> SupportedIdleTimeoutsMinutes { get; } = [15, 30, 60];

    public static bool IsValidIdleTimeout(int? minutes)
        => minutes is null || minutes is 15 or 30 or 60;

    public static string DefaultCalendarForLanguage(string? language)
        => string.Equals(NormalizeLanguage(language), EnglishLanguage, StringComparison.Ordinal)
            ? GregorianCalendar
            : PersianCalendar;

    public static string? NormalizeTimeZoneId(string? raw)
    {
        var candidate = raw?.Trim();
        if (string.IsNullOrEmpty(candidate) || candidate.Length > 64)
            return null;

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out var info))
            return null;

        var canonical = info.HasIanaId
            ? info.Id
            : TimeZoneInfo.TryConvertWindowsIdToIanaId(info.Id, out var iana) ? iana : info.Id;
        return canonical.Length <= 64 ? canonical : null;
    }

    public static TimeZoneInfo ResolveTimeZoneOrDefault(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out var info))
            return info;

        if (TimeZoneInfo.TryFindSystemTimeZoneById(DefaultTimeZoneId, out var tehran))
            return tehran;

        return TimeZoneInfo.Utc;
    }

    public static bool IsValidEmailOrEmpty(string? email, out string? normalizedTrimmed)
    {
        normalizedTrimmed = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (normalizedTrimmed is null)
            return true;
        if (normalizedTrimmed.Length > 256 || normalizedTrimmed.Any(char.IsControl))
            return false;
        try
        {
            var addr = new MailAddress(normalizedTrimmed);
            return string.Equals(addr.Address, normalizedTrimmed, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsValidPhoneOrEmpty(string? phone, out string? normalizedTrimmed)
    {
        normalizedTrimmed = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (normalizedTrimmed is null)
            return true;
        if (normalizedTrimmed.Length > 64)
            return false;
        return normalizedTrimmed.All(c => char.IsDigit(c) || c is '+' or '-' or ' ' or '(' or ')');
    }

    public static bool IsValidAvatar(byte[]? bytes, string? contentType)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxAvatarBytes)
            return false;

        return contentType switch
        {
            "image/png" => bytes.Length >= 8
                           && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                           && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/webp" => bytes.Length >= 12
                            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P',
            _ => false
        };
    }

    private static IReadOnlyList<TimeZoneOption> BuildCuratedTimeZones()
    {
        var list = new List<TimeZoneOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in CuratedTimeZoneIds)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var info))
                continue;
            var canonical = info.HasIanaId
                ? info.Id
                : TimeZoneInfo.TryConvertWindowsIdToIanaId(info.Id, out var iana) ? iana : info.Id;
            if (!seen.Add(canonical))
                continue;

            var offset = info.BaseUtcOffset;
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var abs = offset.Duration();
            var label = $"{canonical} (UTC{sign}{abs.Hours:00}:{abs.Minutes:00})";
            list.Add(new TimeZoneOption(canonical, label));
        }

        if (list.Count == 0)
            list.Add(new TimeZoneOption("UTC", "UTC (UTC+00:00)"));
        return list;
    }
}

public sealed record TimeZoneOption(string Id, string Label);

public sealed record UserPreferencesSnapshot(
    string Language,
    string Theme,
    string CalendarSystem,
    string TimeZoneId);

public sealed record UpdateUserPreferencesRequest(
    string Language,
    string Theme,
    string CalendarSystem,
    string TimeZoneId);

public sealed record UserPreferencesResult(
    string Code,
    UserPreferencesSnapshot? Preferences = null);

public sealed record UserProfileSnapshot(
    long UserId,
    string UserName,
    string? DisplayName,
    string? Email,
    string? PhoneNumber,
    bool HasAvatar);

public sealed record UpdateUserProfileRequest(
    string? DisplayName,
    string? Email,
    string? PhoneNumber,
    byte[]? AvatarBytes = null,
    string? AvatarContentType = null,
    bool RemoveAvatar = false);

public sealed record UserProfileResult(
    string Code,
    UserProfileSnapshot? Profile = null);

public sealed record AccessibleSiteOption(
    long OrganizationId,
    string OrganizationCode,
    string OrganizationName,
    long SiteId,
    string SiteCode,
    string SiteName,
    string? RoleName);

public sealed record UserMenuContextSnapshot(
    string UserName,
    string? DisplayName,
    bool HasAvatar,
    string? ActiveRoleName,
    string? ActiveSiteName,
    long? ActiveOrganizationId,
    long? ActiveSiteId,
    IReadOnlyList<AccessibleSiteOption> AccessibleSites,
    DateTime? PreviousSignInUtc,
    string? PreviousSignInIp,
    int FailedSignInsSincePrevious,
    int ActiveSessionCount,
    bool TwoFactorEnabled = false);

public sealed record UserPasskeySnapshot(
    long Id,
    string DisplayName,
    DateTime CreatedAtUtc,
    DateTime? LastUsedAtUtc);

public sealed record AccountSecuritySnapshot(
    DateTime? PasswordChangedAtUtc,
    bool TwoFactorEnabled,
    int RecoveryCodesRemaining,
    int? IdleTimeoutMinutes,
    int ActiveSessionCount,
    bool NewDeviceAlertEnabled,
    DateTime? TwoFactorReminderSnoozedUntilUtc,
    IReadOnlyList<UserPasskeySnapshot>? Passkeys = null);

public sealed record TotpSetupInfo(
    string SharedKey,
    string AuthenticatorUri);

public sealed record TotpConfirmResult(
    string Code,
    IReadOnlyList<string> RecoveryCodes);

public sealed record TwoFactorReminderStatus(
    bool TwoFactorEnabled,
    DateTime? SnoozedUntilUtc);

public sealed record ActiveSessionSnapshot(
    Guid SessionId,
    string? IpAddress,
    string DeviceSummary,
    bool IsMobileDevice,
    DateTime IssuedAtUtc,
    DateTime LastActivityAtUtc,
    bool IsCurrent);

public sealed record SessionOperationResult(
    string Code,
    int AffectedCount = 0);

public static class UserAgentDeviceParser
{
    public static (string DeviceSummary, bool IsMobile) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return ("Unknown", false);

        var ua = userAgent.Trim();
        var isMobile = ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
                       || ua.Contains("Android", StringComparison.OrdinalIgnoreCase)
                       || ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
                       || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase);

        var os = ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone"
            : ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad"
            : ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : ua.Contains("Windows NT 10.0", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : ua.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) ? "macOS"
            : ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : "Unknown";

        var browser = ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) || ua.Contains("EdgA/", StringComparison.OrdinalIgnoreCase) || ua.Contains("EdgiOS/", StringComparison.OrdinalIgnoreCase) ? "Edge"
            : ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) || ua.Contains("Opera", StringComparison.OrdinalIgnoreCase) ? "Opera"
            : ua.Contains("Vivaldi/", StringComparison.OrdinalIgnoreCase) ? "Vivaldi"
            : ua.Contains("Brave", StringComparison.OrdinalIgnoreCase) ? "Brave"
            : ua.Contains("SamsungBrowser/", StringComparison.OrdinalIgnoreCase) ? "Samsung Internet"
            : ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) || ua.Contains("FxiOS/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) || ua.Contains("CriOS/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : "Browser";

        return ($"{browser} · {os}", isMobile);
    }
}

public sealed record MyActivityQuery(
    int RangeDays = 30,
    string? EventCode = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 6);

public sealed record MyActivityEntrySummary(
    ForgeFlow.Application.Audit.AuditSourceKind Source,
    long Id,
    DateTime OccurredAtUtc,
    string EventCode,
    string? IpAddress,
    string? DeviceSummary,
    byte? Outcome,
    Guid CorrelationId,
    string RawJson);

public sealed record MyActivityEntryDetails(
    MyActivityEntrySummary Summary,
    IReadOnlyList<KeyValuePair<string, string>> ParsedFields);

public interface IMyActivityStore
{
    Task<ForgeFlow.Application.Organizations.OrganizationPage<MyActivityEntrySummary>> ListMyActivityAsync(
        long userId,
        MyActivityQuery query,
        CancellationToken ct = default);

    Task<MyActivityEntryDetails?> GetMyActivityEntryAsync(
        long userId,
        ForgeFlow.Application.Audit.AuditSourceKind source,
        long entryId,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListMyActivityEventCodesAsync(
        long userId,
        CancellationToken ct = default);
}

public static class NotificationEventCatalog
{
    public const string NewDeviceSignIn = "NewDeviceSignIn";
    public const string FailedSignInAttempt = "FailedSignInAttempt";
    public const string RoleOrAccessChanged = "RoleOrAccessChanged";
    public const string MyProductionOrders = "MyProductionOrders";

    public static IReadOnlyList<string> All { get; } =
    [
        NewDeviceSignIn,
        FailedSignInAttempt,
        RoleOrAccessChanged,
        MyProductionOrders
    ];

    public static string? Normalize(string? code)
        => All.FirstOrDefault(x => string.Equals(x, code?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public enum NotificationCategory : byte
{
    Security = 1,
    Orders = 2
}

public enum NotificationSeverity : byte
{
    Info = 1,
    Warning = 2,
    Danger = 3,
    Success = 4
}

public enum NotificationListFilter : byte
{
    All = 0,
    Unread = 1,
    Security = 2,
    Orders = 3
}

public static class NotificationTemplateCatalog
{
    public const string NewDeviceSignIn = "NewDeviceSignIn";
    public const string FailedSignInAttempts = "FailedSignInAttempts";
    public const string RoleOrAccessChanged = "RoleOrAccessChanged";
    public const string MyProductionOrderStatusChanged = "MyProductionOrderStatusChanged";
    public const string PasswordChangedAlert = "PasswordChangedAlert";
    public const string TwoFactorReminder = "TwoFactorReminder";
    public const string TwoFactorResetAlert = "TwoFactorResetAlert";

    public static IReadOnlyList<string> All { get; } =
    [
        NewDeviceSignIn,
        FailedSignInAttempts,
        RoleOrAccessChanged,
        MyProductionOrderStatusChanged,
        PasswordChangedAlert,
        TwoFactorReminder,
        TwoFactorResetAlert
    ];
}

public sealed record NotificationPreferenceSnapshot(
    string EventCode,
    bool InAppEnabled,
    bool EmailEnabled);

public sealed record UserNotificationSnapshot(
    long Id,
    byte Category,
    byte Severity,
    string TemplateCode,
    string ParamsJson,
    string? LinkUrl,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

public sealed record NotificationPageResult(
    IReadOnlyList<UserNotificationSnapshot> Items,
    int TotalCount,
    int UnreadCount,
    int Page,
    int PageSize);

public interface IUserNotificationPublisher
{
    Task PublishAsync(
        long userId,
        string preferenceEventCode,
        NotificationCategory category,
        NotificationSeverity severity,
        string templateCode,
        object? parameters,
        string? linkUrl,
        CancellationToken ct = default);
}

public interface IUserNotificationLiveChannel
{
    event Action<long>? UserNotificationsChanged;
    void NotifyUser(long userId);
}
