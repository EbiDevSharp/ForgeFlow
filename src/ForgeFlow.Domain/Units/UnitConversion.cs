namespace ForgeFlow.Domain.Units;

/// <summary>Organization-owned directed conversion rule: 1 FromUnit = Factor * ToUnit.</summary>
public sealed class UnitConversion
{
    private UnitConversion() { }

    private UnitConversion(long organizationId, long fromUnitId, long toUnitId, decimal factor)
    {
        if (organizationId <= 0 || fromUnitId <= 0 || toUnitId <= 0)
            throw new UnitRuleException(UnitErrors.NotFound);
        if (fromUnitId == toUnitId)
            throw new UnitRuleException(UnitErrors.ConversionSameUnit);

        OrganizationId = organizationId;
        FromUnitId = fromUnitId;
        ToUnitId = toUnitId;
        Factor = UnitText.ValidateFactor(factor);
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long FromUnitId { get; private set; }
    public long ToUnitId { get; private set; }
    public decimal Factor { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static UnitConversion Create(long organizationId, long fromUnitId, long toUnitId, decimal factor)
        => new(organizationId, fromUnitId, toUnitId, factor);

    public bool UpdateFactor(decimal factor)
    {
        if (!IsActive)
            throw new UnitRuleException(UnitErrors.UnitInactive);

        var validated = UnitText.ValidateFactor(factor);
        if (Factor == validated) return false;
        Factor = validated;
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
