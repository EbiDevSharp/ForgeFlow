using ForgeFlow.Domain.Common;

namespace ForgeFlow.Infrastructure.Diagnostics;

internal static class DiagnosticDateBounds
{
    public static DateTime LocalDayStartToUtc(DateOnly date, TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? OrganizationCalendarUtils.DefaultTimeZone;
        var (startUtc, _) = OrganizationCalendarUtils.GetDayBoundsUtc(date, tz);
        return startUtc;
    }

    public static DateTime LocalDayEndExclusiveToUtc(DateOnly date, TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? OrganizationCalendarUtils.DefaultTimeZone;
        var (_, endUtcExclusive) = OrganizationCalendarUtils.GetDayBoundsUtc(date, tz);
        return endUtcExclusive;
    }
}
