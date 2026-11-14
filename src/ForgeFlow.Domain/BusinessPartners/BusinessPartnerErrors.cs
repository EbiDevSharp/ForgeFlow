using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.BusinessPartners;

public static class BusinessPartnerErrors
{
    public const string NotFound = "BusinessPartner.NotFound";
    public const string AccessDenied = "BusinessPartner.AccessDenied";
    public const string CodeInvalid = "BusinessPartner.CodeInvalid";
    public const string NameInvalid = "BusinessPartner.NameInvalid";
    public const string PersonTypeInvalid = "BusinessPartner.PersonTypeInvalid";
    public const string NationalCodeInvalid = "BusinessPartner.NationalCodeInvalid";
    public const string CompanyNameInvalid = "BusinessPartner.CompanyNameInvalid";
    public const string NationalLegalIdInvalid = "BusinessPartner.NationalLegalIdInvalid";
    public const string EconomicCodeInvalid = "BusinessPartner.EconomicCodeInvalid";
    public const string MobileInvalid = "BusinessPartner.MobileInvalid";
    public const string PhoneInvalid = "BusinessPartner.PhoneInvalid";
    public const string EmailInvalid = "BusinessPartner.EmailInvalid";
    public const string WebsiteInvalid = "BusinessPartner.WebsiteInvalid";
    public const string AddressInvalid = "BusinessPartner.AddressInvalid";
    public const string NotesInvalid = "BusinessPartner.NotesInvalid";
    public const string RoleRequired = "BusinessPartner.RoleRequired";
    public const string Inactive = "BusinessPartner.Inactive";
    public const string DuplicateCode = "BusinessPartner.DuplicateCode";
    public const string Conflict = "BusinessPartner.Conflict";
}

public sealed class BusinessPartnerRuleException(string code, params (string Key, object Value)[] parameters) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object> Parameters { get; } =
        parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
}
