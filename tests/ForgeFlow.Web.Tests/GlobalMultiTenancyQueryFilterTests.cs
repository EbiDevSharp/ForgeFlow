using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Common;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using ForgeFlow.Infrastructure.Persistence.Interceptors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class GlobalMultiTenancyQueryFilterTests
{
    private static readonly TrustedActor TestActor = new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    private sealed class TestActorAccessor(TrustedActor? actor) : ICurrentActorAccessor
    {
        public TrustedActor? CurrentActor => actor;
    }

    private sealed class TestOrgAccessor(long? orgId) : ICurrentOrganizationAccessor
    {
        public long? CurrentOrganizationId => orgId;
    }

    private sealed class SuppressDatabaseExecutionInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => InterceptionResult<int>.SuppressWithResult(1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
    }

    private static OrganizationDbContext CreateOfflineOrgDbContext(
        ICurrentOrganizationAccessor? orgAccessor = null,
        ICurrentActorAccessor? actorAccessor = null,
        long? explicitOrgId = null)
    {
        var builder = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=True;Encrypt=True");

        builder.AddInterceptors(
            new TenantEnforcementSaveChangesInterceptor(),
            new AuditingSaveChangesInterceptor(actorAccessor ?? new TestActorAccessor(TestActor)),
            new SuppressDatabaseExecutionInterceptor());

        var db = new OrganizationDbContext(builder.Options, TimeProvider.System, orgAccessor);
        if (explicitOrgId.HasValue)
        {
            db.CurrentOrganizationId = explicitOrgId.Value;
        }
        return db;
    }

    [Fact]
    public void All_Tenant_Scoped_Entities_Have_Organization_QueryFilter_Configured()
    {
        using var db = CreateOfflineOrgDbContext();
        var model = db.Model;

        var entityTypes = model.GetEntityTypes()
            .Where(e => e.ClrType != typeof(Organization) &&
                        (typeof(IOrganizationEntity).IsAssignableFrom(e.ClrType) ||
                         (e.FindProperty("OrganizationId") is { ClrType: { } clrType } && clrType == typeof(long))))
            .ToList();

        Assert.NotEmpty(entityTypes);

        foreach (var entityType in entityTypes)
        {
            var filters = entityType.GetDeclaredQueryFilters();
            Assert.NotEmpty(filters);
        }
    }

    [Fact]
    public void QueryFilter_Translates_To_Sql_With_CurrentOrganizationId_Filter()
    {
        using var db = CreateOfflineOrgDbContext(explicitOrgId: 42);

        var unitsSql = db.Units.ToQueryString();
        Assert.Contains("OrganizationId", unitsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", unitsSql, StringComparison.OrdinalIgnoreCase);

        var itemsSql = db.Items.ToQueryString();
        Assert.Contains("OrganizationId", itemsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", itemsSql, StringComparison.OrdinalIgnoreCase);

        var ordersSql = db.SalesOrders.ToQueryString();
        Assert.Contains("OrganizationId", ordersSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", ordersSql, StringComparison.OrdinalIgnoreCase);

        var balancesSql = db.InventoryBalances.ToQueryString();
        Assert.Contains("OrganizationId", balancesSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", balancesSql, StringComparison.OrdinalIgnoreCase);

        var productionSql = db.ProductionOrders.ToQueryString();
        Assert.Contains("OrganizationId", productionSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", productionSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IgnoreQueryFilters_Omits_OrganizationId_Filter_From_Generated_Sql()
    {
        using var db = CreateOfflineOrgDbContext(explicitOrgId: 42);

        var filteredSql = db.Units.ToQueryString();
        var unfilteredSql = db.Units.IgnoreQueryFilters().ToQueryString();

        Assert.Contains("WHERE", filteredSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OrganizationId", filteredSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE", unfilteredSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AmbientOrganizationAccessor_Manages_AsyncLocal_Scope()
    {
        Assert.Null(AmbientOrganizationAccessor.Current);

        var accessor = new AmbientOrganizationAccessor();
        Assert.Null(accessor.CurrentOrganizationId);

        using (AmbientOrganizationAccessor.BeginScope(101))
        {
            Assert.Equal(101, AmbientOrganizationAccessor.Current);
            Assert.Equal(101, accessor.CurrentOrganizationId);

            using (AmbientOrganizationAccessor.BeginScope(202))
            {
                Assert.Equal(202, AmbientOrganizationAccessor.Current);
                Assert.Equal(202, accessor.CurrentOrganizationId);
            }

            Assert.Equal(101, AmbientOrganizationAccessor.Current);
            Assert.Equal(101, accessor.CurrentOrganizationId);
        }

        Assert.Null(AmbientOrganizationAccessor.Current);
        Assert.Null(accessor.CurrentOrganizationId);
    }

    [Fact]
    public void OrganizationDbContext_CurrentOrganizationId_Resolution_Precedence()
    {
        // 1. All null -> Fail-closed
        using (var dbNull = CreateOfflineOrgDbContext())
        {
            Assert.Null(dbNull.CurrentOrganizationId);
        }

        // 2. Ambient scope resolves when explicit and accessor are null
        using (AmbientOrganizationAccessor.BeginScope(303))
        {
            using var dbAmbient = CreateOfflineOrgDbContext();
            Assert.Equal(303, dbAmbient.CurrentOrganizationId);

            // 3. Injected accessor overrides ambient
            var accessor = new TestOrgAccessor(404);
            using var dbAccessor = CreateOfflineOrgDbContext(orgAccessor: accessor);
            Assert.Equal(404, dbAccessor.CurrentOrganizationId);

            // 4. Explicit context property overrides both ambient and accessor
            using var dbExplicit = CreateOfflineOrgDbContext(orgAccessor: accessor, explicitOrgId: 505);
            Assert.Equal(505, dbExplicit.CurrentOrganizationId);

            // Mutating explicit property dynamically
            dbExplicit.CurrentOrganizationId = 606;
            Assert.Equal(606, dbExplicit.CurrentOrganizationId);

            dbExplicit.CurrentOrganizationId = null;
            // Falls back to injected accessor
            Assert.Equal(404, dbExplicit.CurrentOrganizationId);
        }
    }

    [Fact]
    public void TenantEnforcement_Interceptor_Blocks_Mutation_When_OrganizationId_Mismatches_Context()
    {
        using var db = CreateOfflineOrgDbContext(explicitOrgId: 100);
        var unit = Unit.Create(10, "KG", "Kilogram", "kg", UnitDimension.Mass, 2);
        db.Units.Add(unit);
        db.Entry(unit).Property("OrganizationId").CurrentValue = 200L;

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal(OrganizationAccessErrors.AccessDenied, ex.Message);
    }

    [Fact]
    public void TenantEnforcement_Interceptor_Blocks_Zero_Or_Negative_OrganizationId()
    {
        using var db = CreateOfflineOrgDbContext(explicitOrgId: 100);
        var unit = Unit.Create(10, "KG", "Kilogram", "kg", UnitDimension.Mass, 2);
        db.Units.Add(unit);
        db.Entry(unit).Property("OrganizationId").CurrentValue = 0L;

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal(OrganizationAccessErrors.AccessDenied, ex.Message);
    }

    [Fact]
    public void TenantEnforcement_Interceptor_Allows_Mutation_When_OrganizationId_Matches_Context()
    {
        using var db = CreateOfflineOrgDbContext(explicitOrgId: 100);
        var unit = Unit.Create(10, "KG", "Kilogram", "kg", UnitDimension.Mass, 2);
        db.Units.Add(unit);
        db.Entry(unit).Property("OrganizationId").CurrentValue = 100L;

        // SuppressDatabaseExecutionInterceptor intercepts and returns 1 without reaching live SQL Server
        var rows = db.SaveChanges();
        Assert.Equal(1, rows);
    }

    [OrganizationSqlServerFact]
    public async Task Sql_Execution_CrossTenant_QueryFilter_Isolation_And_FailClosed()
    {
        var rawConn = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(rawConn))
            return;

        var masterBuilder = new SqlConnectionStringBuilder(rawConn) { InitialCatalog = "master", ConnectTimeout = 5, Pooling = false };
        var testDbName = $"ForgeFlow_FilterTest_{Guid.NewGuid():N}";
        var testConn = new SqlConnectionStringBuilder(masterBuilder.ConnectionString) { InitialCatalog = testDbName }.ConnectionString;

        await using var adminConn = new SqlConnection(masterBuilder.ConnectionString);
        await adminConn.OpenAsync();
        await using var createCmd = adminConn.CreateCommand();
        createCmd.CommandText = $"CREATE DATABASE [{testDbName}]";
        await createCmd.ExecuteNonQueryAsync();

        try
        {
            var options = new DbContextOptionsBuilder<OrganizationDbContext>()
                .UseSqlServer(testConn)
                .Options;

            long orgAId, orgBId;
            var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

            // 1. Setup schema & seed two organizations
            await using (var seedDb = new OrganizationDbContext(options))
            {
                await seedDb.Database.MigrateAsync();

                var orgA = Organization.Create("ORG-A", "Tenant Alpha");
                var orgB = Organization.Create("ORG-B", "Tenant Beta");
                seedDb.Organizations.AddRange(orgA, orgB);
                await seedDb.SaveChangesAsync(actor);

                orgAId = orgA.Id;
                orgBId = orgB.Id;

                var unitA = Unit.Create(orgAId, "KG", "Kilogram", "kg", UnitDimension.Mass, 2);
                var unitB = Unit.Create(orgBId, "METER", "Meter", "m", UnitDimension.Length, 2);
                seedDb.Units.AddRange(unitA, unitB);
                await seedDb.SaveChangesAsync(actor);
            }

            // 2. Query as Tenant A -> only see Tenant A
            await using (var dbA = new OrganizationDbContext(options))
            {
                dbA.CurrentOrganizationId = orgAId;
                var unitsA = await dbA.Units.ToListAsync();
                Assert.Single(unitsA);
                Assert.Equal("KG", unitsA[0].Code);
            }

            // 3. Query as Tenant B -> only see Tenant B
            await using (var dbB = new OrganizationDbContext(options))
            {
                dbB.CurrentOrganizationId = orgBId;
                var unitsB = await dbB.Units.ToListAsync();
                Assert.Single(unitsB);
                Assert.Equal("METER", unitsB[0].Code);
            }

            // 4. Query with CurrentOrganizationId = null -> Fail-closed (0 items)
            await using (var dbNull = new OrganizationDbContext(options))
            {
                dbNull.CurrentOrganizationId = null;
                var unitsNull = await dbNull.Units.ToListAsync();
                Assert.Empty(unitsNull);
            }

            // 5. Query with IgnoreQueryFilters() -> see both tenants
            await using (var dbAll = new OrganizationDbContext(options))
            {
                dbAll.CurrentOrganizationId = null;
                var allUnits = await dbAll.Units.IgnoreQueryFilters().ToListAsync();
                Assert.Equal(2, allUnits.Count);
            }
        }
        finally
        {
            await using var dropConn = new SqlConnection(masterBuilder.ConnectionString);
            await dropConn.OpenAsync();
            await using var dropConnCmd = dropConn.CreateCommand();
            dropConnCmd.CommandText = $"ALTER DATABASE [{testDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{testDbName}];";
            await dropConnCmd.ExecuteNonQueryAsync();
        }
    }
}
