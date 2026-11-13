using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.Sales;

/// <summary>Site-scoped Sales Order managing customer orders, approvals, reservations, and partial/full dispatches.</summary>
public sealed class SalesOrder : IHasRowVersion, IHasDomainEvents
{
    private readonly List<SalesOrderLine> _lines = [];
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    private SalesOrder()
    {
        OrderNumber = string.Empty;
        CurrencyCode = "IRR";
    }

    private SalesOrder(
        long organizationId,
        long siteId,
        long customerId,
        string? orderNumber,
        DateOnly orderDate,
        DateOnly? requestedDeliveryDate,
        long? sourceWarehouseId,
        string? currencyCode,
        string? shippingAddressSnapshot,
        string? notes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(organizationId);
        if (siteId <= 0)
            throw new SalesRuleException(SalesErrors.SiteInvalid);
        if (customerId <= 0)
            throw new SalesRuleException(SalesErrors.CustomerNotFound);
        if (requestedDeliveryDate.HasValue && requestedDeliveryDate.Value < orderDate)
            throw new SalesRuleException(SalesErrors.DatesInvalid);

        OrganizationId = organizationId;
        SiteId = siteId;
        CustomerId = customerId;
        OrderNumber = SalesText.Code(orderNumber, SalesErrors.OrderNumberInvalid);
        Status = SalesOrderStatus.Draft;
        OrderDate = orderDate;
        RequestedDeliveryDate = requestedDeliveryDate;
        SourceWarehouseId = sourceWarehouseId is > 0 ? sourceWarehouseId : null;
        CurrencyCode = SalesText.Currency(currencyCode);
        ShippingAddressSnapshot = SalesText.OptionalText(shippingAddressSnapshot, SalesText.MaxAddressLength, SalesErrors.AddressInvalid);
        Notes = SalesText.OptionalText(notes, SalesText.MaxNotesLength, SalesErrors.NotesInvalid);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public long CustomerId { get; private set; }
    public string OrderNumber { get; private set; }
    public SalesOrderStatus Status { get; private set; } = SalesOrderStatus.Draft;
    public DateOnly OrderDate { get; private set; }
    public DateOnly? RequestedDeliveryDate { get; private set; }
    public long? SourceWarehouseId { get; private set; }
    public string CurrencyCode { get; private set; }
    public string? ShippingAddressSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    public static SalesOrder CreateDraft(
        long organizationId,
        long siteId,
        long customerId,
        string? orderNumber,
        DateOnly orderDate,
        DateOnly? requestedDeliveryDate,
        long? sourceWarehouseId,
        string? currencyCode,
        string? shippingAddressSnapshot,
        string? notes)
        => new(organizationId, siteId, customerId, orderNumber, orderDate, requestedDeliveryDate, sourceWarehouseId, currencyCode, shippingAddressSnapshot, notes);

    public void UpdateDraft(
        long customerId,
        DateOnly orderDate,
        DateOnly? requestedDeliveryDate,
        long? sourceWarehouseId,
        string? currencyCode,
        string? shippingAddressSnapshot,
        string? notes)
    {
        if (Status != SalesOrderStatus.Draft)
            throw new SalesRuleException(SalesErrors.NotDraft);
        if (customerId <= 0)
            throw new SalesRuleException(SalesErrors.CustomerNotFound);
        if (requestedDeliveryDate.HasValue && requestedDeliveryDate.Value < orderDate)
            throw new SalesRuleException(SalesErrors.DatesInvalid);

        CustomerId = customerId;
        OrderDate = orderDate;
        RequestedDeliveryDate = requestedDeliveryDate;
        SourceWarehouseId = sourceWarehouseId is > 0 ? sourceWarehouseId : null;
        CurrencyCode = SalesText.Currency(currencyCode);
        ShippingAddressSnapshot = SalesText.OptionalText(shippingAddressSnapshot, SalesText.MaxAddressLength, SalesErrors.AddressInvalid);
        Notes = SalesText.OptionalText(notes, SalesText.MaxNotesLength, SalesErrors.NotesInvalid);
    }

    public void ReplaceLines(
        IReadOnlyList<(long ItemId, decimal OrderedQuantity, long OrderedUnitId, long BaseUnitIdSnapshot, decimal OrderedBaseQuantity, decimal? UnitPrice, string? Notes)> lines)
    {
        if (Status != SalesOrderStatus.Draft)
            throw new SalesRuleException(SalesErrors.NotDraft);
        if (lines is null || lines.Count == 0)
            throw new SalesRuleException(SalesErrors.EmptyOrder);

        _lines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            var entry = lines[i];
            _lines.Add(SalesOrderLine.Create(
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
        if (Status != SalesOrderStatus.Draft)
            throw new SalesRuleException(SalesErrors.InvalidTransition);
        if (_lines.Count == 0)
            throw new SalesRuleException(SalesErrors.EmptyOrder);

        Status = SalesOrderStatus.Approved;
        ApprovedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        _domainEvents.Add(new SalesOrderApprovedDomainEvent(OrganizationId, SiteId, OrderNumber, ApprovedAtUtc.Value));
    }

    public void ApplyDispatches(IReadOnlyList<(long LineId, decimal DispatchedBaseDelta)> dispatches)
    {
        if (Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyDispatched))
            throw new SalesRuleException(SalesErrors.InvalidTransition);
        if (dispatches is null || dispatches.Count == 0)
            throw new SalesRuleException(SalesErrors.QuantityInvalid);

        foreach (var (lineId, delta) in dispatches)
        {
            var line = _lines.FirstOrDefault(l => l.Id == lineId)
                ?? throw new SalesRuleException(SalesErrors.LineNotFound);
            line.ApplyDispatch(delta);
        }

        Status = _lines.All(l => l.RemainingBaseQuantity == 0m)
            ? SalesOrderStatus.Dispatched
            : SalesOrderStatus.PartiallyDispatched;
    }

    public void RevertDispatches(IReadOnlyList<(long LineId, decimal RevertedBaseDelta)> dispatches)
    {
        if (Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyDispatched or SalesOrderStatus.Dispatched))
            throw new SalesRuleException(SalesErrors.InvalidTransition);
        if (dispatches is null || dispatches.Count == 0)
            throw new SalesRuleException(SalesErrors.QuantityInvalid);

        foreach (var (lineId, delta) in dispatches)
        {
            var line = _lines.FirstOrDefault(l => l.Id == lineId)
                ?? throw new SalesRuleException(SalesErrors.LineNotFound);
            line.RevertDispatch(delta);
        }

        var anyDispatched = _lines.Any(l => l.DispatchedBaseQuantity > 0m);
        if (!anyDispatched)
            Status = SalesOrderStatus.Approved;
        else
            Status = _lines.All(l => l.RemainingBaseQuantity == 0m)
                ? SalesOrderStatus.Dispatched
                : SalesOrderStatus.PartiallyDispatched;
    }

    public void Close(DateTime utcNow)
    {
        if (Status is not (SalesOrderStatus.PartiallyDispatched or SalesOrderStatus.Dispatched))
            throw new SalesRuleException(SalesErrors.InvalidTransition);

        Status = SalesOrderStatus.Closed;
        ClosedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        _domainEvents.Add(new SalesOrderClosedDomainEvent(OrganizationId, SiteId, OrderNumber, ClosedAtUtc.Value));
    }

    public void Cancel(string? reason, DateTime utcNow)
    {
        if (Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Approved))
            throw new SalesRuleException(
                _lines.Any(l => l.DispatchedBaseQuantity > 0m)
                    ? SalesErrors.HasDispatchesCannotCancel
                    : SalesErrors.InvalidTransition);

        CancellationReason = SalesText.RequiredReason(reason);
        Status = SalesOrderStatus.Cancelled;
        CancelledAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        _domainEvents.Add(new SalesOrderCancelledDomainEvent(OrganizationId, SiteId, OrderNumber, CancellationReason, CancelledAtUtc.Value));
    }

    public decimal CalculateTotalAmount()
        => _lines.Sum(l => l.OrderedQuantity * (l.UnitPrice ?? 0m));
}

public sealed record SalesOrderApprovedDomainEvent(long OrganizationId, long SiteId, string OrderNumber, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record SalesOrderClosedDomainEvent(long OrganizationId, long SiteId, string OrderNumber, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record SalesOrderCancelledDomainEvent(long OrganizationId, long SiteId, string OrderNumber, string Reason, DateTime OccurredAtUtc) : IDomainEvent;
