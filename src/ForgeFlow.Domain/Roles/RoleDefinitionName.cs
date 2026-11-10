namespace ForgeFlow.Domain.Roles;

/// <summary>Validates custom role names cleanly without culture-dependent casing bugs.</summary>
public readonly record struct RoleDefinitionName(string Value, string Normalized)
{
    public const int MaxLength = 100;

    public static RoleDefinitionName Parse(string? raw)
    {
        if (raw is null || raw.Any(char.IsControl))
            throw new RoleRuleException(RolePermissionErrors.NameInvalid, ("field", "Name"), ("maxLength", MaxLength));

        var trimmed = raw.Trim();
        if (trimmed.Length is 0 or > MaxLength)
            throw new RoleRuleException(RolePermissionErrors.NameInvalid, ("field", "Name"), ("maxLength", MaxLength));

        return new RoleDefinitionName(trimmed, trimmed.ToUpperInvariant());
    }
}
