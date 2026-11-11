using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Routings;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Dashboard;

public sealed record DashboardProductionStats(
    int DraftCount,
    int ReleasedCount,
    int InProgressCount,
    int OverdueCount);

public sealed record DashboardOrderItemSummary(
    long Id,
    string OrderNumber,
    long SiteId,
    string SiteCode,
    string ItemCode,
    string ItemName,
    decimal RequestedQuantity,
    string UnitSymbol,
    DateOnly PlannedDueDate,
    ProductionOrderStatus Status,
    bool IsOverdue);

public sealed record DashboardShortageStats(
    int OpenOrderCount,
    int ShortageItemCount,
    decimal TotalReservedBaseQuantity,
    decimal TotalShortageBaseQuantity);

public sealed record DashboardShortageItem(
    long ItemId,
    string ItemCode,
    string ItemName,
    decimal RequiredBaseQuantity,
    decimal AvailableBaseQuantity,
    decimal ReservedBaseQuantity,
    decimal ShortageBaseQuantity,
    string BaseUnitSymbol);

public sealed record DashboardQualityStats(
    int PendingCount,
    int InReviewCount,
    decimal TotalBlockedBaseQuantity);

public sealed record DashboardQualityQueueItem(
    long Id,
    string InspectionNumber,
    string SiteCode,
    string ItemCode,
    string ItemName,
    string LotCode,
    decimal RemainingBlockedQuantity,
    string UnitSymbol,
    QualityInspectionStatus Status);

public sealed record DashboardProcurementStats(
    int ApprovedCount,
    int PartiallyReceivedCount,
    int OverduePoCount);

public sealed record DashboardPurchaseOrderItem(
    long Id,
    string OrderNumber,
    string SiteCode,
    string SupplierName,
    DateOnly? ExpectedDeliveryDate,
    PurchaseOrderStatus Status,
    bool IsOverdue);

public sealed record DashboardCapabilities(
    bool Production,
    bool Inventory,
    bool Reports,
    bool Quality,
    bool Procurement,
    bool Routings);

public sealed record DashboardSnapshot(
    DashboardCapabilities Capabilities,
    DashboardProductionStats? ProductionStats,
    IReadOnlyList<DashboardOrderItemSummary> AttentionOrders,
    DashboardShortageStats? ShortageStats,
    IReadOnlyList<DashboardShortageItem> TopShortages,
    DashboardQualityStats? QualityStats,
    IReadOnlyList<DashboardQualityQueueItem> PendingInspections,
    DashboardProcurementStats? ProcurementStats,
    IReadOnlyList<DashboardPurchaseOrderItem> OpenPurchaseOrders,
    IReadOnlyList<WorkCenterLoadSummary> WorkCenterLoads);

public interface IDashboardQueryStore
{
    Task<DashboardSnapshot> GetSnapshot(
        long organizationId,
        long? siteId,
        DateOnly today,
        DashboardCapabilities capabilities,
        CancellationToken ct);
}
