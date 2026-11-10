using ForgeFlow.Domain.Units;

namespace ForgeFlow.Domain.Boms;

internal static class BomText
{
    internal const int CodeMaxLength = 32;
    internal const int NameMaxLength = 200;
    internal const int NotesMaxLength = 500;
    internal const int ConditionValueMaxLength = 400;

    internal static string Code(string? raw)
    {
        var value = raw?.Trim(' ');
        if (string.IsNullOrEmpty(value) || value.Length > CodeMaxLength)
            throw new BomRuleException(BomErrors.CodeInvalid, ("field", "Code"), ("maxLength", CodeMaxLength));

        var characters = value.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            var c = characters[i];
            if (c is >= 'a' and <= 'z') c = (char)(c - 'a' + 'A');
            if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9') && !(i > 0 && c is '_' or '-'))
                throw new BomRuleException(BomErrors.CodeInvalid, ("field", "Code"), ("maxLength", CodeMaxLength));
            characters[i] = c;
        }
        return new string(characters);
    }

    internal static string Name(string? raw)
    {
        if (raw is null || raw.Any(char.IsControl))
            throw new BomRuleException(BomErrors.NameInvalid, ("field", "Name"), ("maxLength", NameMaxLength));

        var value = raw.Trim();
        if (value.Length == 0 || value.Length > NameMaxLength)
            throw new BomRuleException(BomErrors.NameInvalid, ("field", "Name"), ("maxLength", NameMaxLength));

        return value;
    }

    internal static string? OptionalNotes(string? raw)
    {
        if (raw is null) return null;
        if (raw.Any(char.IsControl))
            throw new BomRuleException(BomErrors.NameInvalid, ("field", "Notes"), ("maxLength", NotesMaxLength));
        var value = raw.Trim();
        if (value.Length == 0) return null;
        if (value.Length > NotesMaxLength)
            throw new BomRuleException(BomErrors.NameInvalid, ("field", "Notes"), ("maxLength", NotesMaxLength));
        return value;
    }

    internal static decimal ValidateQuantity(decimal qty, string errorCode)
    {
        if (qty <= 0m || qty > UnitText.MaxFactorValue)
            throw new BomRuleException(errorCode);
        if (decimal.Round(qty, UnitText.MaxPrecisionScale, MidpointRounding.AwayFromZero) != qty)
            throw new BomRuleException(errorCode);
        return qty;
    }

    internal static decimal ValidateScrap(decimal scrap)
    {
        if (scrap < 0m || scrap >= 100m)
            throw new BomRuleException(BomErrors.ScrapInvalid);
        if (decimal.Round(scrap, 2, MidpointRounding.AwayFromZero) != scrap)
            throw new BomRuleException(BomErrors.ScrapInvalid);
        return scrap;
    }
}
