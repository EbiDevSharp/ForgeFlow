using System.Globalization;

namespace ForgeFlow.Web.Localization;

public sealed class DisplayFormatter(CulturePolicy culture)
{
    private static readonly string[] CurrencyCodes = ["USD", "EUR", "IRR"];

    public string Date(DateOnly value)
    {
        var date = value.ToDateTime(TimeOnly.MinValue);
        var calendar = culture.Calendar;
        return string.Create(CultureInfo.InvariantCulture,
            $"{calendar.GetYear(date):0000}/{calendar.GetMonth(date):00}/{calendar.GetDayOfMonth(date):00}");
    }

    public string Number(decimal value) => value.ToString("N2", culture.Current);

    public string Money(decimal amount, string currencyCode)
        => string.Concat(Number(amount), " ", TryCurrencyCode(currencyCode, out var code) ? code : string.Empty);

    public static string CanonicalDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string CanonicalAmount(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    public bool TryDateOnly(string raw, string inputCulture, out DateOnly value)
    {
        value = default;
        var supported = CulturePolicy.Normalize(inputCulture);
        if (supported is null || raw.Length != 10) return false;
        var normalized = NormalizeDigits(raw);
        var separator = supported == CulturePolicy.Persian ? '/' : '/';
        if (normalized[4] != separator && supported == CulturePolicy.Persian ||
            normalized[2] != separator && supported == CulturePolicy.English) return false;
        var yearFirst = supported == CulturePolicy.Persian;
        var yearIndex = yearFirst ? 0 : 6;
        var monthIndex = yearFirst ? 5 : 0;
        var dayIndex = yearFirst ? 8 : 3;
        if (normalized[yearFirst ? 4 : 2] != separator || normalized[yearFirst ? 7 : 5] != separator) return false;
        if (!TryDigits(normalized.AsSpan(yearIndex, 4), out var year) ||
            !TryDigits(normalized.AsSpan(monthIndex, 2), out var month) ||
            !TryDigits(normalized.AsSpan(dayIndex, 2), out var day)) return false;
        try
        {
            if (yearFirst)
            {
                var date = new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0);
                value = DateOnly.FromDateTime(date);
            }
            else value = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public bool TryCurrencyCode(string raw, out string code)
    {
        code = CurrencyCodes.FirstOrDefault(item => string.Equals(item, raw, StringComparison.Ordinal)) ?? string.Empty;
        return code.Length > 0;
    }

    public static IReadOnlyList<string> SupportedCurrencyCodes => CurrencyCodes;

    public static string CurrencyResourceKey(string currencyCode) => currencyCode switch
    {
        "USD" => "CurrencyUsd",
        "EUR" => "CurrencyEur",
        "IRR" => "CurrencyIrr",
        _ => throw new ArgumentOutOfRangeException(nameof(currencyCode))
    };

    public static string SampleModeValue(SampleMode mode) => mode switch
    {
        SampleMode.Summary => "summary",
        SampleMode.Detailed => "detailed",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static string SampleModeResourceKey(SampleMode mode) => mode switch
    {
        SampleMode.Summary => "SampleModeSummary",
        SampleMode.Detailed => "SampleModeDetailed",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static bool TrySampleMode(string raw, out SampleMode mode)
    {
        foreach (var candidate in Enum.GetValues<SampleMode>())
        {
            if (string.Equals(SampleModeValue(candidate), raw, StringComparison.Ordinal))
            {
                mode = candidate;
                return true;
            }
        }
        mode = default;
        return false;
    }

    public bool TryNumber(string raw, string inputCulture, out decimal value)
    {
        value = default;
        var supported = CulturePolicy.Normalize(inputCulture);
        if (supported is null || string.IsNullOrWhiteSpace(raw) || raw.Length > 64) return false;
        var separator = CultureInfo.GetCultureInfo(supported).NumberFormat.NumberDecimalSeparator;
        var normalized = NormalizeDigits(raw);
        if (normalized.Contains(',') || normalized.Contains('٬') ||
            separator != "." && normalized.Contains('.') || normalized.Count(character => character == separator[0]) > 1) return false;
        normalized = normalized.Replace(separator, ".", StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeDigits(string raw) => new(raw.Select(character => character switch
    {
        >= '\u06f0' and <= '\u06f9' => (char)('0' + character - '\u06f0'),
        >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
        _ => character
    }).ToArray());

    private static bool TryDigits(ReadOnlySpan<char> raw, out int value)
        => int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
