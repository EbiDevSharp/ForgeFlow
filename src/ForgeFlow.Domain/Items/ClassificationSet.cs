namespace ForgeFlow.Domain.Items;

/// <summary>Organization-owned classification scheme/taxonomy root (e.g., Product Line, Material Family).</summary>
public sealed class ClassificationSet
{
    private ClassificationSet()
    {
        Code = null!;
        Name = null!;
    }

    private ClassificationSet(long organizationId, string? code, string? name, bool isMandatory, bool allowMultiple)
    {
        if (organizationId <= 0)
            throw new ItemRuleException(ItemErrors.NotFound);

        OrganizationId = organizationId;
        Code = ItemText.Code(code);
        Name = ItemText.Name(name);
        IsMandatory = isMandatory;
        AllowMultiple = allowMultiple;
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsMandatory { get; private set; }
    public bool AllowMultiple { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static ClassificationSet Create(long organizationId, string? code, string? name, bool isMandatory, bool allowMultiple)
        => new(organizationId, code, name, isMandatory, allowMultiple);

    public bool Update(string? name, bool isMandatory, bool allowMultiple)
    {
        if (!IsActive)
            throw new ItemRuleException(ItemErrors.ClassificationInactive);

        var normalizedName = ItemText.Name(name);
        if (Name == normalizedName && IsMandatory == isMandatory && AllowMultiple == allowMultiple)
            return false;

        Name = normalizedName;
        IsMandatory = isMandatory;
        AllowMultiple = allowMultiple;
        return true;
    }

    public bool Activate()
    {
        if (IsActive) return false;
        IsActive = true;
        return true;
    }

    public bool Deactivate()
    {
        if (!IsActive) return false;
        IsActive = false;
        return true;
    }
}
