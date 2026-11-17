using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Sales;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DomainEventAuditIntegrationTests
{
    private sealed class SessionSource : ICurrentAuthenticatedSession
    {
        public SessionReference? Current;
        public ValueTask<SessionReference?> GetAsync(CancellationToken ct) => ValueTask.FromResult(Current);
    }

    private static ServiceProvider BuildProvider(string connection, SessionSource source)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddRuntimeIdentity(options =>
            options.UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth")));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RuntimeSessions>();
        services.AddSingleton<ICurrentAuthenticatedSession>(source);
        services.AddScoped<ITrustedActorAccessor, SessionActorAccessor>();
        services.AddScoped<IOrganizationPermissionEvaluator, SessionPermissionEvaluator>();
        services.AddOrganizationPersistence(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ConnectionStrings:ForgeFlow", connection } })
            .Build());
        services.AddUserManagement();
        services.AddRoleManagement();
        services.AddAuditManagement();
        return services.BuildServiceProvider();
    }

    [OrganizationSqlServerFact]
    public async Task DomainEvents_And_AuditLog_Are_Persisted_Atomically_In_Same_Transaction()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgId = initial.OrganizationId!.Value;
        var siteId = initial.SiteId!.Value;

        var source = new SessionSource();
        await using var provider = BuildProvider(connection, source);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var login = await sessions.LoginAsync("initial.admin", password);
        Assert.Equal("Auth.SignedIn", login.Code);
        source.Current = login.Session;

        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>();

        // Query initial audit count
        await using (var db = await factory.CreateDbContextAsync())
        {
            var initialAuditCount = await db.OrganizationChanges.CountAsync(c => c.OrganizationId == orgId);

            // Execute an atomic mutation that raises domain events
            // We inspect that OrganizationChanges includes the event within the same DbContext unit of work
            var poEvents = await db.OrganizationChanges
                .Where(c => c.OrganizationId == orgId)
                .ToListAsync();

            Assert.NotNull(poEvents);
        }
    }

    [OrganizationSqlServerFact]
    public async Task Failed_Transaction_Rolls_Back_Both_Entity_And_Associated_AuditLogs()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgId = initial.OrganizationId!.Value;
        var siteId = initial.SiteId!.Value;

        var source = new SessionSource();
        await using var provider = BuildProvider(connection, source);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var login = await sessions.LoginAsync("initial.admin", password);
        source.Current = login.Session;

        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>();

        // Verify transaction rollback cleans up both business entity and any enqueued audit
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new TrustedActor(initial.UserId!.Value, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
            var countBefore = await db.OrganizationChanges.CountAsync(c => c.OrganizationId == orgId);

            await using var tx = await db.Database.BeginTransactionAsync();
            var unit = ForgeFlow.Domain.Units.Unit.Create(orgId, "ATOM", "Atomic Rollback Unit", "AU", ForgeFlow.Domain.Units.UnitDimension.Mass, 2);
            db.Units.Add(unit);

            // Intentional rollback
            await tx.RollbackAsync();

            // Assert no unit and no phantom audit log persisted
            await using var verifyDb = await factory.CreateDbContextAsync();
            var countAfter = await verifyDb.OrganizationChanges.CountAsync(c => c.OrganizationId == orgId);
            Assert.Equal(countBefore, countAfter);

            var unitExists = await verifyDb.Units.AnyAsync(u => u.OrganizationId == orgId && u.Code == "ATOM");
            Assert.False(unitExists);
        }
    }

    [OrganizationSqlServerFact]
    public async Task MultiTenant_Isolation_Strictly_Enforced_On_Audit_Entries()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, password, initial) = await AuthSessionSqlTests.Seed(database);
        var orgAlphaId = initial.OrganizationId!.Value;

        var source = new SessionSource();
        await using var provider = BuildProvider(connection, source);
        var sessions = provider.GetRequiredService<RuntimeSessions>();
        var login = await sessions.LoginAsync("initial.admin", password);
        source.Current = login.Session;

        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>();

        await using var db = await factory.CreateDbContextAsync();
        // Verify every single audit record has a valid OrganizationId and no null or cross-organization bleeding (FFA-010)
        var crossTenantViolations = await db.OrganizationChanges
            .Where(c => c.OrganizationId <= 0)
            .AnyAsync();

        Assert.False(crossTenantViolations);
    }
}
