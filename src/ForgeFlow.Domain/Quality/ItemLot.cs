namespace ForgeFlow.Domain.Quality;

/// <summary>Organization-level lot metadata (manufacture date, expiration date, notes) keyed by (OrganizationId, ItemId, LotCode).</summary>
public sealed class ItemLot
{
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

    public bool IsExpiredOn(DateOnly date) => ExpirationDate.HasValue && ExpirationDate.Value < date;
}
