namespace ForgeFlow.Domain.Units;

internal static class UnitText
{
    internal const int CodeMaxLength = 32;
    internal const int NameMaxLength = 200;
    internal const int SymbolMaxLength = 32;
    internal const byte MaxPrecisionScale = 6;
    internal const decimal MaxFactorValue = 999_999_999_999.999999m;

    internal static string Code(string? raw)
    {
        var value = raw?.Trim(' ');
        if (string.IsNullOrEmpty(value) || value.Length > CodeMaxLength)
            throw new UnitRuleException(UnitErrors.CodeInvalid, ("field", "Code"), ("maxLength", CodeMaxLength));

        var characters = value.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            var c = characters[i];
            if (c is >= 'a' and <= 'z') c = (char)(c - 'a' + 'A');
            if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9') && !(i > 0 && c is '_' or '-'))
                throw new UnitRuleException(UnitErrors.CodeInvalid, ("field", "Code"), ("maxLength", CodeMaxLength));
            characters[i] = c;
        }
        return new string(characters);
    }

    internal static string Name(string? raw) => NormalizeUnicode(raw, NameMaxLength, UnitErrors.NameInvalid, "Name");

    internal static string Symbol(string? raw) => NormalizeUnicode(raw, SymbolMaxLength, UnitErrors.SymbolInvalid, "Symbol");

    internal static decimal ValidateFactor(decimal factor)
    {
        if (factor <= 0m || factor > MaxFactorValue)
            throw new UnitRuleException(UnitErrors.ConversionFactorInvalid, ("field", "Factor"));

        var rounded = decimal.Round(factor, MaxPrecisionScale, MidpointRounding.AwayFromZero);
        if (rounded != factor)
            throw new UnitRuleException(UnitErrors.ConversionFactorInvalid, ("field", "Factor"));

        return factor;
    }

    private static string NormalizeUnicode(string? raw, int maxLength, string errorCode, string fieldName)
    {
        if (raw is null || raw.Any(char.IsControl))
            throw new UnitRuleException(errorCode, ("field", fieldName), ("maxLength", maxLength));

        var value = raw.Trim();
        if (value.Length == 0 || value.Length > maxLength)
            throw new UnitRuleException(errorCode, ("field", fieldName), ("maxLength", maxLength));

        return value;
    }
}
