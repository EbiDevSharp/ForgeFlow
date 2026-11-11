using System.Net.Mail;

namespace ForgeFlow.Domain.Procurement;

public static class ProcurementText
{
    public const int MaxCodeLength = 32;
    public const int MaxNameLength = 200;
    public const int MaxContactLength = 150;
    public const int MaxPhoneLength = 64;
    public const int MaxEmailLength = 256;
    public const int MaxNotesLength = 500;
    public const int MaxLineNotesLength = 300;
    public static readonly IReadOnlyList<string> SupportedCurrencies = ["IRR", "USD", "EUR"];

    public static string Code(string? raw, string errorCode)
    {
        var trimmed = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxCodeLength)
            throw new ProcurementRuleException(errorCode, ("maxLength", MaxCodeLength));
        if (!(trimmed[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new ProcurementRuleException(errorCode, ("maxLength", MaxCodeLength));
        foreach (var ch in trimmed)
        {
            if (!(ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                throw new ProcurementRuleException(errorCode, ("maxLength", MaxCodeLength));
        }
        return trimmed;
    }

    public static string RequiredName(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength || trimmed.Any(char.IsControl))
            throw new ProcurementRuleException(ProcurementErrors.SupplierNameInvalid, ("maxLength", MaxNameLength));
        return trimmed;
    }

    public static string? OptionalText(string? raw, int maxLength, string errorCode)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > maxLength || trimmed.Any(char.IsControl))
            throw new ProcurementRuleException(errorCode, ("maxLength", maxLength));
        return trimmed;
    }

    public static string? OptionalEmail(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxEmailLength || trimmed.Any(char.IsControl))
            throw new ProcurementRuleException(ProcurementErrors.EmailInvalid);
        try
        {
            var addr = new MailAddress(trimmed);
            if (!string.Equals(addr.Address, trimmed, StringComparison.OrdinalIgnoreCase))
                throw new ProcurementRuleException(ProcurementErrors.EmailInvalid);
            return trimmed;
        }
        catch (FormatException)
        {
            throw new ProcurementRuleException(ProcurementErrors.EmailInvalid);
        }
    }

    public static string? OptionalPhone(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxPhoneLength || !trimmed.All(c => char.IsDigit(c) || c is '+' or '-' or ' ' or '(' or ')'))
            throw new ProcurementRuleException(ProcurementErrors.PhoneInvalid);
        return trimmed;
    }

    public static string Currency(string? raw)
    {
        var code = (string.IsNullOrWhiteSpace(raw) ? "IRR" : raw.Trim()).ToUpperInvariant();
        if (code.Length != 3 || code.Any(c => c is < 'A' or > 'Z'))
            throw new ProcurementRuleException(ProcurementErrors.CurrencyInvalid);
        return code;
    }

    public static string RequiredReason(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNotesLength || trimmed.Any(char.IsControl))
            throw new ProcurementRuleException(ProcurementErrors.CancellationReasonInvalid, ("maxLength", MaxNotesLength));
        return trimmed;
    }

    public static decimal PositiveQuantity(decimal value)
    {
        if (value <= 0m || value > 999_999_999_999.999999m)
            throw new ProcurementRuleException(ProcurementErrors.QuantityInvalid);
        return decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    public static decimal? OptionalUnitPrice(decimal? value)
    {
        if (!value.HasValue) return null;
        if (value.Value < 0m || value.Value > 999_999_999_999.9999m)
            throw new ProcurementRuleException(ProcurementErrors.UnitPriceInvalid);
        return decimal.Round(value.Value, 4, MidpointRounding.AwayFromZero);
    }
}
