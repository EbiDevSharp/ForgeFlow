namespace ForgeFlow.Domain.Inventory;

public sealed class InventoryBalance
{
    private InventoryBalance() { }

    private InventoryBalance(
        long organizationId,
        long siteId,
        long warehouseId,
        long itemId,
        string lotCode,
        long baseUnitId,
        DateTime createdAtUtc)
    {
        if (organizationId <= 0) throw new InventoryRuleException(InventoryErrors.ParentOrganizationInactive);
        if (siteId <= 0) throw new InventoryRuleException(InventoryErrors.SiteInvalid);
        if (warehouseId <= 0) throw new InventoryRuleException(InventoryErrors.WarehouseNotFound);
        if (itemId <= 0) throw new InventoryRuleException(InventoryErrors.ItemInvalid);
        if (baseUnitId <= 0) throw new InventoryRuleException(InventoryErrors.UnitInvalid);

        OrganizationId = organizationId;
        SiteId = siteId;
        WarehouseId = warehouseId;
        ItemId = itemId;
        LotCode = InventoryText.LotCode(lotCode);
        BaseUnitId = baseUnitId;
        OnHandBaseQuantity = 0m;
        BlockedBaseQuantity = 0m;
        ReservedBaseQuantity = 0m;
        LastMovedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public long WarehouseId { get; private set; }
    public long ItemId { get; private set; }
    public string LotCode { get; private set; } = string.Empty;
    public long BaseUnitId { get; private set; }
    public decimal OnHandBaseQuantity { get; private set; }
    public decimal BlockedBaseQuantity { get; private set; }
    public decimal ReservedBaseQuantity { get; private set; }
    public decimal AvailableBaseQuantity => decimal.Round(OnHandBaseQuantity - BlockedBaseQuantity - ReservedBaseQuantity, 6, MidpointRounding.AwayFromZero);
    public DateTime LastMovedAtUtc { get; private set; }

    public static InventoryBalance CreateZero(
        long organizationId,
        long siteId,
        long warehouseId,
        long itemId,
        string? lotCode,
        long baseUnitId,
        DateTime createdAtUtc) =>
        new(organizationId, siteId, warehouseId, itemId, lotCode ?? string.Empty, baseUnitId, createdAtUtc);

    public void ApplyDelta(decimal baseQuantityDelta, DateTime movedAtUtc, bool enforceAvailableOnOutbound = true)
    {
        var next = decimal.Round(OnHandBaseQuantity + baseQuantityDelta, 6, MidpointRounding.AwayFromZero);
        if (next < 0m)
            throw new InventoryRuleException(InventoryErrors.InsufficientStock, ("available", AvailableBaseQuantity), ("requestedDelta", baseQuantityDelta));

        if (enforceAvailableOnOutbound && baseQuantityDelta < 0m)
        {
            var nextAvailable = decimal.Round(AvailableBaseQuantity + baseQuantityDelta, 6, MidpointRounding.AwayFromZero);
            if (nextAvailable < 0m)
                throw new InventoryRuleException(InventoryErrors.InsufficientStock, ("available", AvailableBaseQuantity), ("blocked", BlockedBaseQuantity), ("requestedDelta", baseQuantityDelta));
        }

        if (next < decimal.Round(BlockedBaseQuantity + ReservedBaseQuantity, 6, MidpointRounding.AwayFromZero))
            throw new InventoryRuleException(InventoryErrors.InsufficientStock, ("available", AvailableBaseQuantity), ("blocked", BlockedBaseQuantity), ("requestedDelta", baseQuantityDelta));

        OnHandBaseQuantity = next;
        LastMovedAtUtc = movedAtUtc;
    }

    public void BlockQuantity(decimal positiveBaseQuantity, DateTime movedAtUtc)
    {
        var qty = InventoryText.ValidatePositiveQuantity(positiveBaseQuantity);
        var nextBlocked = decimal.Round(BlockedBaseQuantity + qty, 6, MidpointRounding.AwayFromZero);
        if (nextBlocked + ReservedBaseQuantity > OnHandBaseQuantity)
            throw new InventoryRuleException(InventoryErrors.InsufficientStock, ("available", AvailableBaseQuantity), ("requestedDelta", -qty));

        BlockedBaseQuantity = nextBlocked;
        LastMovedAtUtc = movedAtUtc;
    }

    public void ReserveQuantity(decimal positiveBaseQuantity, DateTime movedAtUtc)
    {
        var qty = InventoryText.ValidatePositiveQuantity(positiveBaseQuantity);
        if (qty > AvailableBaseQuantity)
            throw new InventoryRuleException(InventoryErrors.ReservationExceedsAvailable, ("available", AvailableBaseQuantity), ("requested", qty));

        ReservedBaseQuantity = decimal.Round(ReservedBaseQuantity + qty, 6, MidpointRounding.AwayFromZero);
        LastMovedAtUtc = movedAtUtc;
    }

    public void ReleaseReservedQuantity(decimal positiveBaseQuantity, DateTime movedAtUtc)
    {
        var qty = InventoryText.ValidatePositiveQuantity(positiveBaseQuantity);
        var nextReserved = decimal.Round(ReservedBaseQuantity - qty, 6, MidpointRounding.AwayFromZero);
        if (nextReserved < 0m)
            throw new InventoryRuleException(InventoryErrors.QuantityInvalid);

        ReservedBaseQuantity = nextReserved;
        LastMovedAtUtc = movedAtUtc;
    }

    public void ReleaseBlockedQuantity(decimal positiveBaseQuantity, DateTime movedAtUtc)
    {
        var qty = InventoryText.ValidatePositiveQuantity(positiveBaseQuantity);
        var nextBlocked = decimal.Round(BlockedBaseQuantity - qty, 6, MidpointRounding.AwayFromZero);
        if (nextBlocked < 0m)
            throw new InventoryRuleException(InventoryErrors.QuantityInvalid);

        BlockedBaseQuantity = nextBlocked;
        LastMovedAtUtc = movedAtUtc;
    }

    public void ScrapBlockedQuantity(decimal positiveBaseQuantity, DateTime movedAtUtc)
    {
        var qty = InventoryText.ValidatePositiveQuantity(positiveBaseQuantity);
        var nextBlocked = decimal.Round(BlockedBaseQuantity - qty, 6, MidpointRounding.AwayFromZero);
        var nextOnHand = decimal.Round(OnHandBaseQuantity - qty, 6, MidpointRounding.AwayFromZero);
        if (nextBlocked < 0m || nextOnHand < 0m)
            throw new InventoryRuleException(InventoryErrors.InsufficientStock, ("available", AvailableBaseQuantity), ("blocked", BlockedBaseQuantity), ("requestedDelta", -qty));

        BlockedBaseQuantity = nextBlocked;
        OnHandBaseQuantity = nextOnHand;
        LastMovedAtUtc = movedAtUtc;
    }
}
