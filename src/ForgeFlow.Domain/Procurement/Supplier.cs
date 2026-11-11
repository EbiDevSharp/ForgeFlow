namespace ForgeFlow.Domain.Procurement;

/// <summary>Organization-shared vendor/supplier master.</summary>
public sealed class Supplier
{
    private Supplier()
    {
        Code = null!;
        Name = null!;
    }

    private Supplier(
        long organizationId,
        string? code,
        string? name,
        string? contactName,
        string? phone,
        string? email,
        string? address,
        string? notes)
    {
        if (organizationId <= 0)
            throw new ProcurementRuleException(ProcurementErrors.NotFound);

        OrganizationId = organizationId;
        Code = ProcurementText.Code(code, ProcurementErrors.SupplierCodeInvalid);
        Name = ProcurementText.RequiredName(name);
        ContactName = ProcurementText.OptionalText(contactName, ProcurementText.MaxContactLength, ProcurementErrors.ContactInvalid);
        Phone = ProcurementText.OptionalPhone(phone);
        Email = ProcurementText.OptionalEmail(email);
        Address = ProcurementText.OptionalText(address, ProcurementText.MaxNotesLength, ProcurementErrors.AddressInvalid);
        Notes = ProcurementText.OptionalText(notes, ProcurementText.MaxNotesLength, ProcurementErrors.NotesInvalid);
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Supplier Create(
        long organizationId,
        string? code,
        string? name,
        string? contactName,
        string? phone,
        string? email,
        string? address,
        string? notes)
        => new(organizationId, code, name, contactName, phone, email, address, notes);

    public bool Update(
        string? name,
        string? contactName,
        string? phone,
        string? email,
        string? address,
        string? notes)
    {
        if (!IsActive)
            throw new ProcurementRuleException(ProcurementErrors.SupplierInactive);

        var nextName = ProcurementText.RequiredName(name);
        var nextContact = ProcurementText.OptionalText(contactName, ProcurementText.MaxContactLength, ProcurementErrors.ContactInvalid);
        var nextPhone = ProcurementText.OptionalPhone(phone);
        var nextEmail = ProcurementText.OptionalEmail(email);
        var nextAddress = ProcurementText.OptionalText(address, ProcurementText.MaxNotesLength, ProcurementErrors.AddressInvalid);
        var nextNotes = ProcurementText.OptionalText(notes, ProcurementText.MaxNotesLength, ProcurementErrors.NotesInvalid);

        if (string.Equals(Name, nextName, StringComparison.Ordinal)
            && string.Equals(ContactName, nextContact, StringComparison.Ordinal)
            && string.Equals(Phone, nextPhone, StringComparison.Ordinal)
            && string.Equals(Email, nextEmail, StringComparison.Ordinal)
            && string.Equals(Address, nextAddress, StringComparison.Ordinal)
            && string.Equals(Notes, nextNotes, StringComparison.Ordinal))
        {
            return false;
        }

        Name = nextName;
        ContactName = nextContact;
        Phone = nextPhone;
        Email = nextEmail;
        Address = nextAddress;
        Notes = nextNotes;
        return true;
    }

    public bool SetActive(bool isActive)
    {
        if (IsActive == isActive)
            return false;
        IsActive = isActive;
        return true;
    }
}
