using ForgeFlow.Domain.Units;

namespace ForgeFlow.Domain.Items;

internal static class ItemText
{
    internal const int CodeMaxLength = 32;
    internal const int NameMaxLength = 200;
    internal const int DescriptionMaxLength = 500;
    internal const int AttributeTextMaxLength = 400;

    internal static string Code(string? raw)
    {
        var value = raw?.Trim(' ');
        if (string.IsNullOrEmpty(value) || value.Length > CodeMaxLength)
            throw new ItemRuleException(ItemErrors.CodeInvalid, ("field", "Code"), ("maxLength", CodeMaxLength));

        var characters = value.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            var c = characters[i];
            if (c is >= 'a' and <= 'z') c = (char)(c - 'a' + 'A');
            if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9') && !(i > 0 && c is '_' or '-'))
                throw new ItemRuleException(ItemErrors.CodeInvalid, ("field", "Code"), ("maxLength", CodeMaxLength));
            characters[i] = c;
        }
        return new string(characters);
    }

    internal static string Name(string? raw)
    {
        if (raw is null || raw.Any(char.IsControl))
            throw new ItemRuleException(ItemErrors.NameInvalid, ("field", "Name"), ("maxLength", NameMaxLength));

        var value = raw.Trim();
        if (value.Length == 0 || value.Length > NameMaxLength)
            throw new ItemRuleException(ItemErrors.NameInvalid, ("field", "Name"), ("maxLength", NameMaxLength));

        return value;
    }

    internal static string? OptionalText(string? raw, int maxLength, string errorCode, string fieldName)
    {
        if (raw is null) return null;
        if (raw.Any(char.IsControl))
            throw new ItemRuleException(errorCode, ("field", fieldName), ("maxLength", maxLength));

        var value = raw.Trim();
        if (value.Length == 0) return null;
        if (value.Length > maxLength)
            throw new ItemRuleException(errorCode, ("field", fieldName), ("maxLength", maxLength));

        return value;
    }

    internal static decimal ValidateFactor(decimal factor)
    {
        if (factor <= 0m || factor > UnitText.MaxFactorValue)
            throw new ItemRuleException(UnitErrors.ConversionFactorInvalid, ("field", "Factor"));

        var rounded = decimal.Round(factor, UnitText.MaxPrecisionScale, MidpointRounding.AwayFromZero);
        if (rounded != factor)
            throw new ItemRuleException(UnitErrors.ConversionFactorInvalid, ("field", "Factor"));

        return factor;
    }
}
