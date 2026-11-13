using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Quality.Slices;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Quality;

/// <summary>
/// Facade for Quality Control, Quarantine Hold, Lot Metadata, and Multi-Level Lot Genealogy.
/// </summary>
public sealed class QualityUseCases(
    IQualityOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private readonly QualitySliceSupport support = new(actors, permissions, operationRecorder, opContext, clock);
    private readonly QualityInspectionSlices inspectionSlices = new(
        store,
        new QualitySliceSupport(actors, permissions, operationRecorder, opContext, clock));
    private readonly QualityGenealogySlices genealogySlices = new(
        store,
        new QualitySliceSupport(actors, permissions, operationRecorder, opContext, clock));

    public Task<OrganizationResult<OrganizationPage<QualityInspectionSummary>>> ListInspections(
        OrganizationScope? scope,
        long organizationId,
        QualityInspectionListQuery query,
        CancellationToken ct = default)
        => inspectionSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> GetInspection(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => inspectionSlices.GetAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> CreateManualHold(
        OrganizationScope? scope,
        CreateManualQualityHoldRequest request,
        CancellationToken ct = default)
        => inspectionSlices.CreateManualHoldAsync(scope, request, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> UpdateMeasurements(
        OrganizationScope? scope,
        UpdateQualityMeasurementsRequest request,
        CancellationToken ct = default)
        => inspectionSlices.UpdateMeasurementsAsync(scope, request, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> RecordDisposition(
        OrganizationScope? scope,
        RecordQualityDispositionRequest request,
        CancellationToken ct = default)
        => inspectionSlices.RecordDispositionAsync(scope, request, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> CancelHold(
        OrganizationScope? scope,
        CancelQualityHoldRequest request,
        CancellationToken ct = default)
        => inspectionSlices.CancelHoldAsync(scope, request, ct);

    public Task<OrganizationResult<OrganizationPage<ItemLotSummary>>> ListItemLots(
        OrganizationScope? scope,
        long organizationId,
        ItemLotListQuery query,
        CancellationToken ct = default)
        => genealogySlices.ListItemLotsAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<ItemLotSummary>> UpsertItemLotMetadata(
        OrganizationScope? scope,
        UpsertItemLotMetadataRequest request,
        CancellationToken ct = default)
        => genealogySlices.UpsertItemLotMetadataAsync(scope, request, ct);

    public Task<OrganizationResult<LotTraceabilityReport>> TraceLot(
        OrganizationScope? scope,
        long organizationId,
        LotTraceQuery query,
        CancellationToken ct = default)
        => genealogySlices.TraceLotAsync(scope, organizationId, query, ct);

    public static LotTraceabilityReport? ComputeGenealogy(
        LotGenealogyDataset dataset,
        long rootItemId,
        string rootLotCode,
        LotTraceDirection direction = LotTraceDirection.Both)
        => QualityGenealogySlices.ComputeGenealogy(dataset, rootItemId, rootLotCode, direction);
}
