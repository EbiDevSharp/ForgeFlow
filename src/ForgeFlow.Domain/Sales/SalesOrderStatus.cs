namespace ForgeFlow.Domain.Sales;

public enum SalesOrderStatus : byte
{
    Draft = 1,
    Approved = 2,
    PartiallyDispatched = 3,
    Dispatched = 4,
    Cancelled = 5,
    Closed = 6
}
