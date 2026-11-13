using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.Routings;

/// <summary>Site-scoped versioned manufacturing routing defining ordered operations and work centers for an item.</summary>
public sealed class Routing : IHasRowVersion
{
    private readonly List<RoutingOperation> _operations = [];

    private Routing() { Code = null!; Name = null!; }

    private Routing(
        long organizationId,
        long siteId,
        string? code,
        string? name,
        long itemId,
        long? bomVersionId,
        int versionNumber,
        bool isDefault,
        string? notes)
    {
        if (organizationId <= 0)
            throw new RoutingRuleException(RoutingErrors.NotFound);
        if (siteId <= 0)
            throw new RoutingRuleException(RoutingErrors.SiteInvalid);
        if (itemId <= 0)
            throw new RoutingRuleException(RoutingErrors.ItemInvalid);
        if (versionNumber < 1)
            throw new RoutingRuleException(RoutingErrors.VersionInvalid);

        OrganizationId = organizationId;
        SiteId = siteId;
        Code = RoutingText.Code(code, RoutingErrors.RoutingCodeInvalid);
        Name = RoutingText.RequiredName(name);
        ItemId = itemId;
        BomVersionId = bomVersionId is > 0 ? bomVersionId : null;
        VersionNumber = versionNumber;
        Status = RoutingStatus.Draft;
        IsDefault = isDefault;
        Notes = RoutingText.OptionalNotes(notes);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public long ItemId { get; private set; }
    public long? BomVersionId { get; private set; }
    public int VersionNumber { get; private set; }
    public RoutingStatus Status { get; private set; } = RoutingStatus.Draft;
    public bool IsDefault { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<RoutingOperation> Operations => _operations;

    public static Routing CreateDraft(
        long organizationId,
        long siteId,
        string? code,
        string? name,
        long itemId,
        long? bomVersionId,
        int versionNumber,
        bool isDefault,
        string? notes)
        => new(organizationId, siteId, code, name, itemId, bomVersionId, versionNumber, isDefault, notes);

    public void UpdateDraft(string? name, long? bomVersionId, bool isDefault, string? notes)
    {
        if (Status != RoutingStatus.Draft)
            throw new RoutingRuleException(RoutingErrors.RoutingNotDraft);

        Name = RoutingText.RequiredName(name);
        BomVersionId = bomVersionId is > 0 ? bomVersionId : null;
        IsDefault = isDefault;
        Notes = RoutingText.OptionalNotes(notes);
    }

    public void ReplaceOperations(
        IReadOnlyList<(int Sequence, string? OperationCode, string? OperationName, long WorkCenterId, decimal SetupMinutes, decimal RunMinutesPerUnit, decimal QueueMinutes, decimal WaitMinutes, bool RequiresQualityCheck, string? Instructions)> operations)
    {
        if (Status != RoutingStatus.Draft)
            throw new RoutingRuleException(RoutingErrors.RoutingNotDraft);
        if (operations is null || operations.Count == 0)
            throw new RoutingRuleException(RoutingErrors.EmptyOperations);

        var seenSeq = new HashSet<int>();
        _operations.Clear();
        foreach (var op in operations.OrderBy(o => o.Sequence))
        {
            if (!seenSeq.Add(op.Sequence))
                throw new RoutingRuleException(RoutingErrors.DuplicateSequence);

            _operations.Add(RoutingOperation.Create(
                OrganizationId,
                SiteId,
                Id,
                op.Sequence,
                op.OperationCode,
                op.OperationName,
                op.WorkCenterId,
                op.SetupMinutes,
                op.RunMinutesPerUnit,
                op.QueueMinutes,
                op.WaitMinutes,
                op.RequiresQualityCheck,
                op.Instructions));
        }
    }

    public void Activate(DateTime utcNow)
    {
        if (Status != RoutingStatus.Draft)
            throw new RoutingRuleException(RoutingErrors.RoutingInvalidTransition);
        if (_operations.Count == 0)
            throw new RoutingRuleException(RoutingErrors.EmptyOperations);

        Status = RoutingStatus.Active;
        ActivatedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    public void MarkObsolete()
    {
        if (Status == RoutingStatus.Obsolete)
            throw new RoutingRuleException(RoutingErrors.RoutingInvalidTransition);

        Status = RoutingStatus.Obsolete;
        IsDefault = false;
    }

    public bool SetDefault(bool isDefault)
    {
        if (Status == RoutingStatus.Obsolete && isDefault)
            throw new RoutingRuleException(RoutingErrors.RoutingInvalidTransition);
        if (IsDefault == isDefault) return false;
        IsDefault = isDefault;
        return true;
    }
}
