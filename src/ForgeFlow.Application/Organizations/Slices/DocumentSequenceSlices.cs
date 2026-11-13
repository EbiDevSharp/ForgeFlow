using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Domain.Organizations;

namespace ForgeFlow.Application.Organizations.Slices;

/// <summary>
/// Operational vertical slice for tenant document sequence definitions, preview allocation, and prefix formatting.
/// </summary>
public sealed class DocumentSequenceSlices(
    IDocumentSequenceStore store,
    DocumentSequenceSliceSupport support)
{
    public async Task<OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>> ListSequencesAsync(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, DocumentSequencePermissions.Read, ct);
        if (auth.Error is not null)
            return OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>.Failure(auth.Error);
        if (!DocumentSequenceSliceSupport.MatchesOrgOnly(scope, organizationId))
            return OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>.Failure(DocumentSequenceErrors.AccessDenied);

        try
        {
            var canManage = support.Permissions is not null
                && (await support.Permissions.EvaluateAsync(auth.Actor!, scope!, DocumentSequencePermissions.Manage, ct)).IsAllowed;
            var seedAccess = canManage ? support.Access(auth.Actor!, scope!, DocumentSequencePermissions.Manage) : null;
            var list = await store.ListSequences(organizationId, support.UtcNow, seedAccess, ct);
            return OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>.Success(list);
        }
        catch (DocumentSequenceRuleException ex)
        {
            return OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>.Failure(ex.Code, ex.Parameters);
        }
        catch (DocumentSequenceStoreUnavailableException)
        {
            return OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>.Failure(DocumentSequenceErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<DocumentSequenceSummary>> GetPreviewAsync(
        OrganizationScope? scope,
        long organizationId,
        DocumentSequenceType documentType,
        CancellationToken ct = default)
    {
        var actor = await support.ResolveActorAsync(ct);
        if (actor is null) return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.ActorRequired);
        if (!DocumentSequenceSliceSupport.MatchesOrgOrSite(scope, organizationId))
            return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.AccessDenied);
        if (!Enum.IsDefined(documentType))
            return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.DocumentTypeInvalid);

        try
        {
            var preview = await store.GetPreview(organizationId, documentType, support.UtcNow, ct);
            return OrganizationResult<DocumentSequenceSummary>.Success(preview);
        }
        catch (DocumentSequenceStoreUnavailableException)
        {
            return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<DocumentSequenceSummary>> UpdateSequenceAsync(
        OrganizationScope? scope,
        long organizationId,
        DocumentSequenceType documentType,
        UpdateDocumentSequenceRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, DocumentSequencePermissions.Manage, ct);
        OrganizationResult<DocumentSequenceSummary> result;

        if (auth.Error is not null)
            result = OrganizationResult<DocumentSequenceSummary>.Failure(auth.Error);
        else if (!DocumentSequenceSliceSupport.MatchesOrgOnly(scope, organizationId))
            result = OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.AccessDenied);
        else if (!Enum.IsDefined(documentType))
            result = OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.DocumentTypeInvalid);
        else if (DocumentSequenceSliceSupport.DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.ConcurrencyConflict);
        else
        {
            try
            {
                result = await store.UpdateSequence(
                    organizationId,
                    documentType,
                    request,
                    version,
                    support.UtcNow,
                    support.Access(auth.Actor!, scope!, DocumentSequencePermissions.Manage),
                    ct);
            }
            catch (DocumentSequenceRuleException ex)
            {
                result = OrganizationResult<DocumentSequenceSummary>.Failure(ex.Code, ex.Parameters);
            }
            catch (DocumentSequenceStoreUnavailableException)
            {
                result = OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.PersistenceUnavailable);
            }
        }

        if (!result.Succeeded && result.Error is not null)
        {
            await OperationAuditHelper.RecordAsync(
                support.OperationRecorder,
                support.OpContext,
                UserOperationCatalog.DocumentSequenceActionRejected,
                OperationAuditHelper.MapFailureOutcome(result.Error.Code),
                auth.Actor,
                scope,
                organizationId,
                null,
                "DocumentSequence",
                documentType.ToString(),
                result.Error.Code,
                new { Action = nameof(UpdateSequenceAsync), Error = result.Error.Code });
        }

        return result;
    }
}
