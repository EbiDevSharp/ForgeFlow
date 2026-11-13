using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units.Slices;

namespace ForgeFlow.Application.Units;

/// <summary>Secured vertical-slice facade for organization-scoped units of measure, multi-hop conversions, and currencies.</summary>
public sealed class UnitUseCases
{
    private readonly UnitCatalogSlices catalogSlices;
    private readonly UnitConversionSlices conversionSlices;
    private readonly CurrencySlices currencySlices;

    public UnitUseCases(
        IUnitOperationsStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null)
    {
        var support = new UnitSliceSupport(actors, permissions, operationRecorder, opContext);
        catalogSlices = new UnitCatalogSlices(store, support);
        conversionSlices = new UnitConversionSlices(store, support);
        currencySlices = new CurrencySlices(store, support);
    }

    public Task<OrganizationResult<OrganizationPage<UnitSummary>>> ListUnits(
        OrganizationScope? scope,
        long organizationId,
        UnitListQuery query,
        CancellationToken ct = default)
        => catalogSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<IReadOnlyList<UnitSummary>>> ListActiveUnits(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
        => catalogSlices.ListActiveAsync(scope, organizationId, ct);

    public Task<OrganizationResult<UnitSummary>> GetUnit(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => catalogSlices.GetAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<UnitSummary>> CreateUnit(
        OrganizationScope? scope,
        CreateUnitRequest request,
        CancellationToken ct = default)
        => catalogSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<UnitSummary>> UpdateUnit(
        OrganizationScope? scope,
        UpdateUnitRequest request,
        CancellationToken ct = default)
        => catalogSlices.UpdateAsync(scope, request, ct);

    public Task<OrganizationResult<UnitSummary>> SetUnitActive(
        OrganizationScope? scope,
        SetUnitActiveRequest request,
        CancellationToken ct = default)
        => catalogSlices.SetActiveAsync(scope, request, ct);

    public Task<OrganizationResult<OrganizationPage<UnitConversionSummary>>> ListConversions(
        OrganizationScope? scope,
        long organizationId,
        UnitConversionListQuery query,
        CancellationToken ct = default)
        => conversionSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<UnitConversionSummary>> GetConversion(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => conversionSlices.GetAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<UnitConversionSummary>> CreateConversion(
        OrganizationScope? scope,
        CreateUnitConversionRequest request,
        CancellationToken ct = default)
        => conversionSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<UnitConversionSummary>> UpdateConversionFactor(
        OrganizationScope? scope,
        UpdateUnitConversionFactorRequest request,
        CancellationToken ct = default)
        => conversionSlices.UpdateFactorAsync(scope, request, ct);

    public Task<OrganizationResult<UnitConversionSummary>> SetConversionActive(
        OrganizationScope? scope,
        SetUnitConversionActiveRequest request,
        CancellationToken ct = default)
        => conversionSlices.SetActiveAsync(scope, request, ct);

    public Task<OrganizationResult<UnitConversionPreviewResult>> PreviewConversion(
        OrganizationScope? scope,
        ConvertQuantityRequest request,
        CancellationToken ct = default)
        => conversionSlices.PreviewConversionAsync(scope, request, ct);

    public Task<OrganizationResult<SeedStandardUnitsResult>> SeedStandardUnits(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
        => conversionSlices.SeedStandardUnitsAsync(scope, organizationId, ct);

    public Task<OrganizationResult<OrganizationPage<CurrencySummary>>> ListCurrencies(
        OrganizationScope? scope,
        long organizationId,
        CurrencyListQuery query,
        CancellationToken ct = default)
        => currencySlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<IReadOnlyList<CurrencySummary>>> ListActiveCurrencies(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
        => currencySlices.ListActiveAsync(scope, organizationId, ct);

    public Task<OrganizationResult<CurrencySummary>> GetCurrency(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => currencySlices.GetAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<CurrencySummary>> CreateCurrency(
        OrganizationScope? scope,
        CreateCurrencyRequest request,
        CancellationToken ct = default)
        => currencySlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<CurrencySummary>> UpdateCurrency(
        OrganizationScope? scope,
        UpdateCurrencyRequest request,
        CancellationToken ct = default)
        => currencySlices.UpdateAsync(scope, request, ct);

    public Task<OrganizationResult<CurrencySummary>> SetCurrencyActive(
        OrganizationScope? scope,
        SetCurrencyActiveRequest request,
        CancellationToken ct = default)
        => currencySlices.SetActiveAsync(scope, request, ct);
}
