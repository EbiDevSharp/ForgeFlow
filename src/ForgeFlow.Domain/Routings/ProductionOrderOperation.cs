namespace ForgeFlow.Domain.Routings;

/// <summary>Frozen routing operation step snapshotted on a ProductionOrder with shop-floor execution state.</summary>
public sealed class ProductionOrderOperation
{
    private ProductionOrderOperation() { OperationCode = null!; OperationName = null!; }

    private ProductionOrderOperation(
        long organizationId,
        long siteId,
        long productionOrderId,
        int sequence,
        string? operationCode,
        string? operationName,
        long workCenterId,
        decimal plannedSetupMinutes,
        decimal plannedRunMinutes,
        decimal plannedQueueMinutes,
        decimal plannedWaitMinutes,
        bool requiresQualityCheck,
        string? instructions)
    {
        if (organizationId <= 0 || siteId <= 0)
            throw new RoutingRuleException(RoutingErrors.SiteInvalid);
        if (sequence <= 0)
            throw new RoutingRuleException(RoutingErrors.SequenceInvalid);
        if (workCenterId <= 0)
            throw new RoutingRuleException(RoutingErrors.WorkCenterNotFound);

        OrganizationId = organizationId;
        SiteId = siteId;
        ProductionOrderId = productionOrderId;
        Sequence = sequence;
        OperationCode = RoutingText.Code(operationCode, RoutingErrors.OperationCodeInvalid);
        OperationName = RoutingText.RequiredName(operationName);
        WorkCenterId = workCenterId;
        PlannedSetupMinutes = RoutingText.NonNegativeMinutes(plannedSetupMinutes);
        PlannedRunMinutes = RoutingText.NonNegativeMinutes(plannedRunMinutes);
        PlannedQueueMinutes = RoutingText.NonNegativeMinutes(plannedQueueMinutes);
        PlannedWaitMinutes = RoutingText.NonNegativeMinutes(plannedWaitMinutes);
        RequiresQualityCheck = requiresQualityCheck;
        Instructions = RoutingText.OptionalNotes(instructions);
        Status = ProductionOrderOperationStatus.Pending;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public long ProductionOrderId { get; private set; }
    public int Sequence { get; private set; }
    public string OperationCode { get; private set; }
    public string OperationName { get; private set; }
    public long WorkCenterId { get; private set; }
    public decimal PlannedSetupMinutes { get; private set; }
    public decimal PlannedRunMinutes { get; private set; }
    public decimal PlannedQueueMinutes { get; private set; }
    public decimal PlannedWaitMinutes { get; private set; }
    public decimal ActualSetupMinutes { get; private set; }
    public decimal ActualRunMinutes { get; private set; }
    public decimal CompletedQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public bool RequiresQualityCheck { get; private set; }
    public string? Instructions { get; private set; }
    public string? OperatorNotes { get; private set; }
    public ProductionOrderOperationStatus Status { get; private set; } = ProductionOrderOperationStatus.Pending;
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public decimal PlannedTotalMinutes =>
        decimal.Round(PlannedSetupMinutes + PlannedRunMinutes + PlannedQueueMinutes + PlannedWaitMinutes, 2, MidpointRounding.AwayFromZero);

    public static ProductionOrderOperation Create(
        long organizationId,
        long siteId,
        long productionOrderId,
        int sequence,
        string? operationCode,
        string? operationName,
        long workCenterId,
        decimal plannedSetupMinutes,
        decimal plannedRunMinutes,
        decimal plannedQueueMinutes,
        decimal plannedWaitMinutes,
        bool requiresQualityCheck,
        string? instructions)
        => new(organizationId, siteId, productionOrderId, sequence, operationCode, operationName, workCenterId,
            plannedSetupMinutes, plannedRunMinutes, plannedQueueMinutes, plannedWaitMinutes, requiresQualityCheck, instructions);

    public bool MarkReady()
    {
        if (Status != ProductionOrderOperationStatus.Pending) return false;
        Status = ProductionOrderOperationStatus.Ready;
        return true;
    }

    public void Start(DateTime utcNow, string? notes = null)
    {
        if (Status is not (ProductionOrderOperationStatus.Ready or ProductionOrderOperationStatus.Paused or ProductionOrderOperationStatus.Pending))
            throw new RoutingRuleException(RoutingErrors.OperationTransitionInvalid);

        Status = ProductionOrderOperationStatus.InProgress;
        StartedAtUtc ??= DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        if (notes is not null)
            OperatorNotes = RoutingText.OptionalNotes(notes);
    }

    public void Pause(decimal actualSetupMinutes, decimal actualRunMinutes, decimal completedQuantity, decimal scrapQuantity, string? notes)
    {
        if (Status != ProductionOrderOperationStatus.InProgress)
            throw new RoutingRuleException(RoutingErrors.OperationTransitionInvalid);

        ActualSetupMinutes = RoutingText.NonNegativeMinutes(actualSetupMinutes);
        ActualRunMinutes = RoutingText.NonNegativeMinutes(actualRunMinutes);
        CompletedQuantity = RoutingText.NonNegativeQuantity(completedQuantity);
        ScrapQuantity = RoutingText.NonNegativeQuantity(scrapQuantity);
        Status = ProductionOrderOperationStatus.Paused;
        if (notes is not null)
            OperatorNotes = RoutingText.OptionalNotes(notes);
    }

    public void Complete(decimal actualSetupMinutes, decimal actualRunMinutes, decimal completedQuantity, decimal scrapQuantity, string? notes, DateTime utcNow)
    {
        if (Status is not (ProductionOrderOperationStatus.InProgress or ProductionOrderOperationStatus.Paused or ProductionOrderOperationStatus.Ready))
            throw new RoutingRuleException(RoutingErrors.OperationTransitionInvalid);

        ActualSetupMinutes = RoutingText.NonNegativeMinutes(actualSetupMinutes);
        ActualRunMinutes = RoutingText.NonNegativeMinutes(actualRunMinutes);
        CompletedQuantity = RoutingText.NonNegativeQuantity(completedQuantity);
        ScrapQuantity = RoutingText.NonNegativeQuantity(scrapQuantity);
        Status = ProductionOrderOperationStatus.Completed;
        StartedAtUtc ??= DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        CompletedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        if (notes is not null)
            OperatorNotes = RoutingText.OptionalNotes(notes);
    }

    public void Skip(string? notes, DateTime utcNow)
    {
        if (Status is not (ProductionOrderOperationStatus.Pending or ProductionOrderOperationStatus.Ready or ProductionOrderOperationStatus.Paused))
            throw new RoutingRuleException(RoutingErrors.OperationTransitionInvalid);

        Status = ProductionOrderOperationStatus.Skipped;
        CompletedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        if (notes is not null)
            OperatorNotes = RoutingText.OptionalNotes(notes);
    }
}
