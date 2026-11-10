using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.ProductionOrders;

namespace ForgeFlow.Application.ProductionOrders;

public static class ProductionOrderPermissions
{
    public const string ProductionOrdersRead = "ProductionOrders.Read";
    public const string ProductionOrdersManage = "ProductionOrders.Manage";
}

public sealed record ProductionOrderSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    string OrderNumber,
    ProductionOrderStatus Status,
    long ItemId,
    string ItemCode,
    string ItemName,
    decimal RequestedQuantity,
    long RequestedUnitId,
    string RequestedUnitCode,
    string RequestedUnitSymbol,
    DateOnly PlannedStartDate,
    DateOnly PlannedDueDate,
    long BomId,
    string BomCodeSnapshot,
    long BomVersionId,
    int BomVersionNumberSnapshot,
    decimal BaseOutputQuantitySnapshot,
    long BaseOutputUnitIdSnapshot,
    string BaseOutputUnitSymbolSnapshot,
    decimal ActualProducedQuantity,
    decimal ActualScrapQuantity,
    string? Notes,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    DateTime? ReleasedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    string RowVersion);

public sealed record ProductionOrderInputSummary(
    long Id,
    int Level,
    int Sequence,
    long ParentItemId,
    string ParentItemCode,
    long ItemId,
    string ItemCode,
    string ItemName,
    decimal NetQuantity,
    decimal ScrapPercentage,
    decimal GrossQuantity,
    long UnitId,
    string UnitCode,
    string UnitSymbol,
    bool IsOptional,
    bool IsSubAssembly,
    long? ResolvedSubBomVersionId);

public sealed record ProductionOrderOutputSummary(
    long Id,
    int Level,
    int Sequence,
    long ItemId,
    string ItemCode,
    string ItemName,
    BomOutputType OutputType,
    decimal PlannedQuantity,
    long UnitId,
    string UnitCode,
    string UnitSymbol);

public sealed record ProductionOrderContextSummary(
    string DimensionCode,
    string DimensionValue);

public sealed record ProductionOrderDetails(
    ProductionOrderSummary Summary,
    IReadOnlyList<ProductionOrderInputSummary> Inputs,
    IReadOnlyList<ProductionOrderOutputSummary> Outputs,
    IReadOnlyList<ProductionOrderContextSummary> Contexts);

public sealed record ProductionOrderListQuery(
    long? SiteId = null,
    ProductionOrderStatus? Status = null,
    long? ItemId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateProductionOrderRequest(
    long OrganizationId,
    long SiteId,
    string? OrderNumber,
    long ItemId,
    decimal RequestedQuantity,
    long? RequestedUnitId,
    DateOnly PlannedStartDate,
    DateOnly PlannedDueDate,
    IReadOnlyDictionary<string, string>? ContextValues = null,
    string? Notes = null);

public sealed record UpdateDraftProductionOrderRequest(
    long OrganizationId,
    long Id,
    decimal RequestedQuantity,
    long? RequestedUnitId,
    DateOnly PlannedStartDate,
    DateOnly PlannedDueDate,
    IReadOnlyDictionary<string, string>? ContextValues = null,
    string? Notes = null,
    string? ExpectedRowVersion = null);

public sealed record RefreshProductionOrderSnapshotRequest(
    long OrganizationId,
    long Id,
    string? ExpectedRowVersion);

public sealed record ReleaseProductionOrderRequest(
    long OrganizationId,
    long Id,
    string? ExpectedRowVersion);

public sealed record StartProductionOrderRequest(
    long OrganizationId,
    long Id,
    string? ExpectedRowVersion);

public sealed record RecordProductionOrderActualsRequest(
    long OrganizationId,
    long Id,
    decimal ActualProducedQuantity,
    decimal ActualScrapQuantity,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record CompleteProductionOrderRequest(
    long OrganizationId,
    long Id,
    decimal ActualProducedQuantity,
    decimal ActualScrapQuantity,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record CancelProductionOrderRequest(
    long OrganizationId,
    long Id,
    string? Reason,
    string? ExpectedRowVersion);

public sealed record ProductionOrderSiteInfo(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    bool IsActive,
    bool OrganizationIsActive);

public sealed record ResolvedBomSnapshotWrite(
    long BomId,
    string BomCode,
    long BomVersionId,
    int VersionNumber,
    decimal BaseOutputQuantity,
    long BaseOutputUnitId,
    long EffectiveRequestedUnitId,
    IReadOnlyList<BomExplosionLineDto> ConsumedInputs,
    IReadOnlyList<BomExplosionOutputLineDto> ProducedOutputs,
    IReadOnlyDictionary<string, string> EffectiveContext);

public sealed class ProductionOrderStoreUnavailableException(Exception inner)
    : Exception(ProductionOrderErrors.PersistenceUnavailable, inner);

public interface IProductionOrderOperationsStore
{
    Task<OrganizationPage<ProductionOrderSummary>> ListOrders(long organizationId, ProductionOrderListQuery query, CancellationToken ct);
    Task<ProductionOrderDetails?> GetOrder(long organizationId, long id, CancellationToken ct);
    Task<ProductionOrderSiteInfo?> GetSiteInfo(long organizationId, long siteId, CancellationToken ct);
    Task<IReadOnlyList<ProductionOrderSiteInfo>> ListActiveSites(long organizationId, CancellationToken ct);

    Task<OrganizationResult<ProductionOrderDetails>> CreateDraft(
        CreateProductionOrderRequest request,
        ResolvedBomSnapshotWrite snapshot,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderDetails>> UpdateDraft(
        long organizationId,
        long id,
        byte[] version,
        UpdateDraftProductionOrderRequest request,
        ResolvedBomSnapshotWrite snapshot,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderDetails>> RefreshDraftSnapshot(
        long organizationId,
        long id,
        byte[] version,
        ResolvedBomSnapshotWrite snapshot,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderDetails>> ReleaseOrder(
        long organizationId,
        long id,
        byte[] version,
        ResolvedBomSnapshotWrite snapshot,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderDetails>> StartOrder(
        long organizationId,
        long id,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderDetails>> RecordActuals(
        long organizationId,
        long id,
        byte[] version,
        decimal producedQuantity,
        decimal scrapQuantity,
        string? notes,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderDetails>> CompleteOrder(
        long organizationId,
        long id,
        byte[] version,
        decimal producedQuantity,
        decimal scrapQuantity,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderDetails>> CancelOrder(
        long organizationId,
        long id,
        byte[] version,
        string? reason,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);
}
