namespace ForgeFlow.Domain.Sales;

public sealed class SalesOrderLine
{
    private SalesOrderLine() { }

    private SalesOrderLine(
        long organizationId,
        long salesOrderId,
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
            throw new SalesRuleException(SalesErrors.ItemInvalid);

        OrganizationId = organizationId;
        SalesOrderId = salesOrderId;
        LineNumber = lineNumber;
        ItemId = itemId;
        OrderedQuantity = SalesText.PositiveQuantity(orderedQuantity);
        OrderedUnitId = orderedUnitId;
        BaseUnitIdSnapshot = baseUnitIdSnapshot;
        OrderedBaseQuantity = SalesText.PositiveQuantity(orderedBaseQuantity);
        DispatchedBaseQuantity = 0m;
        UnitPrice = SalesText.OptionalUnitPrice(unitPrice);
        Notes = SalesText.OptionalText(notes, SalesText.MaxLineNotesLength, SalesErrors.NotesInvalid);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SalesOrderId { get; private set; }
    public int LineNumber { get; private set; }
    public long ItemId { get; private set; }
    public decimal OrderedQuantity { get; private set; }
    public long OrderedUnitId { get; private set; }
    public long BaseUnitIdSnapshot { get; private set; }
    public decimal OrderedBaseQuantity { get; private set; }
    public decimal DispatchedBaseQuantity { get; private set; }
    public decimal? UnitPrice { get; private set; }
    public string? Notes { get; private set; }

    public decimal RemainingBaseQuantity =>
        decimal.Round(Math.Max(0m, OrderedBaseQuantity - DispatchedBaseQuantity), 6, MidpointRounding.AwayFromZero);

    internal static SalesOrderLine Create(
        long organizationId,
        long salesOrderId,
        int lineNumber,
        long itemId,
        decimal orderedQuantity,
        long orderedUnitId,
        long baseUnitIdSnapshot,
        decimal orderedBaseQuantity,
        decimal? unitPrice,
        string? notes)
        => new(organizationId, salesOrderId, lineNumber, itemId, orderedQuantity, orderedUnitId, baseUnitIdSnapshot, orderedBaseQuantity, unitPrice, notes);

    internal void ApplyDispatch(decimal dispatchedBaseDelta)
    {
        var delta = SalesText.PositiveQuantity(dispatchedBaseDelta);
        var next = decimal.Round(DispatchedBaseQuantity + delta, 6, MidpointRounding.AwayFromZero);
        if (next > OrderedBaseQuantity)
            throw new SalesRuleException(SalesErrors.OverDispatch);
        DispatchedBaseQuantity = next;
    }

    internal void RevertDispatch(decimal revertedBaseDelta)
    {
        var delta = SalesText.PositiveQuantity(revertedBaseDelta);
        var next = decimal.Round(DispatchedBaseQuantity - delta, 6, MidpointRounding.AwayFromZero);
        if (next < 0m)
            throw new SalesRuleException(SalesErrors.InvalidTransition);
        DispatchedBaseQuantity = next;
    }
}
