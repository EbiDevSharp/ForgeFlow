using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Quality;

public static class QualityPermissions
{
    public const string QualityRead = "Quality.Read";
    public const string QualityManage = "Quality.Manage";
}

public sealed record QualityInspectionSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    long WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    long ItemId,
    string ItemCode,
    string ItemName,
    string LotCode,
    string InspectionNumber,
    QualityInspectionSourceType SourceType,
    long? SourceTransactionId,
    string? SourceDocumentNumber,
    long? ProductionOrderId,
    string? ProductionOrderNumber,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    decimal InspectedBaseQuantity,
    decimal AcceptedBaseQuantity,
    decimal RejectedBaseQuantity,
    decimal RemainingBlockedBaseQuantity,
    QualityInspectionStatus Status,
    string? DefectReasonCode,
    string? Notes,
    DateTime? InspectedAtUtc,
    long? InspectedByActorId,
    string? InspectedByActorName,
    string RowVersion);

public sealed record QualityInspectionLineSummary(
    long Id,
    int LineNumber,
    string ParameterName,
    string? ExpectedSpec,
    string? MeasuredValue,
    bool IsPassed,
    string? Notes);

public sealed record QualityInspectionDetails(
    QualityInspectionSummary Summary,
    IReadOnlyList<QualityInspectionLineSummary> Lines);

public sealed record QualityInspectionListQuery(
    long? SiteId = null,
    long? WarehouseId = null,
    long? ItemId = null,
    QualityInspectionStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record QualityInspectionLineInput(
    string ParameterName,
    string? ExpectedSpec,
    string? MeasuredValue,
    bool IsPassed,
    string? Notes = null);

public sealed record CreateManualQualityHoldRequest(
    long OrganizationId,
    long WarehouseId,
    long ItemId,
    string? LotCode,
    string? InspectionNumber,
    decimal Quantity,
    long? UnitId = null,
    string? DefectReasonCode = null,
    string? Notes = null);

public sealed record UpdateQualityMeasurementsRequest(
    long OrganizationId,
    long InspectionId,
    string? DefectReasonCode,
    string? Notes,
    IReadOnlyList<QualityInspectionLineInput> Lines,
    string? ExpectedRowVersion);

public sealed record RecordQualityDispositionRequest(
    long OrganizationId,
    long InspectionId,
    decimal AcceptQuantity,
    decimal RejectQuantity,
    long? UnitId = null,
    string? DefectReasonCode = null,
    string? Notes = null,
    string? ScrapDocumentNumber = null,
    string? ExpectedRowVersion = null);

public sealed record CancelQualityHoldRequest(
    long OrganizationId,
    long InspectionId,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record ItemLotSummary(
    long Id,
    long OrganizationId,
    long ItemId,
    string ItemCode,
    string ItemName,
    string LotCode,
    DateOnly? ManufactureDate,
    DateOnly? ExpirationDate,
    bool IsExpired,
    bool IsExpiringSoon,
    string? Notes,
    string RowVersion);

public sealed record ItemLotListQuery(
    long? ItemId = null,
    string? Search = null,
    bool OnlyExpiredOrExpiring = false,
    int Page = 1,
    int PageSize = 25);

public sealed record UpsertItemLotMetadataRequest(
    long OrganizationId,
    long ItemId,
    string? LotCode,
    DateOnly? ManufactureDate,
    DateOnly? ExpirationDate,
    string? Notes,
    string? ExpectedRowVersion = null);

public enum LotTraceDirection : byte
{
    Both = 0,
    Forward = 1,
    Backward = 2
}

public sealed record LotTraceQuery(
    long ItemId,
    string LotCode,
    long? SiteId = null,
    LotTraceDirection Direction = LotTraceDirection.Both);

public sealed record LotTraceMovementSummary(
    long TransactionId,
    string DocumentNumber,
    InventoryTransactionType TransactionType,
    long SiteId,
    string SiteCode,
    long WarehouseId,
    string WarehouseCode,
    long? ProductionOrderId,
    string? ProductionOrderNumber,
    long? QualityInspectionId,
    decimal BaseQuantityDelta,
    string BaseUnitSymbol,
    DateTime OccurredAtUtc,
    bool IsReversed,
    string? Notes);

public sealed record LotGenealogyEdge(
    int Level,
    string Direction,
    long ProductionOrderId,
    string OrderNumber,
    long SiteId,
    string SiteCode,
    ProductionOrderStatus OrderStatus,
    long SourceItemId,
    string SourceItemCode,
    string SourceItemName,
    string SourceLotCode,
    decimal SourceNetBaseQuantity,
    string SourceUnitSymbol,
    long TargetItemId,
    string TargetItemCode,
    string TargetItemName,
    string TargetLotCode,
    decimal TargetNetBaseQuantity,
    string TargetUnitSymbol);

public sealed record LotTraceNodeSummary(
    long ItemId,
    string ItemCode,
    string ItemName,
    string LotCode,
    string BaseUnitSymbol,
    DateOnly? ManufactureDate,
    DateOnly? ExpirationDate,
    bool IsExpired,
    bool IsExpiringSoon,
    decimal TotalOnHandBaseQuantity,
    decimal TotalBlockedBaseQuantity,
    decimal TotalAvailableBaseQuantity,
    IReadOnlyList<QualityInspectionSummary> Inspections,
    IReadOnlyList<LotTraceMovementSummary> Movements);

public sealed record LotTraceabilityReport(
    LotTraceNodeSummary RootLot,
    IReadOnlyList<LotGenealogyEdge> BackwardEdges,
    IReadOnlyList<LotGenealogyEdge> ForwardEdges,
    IReadOnlyList<LotTraceNodeSummary> RelatedLots);

public sealed record LotGenealogyOrderRow(
    long ProductionOrderId,
    string OrderNumber,
    long SiteId,
    string SiteCode,
    ProductionOrderStatus Status);

public sealed record LotGenealogyMovementRow(
    long TransactionId,
    InventoryTransactionType TransactionType,
    long? ReversedTransactionId,
    long? QualityInspectionId,
    long? ProductionOrderId,
    string? ProductionOrderNumber,
    long SiteId,
    string SiteCode,
    long WarehouseId,
    string WarehouseCode,
    string DocumentNumber,
    long ItemId,
    string ItemCode,
    string ItemName,
    string LotCode,
    decimal BaseQuantityDelta,
    string BaseUnitSymbol,
    DateTime OccurredAtUtc,
    string? Notes);

public sealed record LotGenealogyDataset(
    IReadOnlyList<LotGenealogyOrderRow> Orders,
    IReadOnlyList<LotGenealogyMovementRow> Movements,
    IReadOnlyList<ItemLotSummary> Lots,
    IReadOnlyList<InventoryBalanceSummary> Balances,
    IReadOnlyList<QualityInspectionSummary> Inspections);

public sealed record QualityInspectionBalanceTarget(
    long OrganizationId,
    long SiteId,
    long WarehouseId,
    long ItemId,
    string LotCode,
    long BaseUnitId,
    decimal AvailableBaseQuantity,
    IReadOnlyList<UnitConversionEdge> UnitEdges);

public sealed class QualityStoreUnavailableException(Exception inner)
    : Exception(QualityErrors.PersistenceUnavailable, inner);

public interface IQualityOperationsStore
{
    Task<OrganizationPage<QualityInspectionSummary>> ListInspections(long organizationId, QualityInspectionListQuery query, CancellationToken ct);
    Task<QualityInspectionDetails?> GetInspection(long organizationId, long id, CancellationToken ct);
    Task<QualityInspectionBalanceTarget?> GetHoldTargetLookup(long organizationId, long warehouseId, long itemId, string lotCode, CancellationToken ct);
    Task<IReadOnlyList<UnitConversionEdge>> LoadItemUnitEdges(long organizationId, long itemId, CancellationToken ct);

    Task<OrganizationResult<QualityInspectionDetails>> CreateManualHold(
        CreateManualQualityHoldRequest request,
        long siteId,
        string inspectionNumber,
        string normalizedLotCode,
        long baseUnitId,
        decimal holdBaseQuantity,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<QualityInspectionDetails>> UpdateMeasurements(
        long organizationId,
        long inspectionId,
        byte[] version,
        string? defectReasonCode,
        string? notes,
        IReadOnlyList<QualityInspectionLineInput> lines,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<QualityInspectionDetails>> RecordDisposition(
        long organizationId,
        long inspectionId,
        byte[] version,
        decimal acceptBaseQuantity,
        decimal rejectBaseQuantity,
        string? defectReasonCode,
        string? notes,
        string? scrapDocumentNumber,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<QualityInspectionDetails>> CancelHold(
        long organizationId,
        long inspectionId,
        byte[] version,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationPage<ItemLotSummary>> ListItemLots(long organizationId, ItemLotListQuery query, DateOnly today, CancellationToken ct);
    Task<OrganizationResult<ItemLotSummary>> UpsertItemLotMetadata(UpsertItemLotMetadataRequest request, DateOnly today, OrganizationWriteAccess access, CancellationToken ct);
    Task<LotGenealogyDataset> GetGenealogyDataset(long organizationId, long? forcedSiteId, DateOnly today, CancellationToken ct);
}
