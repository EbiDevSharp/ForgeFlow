namespace ForgeFlow.Domain.Boms;

public sealed class BomInput
{
    private BomInput() { }

    public BomInput(
        long organizationId,
        long bomVersionId,
        int sequence,
        long itemId,
        decimal quantity,
        long unitId,
        decimal scrapPercentage,
        bool isOptional,
        string? notes)
    {
        if (organizationId <= 0 || bomVersionId <= 0)
            throw new BomRuleException(BomErrors.VersionNotFound);
        if (itemId <= 0)
            throw new BomRuleException(BomErrors.InputItemInvalid);
        if (unitId <= 0)
            throw new BomRuleException(BomErrors.UnitInvalid);
        if (sequence <= 0)
            throw new BomRuleException(BomErrors.QuantityInvalid);

        OrganizationId = organizationId;
        BomVersionId = bomVersionId;
        Sequence = sequence;
        ItemId = itemId;
        Quantity = BomText.ValidateQuantity(quantity, BomErrors.QuantityInvalid);
        UnitId = unitId;
        ScrapPercentage = BomText.ValidateScrap(scrapPercentage);
        IsOptional = isOptional;
        Notes = BomText.OptionalNotes(notes);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long BomVersionId { get; private set; }
    public int Sequence { get; private set; }
    public long ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public long UnitId { get; private set; }
    public decimal ScrapPercentage { get; private set; }
    public bool IsOptional { get; private set; }
    public string? Notes { get; private set; }
}
