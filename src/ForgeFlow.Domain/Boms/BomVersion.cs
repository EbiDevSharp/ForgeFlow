namespace ForgeFlow.Domain.Boms;

/// <summary>Versioned manufacturing structure for a BOM. Approved/Active/historical versions are structurally immutable.</summary>
public sealed class BomVersion
{
    private BomVersion() { }

    private BomVersion(
        long organizationId,
        long bomId,
        int versionNumber,
        decimal baseOutputQuantity,
        long baseOutputUnitId,
        DateOnly effectiveFromDate,
        DateOnly? effectiveToDate,
        string? notes)
    {
        if (organizationId <= 0 || bomId <= 0)
            throw new BomRuleException(BomErrors.NotFound);
        if (versionNumber <= 0)
            throw new BomRuleException(BomErrors.VersionNumberInvalid);
        if (baseOutputUnitId <= 0)
            throw new BomRuleException(BomErrors.UnitInvalid);
        if (effectiveToDate.HasValue && effectiveToDate.Value < effectiveFromDate)
            throw new BomRuleException(BomErrors.EffectiveDatesInvalid);

        OrganizationId = organizationId;
        BomId = bomId;
        VersionNumber = versionNumber;
        Status = BomVersionStatus.Draft;
        BaseOutputQuantity = BomText.ValidateQuantity(baseOutputQuantity, BomErrors.BaseOutputQuantityInvalid);
        BaseOutputUnitId = baseOutputUnitId;
        EffectiveFromDate = effectiveFromDate;
        EffectiveToDate = effectiveToDate;
        Notes = BomText.OptionalNotes(notes);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long BomId { get; private set; }
    public int VersionNumber { get; private set; }
    public BomVersionStatus Status { get; private set; } = BomVersionStatus.Draft;
    public decimal BaseOutputQuantity { get; private set; }
    public long BaseOutputUnitId { get; private set; }
    public DateOnly EffectiveFromDate { get; private set; }
    public DateOnly? EffectiveToDate { get; private set; }
    public string? Notes { get; private set; }

    public static BomVersion CreateDraft(
        long organizationId,
        long bomId,
        int versionNumber,
        decimal baseOutputQuantity,
        long baseOutputUnitId,
        DateOnly effectiveFromDate,
        DateOnly? effectiveToDate,
        string? notes)
        => new(organizationId, bomId, versionNumber, baseOutputQuantity, baseOutputUnitId, effectiveFromDate, effectiveToDate, notes);

    public void EnsureMutableDraft()
    {
        if (Status != BomVersionStatus.Draft)
            throw new BomRuleException(BomErrors.VersionImmutable);
    }

    public bool UpdateDraftHeader(
        decimal baseOutputQuantity,
        long baseOutputUnitId,
        DateOnly effectiveFromDate,
        DateOnly? effectiveToDate,
        string? notes)
    {
        EnsureMutableDraft();
        if (baseOutputUnitId <= 0)
            throw new BomRuleException(BomErrors.UnitInvalid);
        if (effectiveToDate.HasValue && effectiveToDate.Value < effectiveFromDate)
            throw new BomRuleException(BomErrors.EffectiveDatesInvalid);

        var validatedQty = BomText.ValidateQuantity(baseOutputQuantity, BomErrors.BaseOutputQuantityInvalid);
        var normalizedNotes = BomText.OptionalNotes(notes);

        if (BaseOutputQuantity == validatedQty
            && BaseOutputUnitId == baseOutputUnitId
            && EffectiveFromDate == effectiveFromDate
            && EffectiveToDate == effectiveToDate
            && Notes == normalizedNotes)
        {
            return false;
        }

        BaseOutputQuantity = validatedQty;
        BaseOutputUnitId = baseOutputUnitId;
        EffectiveFromDate = effectiveFromDate;
        EffectiveToDate = effectiveToDate;
        Notes = normalizedNotes;
        return true;
    }

    public bool TransitionTo(BomVersionStatus target)
    {
        if (Status == target)
            return false;

        var valid = (Status, target) switch
        {
            (BomVersionStatus.Draft, BomVersionStatus.Approved) => true,
            (BomVersionStatus.Draft, BomVersionStatus.Active) => true,
            (BomVersionStatus.Draft, BomVersionStatus.Obsolete) => true,
            (BomVersionStatus.Approved, BomVersionStatus.Active) => true,
            (BomVersionStatus.Approved, BomVersionStatus.Obsolete) => true,
            (BomVersionStatus.Active, BomVersionStatus.Superseded) => true,
            (BomVersionStatus.Active, BomVersionStatus.Obsolete) => true,
            _ => false
        };

        if (!valid)
            throw new BomRuleException(BomErrors.VersionTransitionInvalid);

        Status = target;
        return true;
    }
}
