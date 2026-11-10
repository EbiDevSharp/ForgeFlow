namespace ForgeFlow.Domain.Items;

/// <summary>Hierarchical taxonomy node inside an organization's ClassificationSet.</summary>
public sealed class Classification
{
    private Classification()
    {
        Code = null!;
        Name = null!;
    }

    private Classification(long organizationId, long classificationSetId, long? parentClassificationId, string? code, string? name)
    {
        if (organizationId <= 0 || classificationSetId <= 0)
            throw new ItemRuleException(ItemErrors.ClassificationSetNotFound);
        if (parentClassificationId is <= 0)
            throw new ItemRuleException(ItemErrors.ClassificationParentInvalid);

        OrganizationId = organizationId;
        ClassificationSetId = classificationSetId;
        ParentClassificationId = parentClassificationId;
        Code = ItemText.Code(code);
        Name = ItemText.Name(name);
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long ClassificationSetId { get; private set; }
    public long? ParentClassificationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Classification Create(long organizationId, long classificationSetId, long? parentClassificationId, string? code, string? name)
        => new(organizationId, classificationSetId, parentClassificationId, code, name);

    public bool Update(string? name, long? parentClassificationId)
    {
        if (!IsActive)
            throw new ItemRuleException(ItemErrors.ClassificationInactive);
        if (parentClassificationId is <= 0)
            throw new ItemRuleException(ItemErrors.ClassificationParentInvalid);
        if (parentClassificationId == Id)
            throw new ItemRuleException(ItemErrors.ClassificationCircularParent);

        var normalizedName = ItemText.Name(name);
        if (Name == normalizedName && ParentClassificationId == parentClassificationId)
            return false;

        Name = normalizedName;
        ParentClassificationId = parentClassificationId;
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
