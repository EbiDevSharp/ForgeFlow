namespace ForgeFlow.Domain.Items;

/// <summary>Extensible industry-independent item property definition (e.g., MFI, Density, Viscosity, Purity).</summary>
public sealed class AttributeDefinition
{
    private AttributeDefinition()
    {
        Code = null!;
        Name = null!;
    }

    private AttributeDefinition(long organizationId, string? code, string? name, AttributeDataType dataType, long? unitId, bool isRequired)
    {
        if (organizationId <= 0)
            throw new ItemRuleException(ItemErrors.NotFound);
        if (!Enum.IsDefined(dataType))
            throw new ItemRuleException(ItemErrors.AttributeDataTypeInvalid, ("field", "DataType"));
        if (unitId is <= 0)
            throw new ItemRuleException(ItemErrors.AttributeUnitNotFound, ("field", "UnitId"));

        OrganizationId = organizationId;
        Code = ItemText.Code(code);
        Name = ItemText.Name(name);
        DataType = dataType;
        UnitId = dataType == AttributeDataType.Decimal ? unitId : null;
        IsRequired = isRequired;
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public AttributeDataType DataType { get; private set; }
    public long? UnitId { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static AttributeDefinition Create(long organizationId, string? code, string? name, AttributeDataType dataType, long? unitId, bool isRequired)
        => new(organizationId, code, name, dataType, unitId, isRequired);

    public bool Update(string? name, long? unitId, bool isRequired)
    {
        if (!IsActive)
            throw new ItemRuleException(ItemErrors.AttributeValueInvalid);
        if (unitId is <= 0)
            throw new ItemRuleException(ItemErrors.AttributeUnitNotFound, ("field", "UnitId"));

        var normalizedName = ItemText.Name(name);
        var normalizedUnitId = DataType == AttributeDataType.Decimal ? unitId : null;
        if (Name == normalizedName && UnitId == normalizedUnitId && IsRequired == isRequired)
            return false;

        Name = normalizedName;
        UnitId = normalizedUnitId;
        IsRequired = isRequired;
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
