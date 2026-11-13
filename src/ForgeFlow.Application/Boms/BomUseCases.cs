using ForgeFlow.Application.Boms.Slices;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Application.Boms;

/// <summary>
/// Facade for Manufacturing BOMs, Versions, Context Dimensions,
/// deterministic BOM Resolver, and multi-level BOM Explosion.
/// </summary>
public sealed class BomUseCases
{
    private readonly ContextDimensionSlices dimensionSlices;
    private readonly BomCatalogSlices catalogSlices;
    private readonly BomExplosionSlices explosionSlices;

    public BomUseCases(
        IBomOperationsStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null)
    {
        var support = new BomSliceSupport(actors, permissions, operationRecorder, opContext);
        dimensionSlices = new ContextDimensionSlices(store, support);
        catalogSlices = new BomCatalogSlices(store, support);
        explosionSlices = new BomExplosionSlices(store, support);
    }

    // --- BOM Catalog & Versions ---
    public Task<OrganizationResult<OrganizationPage<BomSummary>>> ListBoms(
        OrganizationScope? scope,
        long organizationId,
        BomListQuery query,
        CancellationToken ct = default)
        => catalogSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<BomDetails>> GetBom(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
        => catalogSlices.GetAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<BomVersionDetails>> GetBomVersion(
        OrganizationScope? scope,
        long organizationId,
        long bomVersionId,
        CancellationToken ct = default)
        => catalogSlices.GetVersionAsync(scope, organizationId, bomVersionId, ct);

    public Task<OrganizationResult<BomDetails>> CreateBom(
        OrganizationScope? scope,
        CreateBomRequest request,
        CancellationToken ct = default)
        => catalogSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<BomDetails>> UpdateBom(
        OrganizationScope? scope,
        UpdateBomRequest request,
        CancellationToken ct = default)
        => catalogSlices.UpdateAsync(scope, request, ct);

    public Task<OrganizationResult<BomDetails>> SetBomActive(
        OrganizationScope? scope,
        SetBomActiveRequest request,
        CancellationToken ct = default)
        => catalogSlices.SetActiveAsync(scope, request, ct);

    public Task<OrganizationResult<BomVersionDetails>> CreateBomVersion(
        OrganizationScope? scope,
        CreateBomVersionRequest request,
        CancellationToken ct = default)
        => catalogSlices.CreateVersionAsync(scope, request, ct);

    public Task<OrganizationResult<BomVersionDetails>> UpdateDraftBomVersion(
        OrganizationScope? scope,
        UpdateDraftBomVersionRequest request,
        CancellationToken ct = default)
        => catalogSlices.UpdateDraftVersionAsync(scope, request, ct);

    public Task<OrganizationResult<BomVersionDetails>> SaveDraftBomLines(
        OrganizationScope? scope,
        SaveDraftBomLinesRequest request,
        CancellationToken ct = default)
        => catalogSlices.SaveDraftLinesAsync(scope, request, ct);

    public Task<OrganizationResult<BomVersionDetails>> SaveDraftBomApplicability(
        OrganizationScope? scope,
        SaveDraftBomApplicabilityRequest request,
        CancellationToken ct = default)
        => catalogSlices.SaveDraftApplicabilityAsync(scope, request, ct);

    public Task<OrganizationResult<BomVersionDetails>> TransitionBomVersionStatus(
        OrganizationScope? scope,
        TransitionBomVersionStatusRequest request,
        CancellationToken ct = default)
        => catalogSlices.TransitionStatusAsync(scope, request, ct);

    // --- Context Dimensions ---
    public Task<OrganizationResult<OrganizationPage<ContextDimensionSummary>>> ListContextDimensions(
        OrganizationScope? scope,
        long organizationId,
        ContextDimensionListQuery query,
        CancellationToken ct = default)
        => dimensionSlices.ListAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<IReadOnlyList<ContextDimensionSummary>>> ListActiveContextDimensions(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
        => dimensionSlices.ListActiveAsync(scope, organizationId, ct);

    public Task<OrganizationResult<ContextDimensionSummary>> CreateContextDimension(
        OrganizationScope? scope,
        CreateContextDimensionRequest request,
        CancellationToken ct = default)
        => dimensionSlices.CreateAsync(scope, request, ct);

    public Task<OrganizationResult<ContextDimensionSummary>> UpdateContextDimension(
        OrganizationScope? scope,
        UpdateContextDimensionRequest request,
        CancellationToken ct = default)
        => dimensionSlices.UpdateAsync(scope, request, ct);

    public Task<OrganizationResult<ContextDimensionSummary>> SetContextDimensionActive(
        OrganizationScope? scope,
        SetContextDimensionActiveRequest request,
        CancellationToken ct = default)
        => dimensionSlices.SetActiveAsync(scope, request, ct);

    public Task<OrganizationResult<SeedContextDimensionsResult>> SeedStandardContextDimensions(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
        => dimensionSlices.SeedStandardAsync(scope, organizationId, ct);

    // --- BOM Resolution & Explosion ---
    public Task<OrganizationResult<BomResolutionResultDto>> ResolveBom(
        OrganizationScope? scope,
        ResolveBomRequest request,
        CancellationToken ct = default)
        => explosionSlices.ResolveAsync(scope, request, ct);

    public Task<OrganizationResult<BomExplosionResultDto>> ExplodeBom(
        OrganizationScope? scope,
        ExplodeBomRequest request,
        CancellationToken ct = default)
        => explosionSlices.ExplodeAsync(scope, request, ct);

    internal Task<OrganizationResult<(BomResolutionResultDto Resolution, BomExplosionResultDto Explosion)>> ResolveAndExplodeCoreAsync(
        long organizationId,
        long itemId,
        DateOnly productionDate,
        decimal requestedQuantity,
        long? requestedUnitIdInput,
        IReadOnlyDictionary<string, string>? contextValues,
        CancellationToken ct)
        => explosionSlices.ResolveAndExplodeCoreAsync(
            organizationId, itemId, productionDate, requestedQuantity, requestedUnitIdInput, contextValues, ct);
}
