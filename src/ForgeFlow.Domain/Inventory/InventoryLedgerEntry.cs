namespace ForgeFlow.Domain.Inventory;

public sealed class InventoryLedgerEntry
{
    private InventoryLedgerEntry() { }

    private InventoryLedgerEntry(
        long organizationId,
        long inventoryTransactionId,
        long siteId,
        long warehouseId,
        int lineNumber,
        long itemId,
        string lotCode,
        decimal enteredQuantity,
        long enteredUnitId,
        decimal baseQuantityDelta,
        long baseUnitId,
        DateTime occurredAtUtc,
        string? notes)
    {
        if (organizationId <= 0) throw new InventoryRuleException(InventoryErrors.ParentOrganizationInactive);
        if (inventoryTransactionId <= 0) throw new InventoryRuleException(InventoryErrors.TransactionNotFound);
        if (siteId <= 0) throw new InventoryRuleException(InventoryErrors.SiteInvalid);
        if (warehouseId <= 0) throw new InventoryRuleException(InventoryErrors.WarehouseNotFound);
        if (lineNumber <= 0) throw new InventoryRuleException(InventoryErrors.LinesRequired);
        if (itemId <= 0) throw new InventoryRuleException(InventoryErrors.ItemInvalid);
        if (enteredUnitId <= 0 || baseUnitId <= 0) throw new InventoryRuleException(InventoryErrors.UnitInvalid);

        OrganizationId = organizationId;
        InventoryTransactionId = inventoryTransactionId;
        SiteId = siteId;
        WarehouseId = warehouseId;
        LineNumber = lineNumber;
        ItemId = itemId;
        LotCode = InventoryText.LotCode(lotCode);
        EnteredQuantity = InventoryText.ValidateNonZeroQuantity(enteredQuantity);
        EnteredUnitId = enteredUnitId;
        BaseQuantityDelta = InventoryText.ValidateNonZeroQuantity(baseQuantityDelta);
        BaseUnitId = baseUnitId;
        OccurredAtUtc = occurredAtUtc;
        Notes = InventoryText.OptionalNote(notes, InventoryText.MaxLineNoteLength);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long InventoryTransactionId { get; private set; }
    public long SiteId { get; private set; }
    public long WarehouseId { get; private set; }
    public int LineNumber { get; private set; }
    public long ItemId { get; private set; }
    public string LotCode { get; private set; } = string.Empty;
    public decimal EnteredQuantity { get; private set; }
    public long EnteredUnitId { get; private set; }
    public decimal BaseQuantityDelta { get; private set; }
    public long BaseUnitId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? Notes { get; private set; }

    public static InventoryLedgerEntry Create(
        long organizationId,
        long inventoryTransactionId,
        long siteId,
        long warehouseId,
        int lineNumber,
        long itemId,
        string? lotCode,
        decimal enteredQuantity,
        long enteredUnitId,
        decimal baseQuantityDelta,
        long baseUnitId,
        DateTime occurredAtUtc,
        string? notes) =>
        new(organizationId, inventoryTransactionId, siteId, warehouseId, lineNumber, itemId,
            lotCode ?? string.Empty, enteredQuantity, enteredUnitId, baseQuantityDelta, baseUnitId, occurredAtUtc, notes);
}
