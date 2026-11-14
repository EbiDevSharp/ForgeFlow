using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.BusinessPartners;

public sealed class BusinessPartner : IHasRowVersion
{
    private readonly List<BusinessPartnerRoleAssignment> _roles = [];

    public IReadOnlyCollection<BusinessPartnerRoleAssignment> Roles => _roles.AsReadOnly();

    private BusinessPartner()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private BusinessPartner(
        long organizationId,
        string? code,
        PersonType personType,
        string? name,
        string? firstName,
        string? lastName,
        string? nationalCode,
        string? companyName,
        string? nationalLegalId,
        string? economicCode,
        string? mobile,
        string? phone,
        string? email,
        string? website,
        string? country,
        string? province,
        string? city,
        string? address,
        string? postalCode,
        string? notes,
        IEnumerable<int> roleIds)
    {
        if (organizationId <= 0)
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.NotFound);

        OrganizationId = organizationId;
        Code = BusinessPartnerText.Code(code, BusinessPartnerErrors.CodeInvalid);
        PersonType = BusinessPartnerText.ValidatePersonType(personType);

        var resolvedName = !string.IsNullOrWhiteSpace(name)
            ? name
            : (personType == PersonType.Legal ? companyName : $"{firstName} {lastName}".Trim());
        Name = BusinessPartnerText.RequiredName(resolvedName);

        FirstName = BusinessPartnerText.OptionalText(firstName, 100, BusinessPartnerErrors.NameInvalid);
        LastName = BusinessPartnerText.OptionalText(lastName, 100, BusinessPartnerErrors.NameInvalid);
        NationalCode = BusinessPartnerText.OptionalText(nationalCode, 20, BusinessPartnerErrors.NationalCodeInvalid);
        CompanyName = BusinessPartnerText.OptionalText(companyName, 200, BusinessPartnerErrors.CompanyNameInvalid);
        NationalLegalId = BusinessPartnerText.OptionalText(nationalLegalId, 30, BusinessPartnerErrors.NationalLegalIdInvalid);
        EconomicCode = BusinessPartnerText.OptionalText(economicCode, 30, BusinessPartnerErrors.EconomicCodeInvalid);
        Mobile = BusinessPartnerText.OptionalPhone(mobile, BusinessPartnerErrors.MobileInvalid);
        Phone = BusinessPartnerText.OptionalPhone(phone, BusinessPartnerErrors.PhoneInvalid);
        Email = BusinessPartnerText.OptionalEmail(email);
        Website = BusinessPartnerText.OptionalText(website, 200, BusinessPartnerErrors.WebsiteInvalid);
        Country = BusinessPartnerText.OptionalText(country, 100, BusinessPartnerErrors.AddressInvalid);
        Province = BusinessPartnerText.OptionalText(province, 100, BusinessPartnerErrors.AddressInvalid);
        City = BusinessPartnerText.OptionalText(city, 100, BusinessPartnerErrors.AddressInvalid);
        Address = BusinessPartnerText.OptionalText(address, BusinessPartnerText.MaxAddressLength, BusinessPartnerErrors.AddressInvalid);
        PostalCode = BusinessPartnerText.OptionalText(postalCode, 20, BusinessPartnerErrors.AddressInvalid);
        Notes = BusinessPartnerText.OptionalText(notes, BusinessPartnerText.MaxNotesLength, BusinessPartnerErrors.NotesInvalid);
        IsActive = true;

        var rolesList = roleIds.Distinct().ToList();
        if (rolesList.Count == 0)
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.RoleRequired);

        foreach (var rId in rolesList)
            _roles.Add(new BusinessPartnerRoleAssignment(organizationId, 0, rId));
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public PersonType PersonType { get; private set; }
    public string Name { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? NationalCode { get; private set; }
    public string? CompanyName { get; private set; }
    public string? NationalLegalId { get; private set; }
    public string? EconomicCode { get; private set; }
    public string? Mobile { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public string? Country { get; private set; }
    public string? Province { get; private set; }
    public string? City { get; private set; }
    public string? Address { get; private set; }
    public string? PostalCode { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public byte[] RowVersion { get; private set; } = [];

    public bool HasRole(int roleId) => _roles.Any(r => r.RoleId == roleId);
    public bool IsSupplier => HasRole(BusinessPartnerRoleDefinition.RoleSupplier);
    public bool IsCustomer => HasRole(BusinessPartnerRoleDefinition.RoleCustomer);
    public bool IsContractor => HasRole(BusinessPartnerRoleDefinition.RoleContractor);

    public static BusinessPartner Create(
        long organizationId,
        string? code,
        PersonType personType,
        string? name,
        string? firstName,
        string? lastName,
        string? nationalCode,
        string? companyName,
        string? nationalLegalId,
        string? economicCode,
        string? mobile,
        string? phone,
        string? email,
        string? website,
        string? country,
        string? province,
        string? city,
        string? address,
        string? postalCode,
        string? notes,
        IEnumerable<int> roleIds)
        => new(organizationId, code, personType, name, firstName, lastName, nationalCode, companyName, nationalLegalId, economicCode, mobile, phone, email, website, country, province, city, address, postalCode, notes, roleIds);

    public bool Update(
        PersonType personType,
        string? name,
        string? firstName,
        string? lastName,
        string? nationalCode,
        string? companyName,
        string? nationalLegalId,
        string? economicCode,
        string? mobile,
        string? phone,
        string? email,
        string? website,
        string? country,
        string? province,
        string? city,
        string? address,
        string? postalCode,
        string? notes,
        IEnumerable<int> roleIds)
    {
        if (!IsActive)
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.Inactive);

        var nextPersonType = BusinessPartnerText.ValidatePersonType(personType);
        var resolvedName = !string.IsNullOrWhiteSpace(name)
            ? name
            : (personType == PersonType.Legal ? companyName : $"{firstName} {lastName}".Trim());
        var nextName = BusinessPartnerText.RequiredName(resolvedName);
        var nextFirst = BusinessPartnerText.OptionalText(firstName, 100, BusinessPartnerErrors.NameInvalid);
        var nextLast = BusinessPartnerText.OptionalText(lastName, 100, BusinessPartnerErrors.NameInvalid);
        var nextNatCode = BusinessPartnerText.OptionalText(nationalCode, 20, BusinessPartnerErrors.NationalCodeInvalid);
        var nextCompName = BusinessPartnerText.OptionalText(companyName, 200, BusinessPartnerErrors.CompanyNameInvalid);
        var nextNatLegalId = BusinessPartnerText.OptionalText(nationalLegalId, 30, BusinessPartnerErrors.NationalLegalIdInvalid);
        var nextEcon = BusinessPartnerText.OptionalText(economicCode, 30, BusinessPartnerErrors.EconomicCodeInvalid);
        var nextMobile = BusinessPartnerText.OptionalPhone(mobile, BusinessPartnerErrors.MobileInvalid);
        var nextPhone = BusinessPartnerText.OptionalPhone(phone, BusinessPartnerErrors.PhoneInvalid);
        var nextEmail = BusinessPartnerText.OptionalEmail(email);
        var nextWeb = BusinessPartnerText.OptionalText(website, 200, BusinessPartnerErrors.WebsiteInvalid);
        var nextCountry = BusinessPartnerText.OptionalText(country, 100, BusinessPartnerErrors.AddressInvalid);
        var nextProv = BusinessPartnerText.OptionalText(province, 100, BusinessPartnerErrors.AddressInvalid);
        var nextCity = BusinessPartnerText.OptionalText(city, 100, BusinessPartnerErrors.AddressInvalid);
        var nextAddress = BusinessPartnerText.OptionalText(address, BusinessPartnerText.MaxAddressLength, BusinessPartnerErrors.AddressInvalid);
        var nextPostal = BusinessPartnerText.OptionalText(postalCode, 20, BusinessPartnerErrors.AddressInvalid);
        var nextNotes = BusinessPartnerText.OptionalText(notes, BusinessPartnerText.MaxNotesLength, BusinessPartnerErrors.NotesInvalid);

        var newRoles = roleIds.Distinct().OrderBy(x => x).ToList();
        if (newRoles.Count == 0)
            throw new BusinessPartnerRuleException(BusinessPartnerErrors.RoleRequired);

        var currentRoles = _roles.Select(r => r.RoleId).OrderBy(x => x).ToList();
        var rolesChanged = !currentRoles.SequenceEqual(newRoles);

        if (PersonType == nextPersonType
            && string.Equals(Name, nextName, StringComparison.Ordinal)
            && string.Equals(FirstName, nextFirst, StringComparison.Ordinal)
            && string.Equals(LastName, nextLast, StringComparison.Ordinal)
            && string.Equals(NationalCode, nextNatCode, StringComparison.Ordinal)
            && string.Equals(CompanyName, nextCompName, StringComparison.Ordinal)
            && string.Equals(NationalLegalId, nextNatLegalId, StringComparison.Ordinal)
            && string.Equals(EconomicCode, nextEcon, StringComparison.Ordinal)
            && string.Equals(Mobile, nextMobile, StringComparison.Ordinal)
            && string.Equals(Phone, nextPhone, StringComparison.Ordinal)
            && string.Equals(Email, nextEmail, StringComparison.Ordinal)
            && string.Equals(Website, nextWeb, StringComparison.Ordinal)
            && string.Equals(Country, nextCountry, StringComparison.Ordinal)
            && string.Equals(Province, nextProv, StringComparison.Ordinal)
            && string.Equals(City, nextCity, StringComparison.Ordinal)
            && string.Equals(Address, nextAddress, StringComparison.Ordinal)
            && string.Equals(PostalCode, nextPostal, StringComparison.Ordinal)
            && string.Equals(Notes, nextNotes, StringComparison.Ordinal)
            && !rolesChanged)
        {
            return false;
        }

        PersonType = nextPersonType;
        Name = nextName;
        FirstName = nextFirst;
        LastName = nextLast;
        NationalCode = nextNatCode;
        CompanyName = nextCompName;
        NationalLegalId = nextNatLegalId;
        EconomicCode = nextEcon;
        Mobile = nextMobile;
        Phone = nextPhone;
        Email = nextEmail;
        Website = nextWeb;
        Country = nextCountry;
        Province = nextProv;
        City = nextCity;
        Address = nextAddress;
        PostalCode = nextPostal;
        Notes = nextNotes;

        if (rolesChanged)
        {
            _roles.Clear();
            foreach (var r in newRoles)
                _roles.Add(new BusinessPartnerRoleAssignment(OrganizationId, Id, r));
        }

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
