using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Reporting;

public static class ReportingPermissions
{
    public const string ReportsRead = "Reports.Read";
    public const string ReportsExport = "Reports.Export";
}

public sealed class ReportingStoreUnavailableException(Exception? inner = null)
    : Exception("Reporting store unavailable.", inner);

public sealed record MaterialShortageQuery(
    long? SiteId = null,
    DateOnly? DueFrom = null,
    DateOnly? DueTo = null,
    bool OnlyShortages = false,
    string? Search = null);

public sealed record MaterialShortageOrderDetail(
    long ProductionOrderId,
    string OrderNumber,
    long SiteId,
    string SiteCode,
    DateOnly? PlannedDueDate,
    ProductionOrderStatus Status,
    decimal RequiredBaseQuantity);

public sealed record MaterialShortageItemSummary(
    long ItemId,
    string ItemCode,
    string ItemName,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    decimal RequiredBaseQuantity,
    decimal OnHandBaseQuantity,
    decimal ShortageBaseQuantity,
    decimal ExcessBaseQuantity,
    decimal CoveragePercent,
    int OpenOrderCount,
    IReadOnlyList<MaterialShortageOrderDetail> Orders,
    decimal BlockedBaseQuantity = 0m,
    decimal AvailableBaseQuantity = 0m,
    decimal OnOrderBaseQuantity = 0m,
    decimal ReservedBaseQuantity = 0m);

public sealed record MaterialShortageReport(
    int OpenOrderCount,
    int TotalMaterialCount,
    int ShortageMaterialCount,
    decimal TotalRequiredBaseQuantity,
    decimal TotalOnHandBaseQuantity,
    decimal TotalShortageBaseQuantity,
    IReadOnlyList<MaterialShortageItemSummary> Items,
    decimal TotalBlockedBaseQuantity = 0m,
    decimal TotalAvailableBaseQuantity = 0m,
    decimal TotalOnOrderBaseQuantity = 0m,
    decimal TotalReservedBaseQuantity = 0m);

public sealed record ProductionVarianceQuery(
    long? SiteId = null,
    ProductionOrderStatus? Status = null,
    DateOnly? DueFrom = null,
    DateOnly? DueTo = null,
    string? Search = null);

public sealed record ProductionVarianceSummary(
    long ProductionOrderId,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    string OrderNumber,
    ProductionOrderStatus Status,
    long ItemId,
    string ItemCode,
    string ItemName,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    DateOnly? PlannedStartDate,
    DateOnly? PlannedDueDate,
    decimal PlannedBaseQuantity,
    decimal ActualProducedBaseQuantity,
    decimal ActualScrapBaseQuantity,
    decimal PostedReceivedBaseQuantity,
    decimal OutputVarianceBaseQuantity,
    decimal YieldPercent,
    decimal TotalPlannedInputBaseQuantity,
    decimal TotalIssuedInputBaseQuantity,
    decimal TotalInputVarianceBaseQuantity,
    int MaterialShortageLineCount);

public sealed record ProductionInputVarianceLine(
    long ItemId,
    string ItemCode,
    string ItemName,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    decimal PlannedBaseQuantity,
    decimal IssuedBaseQuantity,
    decimal VarianceBaseQuantity,
    decimal RemainingToIssueBaseQuantity,
    decimal ConsumptionPercent);

public sealed record ProductionOutputVarianceLine(
    long ItemId,
    string ItemCode,
    string ItemName,
    BomOutputType OutputType,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    decimal PlannedBaseQuantity,
    decimal ReceivedBaseQuantity,
    decimal VarianceBaseQuantity,
    decimal AchievementPercent);

public sealed record ProductionVarianceDetail(
    ProductionVarianceSummary Summary,
    IReadOnlyList<ProductionInputVarianceLine> Inputs,
    IReadOnlyList<ProductionOutputVarianceLine> Outputs);

public sealed record ProductionVarianceReport(
    int TotalOrderCount,
    int OverConsumedOrderCount,
    int UnderYieldOrderCount,
    decimal AverageYieldPercent,
    IReadOnlyList<ProductionVarianceSummary> Orders);

public sealed record CsvExportColumnHeaders(
    IReadOnlyList<string> Headers,
    string FileName);

public sealed record CsvExportFile(
    string FileName,
    string ContentType,
    byte[] Bytes);

// Raw read-only dataset projections supplied by Infrastructure store for exact Application calculations.
public sealed record ReportItemRef(
    long ItemId,
    string ItemCode,
    string ItemName,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol);

public sealed record ReportConversionEdgeRow(
    long? ItemId,
    long FromUnitId,
    long ToUnitId,
    decimal Factor);

public sealed record ReportOpenOrderRow(
    long ProductionOrderId,
    string OrderNumber,
    long SiteId,
    string SiteCode,
    DateOnly? PlannedDueDate,
    ProductionOrderStatus Status);

public sealed record ReportOpenOrderInputRow(
    long ProductionOrderId,
    long ItemId,
    decimal GrossQuantity,
    long UnitId);

public sealed record ReportStockBalanceRow(
    long SiteId,
    long ItemId,
    decimal OnHandBaseQuantity,
    decimal BlockedBaseQuantity = 0m,
    decimal ReservedBaseQuantity = 0m);

public sealed record ReportOpenPurchaseLineRow(
    long SiteId,
    long ItemId,
    decimal RemainingBaseQuantity);

public sealed record ShortageDataset(
    IReadOnlyList<ReportOpenOrderRow> Orders,
    IReadOnlyList<ReportOpenOrderInputRow> Inputs,
    IReadOnlyList<ReportStockBalanceRow> Balances,
    IReadOnlyList<ReportItemRef> Items,
    IReadOnlyList<ReportConversionEdgeRow> Conversions,
    IReadOnlyList<ReportOpenPurchaseLineRow>? OpenPurchaseLines = null);

public sealed record ReportVarianceOrderRow(
    long ProductionOrderId,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    string OrderNumber,
    ProductionOrderStatus Status,
    long ItemId,
    decimal RequestedQuantity,
    long RequestedUnitId,
    DateOnly? PlannedStartDate,
    DateOnly? PlannedDueDate,
    decimal ActualProducedQuantity,
    decimal ActualScrapQuantity);

public sealed record ReportVarianceInputRow(
    long ProductionOrderId,
    long ItemId,
    decimal GrossQuantity,
    long UnitId,
    bool IsSubAssembly);

public sealed record ReportVarianceOutputRow(
    long ProductionOrderId,
    long ItemId,
    BomOutputType OutputType,
    decimal PlannedQuantity,
    long UnitId);

public sealed record ReportOrderLedgerRow(
    long ProductionOrderId,
    InventoryTransactionType RootTransactionType,
    long ItemId,
    decimal BaseQuantityDelta);

public sealed record VarianceDataset(
    IReadOnlyList<ReportVarianceOrderRow> Orders,
    IReadOnlyList<ReportVarianceInputRow> Inputs,
    IReadOnlyList<ReportVarianceOutputRow> Outputs,
    IReadOnlyList<ReportOrderLedgerRow> LedgerRows,
    IReadOnlyList<ReportItemRef> Items,
    IReadOnlyList<ReportConversionEdgeRow> Conversions);

public sealed record ReportSiteInfo(
    long SiteId,
    string SiteCode,
    string SiteName);

public interface IReportingQueryStore
{
    Task<IReadOnlyList<ReportSiteInfo>> ListAuthorizedSites(
        long organizationId,
        long? forcedSiteId,
        CancellationToken ct);

    Task<ShortageDataset> GetShortageDataset(
        long organizationId,
        long? siteId,
        DateOnly? dueFrom,
        DateOnly? dueTo,
        CancellationToken ct);

    Task<VarianceDataset> GetVarianceDataset(
        long organizationId,
        long? siteId,
        long? productionOrderId,
        ProductionOrderStatus? status,
        DateOnly? dueFrom,
        DateOnly? dueTo,
        string? search,
        CancellationToken ct);
}
