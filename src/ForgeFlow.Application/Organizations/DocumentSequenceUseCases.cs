using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Domain.Organizations;

namespace ForgeFlow.Application.Organizations;

public sealed class DocumentSequenceUseCases(
    IDocumentSequenceStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, DocumentSequenceErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, DocumentSequenceErrors.ActorRequired);
        if (scope is null) return (null, DocumentSequenceErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, DocumentSequenceErrors.AccessDenied);
        return (actor, null);
    }

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    private static bool MatchesOrgOnly(OrganizationScope? scope, long organizationId)
        => scope is OrganizationScope.Organization org && org.OrganizationId == organizationId && organizationId > 0;

    private static bool MatchesOrgOrSite(OrganizationScope? scope, long organizationId)
        => organizationId > 0 && scope switch
        {
            OrganizationScope.Organization org => org.OrganizationId == organizationId,
            OrganizationScope.Site site => site.OrganizationId == organizationId,
            _ => false
        };

    private static byte[]? DecodeVersion(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            var bytes = Convert.FromBase64String(raw);
            return bytes.Length == 8 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async Task<OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>> ListSequences(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, DocumentSequencePermissions.Read, ct);
        if (auth.Error is not null)
            return OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>.Failure(auth.Error);
        if (!MatchesOrgOnly(scope, organizationId))
            return OrganizationResult<IReadOnlyList<DocumentSequenceSummary>>.Failure(DocumentSequenceErrors.AccessDenied);

        try
        {
            var canManage = (await permissions!.EvaluateAsync(auth.Actor!, scope!, DocumentSequencePermissions.Manage, ct)).IsAllowed;
            var seedAccess = canManage ? Access(auth.Actor!, scope!, DocumentSequencePermissions.Manage) : null;
            var list = await store.ListSequences(organizationId, UtcNow, seedAccess, ct);
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

    public async Task<OrganizationResult<DocumentSequenceSummary>> GetPreview(
        OrganizationScope? scope,
        long organizationId,
        DocumentSequenceType documentType,
        CancellationToken ct = default)
    {
        if (actors is null) return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.ActorRequired);
        if (!MatchesOrgOrSite(scope, organizationId))
            return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.AccessDenied);
        if (!Enum.IsDefined(documentType))
            return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.DocumentTypeInvalid);

        try
        {
            var preview = await store.GetPreview(organizationId, documentType, UtcNow, ct);
            return OrganizationResult<DocumentSequenceSummary>.Success(preview);
        }
        catch (DocumentSequenceStoreUnavailableException)
        {
            return OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<DocumentSequenceSummary>> UpdateSequence(
        OrganizationScope? scope,
        long organizationId,
        DocumentSequenceType documentType,
        UpdateDocumentSequenceRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, DocumentSequencePermissions.Manage, ct);
        OrganizationResult<DocumentSequenceSummary> result;

        if (auth.Error is not null)
            result = OrganizationResult<DocumentSequenceSummary>.Failure(auth.Error);
        else if (!MatchesOrgOnly(scope, organizationId))
            result = OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.AccessDenied);
        else if (!Enum.IsDefined(documentType))
            result = OrganizationResult<DocumentSequenceSummary>.Failure(DocumentSequenceErrors.DocumentTypeInvalid);
        else if (DecodeVersion(expectedVersion) is not byte[] version)
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
                    UtcNow,
                    Access(auth.Actor!, scope!, DocumentSequencePermissions.Manage),
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
                operationRecorder,
                opContext,
                UserOperationCatalog.DocumentSequenceActionRejected,
                OperationAuditHelper.MapFailureOutcome(result.Error.Code),
                auth.Actor,
                scope,
                organizationId,
                null,
                "DocumentSequence",
                documentType.ToString(),
                result.Error.Code,
                new { Action = nameof(UpdateSequence), Error = result.Error.Code });
        }

        return result;
    }
}
