namespace ForgeFlow.Domain.ProductionOrders;

public static class ProductionOrderText
{
    public const int MaxOrderNumberLength = 32;
    public const int MaxNotesLength = 500;
    public const int MaxContextValueLength = 400;

    public static string OrderNumber(string? raw)
    {
        var trimmed = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxOrderNumberLength)
            throw new ProductionOrderRuleException(ProductionOrderErrors.OrderNumberInvalid, ("maxLength", MaxOrderNumberLength));
        if (!(trimmed[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new ProductionOrderRuleException(ProductionOrderErrors.OrderNumberInvalid, ("maxLength", MaxOrderNumberLength));
        foreach (var ch in trimmed)
        {
            if (!(ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                throw new ProductionOrderRuleException(ProductionOrderErrors.OrderNumberInvalid, ("maxLength", MaxOrderNumberLength));
        }
        return trimmed;
    }

    public static string? OptionalText(string? raw, string errorCode)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxNotesLength || trimmed.Any(char.IsControl))
            throw new ProductionOrderRuleException(errorCode, ("maxLength", MaxNotesLength));
        return trimmed;
    }

    public static string RequiredReason(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNotesLength || trimmed.Any(char.IsControl))
            throw new ProductionOrderRuleException(ProductionOrderErrors.CancellationReasonInvalid, ("maxLength", MaxNotesLength));
        return trimmed;
    }

    public static decimal ValidatePositiveQuantity(decimal value, string errorCode = ProductionOrderErrors.QuantityInvalid)
    {
        if (value <= 0m || value > 999_999_999_999.999999m)
            throw new ProductionOrderRuleException(errorCode);
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    public static decimal ValidateNonNegativeQuantity(decimal value)
    {
        if (value < 0m || value > 999_999_999_999.999999m)
            throw new ProductionOrderRuleException(ProductionOrderErrors.ActualQuantityInvalid);
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
