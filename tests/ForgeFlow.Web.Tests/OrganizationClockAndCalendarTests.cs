using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Common;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Common;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class OrganizationClockAndCalendarTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan delta) => current += delta;
        public void Set(DateTimeOffset newTime) => current = newTime;
    }

    private static readonly TimeZoneInfo TehranZone = OrganizationCalendarUtils.DefaultTimeZone;

    [Fact]
    public void OrganizationCalendarUtils_PersianToDateOnly_and_roundtrip_succeeds()
    {
        // 1403/01/01 Farvardin 1 (Nowruz)
        var date1 = OrganizationCalendarUtils.PersianToDateOnly(1403, 1, 1);
        Assert.Equal(new DateOnly(2024, 3, 20), date1);

        var (y1, m1, d1) = OrganizationCalendarUtils.DateOnlyToPersian(date1);
        Assert.Equal(1403, y1);
        Assert.Equal(1, m1);
        Assert.Equal(1, d1);

        // 1405/07/15
        var date2 = OrganizationCalendarUtils.PersianToDateOnly(1405, 7, 15);
        var (y2, m2, d2) = OrganizationCalendarUtils.DateOnlyToPersian(date2);
        Assert.Equal(1405, y2);
        Assert.Equal(7, m2);
        Assert.Equal(15, d2);
    }

    [Fact]
    public void OrganizationCalendarUtils_LeapYears_correctly_identified()
    {
        // Persian calendar: 1399 and 1403 are leap years; 1404 is not.
        Assert.True(OrganizationCalendarUtils.IsPersianLeapYear(1399));
        Assert.True(OrganizationCalendarUtils.IsPersianLeapYear(1403));
        Assert.False(OrganizationCalendarUtils.IsPersianLeapYear(1404));
        Assert.False(OrganizationCalendarUtils.IsPersianLeapYear(1405));

        // 1403/12/30 is valid (leap year)
        Assert.True(OrganizationCalendarUtils.TryPersianToDateOnly(1403, 12, 30, out var leapDate));
        Assert.Equal(new DateOnly(2025, 3, 20), leapDate);

        // 1404/12/30 is invalid (non-leap year, Esfand has 29 days)
        Assert.False(OrganizationCalendarUtils.TryPersianToDateOnly(1404, 12, 30, out _));

        // Gregorian calendar
        Assert.True(OrganizationCalendarUtils.IsGregorianLeapYear(2024));
        Assert.False(OrganizationCalendarUtils.IsGregorianLeapYear(2025));
        Assert.False(OrganizationCalendarUtils.IsGregorianLeapYear(2026));
    }

    [Fact]
    public void OrganizationCalendarUtils_GetYear_with_TimeZone_and_Midnight_boundary()
    {
        // Tehran is UTC+03:30 (standard).
        // 1 second before Persian New Year 1405:
        // UTC: 2026-03-20 20:29:59 -> Tehran: 2026-03-20 23:59:59 (Year 1404)
        var beforeNewYearUtc = new DateTime(2026, 3, 20, 20, 29, 59, DateTimeKind.Utc);
        var yearBefore = OrganizationCalendarUtils.GetYear(beforeNewYearUtc, DocumentYearMode.Persian, TehranZone);
        Assert.Equal(1404, yearBefore);

        // Exactly at Persian New Year 1405 midnight:
        // UTC: 2026-03-20 20:30:00 -> Tehran: 2026-03-21 00:00:00 (Farvardin 1, Year 1405)
        var atNewYearUtc = new DateTime(2026, 3, 20, 20, 30, 0, DateTimeKind.Utc);
        var yearAt = OrganizationCalendarUtils.GetYear(atNewYearUtc, DocumentYearMode.Persian, TehranZone);
        Assert.Equal(1405, yearAt);

        // Gregorian year boundary:
        // UTC: 2026-12-31 20:29:59 -> Tehran: 2026-12-31 23:59:59 (Year 2026)
        var beforeGregNewYearUtc = new DateTime(2026, 12, 31, 20, 29, 59, DateTimeKind.Utc);
        var gregYearBefore = OrganizationCalendarUtils.GetYear(beforeGregNewYearUtc, DocumentYearMode.Gregorian, TehranZone);
        Assert.Equal(2026, gregYearBefore);

        // UTC: 2026-12-31 20:30:00 -> Tehran: 2027-01-01 00:00:00 (Year 2027)
        var atGregNewYearUtc = new DateTime(2026, 12, 31, 20, 30, 0, DateTimeKind.Utc);
        var gregYearAt = OrganizationCalendarUtils.GetYear(atGregNewYearUtc, DocumentYearMode.Gregorian, TehranZone);
        Assert.Equal(2027, gregYearAt);
    }

    [Fact]
    public void DocumentSequence_Resets_and_increments_across_Persian_year_transition()
    {
        var seq = DocumentSequence.Create(1, DocumentSequenceType.DeliveryOrder, "DO", "-", DocumentYearMode.Persian, 5, 42, false);

        // Allocations in year 1404
        var time1404 = new DateTime(2026, 3, 20, 20, 0, 0, DateTimeKind.Utc); // 23:30 local in Tehran
        Assert.Equal("DO-1404-00042", seq.FormatPreview(time1404, TehranZone));
        Assert.Equal("DO-1404-00042", seq.AllocateNextNumber(time1404, TehranZone));
        Assert.Equal("DO-1404-00043", seq.FormatPreview(time1404, TehranZone));

        // Cross midnight into year 1405
        var time1405 = new DateTime(2026, 3, 20, 20, 30, 1, DateTimeKind.Utc); // 00:00:01 local in Tehran
        Assert.Equal("DO-1405-00001", seq.FormatPreview(time1405, TehranZone));
        Assert.Equal("DO-1405-00001", seq.AllocateNextNumber(time1405, TehranZone));
        Assert.Equal("DO-1405-00002", seq.FormatPreview(time1405, TehranZone));
        Assert.Equal("DO-1405-00002", seq.AllocateNextNumber(time1405, TehranZone));
    }

    [Fact]
    public void DocumentSequence_Resets_and_increments_across_Gregorian_year_transition()
    {
        var seq = DocumentSequence.Create(1, DocumentSequenceType.PurchaseOrder, "PO", "_", DocumentYearMode.Gregorian, 6, 99, false);

        // Allocations in year 2026
        var time2026 = new DateTime(2026, 12, 31, 20, 25, 0, DateTimeKind.Utc); // 23:55 local in Tehran
        Assert.Equal("PO_2026_000099", seq.FormatPreview(time2026, TehranZone));
        Assert.Equal("PO_2026_000099", seq.AllocateNextNumber(time2026, TehranZone));

        // Cross midnight into year 2027
        var time2027 = new DateTime(2026, 12, 31, 20, 35, 0, DateTimeKind.Utc); // 00:05 local in Tehran
        Assert.Equal("PO_2027_000001", seq.FormatPreview(time2027, TehranZone));
        Assert.Equal("PO_2027_000001", seq.AllocateNextNumber(time2027, TehranZone));
        Assert.Equal("PO_2027_000002", seq.FormatPreview(time2027, TehranZone));
    }

    [Fact]
    public void OrganizationCalendarUtils_GetDayBoundsUtc_handles_fractional_timezones_accurately()
    {
        var localDate = new DateOnly(2026, 3, 21); // Farvardin 1, 1405
        var (startUtc, endUtcExclusive) = OrganizationCalendarUtils.GetDayBoundsUtc(localDate, TehranZone);

        // In Tehran (UTC+03:30): 2026-03-21 00:00:00 local is 2026-03-20 20:30:00 UTC
        Assert.Equal(new DateTime(2026, 3, 20, 20, 30, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 3, 21, 20, 30, 0, DateTimeKind.Utc), endUtcExclusive);

        // An event at 2026-03-20 21:00:00 UTC (00:30 local) falls cleanly within the day bounds
        var testEventUtc = new DateTime(2026, 3, 20, 21, 0, 0, DateTimeKind.Utc);
        Assert.True(testEventUtc >= startUtc && testEventUtc < endUtcExclusive);

        // An event 1 second before midnight local time is excluded
        var previousDayEventUtc = new DateTime(2026, 3, 20, 20, 29, 59, DateTimeKind.Utc);
        Assert.False(previousDayEventUtc >= startUtc);
    }

    [Fact]
    public void OrganizationCalendarUtils_GetPersianYearBoundsUtc_covers_full_year_including_leap()
    {
        // Year 1403 was a leap year: 366 days
        var (start1403, end1403) = OrganizationCalendarUtils.GetPersianYearBoundsUtc(1403, TehranZone);
        Assert.Equal(new DateTime(2024, 3, 19, 20, 30, 0, DateTimeKind.Utc), start1403);
        Assert.Equal(new DateTime(2025, 3, 20, 20, 30, 0, DateTimeKind.Utc), end1403);
        Assert.Equal(366, (end1403 - start1403).TotalDays);

        // Year 1404 is a regular year: 365 days
        var (start1404, end1404) = OrganizationCalendarUtils.GetPersianYearBoundsUtc(1404, TehranZone);
        Assert.Equal(365, (end1404 - start1404).TotalDays);
    }

    [Fact]
    public void OrganizationClock_Deterministic_TimeProvider_and_Today_calculation()
    {
        // 2026-03-20 21:00:00 UTC
        // UTC Date is March 20. But in Tehran (+03:30), local time is March 21 00:30 AM!
        var fixedInstant = new DateTimeOffset(2026, 3, 20, 21, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedInstant);
        var clock = new OrganizationClock(timeProvider, new DefaultOrganizationTimeZoneProvider());

        Assert.Equal(new DateTime(2026, 3, 20, 21, 0, 0, DateTimeKind.Utc), clock.UtcNow);
        Assert.Equal(fixedInstant, clock.UtcNowOffset);

        // Organization-local today is March 21 (NOT March 20!)
        var today = clock.GetToday(1);
        Assert.Equal(new DateOnly(2026, 3, 21), today);

        // ToLocal converts to local time
        var localTime = clock.ToLocal(clock.UtcNow, 1);
        Assert.Equal(new DateTime(2026, 3, 21, 0, 30, 0), localTime);

        // ToUtc converts back
        var convertedBackUtc = clock.ToUtc(localTime, 1);
        Assert.Equal(clock.UtcNow, convertedBackUtc);

        // Advance 24 hours
        timeProvider.Advance(TimeSpan.FromHours(24));
        Assert.Equal(new DateOnly(2026, 3, 22), clock.GetToday(1));
    }
}
