using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class OrganizationSqlServerFactAttribute : FactAttribute
{
    public OrganizationSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER")))
            Skip = "Requires FORGEFLOW_TEST_SQLSERVER with authenticated SQL Server CREATE/DROP DATABASE access; never uses an existing database.";
    }
}

public sealed class OrganizationSqlServerTests
{
    // Synthetic audit attribution only; not an authentication or permission provider.
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task Fresh_migration_rollback_and_reapplication_only_touch_a_new_disposable_database()
    {
        await using var database = await DisposableDatabase.Create();
        await using var db = database.Context();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(39, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE schema_id = SCHEMA_ID('organization')").SingleAsync());
        await db.GetService<IMigrator>().MigrateAsync("0");
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE schema_id = SCHEMA_ID('organization')").SingleAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(39, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE schema_id = SCHEMA_ID('organization')").SingleAsync());
        Assert.Equal(11, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    [OrganizationSqlServerFact]
    public async Task Organization_and_site_unique_scopes_reserve_inactive_codes()
    {
        await using var database = await DisposableDatabase.Create();
        long alphaId;
        await using (var db = database.Context())
        {
            var alpha = Organization.Create("ALPHA", "Company");
            alpha.CreateSite("SAME", "Site");
            var beta = Organization.Create("BETA", "Company");
            beta.CreateSite("SAME", "Site");
            db.AddRange(alpha, beta);
            await db.SaveChangesAsync(Attribution());
            alphaId = alpha.Id;
            Assert.All(alpha.Sites, s => Assert.Equal(alpha.Id, s.OrganizationId));
        }
        await using (var db = database.Context())
        {
            var alpha = await db.Organizations.SingleAsync(o => o.Id == alphaId);
            Assert.Single(alpha.Sites); // EF materialized the complete aggregate, including inactive children.
            alpha.Sites[0].Deactivate();
            alpha.Deactivate();
            await db.SaveChangesAsync(Attribution());
        }
        await using (var db = database.Context())
        {
            db.Organizations.Add(Organization.Create("ALPHA", "Duplicate"));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Attribution()));
            Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
        }
        await using (var db = database.Context())
        {
            // Raw SQL deliberately bypasses aggregate duplicate checks to prove the SQL constraint.
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT [organization].[Site] ([OrganizationId],[Code],[Name],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({alphaId},'SAME',N'Duplicate',0,SYSUTCDATETIME(),1)"));
            Assert.Contains(error.Number, new[] { 2601, 2627 });
        }
    }

    [OrganizationSqlServerFact]
    public async Task Sql_constraints_reject_invalid_codes_foreign_keys_and_referenced_deletion()
    {
        await using var database = await DisposableDatabase.Create();
        await using var db = database.Context();
        var org = Organization.Create("ALPHA", "Company");
        org.CreateSite("A1", "Site");
        db.Add(org);
        await db.SaveChangesAsync(Attribution());
        foreach (var code in new[] { "bad", "A B", "A ", "_BAD", "A/B" })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT [organization].[Organization] ([Code],[Name],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({code},N'Invalid',1,SYSUTCDATETIME(),1)"));
            Assert.Equal(547, error.Number);
        }
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT [organization].[Site] ([OrganizationId],[Code],[Name],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({long.MaxValue},'BADFK',N'Invalid',1,SYSUTCDATETIME(),1)"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [organization].[Organization] WHERE [Id] = {org.Id}"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [organization].[Site] WHERE [Id] = {org.Sites[0].Id}"))).Number);
        var beta = Organization.Create("BETA", "Company");
        var foreignSite = beta.CreateSite("B1", "Site");
        db.Add(beta);
        await db.SaveChangesAsync(Attribution());
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT [organization].[OrganizationChange] ([OrganizationId],[SiteId],[ActorId],[EventCode],[OccurredAtUtc],[CorrelationId],[ChangesJson]) VALUES ({org.Id},{foreignSite.Id},1,'Site.Created',SYSUTCDATETIME(),NEWID(),N'{{}}')"))).Number);
    }

    [OrganizationSqlServerFact]
    public async Task Two_independent_contexts_enforce_rowversion_and_do_not_audit_the_losing_write()
    {
        await using var database = await DisposableDatabase.Create();
        await using (var setup = database.Context())
        {
            setup.Add(Organization.Create("ALPHA", "Company"));
            await setup.SaveChangesAsync(Attribution());
        }
        await using var first = database.Context();
        await using var second = database.Context();
        var winner = await first.Organizations.SingleAsync();
        var loser = await second.Organizations.SingleAsync();
        var initialVersion = first.Entry(winner).Property<byte[]>("RowVersion").CurrentValue!.ToArray();
        winner.Rename("First");
        loser.Rename("Second");
        await first.SaveChangesAsync(Attribution());
        Assert.False(initialVersion.SequenceEqual(first.Entry(winner).Property<byte[]>("RowVersion").CurrentValue!));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Attribution()));
        await using var verify = database.Context();
        Assert.Equal("First", (await verify.Organizations.SingleAsync()).Name);
        Assert.Equal(2, await verify.OrganizationChanges.CountAsync());
    }

    [OrganizationSqlServerFact]
    public async Task Audit_failure_rolls_back_business_write_and_unicode_names_fit_the_audit_contract()
    {
        await using var database = await DisposableDatabase.Create();
        await using (var setup = database.Context())
            await setup.Database.ExecuteSqlRawAsync("CREATE TRIGGER [organization].[RejectAuditFixture] ON [organization].[OrganizationChange] AFTER INSERT AS THROW 51000, 'Synthetic audit failure', 1;");
        await using (var failure = database.Context())
        {
            failure.Add(Organization.Create("ALPHA", "Company"));
            await Assert.ThrowsAsync<DbUpdateException>(() => failure.SaveChangesAsync(Attribution()));
        }
        await using var db = database.Context();
        Assert.Empty(await db.Organizations.ToListAsync());
        Assert.Empty(await db.OrganizationChanges.ToListAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [organization].[RejectAuditFixture]");
        var org = Organization.Create("ALPHA", new string('ن', 200));
        db.Add(org);
        await db.SaveChangesAsync(Attribution());
        org.Rename(new string('ک', 200));
        await db.SaveChangesAsync(Attribution());
        Assert.All(await db.OrganizationChanges.ToListAsync(), change => Assert.True(change.ChangesJson.Length <= 2000));
        Assert.Equal(DateTimeKind.Utc, db.Entry(org).Property<DateTime>("CreatedAtUtc").CurrentValue.Kind);
    }

    [OrganizationSqlServerFact]
    public async Task Runtime_database_role_cannot_delete_business_data_or_mutate_audit_history()
    {
        await using var database = await DisposableDatabase.Create();
        await using var db = database.Context();
        await db.Database.ExecuteSqlRawAsync("CREATE USER [OrganizationPermissionFixture] WITHOUT LOGIN; ALTER ROLE [ForgeFlowOrganizationRuntime] ADD MEMBER [OrganizationPermissionFixture];");
        foreach (var statement in new[] { "DELETE FROM [organization].[Site]", "DELETE FROM [organization].[Organization]",
            "UPDATE [organization].[OrganizationChange] SET [ActorId] = 2", "DELETE FROM [organization].[OrganizationChange]" })
        {
            // Only fixed fixture SQL; no user-supplied identifiers or payloads.
            var sql = "EXECUTE AS USER = 'OrganizationPermissionFixture'; BEGIN TRY " + statement + "; END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;";
            Assert.Equal(229, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql))).Number);
        }
    }

    internal sealed class DisposableDatabase : IAsyncDisposable
    {
        private readonly string name = "ForgeFlow_Slice01_Test_" + Guid.NewGuid().ToString("N");
        private readonly SqlConnectionStringBuilder server;
        private bool created;
        private DisposableDatabase(string connection)
        {
            server = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", ConnectTimeout = 5, Pooling = false };
        }

        internal OrganizationDbContext Context(Action<DbContextOptionsBuilder<OrganizationDbContext>>? configure = null)
        {
            var connection = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name };
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(connection.ConnectionString);
            configure?.Invoke(options);
            return new OrganizationDbContext(options.Options);
        }

        internal static async Task<DisposableDatabase> Create()
        {
            var database = new DisposableDatabase(Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER")!);
            try
            {
                await using var connection = new SqlConnection(database.server.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{database.name}]";
                await command.ExecuteNonQueryAsync();
                database.created = true;
                await using var db = database.Context();
                await db.Database.MigrateAsync();
                return database;
            }
            catch { await database.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            if (!created) return;
            // Generated name only; never drop InitialCatalog from a supplied connection string.
            const string prefix = "ForgeFlow_Slice01_Test_";
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
                throw new InvalidOperationException("Invalid disposable database name");
            await using var connection = new SqlConnection(server.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await command.ExecuteNonQueryAsync();
            created = false;
        }
    }
}
