namespace ForgeFlow.Domain.Quality;

public static class QualityText
{
    public const int MaxCodeLength = 32;
    public const int MaxDefectReasonLength = 64;
    public const int MaxParameterLength = 100;
    public const int MaxNoteLength = 1000;
    public const int MaxLotNoteLength = 500;
    public const int MaxLineNoteLength = 300;
    public const int MaxSymbolLength = 24;
    public const int MaxTestMethodLength = 64;
    public const int MaxCertificateLength = 64;
    public const int MaxSpecNameLength = 120;
    private const decimal MaxQuantityMagnitude = 999_999_999_999.999999m;

    public static string Code(string? raw, string errorCode = QualityErrors.InspectionNumberInvalid)
    {
        var trimmed = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxCodeLength)
            throw new QualityRuleException(errorCode, ("maxLength", MaxCodeLength));
        if (!(trimmed[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new QualityRuleException(errorCode, ("maxLength", MaxCodeLength));
        foreach (var ch in trimmed)
        {
            if (!(ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                throw new QualityRuleException(errorCode, ("maxLength", MaxCodeLength));
        }
        return trimmed;
    }

    public static string OptionalLotCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return Code(raw, QualityErrors.LotCodeInvalid);
    }

    public static string RequiredLotCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new QualityRuleException(QualityErrors.LotCodeRequired);
        return Code(raw, QualityErrors.LotCodeInvalid);
    }

    public static string? OptionalDefectReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed.Length > MaxDefectReasonLength)
            throw new QualityRuleException(QualityErrors.DefectReasonInvalid, ("maxLength", MaxDefectReasonLength));
        if (!(trimmed[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new QualityRuleException(QualityErrors.DefectReasonInvalid, ("maxLength", MaxDefectReasonLength));
        foreach (var ch in trimmed)
        {
            if (!(ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                throw new QualityRuleException(QualityErrors.DefectReasonInvalid, ("maxLength", MaxDefectReasonLength));
        }
        return trimmed;
    }

    public static string ParameterName(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxParameterLength || trimmed.Any(char.IsControl))
            throw new QualityRuleException(QualityErrors.ParameterInvalid, ("maxLength", MaxParameterLength));
        return trimmed;
    }

    public static string? OptionalSpecOrValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > MaxParameterLength || trimmed.Any(char.IsControl))
            throw new QualityRuleException(QualityErrors.ParameterInvalid, ("maxLength", MaxParameterLength));
        return trimmed;
    }

    public static string? OptionalNote(string? raw, int maxLength = MaxNoteLength)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > maxLength || trimmed.Any(char.IsControl))
            throw new QualityRuleException(QualityErrors.NotesInvalid, ("maxLength", maxLength));
        return trimmed;
    }

    public static decimal ValidatePositiveQuantity(decimal value)
    {
        if (value <= 0m || value > MaxQuantityMagnitude)
            throw new QualityRuleException(QualityErrors.QuantityInvalid);
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    public static decimal ValidateNonNegativeQuantity(decimal value)
    {
        if (value < 0m || value > MaxQuantityMagnitude)
            throw new QualityRuleException(QualityErrors.QuantityInvalid);
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    public static void ValidateDates(DateOnly? manufactureDate, DateOnly? expirationDate)
    {
        if (manufactureDate.HasValue && expirationDate.HasValue && expirationDate.Value < manufactureDate.Value)
            throw new QualityRuleException(QualityErrors.LotDatesInvalid);
    }

    public static string SpecName(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxSpecNameLength || trimmed.Any(char.IsControl))
            throw new QualityRuleException(QualityErrors.ParameterInvalid, ("maxLength", MaxSpecNameLength));
        return trimmed;
    }

    public static string Symbol(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxSymbolLength || trimmed.Any(char.IsControl))
            throw new QualityRuleException(QualityErrors.ParameterSymbolInvalid, ("maxLength", MaxSymbolLength));
        return trimmed;
    }

    public static string? OptionalUnitSymbol(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > MaxSymbolLength || trimmed.Any(char.IsControl))
            throw new QualityRuleException(QualityErrors.ParameterUnitInvalid, ("maxLength", MaxSymbolLength));
        return trimmed;
    }

    public static string? OptionalTestMethod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > MaxTestMethodLength || trimmed.Any(char.IsControl))
            throw new QualityRuleException(QualityErrors.ParameterInvalid, ("maxLength", MaxTestMethodLength));
        return trimmed;
    }

    public static string? OptionalCertificateNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > MaxCertificateLength || trimmed.Any(char.IsControl))
            throw new QualityRuleException(QualityErrors.ParameterInvalid, ("maxLength", MaxCertificateLength));
        return trimmed;
    }

    public static byte ValidateDecimalPlaces(byte places)
    {
        if (places > 6)
            throw new QualityRuleException(QualityErrors.ParameterInvalid);
        return places;
    }
}
