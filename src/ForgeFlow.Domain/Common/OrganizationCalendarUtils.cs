using System.Globalization;
using ForgeFlow.Domain.Organizations;

namespace ForgeFlow.Domain.Common;

/// <summary>
/// Domain-level calendar calculations and conversions for Persian (Solar Hijri) and Gregorian calendars,
/// including leap year validation, year boundary calculations, and time zone aware year extraction.
/// </summary>
public static class OrganizationCalendarUtils
{
    public static readonly PersianCalendar PersianCal = new();

    private static readonly Lazy<TimeZoneInfo> LazyDefaultTimeZone = new(() =>
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Tehran", out var tehran))
            return tehran;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Iran Standard Time", out var iran))
            return iran;
        return TimeZoneInfo.Utc;
    });

    /// <summary>Default organization time zone ("Asia/Tehran"), falling back to UTC if system cannot resolve it.</summary>
    public static TimeZoneInfo DefaultTimeZone => LazyDefaultTimeZone.Value;

    public static int GetPersianYear(DateTime dateTime) => PersianCal.GetYear(dateTime);
    public static int GetGregorianYear(DateTime dateTime) => dateTime.Year;

    public static bool IsPersianLeapYear(int year) => PersianCal.IsLeapYear(year);
    public static bool IsGregorianLeapYear(int year) => DateTime.IsLeapYear(year);

    /// <summary>
    /// Gets the effective calendar year for a UTC instant in an optional time zone according to the specified DocumentYearMode.
    /// </summary>
    public static int? GetYear(DateTime utcTime, DocumentYearMode mode, TimeZoneInfo? timeZone = null)
    {
        if (mode == DocumentYearMode.None)
            return null;

        var tz = timeZone ?? DefaultTimeZone;
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc), tz);

        return mode switch
        {
            DocumentYearMode.Gregorian => localTime.Year,
            DocumentYearMode.Persian => PersianCal.GetYear(localTime),
            _ => null
        };
    }

    /// <summary>
    /// Converts a Persian date (year, month, day) to Gregorian DateOnly.
    /// Throws ArgumentOutOfRangeException if the date is invalid.
    /// </summary>
    public static DateOnly PersianToDateOnly(int year, int month, int day)
    {
        var dt = PersianCal.ToDateTime(year, month, day, 0, 0, 0, 0);
        return DateOnly.FromDateTime(dt);
    }

    /// <summary>
    /// Safely attempts to convert a Persian date (year, month, day) to Gregorian DateOnly without throwing.
    /// </summary>
    public static bool TryPersianToDateOnly(int year, int month, int day, out DateOnly date)
    {
        date = default;
        try
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > 31)
                return false;

            if (month > 6 && day > 30)
                return false;

            if (month == 12 && day == 30 && !IsPersianLeapYear(year))
                return false;

            date = PersianToDateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decomposes a Gregorian DateOnly into its Persian (Year, Month, Day) components.
    /// </summary>
    public static (int Year, int Month, int Day) DateOnlyToPersian(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return (PersianCal.GetYear(dt), PersianCal.GetMonth(dt), PersianCal.GetDayOfMonth(dt));
    }

    /// <summary>
    /// Standardized Persian date formatting as "yyyy/MM/dd".
    /// </summary>
    public static string FormatPersianDate(DateOnly date, string separator = "/")
    {
        var (y, m, d) = DateOnlyToPersian(date);
        return string.Create(CultureInfo.InvariantCulture, $"{y:0000}{separator}{m:00}{separator}{d:00}");
    }

    /// <summary>
    /// Standardized Gregorian date formatting as "yyyy/MM/dd".
    /// </summary>
    public static string FormatGregorianDate(DateOnly date, string separator = "/")
    {
        return string.Create(CultureInfo.InvariantCulture, $"{date.Year:0000}{separator}{date.Month:00}{separator}{date.Day:00}");
    }

    /// <summary>
    /// Calculates the exact UTC bounds [StartUtc, EndUtcExclusive) for a given organization-local date.
    /// Handles fractional-hour offsets and Daylight Saving Time transitions cleanly.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) GetDayBoundsUtc(DateOnly localDate, TimeZoneInfo timeZone)
    {
        var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);

        var localNext = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localNext, timeZone);

        return (startUtc, endUtc);
    }

    /// <summary>
    /// Calculates the exact UTC bounds [StartUtc, EndUtcExclusive) for an entire Persian calendar year (Farvardin 1 to Esfand end).
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) GetPersianYearBoundsUtc(int persianYear, TimeZoneInfo timeZone)
    {
        var startLocal = PersianCal.ToDateTime(persianYear, 1, 1, 0, 0, 0, 0);
        var endLocal = PersianCal.ToDateTime(persianYear + 1, 1, 1, 0, 0, 0, 0);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), timeZone);

        return (startUtc, endUtc);
    }

    /// <summary>
    /// Calculates the exact UTC bounds [StartUtc, EndUtcExclusive) for an entire Gregorian calendar year (Jan 1 to Dec 31).
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) GetGregorianYearBoundsUtc(int gregorianYear, TimeZoneInfo timeZone)
    {
        var startLocal = new DateTime(gregorianYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var endLocal = new DateTime(gregorianYear + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone);

        return (startUtc, endUtc);
    }
}
