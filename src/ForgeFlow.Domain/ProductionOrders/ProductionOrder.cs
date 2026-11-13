using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.ProductionOrders;

/// <summary>
/// Site-scoped manufacturing order capturing a resolved BOM version and planned input/output snapshot.
/// Structurally immutable after Release so subsequent BOM updates never alter historical orders (Day 11).
/// </summary>
public sealed class ProductionOrder : IHasRowVersion, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();
    private ProductionOrder() { }

    private ProductionOrder(
        long organizationId,
        long siteId,
        string orderNumber,
        long itemId,
        decimal requestedQuantity,
        long requestedUnitId,
        DateOnly plannedStartDate,
        DateOnly plannedDueDate,
        long bomId,
        string bomCodeSnapshot,
        long bomVersionId,
        int bomVersionNumberSnapshot,
        decimal baseOutputQuantitySnapshot,
        long baseOutputUnitIdSnapshot,
        string? notes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(organizationId);
        if (siteId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.SiteInvalid);
        if (itemId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.ItemInvalid);
        if (requestedUnitId <= 0 || baseOutputUnitIdSnapshot <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.UnitInvalid);
        if (plannedDueDate < plannedStartDate)
            throw new ProductionOrderRuleException(ProductionOrderErrors.DatesInvalid);
        if (bomId <= 0 || bomVersionId <= 0 || bomVersionNumberSnapshot <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.NoActiveBomFound);

        OrganizationId = organizationId;
        SiteId = siteId;
        OrderNumber = ProductionOrderText.OrderNumber(orderNumber);
        Status = ProductionOrderStatus.Draft;
        ItemId = itemId;
        RequestedQuantity = ProductionOrderText.ValidatePositiveQuantity(requestedQuantity);
        RequestedUnitId = requestedUnitId;
        PlannedStartDate = plannedStartDate;
        PlannedDueDate = plannedDueDate;
        BomId = bomId;
        BomCodeSnapshot = ProductionOrderText.OrderNumber(bomCodeSnapshot);
        BomVersionId = bomVersionId;
        BomVersionNumberSnapshot = bomVersionNumberSnapshot;
        BaseOutputQuantitySnapshot = ProductionOrderText.ValidatePositiveQuantity(baseOutputQuantitySnapshot);
        BaseOutputUnitIdSnapshot = baseOutputUnitIdSnapshot;
        Notes = ProductionOrderText.OptionalText(notes, ProductionOrderErrors.NotesInvalid);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public ProductionOrderStatus Status { get; private set; } = ProductionOrderStatus.Draft;

    public long ItemId { get; private set; }
    public decimal RequestedQuantity { get; private set; }
    public long RequestedUnitId { get; private set; }
    public DateOnly PlannedStartDate { get; private set; }
    public DateOnly PlannedDueDate { get; private set; }

    public long BomId { get; private set; }
    public string BomCodeSnapshot { get; private set; } = string.Empty;
    public long BomVersionId { get; private set; }
    public int BomVersionNumberSnapshot { get; private set; }
    public decimal BaseOutputQuantitySnapshot { get; private set; }
    public long BaseOutputUnitIdSnapshot { get; private set; }

    public decimal ActualProducedQuantity { get; private set; }
    public decimal ActualScrapQuantity { get; private set; }
    public long? RoutingId { get; private set; }
    public string? RoutingCodeSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public string? CancellationReason { get; private set; }

    public DateTime? ReleasedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static ProductionOrder CreateDraft(
        long organizationId,
        long siteId,
        string? orderNumber,
        long itemId,
        decimal requestedQuantity,
        long requestedUnitId,
        DateOnly plannedStartDate,
        DateOnly plannedDueDate,
        long bomId,
        string bomCodeSnapshot,
        long bomVersionId,
        int bomVersionNumberSnapshot,
        decimal baseOutputQuantitySnapshot,
        long baseOutputUnitIdSnapshot,
        string? notes) => new(
            organizationId,
            siteId,
            orderNumber ?? string.Empty,
            itemId,
            requestedQuantity,
            requestedUnitId,
            plannedStartDate,
            plannedDueDate,
            bomId,
            bomCodeSnapshot,
            bomVersionId,
            bomVersionNumberSnapshot,
            baseOutputQuantitySnapshot,
            baseOutputUnitIdSnapshot,
            notes);

    public void EnsureDraftMutable()
    {
        if (Status != ProductionOrderStatus.Draft)
            throw new ProductionOrderRuleException(ProductionOrderErrors.OrderImmutable);
    }

    public void UpdateDraftHeader(
        decimal requestedQuantity,
        long requestedUnitId,
        DateOnly plannedStartDate,
        DateOnly plannedDueDate,
        string? notes)
    {
        EnsureDraftMutable();
        if (requestedUnitId <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.UnitInvalid);
        if (plannedDueDate < plannedStartDate)
            throw new ProductionOrderRuleException(ProductionOrderErrors.DatesInvalid);

        RequestedQuantity = ProductionOrderText.ValidatePositiveQuantity(requestedQuantity);
        RequestedUnitId = requestedUnitId;
        PlannedStartDate = plannedStartDate;
        PlannedDueDate = plannedDueDate;
        Notes = ProductionOrderText.OptionalText(notes, ProductionOrderErrors.NotesInvalid);
    }

    public void ApplyResolvedBomSnapshot(
        long bomId,
        string bomCodeSnapshot,
        long bomVersionId,
        int bomVersionNumberSnapshot,
        decimal baseOutputQuantitySnapshot,
        long baseOutputUnitIdSnapshot)
    {
        EnsureDraftMutable();
        if (bomId <= 0 || bomVersionId <= 0 || bomVersionNumberSnapshot <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.NoActiveBomFound);
        if (baseOutputUnitIdSnapshot <= 0)
            throw new ProductionOrderRuleException(ProductionOrderErrors.UnitInvalid);

        BomId = bomId;
        BomCodeSnapshot = ProductionOrderText.OrderNumber(bomCodeSnapshot);
        BomVersionId = bomVersionId;
        BomVersionNumberSnapshot = bomVersionNumberSnapshot;
        BaseOutputQuantitySnapshot = ProductionOrderText.ValidatePositiveQuantity(baseOutputQuantitySnapshot);
        BaseOutputUnitIdSnapshot = baseOutputUnitIdSnapshot;
    }

    public void ApplyRoutingSnapshot(long? routingId, string? routingCodeSnapshot)
    {
        EnsureDraftMutable();
        if (routingId is > 0)
        {
            if (string.IsNullOrWhiteSpace(routingCodeSnapshot))
                throw new ProductionOrderRuleException(ProductionOrderErrors.RoutingInvalid);

            RoutingId = routingId.Value;
            RoutingCodeSnapshot = ProductionOrderText.OrderNumber(routingCodeSnapshot);
        }
        else
        {
            RoutingId = null;
            RoutingCodeSnapshot = null;
        }
    }

    public void Release(DateTime utcNow)
    {
        if (Status != ProductionOrderStatus.Draft)
            throw new ProductionOrderRuleException(ProductionOrderErrors.TransitionInvalid);
        Status = ProductionOrderStatus.Released;
        ReleasedAtUtc = utcNow;
        _domainEvents.Add(new ProductionOrderReleasedDomainEvent(OrganizationId, SiteId, OrderNumber, utcNow));
    }

    public void Start(DateTime utcNow)
    {
        if (Status != ProductionOrderStatus.Released)
            throw new ProductionOrderRuleException(ProductionOrderErrors.TransitionInvalid);
        Status = ProductionOrderStatus.InProgress;
        StartedAtUtc = utcNow;
    }

    public void RecordActuals(decimal producedQuantity, decimal scrapQuantity, string? notes)
    {
        if (Status != ProductionOrderStatus.InProgress)
            throw new ProductionOrderRuleException(ProductionOrderErrors.TransitionInvalid);
        ActualProducedQuantity = ProductionOrderText.ValidateNonNegativeQuantity(producedQuantity);
        ActualScrapQuantity = ProductionOrderText.ValidateNonNegativeQuantity(scrapQuantity);
        if (notes is not null)
            Notes = ProductionOrderText.OptionalText(notes, ProductionOrderErrors.NotesInvalid);
    }

    public void Complete(decimal producedQuantity, decimal scrapQuantity, string? notes, DateTime utcNow)
    {
        if (Status != ProductionOrderStatus.InProgress)
            throw new ProductionOrderRuleException(ProductionOrderErrors.TransitionInvalid);
        ActualProducedQuantity = ProductionOrderText.ValidateNonNegativeQuantity(producedQuantity);
        ActualScrapQuantity = ProductionOrderText.ValidateNonNegativeQuantity(scrapQuantity);
        if (notes is not null)
            Notes = ProductionOrderText.OptionalText(notes, ProductionOrderErrors.NotesInvalid);
        Status = ProductionOrderStatus.Completed;
        CompletedAtUtc = utcNow;
        _domainEvents.Add(new ProductionOrderCompletedDomainEvent(OrganizationId, SiteId, OrderNumber, utcNow));
    }

    public void Cancel(string? reason, DateTime utcNow)
    {
        if (Status is not (ProductionOrderStatus.Draft or ProductionOrderStatus.Released or ProductionOrderStatus.InProgress))
            throw new ProductionOrderRuleException(ProductionOrderErrors.TransitionInvalid);
        CancellationReason = ProductionOrderText.RequiredReason(reason);
        Status = ProductionOrderStatus.Cancelled;
        CancelledAtUtc = utcNow;
        _domainEvents.Add(new ProductionOrderCancelledDomainEvent(OrganizationId, SiteId, OrderNumber, CancellationReason, utcNow));
    }
}

public sealed record ProductionOrderReleasedDomainEvent(long OrganizationId, long SiteId, string OrderNumber, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record ProductionOrderCompletedDomainEvent(long OrganizationId, long SiteId, string OrderNumber, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record ProductionOrderCancelledDomainEvent(long OrganizationId, long SiteId, string OrderNumber, string Reason, DateTime OccurredAtUtc) : IDomainEvent;
