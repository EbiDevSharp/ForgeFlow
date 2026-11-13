using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Audit;

namespace ForgeFlow.Application.Audit.Slices;

/// <summary>
/// Query vertical slice for operational and business audit entries, actors, and event code registries.
/// </summary>
public sealed class AuditQuerySlices(
    IAuditOperationsStore store,
    AuditSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<AuditEntrySummary>>> ListAuditEntriesAsync(
        OrganizationScope scope,
        AuditListQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        if (!AuditSliceSupport.TryNormalizeQuery(scope, query, out var normalized))
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(AuditErrors.InvalidQuery);

        var (allowed, _, error) = await support.AuthorizePermissionAsync(scope, AuditPermissions.AuditRead, ct);
        if (!allowed)
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(error!);

        try
        {
            var page = await store.ListAuditEntries(scope, normalized!, ct);
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Success(page);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<AuditEntryDetails>> GetAuditEntryAsync(
        OrganizationScope scope,
        AuditSourceKind source,
        long entryId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(source) || entryId <= 0)
            return OrganizationResult<AuditEntryDetails>.Failure(AuditErrors.NotFound);

        var (allowed, _, error) = await support.AuthorizePermissionAsync(scope, AuditPermissions.AuditRead, ct);
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

    public async Task<OrganizationResult<IReadOnlyList<AuditActorOption>>> ListActorsAsync(
        OrganizationScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var (allowed, _, error) = await support.AuthorizePermissionAsync(scope, AuditPermissions.AuditRead, ct);
        if (!allowed)
            return OrganizationResult<IReadOnlyList<AuditActorOption>>.Failure(error!);

        try
        {
            var list = await store.ListActors(scope, ct);
            return OrganizationResult<IReadOnlyList<AuditActorOption>>.Success(list);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<IReadOnlyList<AuditActorOption>>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<IReadOnlyList<string>>> ListEventCodesAsync(
        OrganizationScope scope,
        AuditCategory category = AuditCategory.All,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(category))
            return OrganizationResult<IReadOnlyList<string>>.Failure(AuditErrors.InvalidQuery);

        var (allowed, _, error) = await support.AuthorizePermissionAsync(scope, AuditPermissions.AuditRead, ct);
        if (!allowed)
            return OrganizationResult<IReadOnlyList<string>>.Failure(error!);

        try
        {
            var observed = await store.ListDistinctEventCodes(scope, category, ct);
            var registered = AuditEventCatalog.All
                .Where(d => category == AuditCategory.All || d.Category == category)
                .Where(d => scope is not OrganizationScope.Platform || d.Category != AuditCategory.Business)
                .Select(d => d.Code);

            var merged = registered
                .Concat(observed)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return OrganizationResult<IReadOnlyList<string>>.Success(merged);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<IReadOnlyList<string>>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }
}
