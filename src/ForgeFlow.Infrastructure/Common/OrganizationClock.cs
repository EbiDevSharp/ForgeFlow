using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Common;
using ForgeFlow.Domain.Organizations;

namespace ForgeFlow.Infrastructure.Common;

/// <summary>
/// Production and test implementation of IOrganizationClock using TimeProvider and IOrganizationTimeZoneProvider.
/// Ensures all timestamps remain stored in UTC while local conversions, date boundaries, and year transitions
/// honor the organization's time zone and calendar system.
/// </summary>
public sealed class OrganizationClock : IOrganizationClock
{
    private readonly TimeProvider timeProvider;
    private readonly IOrganizationTimeZoneProvider timeZoneProvider;

    public OrganizationClock(
        TimeProvider? timeProvider = null,
        IOrganizationTimeZoneProvider? timeZoneProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.timeZoneProvider = timeZoneProvider ?? new DefaultOrganizationTimeZoneProvider();
    }

    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public DateTimeOffset UtcNowOffset => timeProvider.GetUtcNow();

    public TimeZoneInfo GetTimeZone(long organizationId = 0)
        => timeZoneProvider.GetTimeZone(organizationId);

    public DateTime ToLocal(DateTime utcTime, long organizationId = 0)
    {
        var tz = GetTimeZone(organizationId);
        var utc = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    public DateTime ToUtc(DateTime localTime, long organizationId = 0)
    {
        var tz = GetTimeZone(organizationId);
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }

    public DateOnly GetToday(long organizationId = 0)
    {
        var local = ToLocal(UtcNow, organizationId);
        return DateOnly.FromDateTime(local);
    }

    public (DateTime StartUtc, DateTime EndUtcExclusive) GetDayBoundsUtc(DateOnly localDate, long organizationId = 0)
    {
        var tz = GetTimeZone(organizationId);
        return OrganizationCalendarUtils.GetDayBoundsUtc(localDate, tz);
    }

    public (DateTime StartUtc, DateTime EndUtcExclusive) GetYearBoundsUtc(int year, bool isPersian, long organizationId = 0)
    {
        var tz = GetTimeZone(organizationId);
        return isPersian
            ? OrganizationCalendarUtils.GetPersianYearBoundsUtc(year, tz)
            : OrganizationCalendarUtils.GetGregorianYearBoundsUtc(year, tz);
    }

    public int GetYear(DateTime utcTime, bool isPersian, long organizationId = 0)
    {
        var tz = GetTimeZone(organizationId);
        var mode = isPersian ? DocumentYearMode.Persian : DocumentYearMode.Gregorian;
        return OrganizationCalendarUtils.GetYear(utcTime, mode, tz) ?? utcTime.Year;
    }
}
