using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Routings;

namespace ForgeFlow.Application.Routings;

public static class RoutingPermissions
{
    public const string RoutingsRead = "Routings.Read";
    public const string RoutingsManage = "Routings.Manage";
    public const string OperationsExecute = "Operations.Execute";
}

public sealed record WorkCenterSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    string Code,
    string Name,
    WorkCenterType WorkCenterType,
    decimal CapacityUnitsPerHour,
    decimal DailyAvailableHours,
    decimal? HourlyCostRate,
    string? Notes,
    bool IsActive,
    string RowVersion);

public sealed record WorkCenterListQuery(
    long? SiteId = null,
    WorkCenterType? WorkCenterType = null,
    bool? IsActive = true,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateWorkCenterRequest(
    long OrganizationId,
    long SiteId,
    string? Code,
    string? Name,
    WorkCenterType WorkCenterType,
    decimal CapacityUnitsPerHour,
    decimal DailyAvailableHours = 8m,
    decimal? HourlyCostRate = null,
    string? Notes = null);

public sealed record UpdateWorkCenterRequest(
    long OrganizationId,
    long Id,
    string? Name,
    WorkCenterType WorkCenterType,
    decimal CapacityUnitsPerHour,
    decimal DailyAvailableHours,
    decimal? HourlyCostRate,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record SetWorkCenterActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record RoutingSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    string SiteCode,
    string SiteName,
    string Code,
    string Name,
    long ItemId,
    string ItemSku,
    string ItemName,
    long? BomVersionId,
    string? BomCode,
    int? BomVersionNumber,
    int VersionNumber,
    RoutingStatus Status,
    bool IsDefault,
    int OperationCount,
    decimal TotalSetupMinutes,
    decimal TotalRunMinutesPerUnit,
    string? Notes,
    DateTime? ActivatedAtUtc,
    string RowVersion);

public sealed record RoutingOperationSummary(
    long Id,
    int Sequence,
    string OperationCode,
    string OperationName,
    long WorkCenterId,
    string WorkCenterCode,
    string WorkCenterName,
    decimal SetupMinutes,
    decimal RunMinutesPerUnit,
    decimal QueueMinutes,
    decimal WaitMinutes,
    bool RequiresQualityCheck,
    string? Instructions);

public sealed record RoutingDetails(
    RoutingSummary Summary,
    IReadOnlyList<RoutingOperationSummary> Operations);

public sealed record RoutingOperationInput(
    int Sequence,
    string? OperationCode,
    string? OperationName,
    long WorkCenterId,
    decimal SetupMinutes,
    decimal RunMinutesPerUnit,
    decimal QueueMinutes = 0m,
    decimal WaitMinutes = 0m,
    bool RequiresQualityCheck = false,
    string? Instructions = null);

public sealed record RoutingListQuery(
    long? SiteId = null,
    long? ItemId = null,
    RoutingStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateRoutingRequest(
    long OrganizationId,
    long SiteId,
    string? Code,
    string? Name,
    long ItemId,
    long? BomVersionId = null,
    int VersionNumber = 1,
    bool IsDefault = false,
    string? Notes = null);

public sealed record UpdateDraftRoutingRequest(
    long OrganizationId,
    long Id,
    string? Name,
    long? BomVersionId,
    bool IsDefault,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record SaveRoutingOperationsRequest(
    long OrganizationId,
    long RoutingId,
    IReadOnlyList<RoutingOperationInput> Operations,
    string? ExpectedRowVersion);

public sealed record ActivateRoutingRequest(
    long OrganizationId,
    long Id,
    string? ExpectedRowVersion);

public sealed record MarkRoutingObsoleteRequest(
    long OrganizationId,
    long Id,
    string? ExpectedRowVersion);

public sealed record ProductionOrderOperationSummary(
    long Id,
    long OrganizationId,
    long SiteId,
    long ProductionOrderId,
    string? ProductionOrderNumber,
    int Sequence,
    string OperationCode,
    string OperationName,
    long WorkCenterId,
    string WorkCenterCode,
    string WorkCenterName,
    decimal PlannedSetupMinutes,
    decimal PlannedRunMinutes,
    decimal PlannedQueueMinutes,
    decimal PlannedWaitMinutes,
    decimal PlannedTotalMinutes,
    decimal ActualSetupMinutes,
    decimal ActualRunMinutes,
    decimal CompletedQuantity,
    decimal ScrapQuantity,
    bool RequiresQualityCheck,
    string? Instructions,
    string? OperatorNotes,
    ProductionOrderOperationStatus Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string RowVersion);

public sealed record StartProductionOrderOperationRequest(
    long OrganizationId,
    long OperationId,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record PauseProductionOrderOperationRequest(
    long OrganizationId,
    long OperationId,
    decimal ActualSetupMinutes,
    decimal ActualRunMinutes,
    decimal CompletedQuantity,
    decimal ScrapQuantity,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record CompleteProductionOrderOperationRequest(
    long OrganizationId,
    long OperationId,
    decimal ActualSetupMinutes,
    decimal ActualRunMinutes,
    decimal CompletedQuantity,
    decimal ScrapQuantity,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record SkipProductionOrderOperationRequest(
    long OrganizationId,
    long OperationId,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record WorkCenterLoadSummary(
    long WorkCenterId,
    long SiteId,
    string WorkCenterCode,
    string WorkCenterName,
    decimal DailyAvailableHours,
    int ActiveOperationCount,
    decimal TotalPlannedMinutes,
    decimal UtilizationPercent);

public sealed record ResolvedRoutingOperationSnapshotWrite(
    int Sequence,
    string OperationCode,
    string OperationName,
    long WorkCenterId,
    decimal PlannedSetupMinutes,
    decimal PlannedRunMinutes,
    decimal PlannedQueueMinutes,
    decimal PlannedWaitMinutes,
    bool RequiresQualityCheck,
    string? Instructions);

public sealed record ResolvedRoutingSnapshotWrite(
    long RoutingId,
    string RoutingCode,
    IReadOnlyList<ResolvedRoutingOperationSnapshotWrite> Operations);

public sealed class RoutingStoreUnavailableException(Exception inner)
    : Exception(RoutingErrors.PersistenceUnavailable, inner);

public interface IRoutingOperationsStore
{
    Task<OrganizationPage<WorkCenterSummary>> ListWorkCenters(long organizationId, WorkCenterListQuery query, CancellationToken ct);
    Task<IReadOnlyList<WorkCenterSummary>> ListActiveWorkCenters(long organizationId, long? siteId, CancellationToken ct);
    Task<WorkCenterSummary?> GetWorkCenter(long organizationId, long id, CancellationToken ct);

    Task<OrganizationResult<WorkCenterSummary>> CreateWorkCenter(
        CreateWorkCenterRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<WorkCenterSummary>> UpdateWorkCenter(
        long organizationId,
        long id,
        byte[] version,
        UpdateWorkCenterRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<WorkCenterSummary>> SetWorkCenterActive(
        long organizationId,
        long id,
        byte[] version,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationPage<RoutingSummary>> ListRoutings(long organizationId, RoutingListQuery query, CancellationToken ct);
    Task<RoutingDetails?> GetRouting(long organizationId, long id, CancellationToken ct);
    Task<IReadOnlyList<RoutingSummary>> ListActiveRoutingsForItem(long organizationId, long siteId, long itemId, CancellationToken ct);
    Task<RoutingDetails?> ResolveRoutingForProductionOrder(long organizationId, long siteId, long itemId, long? requestedRoutingId, CancellationToken ct);

    Task<OrganizationResult<RoutingDetails>> CreateDraftRouting(
        CreateRoutingRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<RoutingDetails>> UpdateDraftRouting(
        long organizationId,
        long id,
        byte[] version,
        UpdateDraftRoutingRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<RoutingDetails>> SaveRoutingOperations(
        long organizationId,
        long routingId,
        byte[] version,
        IReadOnlyList<RoutingOperationInput> operations,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<RoutingDetails>> ActivateRouting(
        long organizationId,
        long id,
        byte[] version,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<RoutingDetails>> MarkRoutingObsolete(
        long organizationId,
        long id,
        byte[] version,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<ProductionOrderOperationSummary?> GetProductionOrderOperation(long organizationId, long operationId, CancellationToken ct);
    Task<IReadOnlyList<ProductionOrderOperationSummary>> ListOperationsForOrder(long organizationId, long productionOrderId, CancellationToken ct);

    Task<OrganizationResult<ProductionOrderOperationSummary>> StartOperation(
        long organizationId,
        long operationId,
        byte[] version,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderOperationSummary>> PauseOperation(
        long organizationId,
        long operationId,
        byte[] version,
        decimal actualSetupMinutes,
        decimal actualRunMinutes,
        decimal completedQuantity,
        decimal scrapQuantity,
        string? notes,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderOperationSummary>> CompleteOperation(
        long organizationId,
        long operationId,
        byte[] version,
        decimal actualSetupMinutes,
        decimal actualRunMinutes,
        decimal completedQuantity,
        decimal scrapQuantity,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<OrganizationResult<ProductionOrderOperationSummary>> SkipOperation(
        long organizationId,
        long operationId,
        byte[] version,
        string? notes,
        DateTime utcNow,
        OrganizationWriteAccess access,
        CancellationToken ct);

    Task<IReadOnlyList<WorkCenterLoadSummary>> GetWorkCenterLoads(long organizationId, long? siteId, CancellationToken ct);
}
