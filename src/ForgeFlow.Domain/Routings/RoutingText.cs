namespace ForgeFlow.Domain.Routings;

public static class RoutingText
{
    public const int MaxCodeLength = 32;
    public const int MaxNameLength = 200;
    public const int MaxNotesLength = 500;

    public static string Code(string? raw, string errorCode)
    {
        var trimmed = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxCodeLength)
            throw new RoutingRuleException(errorCode, ("maxLength", MaxCodeLength));
        if (!(trimmed[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new RoutingRuleException(errorCode, ("maxLength", MaxCodeLength));
        foreach (var ch in trimmed)
        {
            if (!(ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                throw new RoutingRuleException(errorCode, ("maxLength", MaxCodeLength));
        }
        return trimmed;
    }

    public static string RequiredName(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength || trimmed.Any(char.IsControl))
            throw new RoutingRuleException(RoutingErrors.NameInvalid, ("maxLength", MaxNameLength));
        return trimmed;
    }

    public static string? OptionalNotes(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxNotesLength || trimmed.Any(char.IsControl))
            throw new RoutingRuleException(RoutingErrors.NotesInvalid, ("maxLength", MaxNotesLength));
        return trimmed;
    }

    public static decimal PositiveCapacity(decimal value)
    {
        if (value <= 0m || value > 999_999.9999m)
            throw new RoutingRuleException(RoutingErrors.CapacityInvalid);
        return decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    public static decimal DailyHours(decimal value)
    {
        if (value <= 0m || value > 24m)
            throw new RoutingRuleException(RoutingErrors.DailyHoursInvalid);
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal? OptionalHourlyCost(decimal? value)
    {
        if (!value.HasValue) return null;
        if (value.Value < 0m || value.Value > 999_999_999.9999m)
            throw new RoutingRuleException(RoutingErrors.CostRateInvalid);
        return decimal.Round(value.Value, 4, MidpointRounding.AwayFromZero);
    }

    public static decimal NonNegativeMinutes(decimal value)
    {
        if (value < 0m || value > 999_999.99m)
            throw new RoutingRuleException(RoutingErrors.OperationTimeInvalid);
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal PositiveRunMinutesPerUnit(decimal value)
    {
        if (value <= 0m || value > 999_999.9999m)
            throw new RoutingRuleException(RoutingErrors.RunTimeInvalid);
        return decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    public static decimal NonNegativeQuantity(decimal value)
    {
        if (value < 0m || value > 999_999_999_999.999999m)
            throw new RoutingRuleException(RoutingErrors.QuantityInvalid);
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
