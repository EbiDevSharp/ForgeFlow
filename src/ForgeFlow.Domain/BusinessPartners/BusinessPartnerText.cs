using System.Net.Mail;

namespace ForgeFlow.Domain.BusinessPartners;

public static class BusinessPartnerText
{
    public const int MaxCodeLength = 32;
    public const int MaxNameLength = 200;
    public const int MaxAddressLength = 500;
    public const int MaxNotesLength = 1000;

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

    public static string? OptionalPhone(string? raw, string errorCode)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > 32)
            throw new BusinessPartnerRuleException(errorCode, ("maxLength", 32));
        foreach (var ch in trimmed)
        {
            if (!(char.IsDigit(ch) || ch is '+' or '-' or ' ' or '(' or ')' or '.'))
                throw new BusinessPartnerRuleException(errorCode);
        }
        return trimmed;
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
