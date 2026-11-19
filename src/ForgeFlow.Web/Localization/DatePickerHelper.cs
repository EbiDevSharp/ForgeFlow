namespace ForgeFlow.Web.Localization;

/// <summary>
/// Safe DateOnly &lt;-&gt; DateTimeOffset conversions for BitDatePicker under PersianCalendar (MinSupportedDateTime: 0622-03-22).
/// </summary>
public static class DatePickerHelper
{
    public static DateTimeOffset? ToDateTimeOffset(DateOnly date)
        => date == default ? null : new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset? ToDateTimeOffset(DateOnly? date)
        => date is { } d ? ToDateTimeOffset(d) : null;

    public static DateOnly? FromDateTimeOffset(DateTimeOffset? value)
        => value.HasValue ? DateOnly.FromDateTime(value.Value.Date) : null;
}
