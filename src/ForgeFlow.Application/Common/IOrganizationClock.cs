namespace ForgeFlow.Application.Common;

/// <summary>
/// Centralized organization-scoped clock and calendar abstraction providing deterministic UTC timestamps,
/// organization-local conversions, and calendar boundary calculations.
/// </summary>
public interface IOrganizationClock
{
    /// <summary>Current UTC instant as DateTime (DateTimeKind.Utc).</summary>
    DateTime UtcNow { get; }

    /// <summary>Current UTC instant as DateTimeOffset.</summary>
    DateTimeOffset UtcNowOffset { get; }

    /// <summary>Resolves the configured TimeZoneInfo for an organization (or default Asia/Tehran).</summary>
    TimeZoneInfo GetTimeZone(long organizationId = 0);

    /// <summary>Converts a UTC instant to organization-local DateTime.</summary>
    DateTime ToLocal(DateTime utcTime, long organizationId = 0);

    /// <summary>Converts an organization-local DateTime to UTC DateTime.</summary>
    DateTime ToUtc(DateTime localTime, long organizationId = 0);

    /// <summary>Gets the current organization-local date (Today).</summary>
    DateOnly GetToday(long organizationId = 0);

    /// <summary>Calculates the UTC start (inclusive) and next-day start (exclusive) for a given organization-local date.</summary>
    (DateTime StartUtc, DateTime EndUtcExclusive) GetDayBoundsUtc(DateOnly localDate, long organizationId = 0);

    /// <summary>Calculates the UTC start (inclusive) and next-year start (exclusive) for a Persian or Gregorian calendar year.</summary>
    (DateTime StartUtc, DateTime EndUtcExclusive) GetYearBoundsUtc(int year, bool isPersian, long organizationId = 0);

    /// <summary>Gets the effective calendar year (Gregorian or Persian) for a UTC instant in an organization's time zone.</summary>
    int GetYear(DateTime utcTime, bool isPersian, long organizationId = 0);
}
