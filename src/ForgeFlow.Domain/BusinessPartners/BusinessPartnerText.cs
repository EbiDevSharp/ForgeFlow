using System.Net.Mail;

namespace ForgeFlow.Domain.BusinessPartners;

public static class BusinessPartnerText
{
    public const int MaxCodeLength = 32;
    public const int MaxNameLength = 200;
    public const int MaxAddressLength = 500;
    public const int MaxNotesLength = 1000;

    public static string NormalizeDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var chars = raw.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '۰' and <= '۹')
                chars[i] = (char)('0' + (chars[i] - '۰'));
            else if (chars[i] is >= '٠' and <= '٩')
                chars[i] = (char)('0' + (chars[i] - '٠'));
        }
        return new string(chars).Trim();
    }

    public static string Code(string? raw, string errorCode)
    {
        var trimmed = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxCodeLength)
            throw new BusinessPartnerRuleException(errorCode, ("maxLength", MaxCodeLength));
        if (!(trimmed[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            throw new BusinessPartnerRuleException(errorCode, ("maxLength", MaxCodeLength));
        foreach (var ch in trimmed)
        {
            if (!(ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                throw new BusinessPartnerRuleException(errorCode, ("maxLength", MaxCodeLength));
        }
        return trimmed;
    }

    public static PersonType ValidatePersonType(PersonType type)
    {
        if (!Enum.IsDefined(type))
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.PersonTypeInvalid);
        return type;
    }

    public static string RequiredName(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength || trimmed.Any(char.IsControl))
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.NameInvalid, ("maxLength", MaxNameLength));
        return trimmed;
    }

    public static string? OptionalText(string? raw, int maxLength, string errorCode)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > maxLength || trimmed.Any(char.IsControl))
            throw new BusinessPartnerRuleException(errorCode, ("maxLength", maxLength));
        return trimmed;
    }

    public static string? OptionalNationalCode(string? raw)
    {
        var normalized = NormalizeDigits(raw);
        if (normalized.Length == 0) return null;
        if (normalized.Length != 10 || !normalized.All(char.IsDigit))
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.NationalCodeInvalid);
        return normalized;
    }

    public static string? OptionalDigits(string? raw, int maxLength, string errorCode)
    {
        var normalized = NormalizeDigits(raw);
        if (normalized.Length == 0) return null;
        if (normalized.Length > maxLength || !normalized.All(char.IsDigit))
            throw new BusinessPartnerRuleException(errorCode, ("maxLength", maxLength));
        return normalized;
    }

    public static string? OptionalMobile(string? raw)
    {
        var normalized = NormalizeDigits(raw);
        if (normalized.Length == 0) return null;
        if (normalized.Length != 11 || !normalized.StartsWith("09", StringComparison.Ordinal) || !normalized.All(char.IsDigit))
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.MobileInvalid);
        return normalized;
    }

    public static string? OptionalPhone(string? raw)
    {
        var normalized = NormalizeDigits(raw);
        if (normalized.Length == 0) return null;
        if (normalized.Length > 32 || !normalized.StartsWith("0", StringComparison.Ordinal) || !normalized.All(char.IsDigit))
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.PhoneInvalid);
        return normalized;
    }

    public static string? OptionalEmail(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 256)
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.EmailInvalid, ("maxLength", 256));
        try
        {
            var address = new MailAddress(trimmed);
            return address.Address;
        }
        catch (FormatException)
        {
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.EmailInvalid);
        }
    }
}
