using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations.Slices;
using ForgeFlow.Domain.Organizations;

namespace ForgeFlow.Application.Organizations;

/// <summary>
/// Secured facade for organization-scoped document sequences, monotonic numbering, and format configurations.
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Organizations.Slices"/>.
/// </summary>
public sealed class DocumentSequenceUseCases
{
    private readonly DocumentSequenceSlices sequenceSlices;

    public DocumentSequenceUseCases(
        IDocumentSequenceStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null,
        TimeProvider? clock = null)
    {
        var support = new DocumentSequenceSliceSupport(actors, permissions, operationRecorder, opContext, clock);
        sequenceSlices = new DocumentSequenceSlices(store, support);
    }

    public Task<OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>> ListSequences(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
        => sequenceSlices.ListSequencesAsync(scope, organizationId, ct);

    public Task<OrganizationResult<DocumentSequenceSummary>> GetPreview(
        OrganizationScope? scope,
        long organizationId,
        DocumentSequenceType documentType,
        CancellationToken ct = default)
        => sequenceSlices.GetPreviewAsync(scope, organizationId, documentType, ct);

    public Task<OrganizationResult<DocumentSequenceSummary>> UpdateSequence(
        OrganizationScope? scope,
        long organizationId,
        DocumentSequenceType documentType,
        UpdateDocumentSequenceRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
        => sequenceSlices.UpdateSequenceAsync(scope, organizationId, documentType, request, expectedVersion, ct);
}
