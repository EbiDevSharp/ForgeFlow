namespace ForgeFlow.Domain.Users;

/// <summary>Canonicalizes and validates ASCII account usernames identically across fa-IR and en-US locales.</summary>
public readonly record struct UserAccountName(string Value, string Normalized)
{
    public const int MaxLength = 128;

    public static UserAccountName Parse(string? raw)
    {
        var value = raw?.Trim(' ');
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            throw new UserRuleException(UserManagementErrors.UserNameInvalid, ("field", "UserName"), ("maxLength", MaxLength));

        var normalized = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var allowed = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '@' or '+' or '-';
            if (!allowed)
                throw new UserRuleException(UserManagementErrors.UserNameInvalid, ("field", "UserName"), ("maxLength", MaxLength));

            normalized[i] = c is >= 'a' and <= 'z' ? (char)(c - 'a' + 'A') : c;
        }

        return new UserAccountName(value, new string(normalized));
    }
}
