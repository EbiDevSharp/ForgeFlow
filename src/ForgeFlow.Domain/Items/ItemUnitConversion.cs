using ForgeFlow.Domain.Units;

namespace ForgeFlow.Domain.Items;

/// <summary>Item-specific directed conversion factor (e.g., 1 BOX = 12 BOTTLE for Item X), integrating with global UnitConversion (A08).</summary>
public sealed class ItemUnitConversion
{
    private ItemUnitConversion() { }

    private ItemUnitConversion(long organizationId, long itemId, long fromUnitId, long toUnitId, decimal factor)
    {
        if (organizationId <= 0 || itemId <= 0 || fromUnitId <= 0 || toUnitId <= 0)
            throw new ItemRuleException(ItemErrors.NotFound);
        if (fromUnitId == toUnitId)
            throw new UnitRuleException(UnitErrors.ConversionSameUnit);

        OrganizationId = organizationId;
        ItemId = itemId;
        FromUnitId = fromUnitId;
        ToUnitId = toUnitId;
        Factor = ItemText.ValidateFactor(factor);
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long ItemId { get; private set; }
    public long FromUnitId { get; private set; }
    public long ToUnitId { get; private set; }
    public decimal Factor { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static ItemUnitConversion Create(long organizationId, long itemId, long fromUnitId, long toUnitId, decimal factor)
        => new(organizationId, itemId, fromUnitId, toUnitId, factor);

    public bool UpdateFactor(decimal factor)
    {
        if (!IsActive)
            throw new UnitRuleException(UnitErrors.UnitInactive);

        var validated = ItemText.ValidateFactor(factor);
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
