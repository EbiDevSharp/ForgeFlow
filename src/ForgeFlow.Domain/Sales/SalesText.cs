using System.Net.Mail;

namespace ForgeFlow.Domain.Sales;

public static class SalesText
{
    public const int MaxCodeLength = 32;
    public const int MaxNameLength = 150;
    public const int MaxContactLength = 120;
    public const int MaxPhoneLength = 32;
    public const int MaxEmailLength = 200;
    public const int MaxAddressLength = 500;
    public const int MaxTaxCodeLength = 40;
    public const int MaxNotesLength = 1000;
    public const int MaxLineNotesLength = 300;
    public const int MaxReasonLength = 300;
    public const int MaxCarrierLength = 120;
    public const int MaxTrackingLength = 64;
    public const int MaxLotCodeLength = 64;

    public static string Code(string? raw, string errorCode)
    {
        var trimmed = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxCodeLength)
            throw new SalesRuleException(errorCode, ("maxLength", MaxCodeLength));
        if (!(trimmed[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new SalesRuleException(errorCode, ("maxLength", MaxCodeLength));
        foreach (var ch in trimmed)
        {
            if (!(ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                throw new SalesRuleException(errorCode, ("maxLength", MaxCodeLength));
        }
        return trimmed;
    }

    public static string RequiredName(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength || trimmed.Any(char.IsControl))
            throw new SalesRuleException(SalesErrors.CustomerNameInvalid, ("maxLength", MaxNameLength));
        return trimmed;
    }

    public static string? OptionalText(string? raw, int maxLength, string errorCode)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > maxLength || trimmed.Any(char.IsControl))
            throw new SalesRuleException(errorCode, ("maxLength", maxLength));
        return trimmed;
    }

    public static string? OptionalEmail(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxEmailLength || trimmed.Any(char.IsControl))
            throw new SalesRuleException(SalesErrors.EmailInvalid);
        try
        {
            var addr = new MailAddress(trimmed);
            if (!string.Equals(addr.Address, trimmed, StringComparison.OrdinalIgnoreCase))
                throw new SalesRuleException(SalesErrors.EmailInvalid);
            return trimmed;
        }
        catch (FormatException)
        {
            throw new SalesRuleException(SalesErrors.EmailInvalid);
        }
    }

    public static string? OptionalPhone(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxPhoneLength || !trimmed.All(c => char.IsDigit(c) || c is '+' or '-' or ' ' or '(' or ')'))
            throw new SalesRuleException(SalesErrors.PhoneInvalid);
        return trimmed;
    }

    public static string Currency(string? raw)
    {
        var code = (string.IsNullOrWhiteSpace(raw) ? "IRR" : raw.Trim()).ToUpperInvariant();
        if (code.Length != 3 || code.Any(c => c is < 'A' or > 'Z'))
            throw new SalesRuleException(SalesErrors.CurrencyInvalid);
        return code;
    }

    public static string RequiredReason(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxReasonLength || trimmed.Any(char.IsControl))
            throw new SalesRuleException(SalesErrors.ReasonInvalid, ("maxLength", MaxReasonLength));
        return trimmed;
    }

    public static string RequiredLotCode(string? raw)
    {
        var trimmed = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxLotCodeLength || trimmed.Any(char.IsControl))
            throw new SalesRuleException(SalesErrors.LotCodeInvalid, ("maxLength", MaxLotCodeLength));
        return trimmed;
    }

    public static decimal PositiveQuantity(decimal value)
    {
        if (value <= 0m || value > 999_999_999_999.999999m)
            throw new SalesRuleException(SalesErrors.QuantityInvalid);
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    public static decimal? OptionalUnitPrice(decimal? value)
    {
        if (!value.HasValue) return null;
        if (value.Value < 0m || value.Value > 999_999_999_999.9999m)
            throw new SalesRuleException(SalesErrors.UnitPriceInvalid);
        return decimal.Round(value.Value, 4, MidpointRounding.AwayFromZero);
    }
}
