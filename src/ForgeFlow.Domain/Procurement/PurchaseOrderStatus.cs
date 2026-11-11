namespace ForgeFlow.Domain.Procurement;

public enum PurchaseOrderStatus : byte
{
    Draft = 1,
    Approved = 2,
    PartiallyReceived = 3,
    Received = 4,
    Cancelled = 5,
    Closed = 6
}
