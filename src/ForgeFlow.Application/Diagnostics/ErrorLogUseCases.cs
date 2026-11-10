using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Diagnostics;

/// <summary>
/// Platform-only read and retention management use cases for sanitized technical error logs.
/// Never exposes raw technical error stacks to tenant or site scopes.
/// </summary>
public sealed class ErrorLogUseCases(
    IErrorLogStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    TimeProvider? clock = null)
{
    public async Task<OrganizationResult<OrganizationPage<ErrorLogSummary>>> ListErrorLogs(
        OrganizationScope? scope,
        ErrorLogListQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var auth = await AuthorizePlatformAsync(scope, DiagnosticPermissions.ErrorLogsRead, ct);
        if (!auth.Ok) return OrganizationResult<OrganizationPage<ErrorLogSummary>>.Failure(auth.Error!);

        if ((query.Severity.HasValue && !Enum.IsDefined(query.Severity.Value))
            || (query.Source.HasValue && !Enum.IsDefined(query.Source.Value))
            || (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate.Value > query.ToDate.Value)
            || query.Page is < 1 or > 100000
            || query.PageSize is < 1 or > 100)
            return OrganizationResult<OrganizationPage<ErrorLogSummary>>.Failure(DiagnosticErrors.InvalidQuery);

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search is not null && search.Length > 100)
            return OrganizationResult<OrganizationPage<ErrorLogSummary>>.Failure(DiagnosticErrors.InvalidQuery);

        var normalized = query with { Search = search };
        try
        {
            var page = await store.ListAsync(normalized, ct);
            return OrganizationResult<OrganizationPage<ErrorLogSummary>>.Success(page);
        }
        catch (DiagnosticStoreUnavailableException)
        {
            return OrganizationResult<OrganizationPage<ErrorLogSummary>>.Failure(DiagnosticErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<ErrorLogDetails>> GetErrorLog(
        OrganizationScope? scope,
        long id,
        CancellationToken ct = default)
    {
        var auth = await AuthorizePlatformAsync(scope, DiagnosticPermissions.ErrorLogsRead, ct);
        if (!auth.Ok) return OrganizationResult<ErrorLogDetails>.Failure(auth.Error!);
        if (id <= 0) return OrganizationResult<ErrorLogDetails>.Failure(DiagnosticErrors.NotFound);

        try
        {
            var details = await store.GetAsync(id, ct);
            return details is null
                ? OrganizationResult<ErrorLogDetails>.Failure(DiagnosticErrors.NotFound)
                : OrganizationResult<ErrorLogDetails>.Success(details);
        }
        catch (DiagnosticStoreUnavailableException)
        {
            return OrganizationResult<ErrorLogDetails>.Failure(DiagnosticErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<ErrorLogStats>> GetStats(
        OrganizationScope? scope,
        CancellationToken ct = default)
    {
        var auth = await AuthorizePlatformAsync(scope, DiagnosticPermissions.ErrorLogsRead, ct);
        if (!auth.Ok) return OrganizationResult<ErrorLogStats>.Failure(auth.Error!);

        try
        {
            var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var stats = await store.GetStatsAsync(now, ct);
            return OrganizationResult<ErrorLogStats>.Success(stats);
        }
        catch (DiagnosticStoreUnavailableException)
        {
            return OrganizationResult<ErrorLogStats>.Failure(DiagnosticErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<int>> PurgeOldErrorLogs(
        OrganizationScope? scope,
        int retainDays,
        CancellationToken ct = default)
    {
        var auth = await AuthorizePlatformAsync(scope, DiagnosticPermissions.ErrorLogsManage, ct);
        if (!auth.Ok) return OrganizationResult<int>.Failure(auth.Error!);
        if (retainDays is < 7 or > 3650)
            return OrganizationResult<int>.Failure(DiagnosticErrors.InvalidRetentionDays);

        try
        {
            var cutoff = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime.AddDays(-retainDays);
            var deleted = await store.PurgeBeforeAsync(cutoff, ct);
            return OrganizationResult<int>.Success(deleted);
        }
        catch (DiagnosticStoreUnavailableException)
        {
            return OrganizationResult<int>.Failure(DiagnosticErrors.PersistenceUnavailable);
        }
    }

    private async Task<(bool Ok, string? Error)> AuthorizePlatformAsync(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null || permissions is null)
            return (false, DiagnosticErrors.ActorRequired);
        if (scope is null)
            return (false, DiagnosticErrors.ScopeRequired);
        if (scope is not OrganizationScope.Platform)
            return (false, DiagnosticErrors.AccessDenied);

        TrustedActor? actor;
        try { actor = await actors.ResolveAsync(ct); }
        catch { return (false, DiagnosticErrors.PersistenceUnavailable); }
        if (actor is null)
            return (false, DiagnosticErrors.ActorRequired);

        PermissionDecision decision;
        try { decision = await permissions.EvaluateAsync(actor, scope, permission, ct); }
        catch { return (false, DiagnosticErrors.PersistenceUnavailable); }

        return decision.IsAllowed ? (true, null) : (false, DiagnosticErrors.AccessDenied);
    }
}
