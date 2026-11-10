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

    public void ApplyDelta(decimal baseQuantityDelta, DateTime movedAtUtc)
    {
        var next = decimal.Round(OnHandBaseQuantity + baseQuantityDelta, 6, MidpointRounding.AwayFromZero);
        if (next < 0m)
            throw new InventoryRuleException(InventoryErrors.InsufficientStock, ("available", OnHandBaseQuantity), ("requestedDelta", baseQuantityDelta));

        OnHandBaseQuantity = next;
        LastMovedAtUtc = movedAtUtc;
    }
}
