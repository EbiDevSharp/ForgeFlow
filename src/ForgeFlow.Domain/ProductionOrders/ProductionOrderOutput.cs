using ForgeFlow.Domain.Boms;

namespace ForgeFlow.Domain.ProductionOrders;

public sealed class ProductionOrderOutput
{
    private ProductionOrderOutput() { }

    public ProductionOrderOutput(
        long organizationId,
        long productionOrderId,
        int level,
        int sequence,
        long itemId,
        BomOutputType outputType,
        decimal plannedQuantity,
        long unitId)
    {
        if (organizationId <= 0 || productionOrderId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.NotFound);
        if (level <= 0 || sequence <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.QuantityInvalid);
        if (itemId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.ItemInvalid);
        if (unitId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.UnitInvalid);
        if (!Enum.IsDefined(outputType))
            throw new ProductionOrderRuleException(ProductionOrderErrors.QuantityInvalid);

        OrganizationId = organizationId;
        ProductionOrderId = productionOrderId;
        Level = level;
        Sequence = sequence;
        ItemId = itemId;
        OutputType = outputType;
        PlannedQuantity = ProductionOrderText.ValidatePositiveQuantity(plannedQuantity);
        UnitId = unitId;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long ProductionOrderId { get; private set; }
    public int Level { get; private set; }
    public int Sequence { get; private set; }
    public long ItemId { get; private set; }
    public BomOutputType OutputType { get; private set; }
    public decimal PlannedQuantity { get; private set; }
    public long UnitId { get; private set; }
}
