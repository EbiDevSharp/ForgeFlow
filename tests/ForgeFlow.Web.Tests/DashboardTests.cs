using ForgeFlow.Application.Dashboard;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Routings;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DashboardTests
{
    private sealed class ActorStub(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult(actor);
    }

    private sealed class PermissionStub(Func<OrganizationScope, string, bool> predicate) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(predicate(scope, permission)));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class DashboardStoreStub : IDashboardQueryStore
    {
        public long? CapturedSiteId { get; private set; }
        public DateOnly CapturedToday { get; private set; }
        public DashboardCapabilities? CapturedCapabilities { get; private set; }

        public Task<DashboardSnapshot> GetSnapshot(
            long organizationId,
            long? siteId,
            DateOnly today,
            DashboardCapabilities capabilities,
            CancellationToken ct)
        {
            CapturedSiteId = siteId;
            CapturedToday = today;
            CapturedCapabilities = capabilities;
            return Task.FromResult(new DashboardSnapshot(
                capabilities,
                capabilities.Production ? new DashboardProductionStats(1, 2, 3, 1) : null,
                [],
                capabilities.Reports ? new DashboardShortageStats(3, 2, 40m, 15m) : null,
                [],
                capabilities.Quality ? new DashboardQualityStats(2, 1, 25m) : null,
                [],
                capabilities.Procurement ? new DashboardProcurementStats(1, 1, 0) : null,
                [],
                []));
        }
    }

    [Fact]
    public async Task R02_GetSnapshot_evaluates_each_read_permission_and_forwards_site_scope()
    {
        var store = new DashboardStoreStub();
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var granted = new HashSet<string>(StringComparer.Ordinal)
        {
            ProductionOrderPermissions.ProductionOrdersRead,
            QualityPermissions.QualityRead
        };
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));
        var uc = new DashboardUseCases(store, new ActorStub(actor), new PermissionStub((_, p) => granted.Contains(p)), clock);

        var siteRes = await uc.GetSnapshot(new OrganizationScope.Site(5, 9), 5);
        Assert.True(siteRes.Succeeded);
        Assert.Equal(9L, store.CapturedSiteId);
        Assert.Equal(new DateOnly(2026, 11, 11), store.CapturedToday);
        Assert.NotNull(store.CapturedCapabilities);
        Assert.True(store.CapturedCapabilities!.Production);
        Assert.True(store.CapturedCapabilities.Quality);
        Assert.False(store.CapturedCapabilities.Inventory);
        Assert.False(store.CapturedCapabilities.Reports);
        Assert.False(store.CapturedCapabilities.Procurement);
        Assert.False(store.CapturedCapabilities.Routings);

        Assert.NotNull(siteRes.Value!.ProductionStats);
        Assert.NotNull(siteRes.Value.QualityStats);
        Assert.Null(siteRes.Value.ShortageStats);
        Assert.Null(siteRes.Value.ProcurementStats);

        var mismatch = await uc.GetSnapshot(new OrganizationScope.Site(5, 9), 5, siteFilter: 10);
        Assert.False(mismatch.Succeeded);
    }
}
