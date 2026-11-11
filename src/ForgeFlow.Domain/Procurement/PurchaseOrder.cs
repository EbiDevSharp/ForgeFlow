namespace ForgeFlow.Domain.Procurement;

/// <summary>Site-scoped Purchase Order managing vendor ordering, approvals, and partial/full goods receipts.</summary>
public sealed class PurchaseOrder
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder()
    {
        OrderNumber = string.Empty;
        CurrencyCode = "IRR";
    }

    private PurchaseOrder(
        long organizationId,
        long siteId,
        long supplierId,
        string? orderNumber,
        DateOnly orderDate,
        DateOnly? expectedDeliveryDate,
        long? targetWarehouseId,
        string? currencyCode,
        string? notes)
    {
        if (organizationId <= 0)
            throw new ProcurementRuleException(ProcurementErrors.NotFound);
        if (siteId <= 0)
            throw new ProcurementRuleException(ProcurementErrors.SiteInvalid);
        if (supplierId <= 0)
            throw new ProcurementRuleException(ProcurementErrors.SupplierNotFound);
        if (expectedDeliveryDate.HasValue && expectedDeliveryDate.Value < orderDate)
            throw new ProcurementRuleException(ProcurementErrors.DatesInvalid);

        OrganizationId = organizationId;
        SiteId = siteId;
        SupplierId = supplierId;
        OrderNumber = ProcurementText.Code(orderNumber, ProcurementErrors.OrderNumberInvalid);
        Status = PurchaseOrderStatus.Draft;
        OrderDate = orderDate;
        ExpectedDeliveryDate = expectedDeliveryDate;
        TargetWarehouseId = targetWarehouseId is > 0 ? targetWarehouseId : null;
        CurrencyCode = ProcurementText.Currency(currencyCode);
        Notes = ProcurementText.OptionalText(notes, ProcurementText.MaxNotesLength, ProcurementErrors.NotesInvalid);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public long SupplierId { get; private set; }
    public string OrderNumber { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public DateOnly OrderDate { get; private set; }
    public DateOnly? ExpectedDeliveryDate { get; private set; }
    public long? TargetWarehouseId { get; private set; }
    public string CurrencyCode { get; private set; }
    public string? Notes { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    public static PurchaseOrder CreateDraft(
        long organizationId,
        long siteId,
        long supplierId,
        string? orderNumber,
        DateOnly orderDate,
        DateOnly? expectedDeliveryDate,
        long? targetWarehouseId,
        string? currencyCode,
        string? notes)
        => new(organizationId, siteId, supplierId, orderNumber, orderDate, expectedDeliveryDate, targetWarehouseId, currencyCode, notes);

    public void UpdateDraft(
        long supplierId,
        DateOnly orderDate,
        DateOnly? expectedDeliveryDate,
        long? targetWarehouseId,
        string? currencyCode,
        string? notes)
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new ProcurementRuleException(ProcurementErrors.NotDraft);
        if (supplierId <= 0)
            throw new ProcurementRuleException(ProcurementErrors.SupplierNotFound);
        if (expectedDeliveryDate.HasValue && expectedDeliveryDate.Value < orderDate)
            throw new ProcurementRuleException(ProcurementErrors.DatesInvalid);

        SupplierId = supplierId;
        OrderDate = orderDate;
        ExpectedDeliveryDate = expectedDeliveryDate;
        TargetWarehouseId = targetWarehouseId is > 0 ? targetWarehouseId : null;
        CurrencyCode = ProcurementText.Currency(currencyCode);
        Notes = ProcurementText.OptionalText(notes, ProcurementText.MaxNotesLength, ProcurementErrors.NotesInvalid);
    }

    public void ReplaceLines(
        IReadOnlyList<(long ItemId, decimal OrderedQuantity, long OrderedUnitId, long BaseUnitIdSnapshot, decimal OrderedBaseQuantity, decimal? UnitPrice, string? Notes)> lines)
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new ProcurementRuleException(ProcurementErrors.NotDraft);
        if (lines is null || lines.Count == 0)
            throw new ProcurementRuleException(ProcurementErrors.EmptyOrder);

        _lines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            var entry = lines[i];
            _lines.Add(PurchaseOrderLine.Create(
                OrganizationId,
                Id,
                i + 1,
                entry.ItemId,
                entry.OrderedQuantity,
                entry.OrderedUnitId,
                entry.BaseUnitIdSnapshot,
                entry.OrderedBaseQuantity,
                entry.UnitPrice,
                entry.Notes));
        }
    }

    public void Approve(DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new ProcurementRuleException(ProcurementErrors.InvalidTransition);
        if (_lines.Count == 0)
            throw new ProcurementRuleException(ProcurementErrors.EmptyOrder);

        Status = PurchaseOrderStatus.Approved;
        ApprovedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    public void ApplyReceipts(IReadOnlyList<(long LineId, decimal ReceivedBaseDelta)> receipts)
    {
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
            throw new ProcurementRuleException(ProcurementErrors.InvalidTransition);
        if (receipts is null || receipts.Count == 0)
            throw new ProcurementRuleException(ProcurementErrors.QuantityInvalid);

        foreach (var (lineId, delta) in receipts)
        {
            var line = _lines.FirstOrDefault(l => l.Id == lineId)
                ?? throw new ProcurementRuleException(ProcurementErrors.LineNotFound);
            line.ApplyReceipt(delta);
        }

        Status = _lines.All(l => l.RemainingBaseQuantity == 0m)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
    }

    public void RevertReceipts(IReadOnlyList<(long LineId, decimal RevertedBaseDelta)> reverted)
    {
        if (Status is not (PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.Closed))
            throw new ProcurementRuleException(ProcurementErrors.InvalidTransition);

        foreach (var (lineId, delta) in reverted)
        {
            var line = _lines.FirstOrDefault(l => l.Id == lineId)
                ?? throw new ProcurementRuleException(ProcurementErrors.LineNotFound);
            line.RevertReceipt(delta);
        }

        if (_lines.All(l => l.ReceivedBaseQuantity == 0m))
        {
            Status = PurchaseOrderStatus.Approved;
            ClosedAtUtc = null;
        }
        else if (_lines.Any(l => l.RemainingBaseQuantity > 0m))
        {
            Status = PurchaseOrderStatus.PartiallyReceived;
            ClosedAtUtc = null;
        }
        else
        {
            Status = PurchaseOrderStatus.Received;
        }
    }

    public void RevertReceiptsByItem(IReadOnlyList<(long ItemId, decimal RevertedBaseDelta)> reverted)
    {
        if (Status is not (PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.Closed))
            throw new ProcurementRuleException(ProcurementErrors.InvalidTransition);

        foreach (var (itemId, delta) in reverted)
        {
            var line = _lines.FirstOrDefault(l => l.ItemId == itemId)
                ?? throw new ProcurementRuleException(ProcurementErrors.LineNotFound);
            line.RevertReceipt(delta);
        }

        if (_lines.All(l => l.ReceivedBaseQuantity == 0m))
        {
            Status = PurchaseOrderStatus.Approved;
            ClosedAtUtc = null;
        }
        else if (_lines.Any(l => l.RemainingBaseQuantity > 0m))
        {
            Status = PurchaseOrderStatus.PartiallyReceived;
            ClosedAtUtc = null;
        }
        else
        {
            Status = PurchaseOrderStatus.Received;
        }
    }

    public void CloseRemaining(DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.PartiallyReceived)
            throw new ProcurementRuleException(ProcurementErrors.InvalidTransition);

        Status = PurchaseOrderStatus.Closed;
        ClosedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    public void Cancel(string? reason, DateTime utcNow)
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved))
            throw new ProcurementRuleException(_lines.Any(l => l.ReceivedBaseQuantity > 0m)
                ? ProcurementErrors.HasReceiptsCannotCancel
                : ProcurementErrors.InvalidTransition);
        if (_lines.Any(l => l.ReceivedBaseQuantity > 0m))
            throw new ProcurementRuleException(ProcurementErrors.HasReceiptsCannotCancel);

        CancellationReason = ProcurementText.RequiredReason(reason);
        Status = PurchaseOrderStatus.Cancelled;
        CancelledAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }
}
