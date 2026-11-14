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
    int PageSize);
