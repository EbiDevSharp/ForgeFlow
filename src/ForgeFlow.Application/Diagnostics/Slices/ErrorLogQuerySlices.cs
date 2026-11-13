using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Diagnostics.Slices;

/// <summary>
/// Operational read query slice for sanitized diagnostic error logs and aggregates.
/// </summary>
public sealed class ErrorLogQuerySlices(
    IErrorLogStore store,
    ErrorLogSliceSupport support,
    TimeProvider? clock = null)
{
    public async Task<OrganizationResult<OrganizationPage<ErrorLogSummary>>> ListErrorLogsAsync(
        OrganizationScope? scope,
        ErrorLogListQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var auth = await support.AuthorizeReadAsync(scope, DiagnosticPermissions.ErrorLogsRead, ct);
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
            var page = await store.ListAsync(scope!, normalized, ct);
            return OrganizationResult<OrganizationPage<ErrorLogSummary>>.Success(page);
        }
        catch (DiagnosticStoreUnavailableException)
        {
            return OrganizationResult<OrganizationPage<ErrorLogSummary>>.Failure(DiagnosticErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<ErrorLogDetails>> GetErrorLogAsync(
        OrganizationScope? scope,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeReadAsync(scope, DiagnosticPermissions.ErrorLogsRead, ct);
        if (!auth.Ok) return OrganizationResult<ErrorLogDetails>.Failure(auth.Error!);
        if (id <= 0) return OrganizationResult<ErrorLogDetails>.Failure(DiagnosticErrors.NotFound);

        try
        {
            var details = await store.GetAsync(scope!, id, ct);
            return details is null
                ? OrganizationResult<ErrorLogDetails>.Failure(DiagnosticErrors.NotFound)
                : OrganizationResult<ErrorLogDetails>.Success(details);
        }
        catch (DiagnosticStoreUnavailableException)
        {
            return OrganizationResult<ErrorLogDetails>.Failure(DiagnosticErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<ErrorLogStats>> GetStatsAsync(
        OrganizationScope? scope,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeReadAsync(scope, DiagnosticPermissions.ErrorLogsRead, ct);
        if (!auth.Ok) return OrganizationResult<ErrorLogStats>.Failure(auth.Error!);

        try
        {
            var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var stats = await store.GetStatsAsync(scope!, now, ct);
            return OrganizationResult<ErrorLogStats>.Success(stats);
        }
        catch (DiagnosticStoreUnavailableException)
        {
            return OrganizationResult<ErrorLogStats>.Failure(DiagnosticErrors.PersistenceUnavailable);
        }
    }
}
