using ForgeFlow.Application.Dashboard.Slices;
using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Application.Dashboard;

/// <summary>
/// Operational facade for capability-evaluated KPI and status summary snapshots.
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Dashboard.Slices"/>.
/// </summary>
public sealed class DashboardUseCases(
    IDashboardQueryStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    TimeProvider? clock = null)
{
    private readonly DashboardMetricSlices metricSlices = new(store, actors, permissions, clock);

    public Task<OrganizationResult<DashboardSnapshot>> GetSnapshot(
        OrganizationScope? scope,
        long organizationId,
        long? siteFilter = null,
        CancellationToken ct = default)
        => metricSlices.GetSnapshotAsync(scope, organizationId, siteFilter, ct);
}
