using ForgeFlow.Domain.BusinessPartners;

namespace ForgeFlow.Application.BusinessPartners;

public static class BusinessPartnerPermissions
{
    public const string Read = "BusinessPartners.Read";
    public const string Manage = "BusinessPartners.Manage";
}

public sealed record BusinessPartnerRoleDto(
    int Id,
    string Code,
    string Name);

public sealed record IranProvinceDto(
    int Id,
    string Name);

public sealed record IranCityDto(
    int Id,
    int ProvinceId,
    string Name);

public sealed record BusinessPartnerSummary(
    long Id,
    long OrganizationId,
    string Code,
    PersonType PersonType,
    string Name,
    string? NationalCode,
    string? CompanyName,
    string? NationalLegalId,
    string? EconomicCode,
    string? Mobile,
    string? Phone,
    string? Email,
    string? City,
    bool IsActive,
    IReadOnlyList<int> RoleIds,
    string RowVersion);

public sealed record BusinessPartnerDetail(
    long Id,
    long OrganizationId,
    string Code,
    PersonType PersonType,
    string Name,
    string? FirstName,
    string? LastName,
    string? NationalCode,
    string? CompanyName,
    string? NationalLegalId,
    string? EconomicCode,
    string? Mobile,
    string? Phone,
    string? Email,
    string? Website,
    string? Country,
    string? Province,
    string? City,
    string? Address,
    string? PostalCode,
    string? Notes,
    bool IsActive,
    IReadOnlyList<BusinessPartnerRoleDto> Roles,
    string RowVersion);

public sealed record CreateBusinessPartnerRequest(
    long OrganizationId,
    string? Code,
    PersonType PersonType,
    string? Name,
    string? FirstName,
    string? LastName,
    string? NationalCode,
    string? CompanyName,
    string? NationalLegalId,
    string? EconomicCode,
    string? Mobile,
    string? Phone,
    string? Email,
    string? Website,
    string? Country,
    string? Province,
    string? City,
    string? Address,
    string? PostalCode,
    string? Notes,
    IReadOnlyList<int> RoleIds);

public sealed record UpdateBusinessPartnerRequest(
    PersonType PersonType,
    string? Name,
    string? FirstName,
    string? LastName,
    string? NationalCode,
    string? CompanyName,
    string? NationalLegalId,
    string? EconomicCode,
    string? Mobile,
    string? Phone,
    string? Email,
    string? Website,
    string? Country,
    string? Province,
    string? City,
    string? Address,
    string? PostalCode,
    string? Notes,
    IReadOnlyList<int> RoleIds);

public sealed record BusinessPartnerListQuery(
    string? Search,
    int? RoleFilter,
    bool IncludeInactive,
    int Page,
    int PageSize,
    PersonType? PersonTypeFilter = null);

public enum PartnerLookupRole
{
    All = 0,
    Supplier = 1,
    Customer = 2,
    Contractor = 3
}

public sealed record PartnerLookupQuery(
    string? Search = null,
    PartnerLookupRole Role = PartnerLookupRole.All,
    int Page = 1,
    int PageSize = 10);

public sealed record PartnerLookupItem(
    long BusinessPartnerId,
    long TargetId,
    string Code,
    string Name,
    PersonType PersonType,
    string? CompanyName,
    string? NationalCode,
    string? NationalLegalId,
    string? EconomicCode,
    string? Mobile,
    string? Phone,
    string? City,
    IReadOnlyList<int> RoleIds);

