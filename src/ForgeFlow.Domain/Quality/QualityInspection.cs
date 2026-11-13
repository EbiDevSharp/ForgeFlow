using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.Quality;

/// <summary>Site-isolated quality inspection and quarantine hold record linked to stocked warehouse balances.</summary>
public sealed class QualityInspection : IHasRowVersion, IHasDomainEvents
{
    private readonly List<QualityInspectionLine> _lines = [];
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    private QualityInspection() { }

    private QualityInspection(
        long organizationId,
        long siteId,
        long warehouseId,
        long itemId,
        string? lotCode,
        string inspectionNumber,
        QualityInspectionSourceType sourceType,
        long? sourceTransactionId,
        long? productionOrderId,
        long baseUnitId,
        decimal inspectedBaseQuantity,
        string? notes)
    {
        if (organizationId <= 0) throw new QualityRuleException(QualityErrors.ParentOrganizationInactive);
        if (siteId <= 0) throw new QualityRuleException(QualityErrors.SiteInvalid);
        if (warehouseId <= 0) throw new QualityRuleException(QualityErrors.WarehouseInvalid);
        if (itemId <= 0) throw new QualityRuleException(QualityErrors.ItemInvalid);
        if (baseUnitId <= 0) throw new QualityRuleException(QualityErrors.UnitInvalid);
        if (!Enum.IsDefined(sourceType)) throw new QualityRuleException(QualityErrors.InspectionNotFound);

        var qty = QualityText.ValidatePositiveQuantity(inspectedBaseQuantity);

        OrganizationId = organizationId;
        SiteId = siteId;
        WarehouseId = warehouseId;
        ItemId = itemId;
        LotCode = QualityText.OptionalLotCode(lotCode);
        InspectionNumber = QualityText.Code(inspectionNumber);
        SourceType = sourceType;
        SourceTransactionId = sourceTransactionId;
        ProductionOrderId = productionOrderId;
        BaseUnitId = baseUnitId;
        InspectedBaseQuantity = qty;
        AcceptedBaseQuantity = 0m;
        RejectedBaseQuantity = 0m;
        RemainingBlockedBaseQuantity = qty;
        Status = QualityInspectionStatus.Pending;
        Notes = QualityText.OptionalNote(notes);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public long WarehouseId { get; private set; }
    public long ItemId { get; private set; }
    public string LotCode { get; private set; } = string.Empty;
    public string InspectionNumber { get; private set; } = string.Empty;
    public QualityInspectionSourceType SourceType { get; private set; }
    public long? SourceTransactionId { get; private set; }
    public long? ProductionOrderId { get; private set; }
    public long BaseUnitId { get; private set; }
    public decimal InspectedBaseQuantity { get; private set; }
    public decimal AcceptedBaseQuantity { get; private set; }
    public decimal RejectedBaseQuantity { get; private set; }
    public decimal RemainingBlockedBaseQuantity { get; private set; }
    public QualityInspectionStatus Status { get; private set; }
    public string? DefectReasonCode { get; private set; }
    public string? Notes { get; private set; }
    public string? CertificateNumber { get; private set; }
    public DateTime? InspectedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public long? InspectedByActorId { get; private set; }

    public IReadOnlyCollection<QualityInspectionLine> Lines => _lines.AsReadOnly();

    public static QualityInspection Create(
        long organizationId,
        long siteId,
        long warehouseId,
        long itemId,
        string? lotCode,
        string? inspectionNumber,
        QualityInspectionSourceType sourceType,
        long? sourceTransactionId,
        long? productionOrderId,
        long baseUnitId,
        decimal inspectedBaseQuantity,
        string? notes) =>
        new(organizationId, siteId, warehouseId, itemId, lotCode, inspectionNumber ?? string.Empty,
            sourceType, sourceTransactionId, productionOrderId, baseUnitId, inspectedBaseQuantity, notes);

    public void ReplaceLines(IEnumerable<(string ParameterName, string? ExpectedSpec, string? MeasuredValue, bool IsPassed, string? Notes)> lines) =>
        ReplaceLines(lines.Select(l => new QualityInspectionLineWriteData(l.ParameterName, l.ExpectedSpec, l.MeasuredValue, l.IsPassed, l.Notes)));

    public void ReplaceLines(IEnumerable<QualityInspectionLineWriteData> lines, bool advanceStatus = true)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(lines);

        _lines.Clear();
        var num = 1;
        foreach (var line in lines)
        {
            _lines.Add(new QualityInspectionLine(
                OrganizationId,
                Id,
                num++,
                line.ParameterName,
                line.ExpectedSpec,
                line.MeasuredValue,
                line.IsPassed,
                line.Notes,
                line.QualityParameterId,
                line.ParameterCodeSnapshot,
                line.SymbolSnapshot,
                line.UnitSymbolSnapshot,
                line.TestMethodSnapshot,
                line.DataType,
                line.MinValueSnapshot,
                line.MaxValueSnapshot,
                line.TargetValueSnapshot,
                line.MeasuredNumericValue,
                line.IsMandatory));
        }

        if (advanceStatus && Status == QualityInspectionStatus.Pending && _lines.Count > 0)
            Status = QualityInspectionStatus.InReview;
    }

    public void UpdateHeaderNotes(string? defectReasonCode, string? notes, string? certificateNumber = null)
    {
        EnsureMutable();
        DefectReasonCode = QualityText.OptionalDefectReason(defectReasonCode);
        Notes = QualityText.OptionalNote(notes);
        if (certificateNumber is not null)
            CertificateNumber = QualityText.OptionalCertificateNumber(certificateNumber);
        if (Status == QualityInspectionStatus.Pending)
            Status = QualityInspectionStatus.InReview;
    }

    public void RecordDisposition(
        decimal acceptBaseQuantity,
        decimal rejectBaseQuantity,
        string? defectReasonCode,
        string? notes,
        long actorId,
        DateTime utcNow)
    {
        EnsureMutable();
        if (actorId <= 0) throw new QualityRuleException(QualityErrors.ActorRequired);

        var accept = QualityText.ValidateNonNegativeQuantity(acceptBaseQuantity);
        var reject = QualityText.ValidateNonNegativeQuantity(rejectBaseQuantity);
        var totalAction = decimal.Round(accept + reject, 6, MidpointRounding.AwayFromZero);

        if (totalAction <= 0m)
            throw new QualityRuleException(QualityErrors.QuantityInvalid);
        if (totalAction > RemainingBlockedBaseQuantity)
            throw new QualityRuleException(QualityErrors.DispositionExceedsRemaining,
                ("remaining", RemainingBlockedBaseQuantity), ("requested", totalAction));

        if (reject > 0m && !string.IsNullOrWhiteSpace(defectReasonCode))
            DefectReasonCode = QualityText.OptionalDefectReason(defectReasonCode);
        else if (!string.IsNullOrWhiteSpace(defectReasonCode))
            DefectReasonCode = QualityText.OptionalDefectReason(defectReasonCode);

        if (notes is not null)
            Notes = QualityText.OptionalNote(notes);

        if (accept > 0m && _lines.Any(l => l.IsMandatory && !l.IsPassed)
            && string.IsNullOrWhiteSpace(DefectReasonCode) && string.IsNullOrWhiteSpace(Notes))
        {
            throw new QualityRuleException(QualityErrors.ConcessionReasonRequired);
        }

        AcceptedBaseQuantity = decimal.Round(AcceptedBaseQuantity + accept, 6, MidpointRounding.AwayFromZero);
        RejectedBaseQuantity = decimal.Round(RejectedBaseQuantity + reject, 6, MidpointRounding.AwayFromZero);
        RemainingBlockedBaseQuantity = decimal.Round(RemainingBlockedBaseQuantity - totalAction, 6, MidpointRounding.AwayFromZero);
        InspectedAtUtc = utcNow;
        InspectedByActorId = actorId;
        Status = RemainingBlockedBaseQuantity == 0m
            ? QualityInspectionStatus.Completed
            : QualityInspectionStatus.InReview;

        if (Status == QualityInspectionStatus.Completed)
            _domainEvents.Add(new QualityInspectionCompletedDomainEvent(OrganizationId, SiteId, InspectionNumber, utcNow));
    }

    public void Cancel(string? notes, long actorId, DateTime utcNow)
    {
        EnsureMutable();
        if (actorId <= 0) throw new QualityRuleException(QualityErrors.ActorRequired);
        if (AcceptedBaseQuantity > 0m || RejectedBaseQuantity > 0m)
            throw new QualityRuleException(QualityErrors.InspectionHasDispositions);

        if (notes is not null)
            Notes = QualityText.OptionalNote(notes);
        RemainingBlockedBaseQuantity = 0m;
        InspectedAtUtc = utcNow;
        InspectedByActorId = actorId;
        Status = QualityInspectionStatus.Cancelled;
        _domainEvents.Add(new QualityInspectionCancelledDomainEvent(OrganizationId, SiteId, InspectionNumber, utcNow));
    }

    private void EnsureMutable()
    {
        if (Status is QualityInspectionStatus.Completed or QualityInspectionStatus.Cancelled)
            throw new QualityRuleException(QualityErrors.InspectionAlreadyTerminal);
    }
}

public sealed record QualityInspectionCompletedDomainEvent(long OrganizationId, long SiteId, string InspectionNumber, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record QualityInspectionCancelledDomainEvent(long OrganizationId, long SiteId, string InspectionNumber, DateTime OccurredAtUtc) : IDomainEvent;
