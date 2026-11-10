using ForgeFlow.Domain.Boms;

namespace ForgeFlow.Domain.ProductionOrders;

public sealed class ProductionOrderInput
{
    private ProductionOrderInput() { }

    public ProductionOrderInput(
        long organizationId,
        long productionOrderId,
        int level,
        int sequence,
        long parentItemId,
        long itemId,
        decimal netQuantity,
        decimal scrapPercentage,
        decimal grossQuantity,
        long unitId,
        bool isOptional,
        bool isSubAssembly,
        long? resolvedSubBomVersionId)
    {
        if (organizationId <= 0 || productionOrderId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.NotFound);
        if (level <= 0 || sequence <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.QuantityInvalid);
        if (parentItemId <= 0 || itemId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.ItemInvalid);
        if (unitId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.UnitInvalid);

        OrganizationId = organizationId;
        ProductionOrderId = productionOrderId;
        Level = level;
        Sequence = sequence;
        ParentItemId = parentItemId;
        ItemId = itemId;
        NetQuantity = ProductionOrderText.ValidatePositiveQuantity(netQuantity);
        ScrapPercentage = BomText.ValidateScrap(scrapPercentage);
        GrossQuantity = ProductionOrderText.ValidatePositiveQuantity(grossQuantity);
        UnitId = unitId;
        IsOptional = isOptional;
        IsSubAssembly = isSubAssembly;
        ResolvedSubBomVersionId = resolvedSubBomVersionId;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long ProductionOrderId { get; private set; }
    public int Level { get; private set; }
    public int Sequence { get; private set; }
    public long ParentItemId { get; private set; }
    public long ItemId { get; private set; }
    public decimal NetQuantity { get; private set; }
    public decimal ScrapPercentage { get; private set; }
    public decimal GrossQuantity { get; private set; }
    public long UnitId { get; private set; }
    public bool IsOptional { get; private set; }
    public bool IsSubAssembly { get; private set; }
    public long? ResolvedSubBomVersionId { get; private set; }
}
