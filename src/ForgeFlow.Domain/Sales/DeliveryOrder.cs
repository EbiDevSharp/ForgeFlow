namespace ForgeFlow.Domain.Sales;

/// <summary>Site-scoped Dispatch / Delivery Order picking non-quarantined, non-expired lots with Lot COA snapshots.</summary>
public sealed class DeliveryOrder
{
    private readonly List<DeliveryOrderLine> _lines = [];

    private DeliveryOrder()
    {
        DispatchNumber = string.Empty;
    }

    private DeliveryOrder(
        long organizationId,
        long siteId,
        long salesOrderId,
        long warehouseId,
        string? dispatchNumber,
        DateOnly dispatchDate,
        string? carrierName,
        string? trackingNumber,
        string? notes,
        DateTime postedAtUtc)
    {
        if (organizationId <= 0)
            throw new SalesRuleException(SalesErrors.NotFound);
        if (siteId <= 0)
            throw new SalesRuleException(SalesErrors.SiteInvalid);
        if (salesOrderId <= 0)
            throw new SalesRuleException(SalesErrors.NotFound);
        if (warehouseId <= 0)
            throw new SalesRuleException(SalesErrors.WarehouseInvalid);

        OrganizationId = organizationId;
        SiteId = siteId;
        SalesOrderId = salesOrderId;
        WarehouseId = warehouseId;
        DispatchNumber = SalesText.Code(dispatchNumber, SalesErrors.DispatchNumberInvalid);
        Status = DeliveryOrderStatus.Posted;
        DispatchDate = dispatchDate;
        CarrierName = SalesText.OptionalText(carrierName, SalesText.MaxCarrierLength, SalesErrors.CarrierInvalid);
        TrackingNumber = SalesText.OptionalText(trackingNumber, SalesText.MaxTrackingLength, SalesErrors.TrackingNumberInvalid);
        Notes = SalesText.OptionalText(notes, SalesText.MaxNotesLength, SalesErrors.NotesInvalid);
        PostedAtUtc = DateTime.SpecifyKind(postedAtUtc, DateTimeKind.Utc);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public long SalesOrderId { get; private set; }
    public long WarehouseId { get; private set; }
    public string DispatchNumber { get; private set; }
    public DeliveryOrderStatus Status { get; private set; } = DeliveryOrderStatus.Posted;
    public DateOnly DispatchDate { get; private set; }
    public string? CarrierName { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTime PostedAtUtc { get; private set; }
    public DateTime? ReversedAtUtc { get; private set; }
    public string? ReversalReason { get; private set; }

    public IReadOnlyList<DeliveryOrderLine> Lines => _lines;

    public static DeliveryOrder CreatePosted(
        long organizationId,
        long siteId,
        long salesOrderId,
        long warehouseId,
        string? dispatchNumber,
        DateOnly dispatchDate,
        string? carrierName,
        string? trackingNumber,
        string? notes,
        DateTime postedAtUtc,
        IReadOnlyList<(long SalesOrderLineId, long ItemId, string LotCode, decimal DispatchedBaseQuantity, long BaseUnitIdSnapshot, long? QualityInspectionId, string? CertificateNumberSnapshot, string? Notes)> lines)
    {
        if (lines is null || lines.Count == 0)
            throw new SalesRuleException(SalesErrors.EmptyDispatch);

        var order = new DeliveryOrder(
            organizationId,
            siteId,
            salesOrderId,
            warehouseId,
            dispatchNumber,
            dispatchDate,
            carrierName,
            trackingNumber,
            notes,
            postedAtUtc);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            order._lines.Add(DeliveryOrderLine.Create(
                organizationId,
                order.Id,
                line.SalesOrderLineId,
                i + 1,
                line.ItemId,
                line.LotCode,
                line.DispatchedBaseQuantity,
                line.BaseUnitIdSnapshot,
                line.QualityInspectionId,
                line.CertificateNumberSnapshot,
                line.Notes));
        }

        return order;
    }

    public void Reverse(string? reason, DateTime utcNow)
    {
        if (Status == DeliveryOrderStatus.Reversed)
            throw new SalesRuleException(SalesErrors.AlreadyReversed);

        ReversalReason = SalesText.RequiredReason(reason);
        Status = DeliveryOrderStatus.Reversed;
        ReversedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }
}
