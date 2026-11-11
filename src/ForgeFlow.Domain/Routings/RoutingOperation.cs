namespace ForgeFlow.Domain.Routings;

public sealed class RoutingOperation
{
    private RoutingOperation() { OperationCode = null!; OperationName = null!; }

    private RoutingOperation(
        long organizationId,
        long siteId,
        long routingId,
        int sequence,
        string? operationCode,
        string? operationName,
        long workCenterId,
        decimal setupMinutes,
        decimal runMinutesPerUnit,
        decimal queueMinutes,
        decimal waitMinutes,
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
        RoutingId = routingId;
        Sequence = sequence;
        OperationCode = RoutingText.Code(operationCode, RoutingErrors.OperationCodeInvalid);
        OperationName = RoutingText.RequiredName(operationName);
        WorkCenterId = workCenterId;
        SetupMinutes = RoutingText.NonNegativeMinutes(setupMinutes);
        RunMinutesPerUnit = RoutingText.PositiveRunMinutesPerUnit(runMinutesPerUnit);
        QueueMinutes = RoutingText.NonNegativeMinutes(queueMinutes);
        WaitMinutes = RoutingText.NonNegativeMinutes(waitMinutes);
        RequiresQualityCheck = requiresQualityCheck;
        Instructions = RoutingText.OptionalNotes(instructions);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public long RoutingId { get; private set; }
    public int Sequence { get; private set; }
    public string OperationCode { get; private set; }
    public string OperationName { get; private set; }
    public long WorkCenterId { get; private set; }
    public decimal SetupMinutes { get; private set; }
    public decimal RunMinutesPerUnit { get; private set; }
    public decimal QueueMinutes { get; private set; }
    public decimal WaitMinutes { get; private set; }
    public bool RequiresQualityCheck { get; private set; }
    public string? Instructions { get; private set; }

    internal static RoutingOperation Create(
        long organizationId,
        long siteId,
        long routingId,
        int sequence,
        string? operationCode,
        string? operationName,
        long workCenterId,
        decimal setupMinutes,
        decimal runMinutesPerUnit,
        decimal queueMinutes,
        decimal waitMinutes,
        bool requiresQualityCheck,
        string? instructions)
        => new(organizationId, siteId, routingId, sequence, operationCode, operationName, workCenterId,
            setupMinutes, runMinutesPerUnit, queueMinutes, waitMinutes, requiresQualityCheck, instructions);
}
