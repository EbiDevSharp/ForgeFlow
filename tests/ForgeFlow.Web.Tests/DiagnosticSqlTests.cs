using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Infrastructure.Diagnostics;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DiagnosticSqlTests
{
    [OrganizationSqlServerFact]
    public async Task S01_S02_diagnostics_error_log_and_user_operation_log_work_end_to_end_in_sql()
    {
        await using var database = await DisposableDiagnosticDatabase.Create();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AuthDbContext>(o => o.UseSqlServer(database.ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth")));
        services.AddDbContextFactory<OrganizationDbContext>(o => o.UseSqlServer(database.ConnectionString));
        services.AddDbContextFactory<DiagnosticsDbContext>(o => o.UseSqlServer(database.ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "diag")));
        await using var provider = services.BuildServiceProvider();

        var diagFactory = provider.GetRequiredService<IDbContextFactory<DiagnosticsDbContext>>();
        var store = new SqlErrorLogStore(
            diagFactory,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SqlErrorLogStore>.Instance);

        var corr = Guid.NewGuid();
        var recorded = await store.RecordExceptionAsync(
            new InvalidOperationException("connectionstring=Server=prod;Password=SecretPass! failed"),
            new ErrorLogRecordContext(
                ErrorSeverity.Critical,
                ErrorSourceKind.InfrastructureStore,
                "/inventory?token=secret99&poId=7",
                "SqlInventoryOperationsStore",
                ActorUserId: 11,
                OrganizationId: 2,
                SiteId: 5,
                CorrelationId: corr));

        Assert.True(recorded.SavedToDatabase);
        Assert.Equal(corr, recorded.CorrelationId);

        var page = await store.ListAsync(new ErrorLogListQuery(Severity: ErrorSeverity.Critical, Search: recorded.ErrorReference));
        var item = Assert.Single(page.Items);
        Assert.DoesNotContain("SecretPass!", item.RedactedMessage);
        Assert.DoesNotContain("secret99", item.RequestPath ?? string.Empty);

        var details = await store.GetAsync(item.Id);
        Assert.NotNull(details);
        Assert.DoesNotContain("SecretPass!", details!.RedactedStackTrace);

        var stats = await store.GetStatsAsync(DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(1, stats.TotalLast24Hours);
        Assert.Equal(1, stats.CriticalLast24Hours);

        // User operation log + SqlAuditOperationsStore isolation across sites
        await store.RecordAsync(new UserOperationRecordRequest(
            UserOperationCatalog.ReportsShortageExported,
            UserOperationOutcome.Succeeded,
            ActorUserId: 11,
            OrganizationId: 2,
            SiteId: 5,
            CorrelationId: corr,
            TargetEntityType: "MaterialShortage",
            TargetEntityRef: "shortage.csv",
            MetadataJson: "{\"rows\":3}"));

        await store.RecordAsync(new UserOperationRecordRequest(
            UserOperationCatalog.InventoryActionRejected,
            UserOperationOutcome.BusinessRuleRejected,
            ActorUserId: 12,
            OrganizationId: 2,
            SiteId: 9,
            CorrelationId: Guid.NewGuid(),
            TargetEntityType: "InventoryTransaction",
            TargetEntityRef: "ISS-99",
            FailureCode: "InventoryErrors.InsufficientStock",
            MetadataJson: "{}"));

        var auditStore = new SqlAuditOperationsStore(provider.GetRequiredService<IServiceScopeFactory>());
        var site5Only = await auditStore.ListAuditEntries(
            new OrganizationScope.Site(2, 5),
            new AuditListQuery(AuditCategory.Operations, Page: 1, PageSize: 20, ActorUserId: 11, OutcomeFilter: AuditOutcomeFilter.Succeeded));
        var opEntry = Assert.Single(site5Only.Items);
        Assert.Equal(AuditSourceKind.Operations, opEntry.Source);
        Assert.Equal(UserOperationCatalog.ReportsShortageExported, opEntry.EventCode);
        Assert.Equal((byte)0, opEntry.Outcome);

        var orgAllRejected = await auditStore.ListAuditEntries(
            new OrganizationScope.Organization(2),
            new AuditListQuery(AuditCategory.Operations, Page: 1, PageSize: 20, OutcomeFilter: AuditOutcomeFilter.RejectedOrFailed));
        var rejEntry = Assert.Single(orgAllRejected.Items);
        Assert.Equal(UserOperationCatalog.InventoryActionRejected, rejEntry.EventCode);
        Assert.Equal(9L, rejEntry.SiteId);
        Assert.Equal((byte)2, rejEntry.Outcome);

        // Purge future cutoff removes error log entry cleanly
        var purged = await store.PurgeBeforeAsync(DateTime.UtcNow.AddDays(1));
        Assert.Equal(1, purged);
    }

    private sealed class DisposableDiagnosticDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public string ConnectionString { get; }

        private DisposableDiagnosticDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            ConnectionString = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
        }

        public static async Task<DisposableDiagnosticDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_diag_{Guid.NewGuid():N}";
            var db = new DisposableDiagnosticDatabase(builder, name);
            try
            {
                await using var conn = new SqlConnection(db.server.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE [{db.name}]";
                await cmd.ExecuteNonQueryAsync();
                db.created = true;

                await using (var orgCtx = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>()
                    .UseSqlServer(db.ConnectionString).Options))
                {
                    await orgCtx.Database.MigrateAsync();
                }

                await using (var authCtx = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
                    .UseSqlServer(db.ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth")).Options))
                {
                    await authCtx.Database.MigrateAsync();
                }

                await using (var diagCtx = new DiagnosticsDbContext(new DbContextOptionsBuilder<DiagnosticsDbContext>()
                    .UseSqlServer(db.ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "diag")).Options))
                {
                    await diagCtx.Database.MigrateAsync();
                }

                return db;
            }
            catch
            {
                await db.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!created) return;
            await using var conn = new SqlConnection(server.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await cmd.ExecuteNonQueryAsync();
            created = false;
        }
    }
}
