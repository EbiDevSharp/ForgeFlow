namespace ForgeFlow.Infrastructure.Diagnostics;

internal static class DiagnosticDateBounds
{
    public static DateTime LocalDayStartToUtc(DateOnly date, TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? TimeZoneInfo.Local;
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    public static DateTime LocalDayEndExclusiveToUtc(DateOnly date, TimeZoneInfo? timeZone = null)
        => LocalDayStartToUtc(date.AddDays(1), timeZone);
}
