using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.Sales;

/// <summary>Organization-shared customer master.</summary>
public sealed class Customer : IHasRowVersion
{
    private Customer()
    {
        Code = null!;
        Name = null!;
    }

    private Customer(
        long organizationId,
        string? code,
        string? name,
        string? contactName,
        string? phone,
        string? email,
        string? shippingAddress,
        string? taxCode,
        string? notes)
    {
        if (organizationId <= 0)
            throw new SalesRuleException(SalesErrors.NotFound);

        OrganizationId = organizationId;
        Code = SalesText.Code(code, SalesErrors.CustomerCodeInvalid);
        Name = SalesText.RequiredName(name);
        ContactName = SalesText.OptionalText(contactName, SalesText.MaxContactLength, SalesErrors.ContactInvalid);
        Phone = SalesText.OptionalPhone(phone);
        Email = SalesText.OptionalEmail(email);
        ShippingAddress = SalesText.OptionalText(shippingAddress, SalesText.MaxAddressLength, SalesErrors.AddressInvalid);
        TaxCode = SalesText.OptionalText(taxCode, SalesText.MaxTaxCodeLength, SalesErrors.TaxCodeInvalid);
        Notes = SalesText.OptionalText(notes, SalesText.MaxNotesLength, SalesErrors.NotesInvalid);
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? ShippingAddress { get; private set; }
    public string? TaxCode { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public byte[] RowVersion { get; private set; } = [];

    public static Customer Create(
        long organizationId,
        string? code,
        string? name,
        string? contactName,
        string? phone,
        string? email,
        string? shippingAddress,
        string? taxCode,
        string? notes)
        => new(organizationId, code, name, contactName, phone, email, shippingAddress, taxCode, notes);

    public bool Update(
        string? name,
        string? contactName,
        string? phone,
        string? email,
        string? shippingAddress,
        string? taxCode,
        string? notes)
    {
        if (!IsActive)
            throw new SalesRuleException(SalesErrors.CustomerInactive);

        var nextName = SalesText.RequiredName(name);
        var nextContact = SalesText.OptionalText(contactName, SalesText.MaxContactLength, SalesErrors.ContactInvalid);
        var nextPhone = SalesText.OptionalPhone(phone);
        var nextEmail = SalesText.OptionalEmail(email);
        var nextAddress = SalesText.OptionalText(shippingAddress, SalesText.MaxAddressLength, SalesErrors.AddressInvalid);
        var nextTaxCode = SalesText.OptionalText(taxCode, SalesText.MaxTaxCodeLength, SalesErrors.TaxCodeInvalid);
        var nextNotes = SalesText.OptionalText(notes, SalesText.MaxNotesLength, SalesErrors.NotesInvalid);

        if (string.Equals(Name, nextName, StringComparison.Ordinal)
            && string.Equals(ContactName, nextContact, StringComparison.Ordinal)
            && string.Equals(Phone, nextPhone, StringComparison.Ordinal)
            && string.Equals(Email, nextEmail, StringComparison.Ordinal)
            && string.Equals(ShippingAddress, nextAddress, StringComparison.Ordinal)
            && string.Equals(TaxCode, nextTaxCode, StringComparison.Ordinal)
            && string.Equals(Notes, nextNotes, StringComparison.Ordinal))
        {
            return false;
        }

        Name = nextName;
        ContactName = nextContact;
        Phone = nextPhone;
        Email = nextEmail;
        ShippingAddress = nextAddress;
        TaxCode = nextTaxCode;
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
