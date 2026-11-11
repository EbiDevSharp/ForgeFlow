using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Units;

public static class UnitPermissions
{
    public const string UnitsRead = "Units.Read";
    public const string UnitsManage = "Units.Manage";
}

public sealed record UnitSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    string Symbol,
    UnitDimension Dimension,
    byte PrecisionScale,
    bool IsActive,
    string RowVersion);

public sealed record UnitConversionSummary(
    long Id,
    long OrganizationId,
    long FromUnitId,
    string FromUnitCode,
    string FromUnitName,
    string FromUnitSymbol,
    long ToUnitId,
    string ToUnitCode,
    string ToUnitName,
    string ToUnitSymbol,
    decimal Factor,
    bool IsActive,
    string RowVersion);

public sealed record UnitListQuery(
    string? Search = null,
    UnitDimension? Dimension = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record UnitConversionListQuery(
    long? UnitId = null,
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateUnitRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    string? Symbol,
    UnitDimension Dimension,
    byte PrecisionScale);

public sealed record UpdateUnitRequest(
    long OrganizationId,
    long Id,
    string? Name,
    string? Symbol,
    byte PrecisionScale,
    string? ExpectedRowVersion);

public sealed record SetUnitActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record CreateUnitConversionRequest(
    long OrganizationId,
    long FromUnitId,
    long ToUnitId,
    decimal Factor);

public sealed record UpdateUnitConversionFactorRequest(
    long OrganizationId,
    long Id,
    decimal Factor,
    string? ExpectedRowVersion);

public sealed record SetUnitConversionActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record ConvertQuantityRequest(
    long OrganizationId,
    long FromUnitId,
    long ToUnitId,
    decimal Quantity);

public sealed record UnitConversionPreviewResult(
    long FromUnitId,
    long ToUnitId,
    decimal InputQuantity,
    decimal EffectiveFactor,
    decimal ConvertedQuantity,
    int Hops);

public sealed record SeedStandardUnitsResult(
    int UnitsCreated,
    int ConversionsCreated,
    int CurrenciesCreated = 0);

public sealed record CurrencySummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    string Symbol,
    byte PrecisionScale,
    bool IsActive,
    string RowVersion);

public sealed record CurrencyListQuery(
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateCurrencyRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    string? Symbol,
    byte PrecisionScale);

public sealed record UpdateCurrencyRequest(
    long OrganizationId,
    long Id,
    string? Name,
    string? Symbol,
    byte PrecisionScale,
    string? ExpectedRowVersion);

public sealed record SetCurrencyActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed class UnitStoreUnavailableException(Exception inner)
    : Exception(UnitErrors.PersistenceUnavailable, inner);

public interface IUnitOperationsStore
{
    Task<OrganizationPage<UnitSummary>> ListUnits(long organizationId, UnitListQuery query, CancellationToken ct);
    Task<IReadOnlyList<UnitSummary>> ListActiveUnits(long organizationId, CancellationToken ct);
    Task<UnitSummary?> GetUnit(long organizationId, long id, CancellationToken ct);
    Task<OrganizationResult<UnitSummary>> CreateUnit(CreateUnitRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<UnitSummary>> UpdateUnit(long organizationId, long id, byte[] version, string? name, string? symbol, byte precisionScale, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<UnitSummary>> SetUnitActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationPage<UnitConversionSummary>> ListConversions(long organizationId, UnitConversionListQuery query, CancellationToken ct);
    Task<UnitConversionSummary?> GetConversion(long organizationId, long id, CancellationToken ct);
    Task<IReadOnlyList<UnitConversionEdge>> ListActiveConversionEdges(long organizationId, CancellationToken ct);
    Task<OrganizationResult<UnitConversionSummary>> CreateConversion(CreateUnitConversionRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<UnitConversionSummary>> UpdateConversionFactor(long organizationId, long id, byte[] version, decimal factor, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<UnitConversionSummary>> SetConversionActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationResult<SeedStandardUnitsResult>> SeedStandardUnits(long organizationId, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationPage<CurrencySummary>> ListCurrencies(long organizationId, CurrencyListQuery query, CancellationToken ct);
    Task<IReadOnlyList<CurrencySummary>> ListActiveCurrencies(long organizationId, CancellationToken ct);
    Task<CurrencySummary?> GetCurrency(long organizationId, long id, CancellationToken ct);
    Task<OrganizationResult<CurrencySummary>> CreateCurrency(CreateCurrencyRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<CurrencySummary>> UpdateCurrency(long organizationId, long id, byte[] version, string? name, string? symbol, byte precisionScale, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<CurrencySummary>> SetCurrencyActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);
}
