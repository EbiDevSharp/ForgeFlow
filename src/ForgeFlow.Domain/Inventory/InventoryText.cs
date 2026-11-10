namespace ForgeFlow.Domain.Inventory;

public static class InventoryText
{
    public const int MaxCodeLength = 32;
    public const int MaxNameLength = 200;
    public const int MaxNoteLength = 500;
    public const int MaxLineNoteLength = 300;
    public const int MaxIdempotencyKeyLength = 64;
    private const decimal MaxQuantityMagnitude = 999_999_999_999.999999m;

    public static string Code(string? raw, string errorCode = InventoryErrors.CodeInvalid)
    {
        var trimmed = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxCodeLength)
            throw new InventoryRuleException(errorCode, ("maxLength", MaxCodeLength));
        if (!(trimmed[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new InventoryRuleException(errorCode, ("maxLength", MaxCodeLength));
        foreach (var ch in trimmed)
        {
            if (!(ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                throw new InventoryRuleException(errorCode, ("maxLength", MaxCodeLength));
        }
        return trimmed;
    }

    public static string Name(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength || trimmed.Any(char.IsControl))
            throw new InventoryRuleException(InventoryErrors.NameInvalid, ("maxLength", MaxNameLength));
        return trimmed;
    }

    public static string LotCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return Code(raw, InventoryErrors.LotCodeInvalid);
    }

    public static string? OptionalNote(string? raw, int maxLength = MaxNoteLength)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > maxLength || trimmed.Any(char.IsControl))
            throw new InventoryRuleException(InventoryErrors.NotesInvalid, ("maxLength", maxLength));
        return trimmed;
    }

    public static string? OptionalIdempotencyKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > MaxIdempotencyKeyLength || trimmed.Any(char.IsControl) || trimmed.Any(char.IsWhiteSpace))
            throw new InventoryRuleException(InventoryErrors.IdempotencyKeyInvalid, ("maxLength", MaxIdempotencyKeyLength));
        foreach (var ch in trimmed)
        {
            if (ch > 127)
                throw new InventoryRuleException(InventoryErrors.IdempotencyKeyInvalid, ("maxLength", MaxIdempotencyKeyLength));
        }
        return trimmed;
    }

    public static decimal ValidatePositiveQuantity(decimal value)
    {
        if (value <= 0m || value > MaxQuantityMagnitude)
            throw new InventoryRuleException(InventoryErrors.QuantityInvalid);
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    public static decimal ValidateNonZeroQuantity(decimal value)
    {
        if (value == 0m || Math.Abs(value) > MaxQuantityMagnitude)
            throw new InventoryRuleException(InventoryErrors.QuantityInvalid);
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
