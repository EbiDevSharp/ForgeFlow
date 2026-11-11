namespace ForgeFlow.Domain.Inventory;

public enum ReservationStatus : byte
{
    Active = 1,
    PartiallyConsumed = 2,
    Fulfilled = 3,
    Released = 4
}

/// <summary>Hard allocation of available site inventory toward a Released or InProgress Production Order.</summary>
public sealed class StockReservation
{
    private StockReservation() { }

    private StockReservation(
        long organizationId,
        long siteId,
        long warehouseId,
        long productionOrderId,
        long? productionOrderInputId,
        long itemId,
        string? lotCode,
        long baseUnitId,
        decimal reservedBaseQuantity,
        string? notes)
    {
        if (organizationId <= 0) throw new InventoryRuleException(InventoryErrors.ParentOrganizationInactive);
        if (siteId <= 0) throw new InventoryRuleException(InventoryErrors.SiteInvalid);
        if (warehouseId <= 0) throw new InventoryRuleException(InventoryErrors.WarehouseNotFound);
        if (productionOrderId <= 0) throw new InventoryRuleException(InventoryErrors.ProductionOrderInvalid);
        if (itemId <= 0) throw new InventoryRuleException(InventoryErrors.ItemInvalid);
        if (baseUnitId <= 0) throw new InventoryRuleException(InventoryErrors.UnitInvalid);

        OrganizationId = organizationId;
        SiteId = siteId;
        WarehouseId = warehouseId;
        ProductionOrderId = productionOrderId;
        ProductionOrderInputId = productionOrderInputId is > 0 ? productionOrderInputId : null;
        ItemId = itemId;
        LotCode = InventoryText.LotCode(lotCode);
        BaseUnitId = baseUnitId;
        ReservedBaseQuantity = InventoryText.ValidatePositiveQuantity(reservedBaseQuantity);
        ConsumedBaseQuantity = 0m;
        Status = ReservationStatus.Active;
        Notes = InventoryText.OptionalNote(notes);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public long WarehouseId { get; private set; }
    public long ProductionOrderId { get; private set; }
    public long? ProductionOrderInputId { get; private set; }
    public long ItemId { get; private set; }
    public string LotCode { get; private set; } = string.Empty;
    public long BaseUnitId { get; private set; }
    public decimal ReservedBaseQuantity { get; private set; }
    public decimal ConsumedBaseQuantity { get; private set; }
    public ReservationStatus Status { get; private set; } = ReservationStatus.Active;
    public string? Notes { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }

    public decimal RemainingReservedBaseQuantity =>
        Status is ReservationStatus.Fulfilled or ReservationStatus.Released
            ? 0m
            : decimal.Round(ReservedBaseQuantity - ConsumedBaseQuantity, 6, MidpointRounding.AwayFromZero);

    public bool IsOpen => Status is ReservationStatus.Active or ReservationStatus.PartiallyConsumed;

    public static StockReservation Create(
        long organizationId,
        long siteId,
        long warehouseId,
        long productionOrderId,
        long? productionOrderInputId,
        long itemId,
        string? lotCode,
        long baseUnitId,
        decimal reservedBaseQuantity,
        string? notes = null) =>
        new(organizationId, siteId, warehouseId, productionOrderId, productionOrderInputId, itemId, lotCode, baseUnitId, reservedBaseQuantity, notes);

    public decimal Consume(decimal positiveBaseQuantity)
    {
        if (!IsOpen)
            throw new InventoryRuleException(InventoryErrors.ReservationInvalidState);

        var requested = InventoryText.ValidatePositiveQuantity(positiveBaseQuantity);
        var remaining = RemainingReservedBaseQuantity;
        var consumedNow = Math.Min(requested, remaining);
        if (consumedNow <= 0m) return 0m;

        ConsumedBaseQuantity = decimal.Round(ConsumedBaseQuantity + consumedNow, 6, MidpointRounding.AwayFromZero);
        Status = ConsumedBaseQuantity >= ReservedBaseQuantity
            ? ReservationStatus.Fulfilled
            : ReservationStatus.PartiallyConsumed;
        return consumedNow;
    }

    public decimal Release(DateTime utcNow)
    {
        if (!IsOpen)
            throw new InventoryRuleException(InventoryErrors.ReservationInvalidState);

        var remaining = RemainingReservedBaseQuantity;
        Status = ReservationStatus.Released;
        ReleasedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        return remaining;
    }
}
