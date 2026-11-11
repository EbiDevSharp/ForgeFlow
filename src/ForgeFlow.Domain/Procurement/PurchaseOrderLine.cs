namespace ForgeFlow.Domain.Procurement;

public sealed class PurchaseOrderLine
{
    private PurchaseOrderLine() { }

    private PurchaseOrderLine(
        long organizationId,
        long purchaseOrderId,
        int lineNumber,
        long itemId,
        decimal orderedQuantity,
        long orderedUnitId,
        long baseUnitIdSnapshot,
        decimal orderedBaseQuantity,
        decimal? unitPrice,
        string? notes)
    {
        if (organizationId <= 0 || lineNumber <= 0 || itemId <= 0 || orderedUnitId <= 0 || baseUnitIdSnapshot <= 0)
            throw new ProcurementRuleException(ProcurementErrors.ItemInvalid);

        OrganizationId = organizationId;
        PurchaseOrderId = purchaseOrderId;
        LineNumber = lineNumber;
        ItemId = itemId;
        OrderedQuantity = ProcurementText.PositiveQuantity(orderedQuantity);
        OrderedUnitId = orderedUnitId;
        BaseUnitIdSnapshot = baseUnitIdSnapshot;
        OrderedBaseQuantity = ProcurementText.PositiveQuantity(orderedBaseQuantity);
        ReceivedBaseQuantity = 0m;
        UnitPrice = ProcurementText.OptionalUnitPrice(unitPrice);
        Notes = ProcurementText.OptionalText(notes, ProcurementText.MaxLineNotesLength, ProcurementErrors.NotesInvalid);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long PurchaseOrderId { get; private set; }
    public int LineNumber { get; private set; }
    public long ItemId { get; private set; }
    public decimal OrderedQuantity { get; private set; }
    public long OrderedUnitId { get; private set; }
    public long BaseUnitIdSnapshot { get; private set; }
    public decimal OrderedBaseQuantity { get; private set; }
    public decimal ReceivedBaseQuantity { get; private set; }
    public decimal? UnitPrice { get; private set; }
    public string? Notes { get; private set; }

    public decimal RemainingBaseQuantity =>
        decimal.Round(Math.Max(0m, OrderedBaseQuantity - ReceivedBaseQuantity), 6, MidpointRounding.AwayFromZero);

    internal static PurchaseOrderLine Create(
        long organizationId,
        long purchaseOrderId,
        int lineNumber,
        long itemId,
        decimal orderedQuantity,
        long orderedUnitId,
        long baseUnitIdSnapshot,
        decimal orderedBaseQuantity,
        decimal? unitPrice,
        string? notes)
        => new(organizationId, purchaseOrderId, lineNumber, itemId, orderedQuantity, orderedUnitId, baseUnitIdSnapshot, orderedBaseQuantity, unitPrice, notes);

    internal void ApplyReceipt(decimal receivedBaseDelta)
    {
        var delta = ProcurementText.PositiveQuantity(receivedBaseDelta);
        var next = decimal.Round(ReceivedBaseQuantity + delta, 6, MidpointRounding.AwayFromZero);
        if (next > OrderedBaseQuantity)
            throw new ProcurementRuleException(ProcurementErrors.OverReceipt);
        ReceivedBaseQuantity = next;
    }

    internal void RevertReceipt(decimal revertedBaseDelta)
    {
        var delta = ProcurementText.PositiveQuantity(revertedBaseDelta);
        var next = decimal.Round(ReceivedBaseQuantity - delta, 6, MidpointRounding.AwayFromZero);
        if (next < 0m)
            throw new ProcurementRuleException(ProcurementErrors.InvalidTransition);
        ReceivedBaseQuantity = next;
    }
}
