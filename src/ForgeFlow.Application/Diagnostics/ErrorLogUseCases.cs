using ForgeFlow.Application.Diagnostics.Slices;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Diagnostics;

/// <summary>
/// Read and retention management use cases for sanitized technical error logs.
/// Read queries support Platform (all logs) or Organization (strictly tenant-isolated logs);
/// retention purge remains Platform-only.
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Diagnostics.Slices"/>.
/// </summary>
public sealed class ErrorLogUseCases
{
    private readonly ErrorLogQuerySlices querySlices;
    private readonly ErrorLogPurgeSlices purgeSlices;

    public ErrorLogUseCases(
        IErrorLogStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        TimeProvider? clock = null)
    {
        var support = new ErrorLogSliceSupport(actors, permissions);
        querySlices = new ErrorLogQuerySlices(store, support, clock);
        purgeSlices = new ErrorLogPurgeSlices(store, support, clock);
    }

    public Task<OrganizationResult<OrganizationPage<ErrorLogSummary>>> ListErrorLogs(
        OrganizationScope? scope,
        ErrorLogListQuery query,
        CancellationToken ct = default)
        => querySlices.ListErrorLogsAsync(scope, query, ct);

    public Task<OrganizationResult<ErrorLogDetails>> GetErrorLog(
        OrganizationScope? scope,
        long id,
        CancellationToken ct = default)
        => querySlices.GetErrorLogAsync(scope, id, ct);

    public Task<OrganizationResult<ErrorLogStats>> GetStats(
        OrganizationScope? scope,
        CancellationToken ct = default)
        => querySlices.GetStatsAsync(scope, ct);

    public Task<OrganizationResult<int>> PurgeOldErrorLogs(
        OrganizationScope? scope,
        int retainDays,
        CancellationToken ct = default)
        => purgeSlices.PurgeOldErrorLogsAsync(scope, retainDays, ct);
}
