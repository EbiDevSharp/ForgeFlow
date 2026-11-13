using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Quality.Slices;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Quality;

/// <summary>
/// Secured facade for organization-scoped Quality Parameters, Specification templates, and Lot Certificates (COA).
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Quality.Slices"/>.
/// </summary>
public sealed class QualitySpecUseCases
{
    private readonly QualityParameterSlices parameterSlices;
    private readonly QualitySpecSlices specSlices;
    private readonly QualityCertificateSlices certificateSlices;

    public QualitySpecUseCases(
        IQualitySpecOperationsStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null)
    {
        var support = new QualitySpecSliceSupport(actors, permissions, operationRecorder, opContext);
        parameterSlices = new QualityParameterSlices(store, support);
        specSlices = new QualitySpecSlices(store, support);
        certificateSlices = new QualityCertificateSlices(store, support);
    }

    public Task<OrganizationResult<OrganizationPage<QualityParameterSummary>>> ListParameters(
        OrganizationScope? scope, long organizationId, QualityParameterListQuery query, CancellationToken ct = default)
        => parameterSlices.ListParametersAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<QualityParameterSummary>> CreateParameter(
        OrganizationScope? scope, CreateQualityParameterRequest request, CancellationToken ct = default)
        => parameterSlices.CreateParameterAsync(scope, request, ct);

    public Task<OrganizationResult<QualityParameterSummary>> UpdateParameter(
        OrganizationScope? scope, UpdateQualityParameterRequest request, CancellationToken ct = default)
        => parameterSlices.UpdateParameterAsync(scope, request, ct);

    public Task<OrganizationResult<QualityParameterSummary>> SetParameterActive(
        OrganizationScope? scope, SetQualityParameterActiveRequest request, CancellationToken ct = default)
        => parameterSlices.SetParameterActiveAsync(scope, request, ct);

    public Task<OrganizationResult<OrganizationPage<QualitySpecSummary>>> ListSpecs(
        OrganizationScope? scope, long organizationId, QualitySpecListQuery query, CancellationToken ct = default)
        => specSlices.ListSpecsAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<QualitySpecDetails>> GetSpec(
        OrganizationScope? scope, long organizationId, long id, CancellationToken ct = default)
        => specSlices.GetSpecAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<QualitySpecDetails?>> ResolveApplicableSpec(
        OrganizationScope? scope, long organizationId, long itemId, QualitySpecCheckpoint checkpoint, CancellationToken ct = default)
        => specSlices.ResolveApplicableSpecAsync(scope, organizationId, itemId, checkpoint, ct);

    public Task<OrganizationResult<QualitySpecDetails>> CreateSpec(
        OrganizationScope? scope, CreateQualitySpecRequest request, CancellationToken ct = default)
        => specSlices.CreateSpecAsync(scope, request, ct);

    public Task<OrganizationResult<QualitySpecDetails>> UpdateSpec(
        OrganizationScope? scope, UpdateQualitySpecRequest request, CancellationToken ct = default)
        => specSlices.UpdateSpecAsync(scope, request, ct);

    public Task<OrganizationResult<QualitySpecDetails>> SetSpecActive(
        OrganizationScope? scope, SetQualitySpecActiveRequest request, CancellationToken ct = default)
        => specSlices.SetSpecActiveAsync(scope, request, ct);

    public Task<OrganizationResult<QualityInspectionDetails>> ApplySpecToInspection(
        OrganizationScope? scope, long organizationId, long inspectionId, string? expectedRowVersion, CancellationToken ct = default)
        => specSlices.ApplySpecToInspectionAsync(scope, organizationId, inspectionId, expectedRowVersion, ct);

    public Task<OrganizationResult<LotCertificateSummary>> GetLotCertificate(
        OrganizationScope? scope, long organizationId, long itemId, string? lotCode, CancellationToken ct = default)
        => certificateSlices.GetLotCertificateAsync(scope, organizationId, itemId, lotCode, ct);
}
