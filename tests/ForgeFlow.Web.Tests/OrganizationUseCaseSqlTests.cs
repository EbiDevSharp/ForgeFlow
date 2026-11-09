using System.Data.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class OrganizationUseCaseSqlTests
{
    private sealed class Factory(OrganizationSqlServerTests.DisposableDatabase database,
        Action<DbContextOptionsBuilder<OrganizationDbContext>>? configure = null) : IDbContextFactory<OrganizationDbContext>
    {
        public OrganizationDbContext CreateDbContext() => database.Context(configure);
    }
    private static OrganizationUseCases Service(Factory factory, string permission, OrganizationScope scope) => new(
        new OrganizationOperationsStore(factory), new OrganizationUseCaseTests.ActorSource(), new OrganizationUseCaseTests.Grants(permission, scope));
    private static TrustedActor Actor() => new OrganizationUseCaseTests.ActorSource().Actor!;

    [OrganizationSqlServerFact]
    public async Task Queries_are_scoped_in_sql_and_stale_or_no_op_writes_do_not_add_audit()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        long orgId, otherId, siteId, otherSite;
        await using (var seed = database.Context())
        {
            var org = Organization.Create("ALPHA", "Company"); var site = org.CreateSite("A1", "Plant"); org.CreateSite("A2", "Other");
            var other = Organization.Create("BETA", "Other company"); var foreign = other.CreateSite("B1", "Plant");
            seed.AddRange(org, other); await seed.SaveChangesAsync(Actor());
            orgId = org.Id; otherId = other.Id; siteId = site.Id; otherSite = foreign.Id;
        }
        var factory = new Factory(database); var orgScope = new OrganizationScope.Organization(orgId);
        await using (var audit = database.Context())
        {
            var rejectionAuditCount = await audit.OrganizationChanges.CountAsync();
            var platform = new OrganizationScope.Platform();
            Assert.Equal("Organization.CodeDuplicate", (await Service(factory, OrganizationPermissions.OrganizationsCreate, platform)
                .CreateOrganization(platform, new("ALPHA", "Duplicate"))).Error!.Code);
            Assert.Equal("Site.CodeDuplicate", (await Service(factory, OrganizationPermissions.SitesCreate, orgScope)
                .CreateSite(orgScope, new(orgId, "A1", "Duplicate"))).Error!.Code);
            Assert.Equal("Site.NotFound", (await Service(factory, OrganizationPermissions.SitesUpdate, orgScope)
                .RenameSite(orgScope, new(orgId, otherSite, "Wrong owner", Convert.ToBase64String(new byte[8])))).Error!.Code);
            Assert.Equal(rejectionAuditCount, await audit.OrganizationChanges.CountAsync());
        }
        var reader = Service(factory, OrganizationPermissions.SitesRead, new OrganizationScope.Site(orgId, siteId));
        var rows = await reader.ListSites(new OrganizationScope.Site(orgId, siteId), orgId, new());
        Assert.Single(rows.Value!.Items); Assert.Equal(1, rows.Value.Total); Assert.Equal(siteId, rows.Value.Items[0].Id);
        Assert.Equal("Site.NotFound", (await reader.GetSite(new OrganizationScope.Site(orgId, siteId), otherId, otherSite)).Error!.Code);
        Assert.Null(await new OrganizationOperationsStore(factory).GetSite(orgId, otherSite, default));
        var orgReader = Service(factory, OrganizationPermissions.OrganizationsRead, orgScope);
        Assert.Single((await orgReader.ListOrganizations(orgScope, new())).Value!.Items);
        Assert.Equal("Organization.NotFound", (await orgReader.GetOrganization(orgScope, otherId)).Error!.Code);
        var before = (await orgReader.GetOrganization(orgScope, orgId)).Value!;
        var writer = Service(factory, OrganizationPermissions.OrganizationsUpdate, orgScope);
        var first = await writer.RenameOrganization(orgScope, new(orgId, "Changed", before.RowVersion));
        Assert.True(first.Succeeded);
        Assert.Equal(OrganizationAccessErrors.ConcurrencyConflict, (await writer.RenameOrganization(orgScope, new(orgId, "Losing", before.RowVersion))).Error!.Code);
        Assert.Equal(OrganizationAccessErrors.ConcurrencyConflict, (await writer.RenameOrganization(orgScope, new(orgId, "Changed", before.RowVersion))).Error!.Code);
        await using var inspect = database.Context();
        var count = await inspect.OrganizationChanges.CountAsync();
        Assert.True((await writer.RenameOrganization(orgScope, new(orgId, "Changed", first.Value!.RowVersion))).Succeeded);
        Assert.Equal(count, await inspect.OrganizationChanges.CountAsync());
        Assert.All(await inspect.OrganizationChanges.Where(a => a.OrganizationId == orgId).ToListAsync(), a => Assert.Equal(42, a.ActorId));
    }

    [OrganizationSqlServerFact]
    public async Task Audit_failure_returns_audit_unavailable_and_rolls_back_while_unknown_business_errors_escape()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var scope = new OrganizationScope.Platform();
        var service = Service(new Factory(database, o => o.AddInterceptors(new RejectInsert(true))), OrganizationPermissions.OrganizationsCreate, scope);
        await using var db = database.Context();
        var rejected = await service.CreateOrganization(scope, new("ALPHA", "Company"));
        Assert.False(rejected.Succeeded);
        Assert.Equal(OrganizationAccessErrors.AuditUnavailable, rejected.Error!.Code);
        Assert.Empty(await db.Organizations.ToListAsync());
        Assert.Empty(await db.OrganizationChanges.ToListAsync());
        var unrelated = Service(new Factory(database, o => o.AddInterceptors(new RejectInsert(false))), OrganizationPermissions.OrganizationsCreate, scope);
        await Assert.ThrowsAsync<DbUpdateException>(() => unrelated.CreateOrganization(scope, new("BETA", "Company")));
        Assert.Empty(await db.Organizations.ToListAsync());
    }

    private sealed class RejectInsert(bool audit) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var table = audit ? "OrganizationChange" : "Organization";
            if (command.CommandText.Contains("INSERT INTO [organization].[" + table + "]", StringComparison.Ordinal))
                command.CommandText = "THROW 51000, 'Synthetic SQL rejection', 1; " + command.CommandText;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ParentBarrier : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    [OrganizationSqlServerFact]
    public async Task Concurrent_site_creation_versus_parent_deactivation_cannot_leave_an_active_child()
        => await LifecycleRace(false);

    [OrganizationSqlServerFact]
    public async Task Concurrent_site_activation_versus_parent_deactivation_cannot_leave_an_active_child()
        => await LifecycleRace(true);

    private static async Task LifecycleRace(bool activate)
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        long orgId; long siteId = 0; string orgVersion; string siteVersion = ""; int originalAudits;
        await using (var seed = database.Context())
        {
            var org = Organization.Create("ALPHA", "Company");
            var site = activate ? org.CreateSite("A1", "Plant") : null;
            seed.Add(org); await seed.SaveChangesAsync(Actor());
            if (site is not null) { site.Deactivate(); await seed.SaveChangesAsync(Actor()); siteId = site.Id; siteVersion = Convert.ToBase64String(seed.Entry(site).Property<byte[]>("RowVersion").CurrentValue!); }
            orgId = org.Id; orgVersion = Convert.ToBase64String(seed.Entry(org).Property<byte[]>("RowVersion").CurrentValue!);
            originalAudits = await seed.OrganizationChanges.CountAsync();
        }
        var barrier = new ParentBarrier();
        var factory = new Factory(database, options => options.AddInterceptors(barrier));
        var scope = new OrganizationScope.Organization(orgId);
        var parent = Service(factory, OrganizationPermissions.OrganizationsSetActive, scope);
        var child = Service(factory, activate ? OrganizationPermissions.SitesSetActive : OrganizationPermissions.SitesCreate, scope);
        var deactivate = parent.SetOrganizationActive(scope, new(orgId, false, orgVersion));
        var siteChange = activate ? child.SetSiteActive(scope, new(orgId, siteId, true, siteVersion)) : child.CreateSite(scope, new(orgId, "A1", "Plant"));
        await Task.WhenAll(deactivate, siteChange);
        Assert.NotEqual(deactivate.Result.Succeeded, siteChange.Result.Succeeded);
        var failure = deactivate.Result.Error ?? siteChange.Result.Error;
        Assert.Contains(failure!.Code, new[] { "Organization.Inactive", "Organization.HasActiveSites" });
        await using var verify = database.Context();
        var stored = await verify.Organizations.SingleAsync();
        Assert.False(!stored.IsActive && stored.Sites.Any(s => s.IsActive));
        Assert.Equal(originalAudits + 1, await verify.OrganizationChanges.CountAsync());
    }

    private sealed class RetryAuditOnce : DbCommandInterceptor
    {
        internal int Hits;
        internal Action? OnFailure;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO [organization].[OrganizationChange]", StringComparison.Ordinal)
                && Interlocked.Increment(ref Hits) == 1)
            { OnFailure?.Invoke(); throw new TimeoutException("Synthetic transient before audit insert"); }
            return ValueTask.FromResult(result);
        }
    }
    [OrganizationSqlServerFact]
    public async Task Configured_retry_replays_the_rolled_back_transaction_once_without_duplicate_effects()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var interceptor = new RetryAuditOnce();
        var factory = new Factory(database, options => { options.UseSqlServer(sql => sql.EnableRetryOnFailure(2, TimeSpan.Zero, null)); options.AddInterceptors(interceptor); });
        var scope = new OrganizationScope.Platform();
        Assert.True((await Service(factory, OrganizationPermissions.OrganizationsCreate, scope).CreateOrganization(scope, new("ALPHA", "Company"))).Succeeded);
        await using var verify = database.Context();
        Assert.Single(await verify.Organizations.ToListAsync()); Assert.Single(await verify.OrganizationChanges.ToListAsync());
        Assert.Equal(2, interceptor.Hits);
        var grants = new OrganizationUseCaseTests.Grants(OrganizationPermissions.OrganizationsCreate, scope);
        var revokedAttempt = new RetryAuditOnce { OnFailure = () => grants.Revoked = true };
        var revokedFactory = new Factory(database, options => { options.UseSqlServer(sql => sql.EnableRetryOnFailure(2, TimeSpan.Zero, null)); options.AddInterceptors(revokedAttempt); });
        var revokedService = new OrganizationUseCases(new OrganizationOperationsStore(revokedFactory), new OrganizationUseCaseTests.ActorSource(), grants);
        Assert.Equal(OrganizationAccessErrors.AccessDenied, (await revokedService.CreateOrganization(scope, new("BETA", "Revoked"))).Error!.Code);
        Assert.Single(await verify.Organizations.ToListAsync()); Assert.Single(await verify.OrganizationChanges.ToListAsync());
    }

    private sealed class LostCommitAcknowledgement : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
            => throw new IOException("Synthetic lost commit acknowledgement");
    }
    [OrganizationSqlServerFact]
    public async Task Unknown_commit_outcome_is_not_replayed()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var factory = new Factory(database, options => { options.UseSqlServer(sql => sql.EnableRetryOnFailure(2, TimeSpan.Zero, null)); options.AddInterceptors(new LostCommitAcknowledgement()); });
        var scope = new OrganizationScope.Platform();
        var result = await Service(factory, OrganizationPermissions.OrganizationsCreate, scope).CreateOrganization(scope, new("ALPHA", "Company"));
        Assert.Equal(OrganizationAccessErrors.OutcomeUnknown, result.Error!.Code);
        await using var verify = database.Context();
        Assert.Single(await verify.Organizations.ToListAsync()); Assert.Single(await verify.OrganizationChanges.ToListAsync());
    }
}
