namespace ForgeFlow.Domain.Boms;

public sealed class BomOutput
{
    private BomOutput() { }

    public BomOutput(
        long organizationId,
        long bomVersionId,
        int sequence,
        long itemId,
        BomOutputType outputType,
        decimal quantity,
        long unitId)
    {
        if (organizationId <= 0 || bomVersionId <= 0)
            throw new BomRuleException(BomErrors.VersionNotFound);
        if (itemId <= 0)
            throw new BomRuleException(BomErrors.OutputItemInvalid);
        if (!Enum.IsDefined(outputType))
            throw new BomRuleException(BomErrors.OutputItemInvalid);
        if (unitId <= 0)
            throw new BomRuleException(BomErrors.UnitInvalid);
        if (sequence <= 0)
            throw new BomRuleException(BomErrors.QuantityInvalid);

        OrganizationId = organizationId;
        BomVersionId = bomVersionId;
        Sequence = sequence;
        ItemId = itemId;
        OutputType = outputType;
        Quantity = BomText.ValidateQuantity(quantity, BomErrors.QuantityInvalid);
        UnitId = unitId;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long BomVersionId { get; private set; }
    public int Sequence { get; private set; }
    public long ItemId { get; private set; }
    public BomOutputType OutputType { get; private set; }
    public decimal Quantity { get; private set; }
    public long UnitId { get; private set; }
}
