namespace ForgeFlow.Domain.Inventory;

public enum InventoryTransactionType : byte
{
    Receipt = 1,
    Issue = 2,
    Adjustment = 3,
    TransferOut = 4,
    TransferIn = 5,
    ProductionIssue = 6,
    ProductionReceipt = 7,
    Reversal = 8,
    QualityScrap = 9
}
