namespace ForgeFlow.Domain.Inventory;

public sealed class InventoryTransaction
{
    private InventoryTransaction() { }

    private InventoryTransaction(
        long organizationId,
        long siteId,
        long warehouseId,
        long? destinationWarehouseId,
        string documentNumber,
        InventoryTransactionType transactionType,
        long? productionOrderId,
        long? reversedTransactionId,
        long? qualityInspectionId,
        long? purchaseOrderId,
        long? deliveryOrderId,
        string? referenceNote,
        string? idempotencyKey,
        DateTime postedAtUtc)
    {
        if (organizationId <= 0) throw new InventoryRuleException(InventoryErrors.ParentOrganizationInactive);
        if (siteId <= 0) throw new InventoryRuleException(InventoryErrors.SiteInvalid);
        if (warehouseId <= 0) throw new InventoryRuleException(InventoryErrors.WarehouseNotFound);
        if (!Enum.IsDefined(transactionType)) throw new InventoryRuleException(InventoryErrors.TransactionNotFound);

        OrganizationId = organizationId;
        SiteId = siteId;
        WarehouseId = warehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        DocumentNumber = InventoryText.Code(documentNumber, InventoryErrors.DocumentNumberInvalid);
        TransactionType = transactionType;
        ProductionOrderId = productionOrderId;
        ReversedTransactionId = reversedTransactionId;
        QualityInspectionId = qualityInspectionId;
        PurchaseOrderId = purchaseOrderId;
        DeliveryOrderId = deliveryOrderId;
        ReferenceNote = InventoryText.OptionalNote(referenceNote);
        IdempotencyKey = InventoryText.OptionalIdempotencyKey(idempotencyKey);
        PostedAtUtc = postedAtUtc;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public long WarehouseId { get; private set; }
    public long? DestinationWarehouseId { get; private set; }
    public string DocumentNumber { get; private set; } = string.Empty;
    public InventoryTransactionType TransactionType { get; private set; }
    public long? ProductionOrderId { get; private set; }
    public long? ReversedTransactionId { get; private set; }
    public long? QualityInspectionId { get; private set; }
    public long? PurchaseOrderId { get; private set; }
    public long? DeliveryOrderId { get; private set; }
    public string? ReferenceNote { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTime PostedAtUtc { get; private set; }

    public static InventoryTransaction Create(
        long organizationId,
        long siteId,
        long warehouseId,
        long? destinationWarehouseId,
        string? documentNumber,
        InventoryTransactionType transactionType,
        long? productionOrderId,
        long? reversedTransactionId,
        string? referenceNote,
        string? idempotencyKey,
        DateTime postedAtUtc,
        long? qualityInspectionId = null,
        long? purchaseOrderId = null,
        long? deliveryOrderId = null) =>
        new(organizationId, siteId, warehouseId, destinationWarehouseId, documentNumber ?? string.Empty,
            transactionType, productionOrderId, reversedTransactionId, qualityInspectionId, purchaseOrderId, deliveryOrderId, referenceNote, idempotencyKey, postedAtUtc);
}
