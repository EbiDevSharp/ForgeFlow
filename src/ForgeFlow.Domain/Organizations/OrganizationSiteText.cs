namespace ForgeFlow.Domain.Organizations;

internal static class OrganizationSiteText
{
    internal const int CodeMaxLength = 32;
    internal const int NameMaxLength = 200;

    internal static string Code(string? raw, string error)
    {
        // ASCII spaces/case only: never transliterate, normalize Unicode, or use the current culture.
        var value = raw?.Trim(' ');
        if (string.IsNullOrEmpty(value) || value.Length > CodeMaxLength)
            throw new OrganizationSiteRuleException(error, ("field", "Code"), ("maxLength", CodeMaxLength));

        var characters = value.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            var c = characters[i];
            if (c is >= 'a' and <= 'z') c = (char)(c - 'a' + 'A');
            if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9') && !(i > 0 && c is '_' or '-'))
                throw new OrganizationSiteRuleException(error, ("field", "Code"), ("maxLength", CodeMaxLength));
            characters[i] = c;
        }
        return new string(characters);
    }

    internal static string Name(string? raw, string error)
    {
        // Check before trimming, so leading/trailing controls (including tabs/newlines) cannot disappear.
        if (raw is null || raw.Any(char.IsControl))
            throw new OrganizationSiteRuleException(error, ("field", "Name"), ("maxLength", NameMaxLength));
        var value = raw.Trim();
        if (value.Length is 0 or > NameMaxLength)
            throw new OrganizationSiteRuleException(error, ("field", "Name"), ("maxLength", NameMaxLength));
        return value;
    }
}
