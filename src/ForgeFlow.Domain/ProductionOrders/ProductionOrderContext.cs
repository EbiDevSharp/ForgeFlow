namespace ForgeFlow.Domain.ProductionOrders;

public sealed class ProductionOrderContext
{
    private ProductionOrderContext() { }

    public ProductionOrderContext(
        long organizationId,
        long productionOrderId,
        string dimensionCode,
        string dimensionValue)
    {
        if (organizationId <= 0 || productionOrderId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.NotFound);

        var trimmedValue = dimensionValue?.Trim() ?? string.Empty;
        if (trimmedValue.Length == 0 || trimmedValue.Length > ProductionOrderText.MaxContextValueLength || trimmedValue.Any(char.IsControl))
            throw new ProductionOrderRuleException(ProductionOrderErrors.NotesInvalid);

        OrganizationId = organizationId;
        ProductionOrderId = productionOrderId;
        DimensionCode = ProductionOrderText.OrderNumber(dimensionCode);
        DimensionValue = trimmedValue;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long ProductionOrderId { get; private set; }
    public string DimensionCode { get; private set; } = string.Empty;
    public string DimensionValue { get; private set; } = string.Empty;
}
