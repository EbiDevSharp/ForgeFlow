namespace ForgeFlow.Domain.Boms;

public sealed class BomApplicability
{
    private BomApplicability()
    {
        Name = null!;
    }

    public BomApplicability(long organizationId, long bomVersionId, string? name, bool isActive)
    {
        if (organizationId <= 0 || bomVersionId <= 0)
            throw new BomRuleException(BomErrors.VersionNotFound);

        OrganizationId = organizationId;
        BomVersionId = bomVersionId;
        Name = BomText.Name(name);
        IsActive = isActive;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long BomVersionId { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; } = true;

    public bool Update(string? name, bool isActive)
    {
        var normalized = BomText.Name(name);
        if (Name == normalized && IsActive == isActive)
            return false;
        Name = normalized;
        IsActive = isActive;
        return true;
    }
}

public sealed class BomApplicabilityCondition
{
    private BomApplicabilityCondition()
    {
        Value = null!;
    }

    public BomApplicabilityCondition(
        long organizationId,
        long bomApplicabilityId,
        long contextDimensionId,
        ApplicabilityOperator op,
        string? value)
    {
        if (organizationId <= 0 || bomApplicabilityId <= 0 || contextDimensionId <= 0)
            throw new BomRuleException(BomErrors.ContextDimensionNotFound);
        if (!Enum.IsDefined(op))
            throw new BomRuleException(BomErrors.ApplicabilityInvalid);
        if (value is null || value.Any(char.IsControl))
            throw new BomRuleException(BomErrors.ApplicabilityInvalid);

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > BomText.ConditionValueMaxLength)
            throw new BomRuleException(BomErrors.ApplicabilityInvalid);

        OrganizationId = organizationId;
        BomApplicabilityId = bomApplicabilityId;
        ContextDimensionId = contextDimensionId;
        Operator = op;
        Value = trimmed;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long BomApplicabilityId { get; private set; }
    public long ContextDimensionId { get; private set; }
    public ApplicabilityOperator Operator { get; private set; }
    public string Value { get; private set; }
}
