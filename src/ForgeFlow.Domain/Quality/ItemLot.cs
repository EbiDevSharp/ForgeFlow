using ForgeFlow.Domain.Common;
using ForgeFlow.Domain.Events;

namespace ForgeFlow.Domain.Quality;

/// <summary>Organization-level lot metadata (manufacture date, expiration date, notes) keyed by (OrganizationId, ItemId, LotCode).</summary>
public sealed class ItemLot : IHasRowVersion, IHasDomainEvents, IOrganizationEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
    public void ClearDomainEvents() => _domainEvents.Clear();

    private ItemLot() { }

    private ItemLot(
        long organizationId,
        long itemId,
        string lotCode,
        DateOnly? manufactureDate,
        DateOnly? expirationDate,
        string? notes)
    {
        if (organizationId <= 0) throw new QualityRuleException(QualityErrors.ParentOrganizationInactive);
        if (itemId <= 0) throw new QualityRuleException(QualityErrors.ItemInvalid);
        QualityText.ValidateDates(manufactureDate, expirationDate);

        OrganizationId = organizationId;
        ItemId = itemId;
        LotCode = QualityText.RequiredLotCode(lotCode);
        ManufactureDate = manufactureDate;
        ExpirationDate = expirationDate;
        Notes = QualityText.OptionalNote(notes, QualityText.MaxLotNoteLength);
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long ItemId { get; private set; }
    public string LotCode { get; private set; } = string.Empty;
    public DateOnly? ManufactureDate { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public string? Notes { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static ItemLot Create(
        long organizationId,
        long itemId,
        string? lotCode,
        DateOnly? manufactureDate = null,
        DateOnly? expirationDate = null,
        string? notes = null) =>
        new(organizationId, itemId, lotCode ?? string.Empty, manufactureDate, expirationDate, notes);

    public bool UpdateMetadata(DateOnly? manufactureDate, DateOnly? expirationDate, string? notes)
    {
        QualityText.ValidateDates(manufactureDate, expirationDate);
        var normalizedNotes = QualityText.OptionalNote(notes, QualityText.MaxLotNoteLength);

        if (ManufactureDate == manufactureDate && ExpirationDate == expirationDate && Notes == normalizedNotes)
            return false;

        ManufactureDate = manufactureDate;
        ExpirationDate = expirationDate;
        Notes = normalizedNotes;
        return true;
    }

    public void RecordCoa(string coaNumber, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coaNumber);
        _domainEvents.Add(new LotCoaIssuedEvent(OrganizationId, null, ItemId, LotCode, coaNumber, utcNow));
    }

    public bool IsExpiredOn(DateOnly date) => ExpirationDate.HasValue && ExpirationDate.Value < date;
}
