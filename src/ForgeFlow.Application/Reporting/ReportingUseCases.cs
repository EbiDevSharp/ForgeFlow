using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting.Slices;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Reporting;

namespace ForgeFlow.Application.Reporting;

/// <summary>
/// Read-only operational analytics and authorized CSV exports facade:
/// Material Shortage / Net Requirements and Production Consumption / Yield Variance.
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Reporting.Slices"/>.
/// </summary>
public sealed class ReportingUseCases
{
    private readonly ReportSiteSlices siteSlices;
    private readonly MaterialShortageSlices shortageSlices;
    private readonly ProductionVarianceSlices varianceSlices;

    public ReportingUseCases(
        IReportingQueryStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null)
    {
        var support = new ReportingSliceSupport(actors, permissions, operationRecorder, opContext);
        siteSlices = new ReportSiteSlices(store, support);
        shortageSlices = new MaterialShortageSlices(store, support);
        varianceSlices = new ProductionVarianceSlices(store, support);
    }

    public Task<OrganizationResult<IReadOnlyList<ReportSiteInfo>>> ListAuthorizedSites(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
        => siteSlices.ListAuthorizedSitesAsync(scope, organizationId, ct);

    public Task<OrganizationResult<MaterialShortageReport>> GetMaterialShortageReport(
        OrganizationScope? scope,
        long organizationId,
        MaterialShortageQuery query,
        CancellationToken ct = default)
        => shortageSlices.GetMaterialShortageReportAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<ProductionVarianceReport>> GetProductionVarianceReport(
        OrganizationScope? scope,
        long organizationId,
        ProductionVarianceQuery query,
        CancellationToken ct = default)
        => varianceSlices.GetProductionVarianceReportAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<ProductionVarianceDetail>> GetProductionVarianceDetail(
        OrganizationScope? scope,
        long organizationId,
        long productionOrderId,
        CancellationToken ct = default)
        => varianceSlices.GetProductionVarianceDetailAsync(scope, organizationId, productionOrderId, ct);

    public Task<OrganizationResult<CsvExportFile>> ExportMaterialShortageCsv(
        OrganizationScope? scope,
        long organizationId,
        MaterialShortageQuery query,
        CsvExportColumnHeaders columns,
        CancellationToken ct = default)
        => shortageSlices.ExportMaterialShortageCsvAsync(scope, organizationId, query, columns, ct);

    public Task<OrganizationResult<CsvExportFile>> ExportProductionVarianceCsv(
        OrganizationScope? scope,
        long organizationId,
        ProductionVarianceQuery query,
        CsvExportColumnHeaders columns,
        CancellationToken ct = default)
        => varianceSlices.ExportProductionVarianceCsvAsync(scope, organizationId, query, columns, ct);

    public static MaterialShortageReport ComputeMaterialShortage(ShortageDataset data, MaterialShortageQuery query)
        => MaterialShortageSlices.ComputeMaterialShortage(data, query);

    public static ProductionVarianceReport ComputeVarianceReport(VarianceDataset data)
        => ProductionVarianceSlices.ComputeVarianceReport(data);

    public static ProductionVarianceDetail ComputeVarianceDetail(ReportVarianceOrderRow order, VarianceDataset data)
        => ProductionVarianceSlices.ComputeVarianceDetail(order, data);

    public static CsvExportFile BuildCsvFile(
        string fileName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows) =>
        CsvExportBuilder.Build(fileName, headers, rows);

    public static string EscapeCsvCell(string? raw) =>
        CsvExportBuilder.EscapeCell(raw);
}
