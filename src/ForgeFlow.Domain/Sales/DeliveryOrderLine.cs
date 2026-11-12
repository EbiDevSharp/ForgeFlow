namespace ForgeFlow.Domain.Sales;

public sealed class DeliveryOrderLine
{
    private DeliveryOrderLine()
    {
        LotCode = string.Empty;
    }

    private DeliveryOrderLine(
        long organizationId,
        long deliveryOrderId,
        long salesOrderLineId,
        int lineNumber,
        long itemId,
        string? lotCode,
        decimal dispatchedBaseQuantity,
        long baseUnitIdSnapshot,
        long? qualityInspectionId,
        string? certificateNumberSnapshot,
        string? notes)
    {
        if (organizationId <= 0 || salesOrderLineId <= 0 || lineNumber <= 0 || itemId <= 0 || baseUnitIdSnapshot <= 0)
            throw new SalesRuleException(SalesErrors.ItemInvalid);

        OrganizationId = organizationId;
        DeliveryOrderId = deliveryOrderId;
        SalesOrderLineId = salesOrderLineId;
        LineNumber = lineNumber;
        ItemId = itemId;
        LotCode = SalesText.RequiredLotCode(lotCode);
        DispatchedBaseQuantity = SalesText.PositiveQuantity(dispatchedBaseQuantity);
        BaseUnitIdSnapshot = baseUnitIdSnapshot;
        QualityInspectionId = qualityInspectionId is > 0 ? qualityInspectionId : null;
        CertificateNumberSnapshot = SalesText.OptionalText(certificateNumberSnapshot, 64, SalesErrors.NotesInvalid);
        Notes = SalesText.OptionalText(notes, SalesText.MaxLineNotesLength, SalesErrors.NotesInvalid);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long DeliveryOrderId { get; private set; }
    public long SalesOrderLineId { get; private set; }
    public int LineNumber { get; private set; }
    public long ItemId { get; private set; }
    public string LotCode { get; private set; }
    public decimal DispatchedBaseQuantity { get; private set; }
    public long BaseUnitIdSnapshot { get; private set; }
    public long? QualityInspectionId { get; private set; }
    public string? CertificateNumberSnapshot { get; private set; }
    public string? Notes { get; private set; }

    internal static DeliveryOrderLine Create(
        long organizationId,
        long deliveryOrderId,
        long salesOrderLineId,
        int lineNumber,
        long itemId,
        string? lotCode,
        decimal dispatchedBaseQuantity,
        long baseUnitIdSnapshot,
        long? qualityInspectionId,
        string? certificateNumberSnapshot,
        string? notes)
        => new(
            organizationId,
            deliveryOrderId,
            salesOrderLineId,
            lineNumber,
            itemId,
            lotCode,
            dispatchedBaseQuantity,
            baseUnitIdSnapshot,
            qualityInspectionId,
            certificateNumberSnapshot,
            notes);
}
