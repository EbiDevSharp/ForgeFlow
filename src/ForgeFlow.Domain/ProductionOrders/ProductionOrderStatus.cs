namespace ForgeFlow.Domain.ProductionOrders;

public enum ProductionOrderStatus : byte
{
    Draft = 1,
    Released = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5
}
