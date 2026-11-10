using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Audit;

namespace ForgeFlow.Application.Audit;

public sealed class AuditUseCases(
    IAuditOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null)
{
    public async Task<OrganizationResult<OrganizationPage<AuditEntrySummary>>> ListAuditEntries(
        OrganizationScope scope,
        AuditListQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        if (!Enum.IsDefined(query.Category)
            || query.Page is < 1 or > 100000
            || query.PageSize is < 1 or > 100)
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(AuditErrors.InvalidQuery);

        var eventCode = string.IsNullOrWhiteSpace(query.EventCode) ? null : query.EventCode.Trim();
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if ((eventCode is not null && eventCode.Length > 64)
            || (search is not null && search.Length > 100))
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(AuditErrors.InvalidQuery);

        var (allowed, error) = await AuthorizeReadAsync(scope, ct);
        if (!allowed)
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(error!);

        var normalized = new AuditListQuery(query.Category, eventCode, search, query.Page, query.PageSize);
        try
        {
            var page = await store.ListAuditEntries(scope, normalized, ct);
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Success(page);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<AuditEntryDetails>> GetAuditEntry(
        OrganizationScope scope,
        AuditSourceKind source,
        long entryId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(source) || entryId <= 0)
            return OrganizationResult<AuditEntryDetails>.Failure(AuditErrors.NotFound);

        var (allowed, error) = await AuthorizeReadAsync(scope, ct);
        if (!allowed)
            return OrganizationResult<AuditEntryDetails>.Failure(error!);

        try
        {
            var details = await store.GetAuditEntry(scope, source, entryId, ct);
            return details is null
                ? OrganizationResult<AuditEntryDetails>.Failure(AuditErrors.NotFound)
                : OrganizationResult<AuditEntryDetails>.Success(details);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<AuditEntryDetails>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }

    private async Task<(bool Allowed, string? Error)> AuthorizeReadAsync(OrganizationScope scope, CancellationToken ct)
    {
        if (actors is null || permissions is null)
            return (false, AuditErrors.ActorRequired);

        TrustedActor? actor;
        try
        {
            actor = await actors.ResolveAsync(ct);
        }
        catch
        {
            return (false, AuditErrors.PersistenceUnavailable);
        }

        if (actor is null || actor.ActorId <= 0)
            return (false, AuditErrors.ActorRequired);

        bool allowed;
        try
        {
            allowed = (await permissions.EvaluateAsync(actor, scope, AuditPermissions.AuditRead, ct)).IsAllowed;
        }
        catch
        {
            return (false, AuditErrors.PersistenceUnavailable);
        }

        return allowed ? (true, null) : (false, AuditErrors.AccessDenied);
    }
}
