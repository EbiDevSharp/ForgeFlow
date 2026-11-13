using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.Quality;

public enum QualitySpecTargetKind : byte
{
    Item = 1,
    Classification = 2
}

public enum QualitySpecCheckpoint : byte
{
    Incoming = 1,
    Production = 2,
    All = 3
}

/// <summary>Inspection specification template attached to either an Item or an Item Classification.</summary>
public sealed class QualitySpec : IHasRowVersion
{
    private readonly List<QualitySpecLine> _lines = [];

    private QualitySpec() { }

    private QualitySpec(
        long organizationId,
        string? code,
        string? name,
        QualitySpecTargetKind targetKind,
        long? itemId,
        long? classificationId,
        QualitySpecCheckpoint checkpoint,
        int version,
        string? notes)
    {
        if (organizationId <= 0)
            throw new QualityRuleException(QualityErrors.ParentOrganizationInactive);
        if (!Enum.IsDefined(targetKind) || !Enum.IsDefined(checkpoint) || version <= 0)
            throw new QualityRuleException(QualityErrors.SpecTargetInvalid);

        if (targetKind == QualitySpecTargetKind.Item)
        {
            if (itemId is null or <= 0 || classificationId.HasValue)
                throw new QualityRuleException(QualityErrors.SpecTargetInvalid);
        }
        else
        {
            if (classificationId is null or <= 0 || itemId.HasValue)
                throw new QualityRuleException(QualityErrors.SpecTargetInvalid);
        }

        OrganizationId = organizationId;
        Code = QualityText.Code(code, QualityErrors.ParameterInvalid);
        Name = QualityText.SpecName(name);
        TargetKind = targetKind;
        ItemId = itemId;
        ClassificationId = classificationId;
        Checkpoint = checkpoint;
        Version = version;
        Notes = QualityText.OptionalNote(notes);
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public QualitySpecTargetKind TargetKind { get; private set; }
    public long? ItemId { get; private set; }
    public long? ClassificationId { get; private set; }
    public QualitySpecCheckpoint Checkpoint { get; private set; }
    public int Version { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<QualitySpecLine> Lines => _lines.AsReadOnly();

    public static QualitySpec Create(
        long organizationId,
        string? code,
        string? name,
        QualitySpecTargetKind targetKind,
        long? itemId,
        long? classificationId,
        QualitySpecCheckpoint checkpoint,
        int version,
        string? notes,
        IEnumerable<QualitySpecLineInputData> lines)
    {
        var spec = new QualitySpec(organizationId, code, name, targetKind, itemId, classificationId, checkpoint, version, notes);
        spec.ReplaceLines(lines);
        return spec;
    }

    public void UpdateHeader(string? name, QualitySpecCheckpoint checkpoint, string? notes)
    {
        if (!Enum.IsDefined(checkpoint))
            throw new QualityRuleException(QualityErrors.SpecTargetInvalid);
        Name = QualityText.SpecName(name);
        Checkpoint = checkpoint;
        Notes = QualityText.OptionalNote(notes);
    }

    public void ReplaceLines(IEnumerable<QualitySpecLineInputData> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var materialized = lines.ToList();
        if (materialized.Count == 0)
            throw new QualityRuleException(QualityErrors.SpecLinesRequired);

        _lines.Clear();
        var seenParams = new HashSet<long>();
        var seq = 1;
        foreach (var l in materialized)
        {
            if (!seenParams.Add(l.QualityParameterId))
                throw new QualityRuleException(QualityErrors.SpecRangeInvalid);

            _lines.Add(new QualitySpecLine(
                OrganizationId,
                Id,
                seq++,
                l.QualityParameterId,
                l.MinValue,
                l.MaxValue,
                l.TargetValue,
                l.ExpectedTextSpec,
                l.IsMandatory,
                l.Notes));
        }
    }

    public bool Activate()
    {
        if (IsActive) return false;
        IsActive = true;
        return true;
    }

    public bool Deactivate()
    {
        if (!IsActive) return false;
        IsActive = false;
        return true;
    }
}

public sealed record QualitySpecLineInputData(
    long QualityParameterId,
    decimal? MinValue,
    decimal? MaxValue,
    decimal? TargetValue,
    string? ExpectedTextSpec,
    bool IsMandatory,
    string? Notes);

public sealed class QualitySpecLine
{
    private QualitySpecLine() { }

    internal QualitySpecLine(
        long organizationId,
        long qualitySpecId,
        int sequence,
        long qualityParameterId,
        decimal? minValue,
        decimal? maxValue,
        decimal? targetValue,
        string? expectedTextSpec,
        bool isMandatory,
        string? notes)
    {
        if (organizationId <= 0) throw new QualityRuleException(QualityErrors.ParentOrganizationInactive);
        if (sequence <= 0) throw new QualityRuleException(QualityErrors.ParameterInvalid);
        if (qualityParameterId <= 0) throw new QualityRuleException(QualityErrors.ParameterNotFound);
        if (minValue.HasValue && maxValue.HasValue && maxValue.Value < minValue.Value)
            throw new QualityRuleException(QualityErrors.SpecRangeInvalid);

        OrganizationId = organizationId;
        QualitySpecId = qualitySpecId;
        Sequence = sequence;
        QualityParameterId = qualityParameterId;
        MinValue = minValue.HasValue ? decimal.Round(minValue.Value, 6, MidpointRounding.AwayFromZero) : null;
        MaxValue = maxValue.HasValue ? decimal.Round(maxValue.Value, 6, MidpointRounding.AwayFromZero) : null;
        TargetValue = targetValue.HasValue ? decimal.Round(targetValue.Value, 6, MidpointRounding.AwayFromZero) : null;
        ExpectedTextSpec = QualityText.OptionalSpecOrValue(expectedTextSpec);
        IsMandatory = isMandatory;
        Notes = QualityText.OptionalNote(notes, QualityText.MaxLineNoteLength);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long QualitySpecId { get; private set; }
    public int Sequence { get; private set; }
    public long QualityParameterId { get; private set; }
    public decimal? MinValue { get; private set; }
    public decimal? MaxValue { get; private set; }
    public decimal? TargetValue { get; private set; }
    public string? ExpectedTextSpec { get; private set; }
    public bool IsMandatory { get; private set; }
    public string? Notes { get; private set; }
}
