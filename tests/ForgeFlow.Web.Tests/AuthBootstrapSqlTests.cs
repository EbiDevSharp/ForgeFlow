using System.Data.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuthBootstrapSqlTheoryAttribute : TheoryAttribute
{
    public AuthBootstrapSqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER")))
            Skip = "Requires authorized disposable SQL Server access through FORGEFLOW_TEST_SQLSERVER.";
    }
}

public sealed class AuthBootstrapSqlTests
{
    private static string Connection(OrganizationSqlServerTests.DisposableDatabase database)
    { using var db = database.Context(); return db.Database.GetConnectionString()!; }
    private static AuthDbContext Auth(string connection) => new(new DbContextOptionsBuilder<AuthDbContext>()
        .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth")).Options);
    private static HostOperatorConsent Consent() => HostOperatorConsent.Confirm("INITIALIZE")!;
    private static async Task Migrate(string connection) { await using var auth = Auth(connection); await auth.Database.MigrateAsync(); }

    [OrganizationSqlServerFact]
    public async Task Additive_auth_migration_preserves_existing_business_data_and_refuses_adoption()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var connection = Connection(database); byte[] version;
        await using (var business = database.Context())
        {
            var org = Organization.Create("EXISTING", "Existing company"); org.CreateSite("SITE", "Existing site");
            business.Add(org); await business.SaveChangesAsync(new TrustedActor(42, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid()));
            version = business.Entry(org).Property<byte[]>("RowVersion").CurrentValue!.ToArray();
        }
        await Migrate(connection);
        await using (var auth = Auth(connection))
        {
            Assert.False(auth.Database.HasPendingModelChanges()); Assert.Single(await auth.Database.GetAppliedMigrationsAsync());
            Assert.Equal(11, await auth.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE schema_id=SCHEMA_ID('auth')").SingleAsync());
            // Empty auth rollback/reapply leaves pre-existing Organization/Site data untouched.
            await auth.GetService<IMigrator>().MigrateAsync("0"); await auth.Database.MigrateAsync();
        }
        var result = await new HostBootstrapper(connection).InitializeAsync(AuthBootstrapTests.Request(), AuthBootstrapTests.Password(), Consent());
        Assert.Equal("Auth.BootstrapPartial", result.Code);
        await using var verify = Auth(connection); Assert.Empty(await verify.Users.ToListAsync());
        Assert.Equal((byte)0, (await verify.BootstrapStates.SingleAsync()).State);
        await using var stored = database.Context(); var existing = await stored.Organizations.SingleAsync();
        Assert.Equal("EXISTING", existing.Code); Assert.Single(existing.Sites);
        Assert.True(version.SequenceEqual(stored.Entry(existing).Property<byte[]>("RowVersion").CurrentValue!));
        Assert.Equal(2, await stored.OrganizationChanges.CountAsync());
    }

    [OrganizationSqlServerFact]
    public async Task Bootstrap_creates_real_identity_company_only_grants_and_correlated_audits_once()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var connection = Connection(database); await Migrate(connection);
        var request = AuthBootstrapTests.Request(); var password = AuthBootstrapTests.Password();
        var bootstrap = new HostBootstrapper(connection);
        var result = await bootstrap.InitializeAsync(request, password, Consent());
        Assert.Equal("Auth.Initialized", result.Code); Assert.True(result.UserId > 0);
        await using var auth = Auth(connection); var account = await auth.Users.SingleAsync();
        Assert.Equal(PasswordVerificationResult.Success, new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationAccount>().VerifyHashedPassword(account, account.PasswordHash!, password));
        Assert.Equal("INITIAL.ADMIN", account.NormalizedUserName); Assert.True(account.IsActive);
        Assert.Single(await auth.OrganizationMemberships.ToListAsync()); Assert.Single(await auth.SiteMemberships.ToListAsync());
        var role = await auth.ScopedRoleAssignments.SingleAsync(); Assert.Equal(FixedAccessRoles.OrganizationAdministrator, role.RoleId);
        Assert.Equal((byte)1, role.ScopeKind); Assert.Equal(result.OrganizationId, role.OrganizationId); Assert.Null(role.SiteId);
        Assert.False(await auth.ScopedRoleAssignments.AnyAsync(r => r.RoleId == FixedAccessRoles.DeploymentProvisioner || r.ScopeKind == 0));
        Assert.Empty(await auth.Sessions.ToListAsync());
        var events = await auth.SecurityEvents.ToListAsync(); Assert.Equal(5, events.Count);
        Assert.All(events, e => { Assert.Equal((byte)2, e.ActorKind); Assert.Null(e.ActorUserId); Assert.Equal(account.Id, e.SubjectUserId); Assert.False(string.IsNullOrWhiteSpace(e.HostOperatorReference)); });
        await using var business = database.Context(); var audits = await business.OrganizationChanges.ToListAsync(); Assert.Equal(2, audits.Count);
        Assert.All(audits, a => { Assert.Equal(account.Id, a.ActorId); Assert.Equal(result.OrganizationId, a.OrganizationId); });
        Assert.Single(audits.Select(a => a.CorrelationId).Concat(events.Select(e => e.CorrelationId)).Distinct());
        Assert.Equal("Auth.AlreadyInitialized", (await bootstrap.InitializeAsync(request, AuthBootstrapTests.Password(), Consent())).Code);
        Assert.Equal("Auth.BootstrapClosed", (await bootstrap.InitializeAsync(request with { OperationId = Guid.NewGuid() }, password, Consent())).Code);
        Assert.Equal("Auth.BootstrapClosed", (await bootstrap.InitializeAsync(request with { OrganizationCode = "DIFFERENT" }, password, Consent())).Code);
        Assert.Single(await auth.Users.ToListAsync()); Assert.Equal(5, await auth.SecurityEvents.CountAsync());
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => auth.GetService<IMigrator>().MigrateAsync("0"));
        // A completed marker with missing required links is partial, never repaired or adopted.
        await auth.Database.ExecuteSqlRawAsync("DELETE FROM [auth].[ScopedRoleAssignment]");
        Assert.Equal("Auth.BootstrapPartial", (await bootstrap.InitializeAsync(request, password, Consent())).Code);
        Assert.Empty(await auth.ScopedRoleAssignments.ToListAsync()); Assert.Single(await auth.Users.ToListAsync());
    }

    internal sealed class RejectStage(string stage) : DbCommandInterceptor
    {
        internal InvalidOperationException Failure { get; } = new("SyntheticBootstrapFailure");
        internal int FailureHits { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var match = stage switch {
                "user" => "INSERT INTO [auth].[User]", "organization" => "INSERT INTO [organization].[Organization]",
                "business-audit" => "INSERT INTO [organization].[OrganizationChange]", "membership" => "INSERT INTO [auth].[OrganizationMembership]",
                "role" => "INSERT INTO [auth].[ScopedRoleAssignment]", "security-event" => "INSERT INTO [auth].[SecurityEvent]",
                "completion" => "UPDATE [auth].[BootstrapState]", _ => throw new InvalidOperationException() };
            // Identity-returning batches can use MERGE for audit as well as security-event inserts.
            var merge = match.StartsWith("INSERT INTO ", StringComparison.Ordinal)
                ? match.Replace("INSERT INTO ", "MERGE ", StringComparison.Ordinal) : null;
            if (command.CommandText.Contains(match, StringComparison.Ordinal)
                || (merge is not null && command.CommandText.Contains(merge, StringComparison.Ordinal)))
            {
                FailureHits++;
                throw Failure;
            }
            return ValueTask.FromResult(result);
        }
    }

    [AuthBootstrapSqlTheory]
    [InlineData("user")]
    [InlineData("organization")]
    [InlineData("business-audit")]
    [InlineData("membership")]
    [InlineData("role")]
    [InlineData("security-event")]
    [InlineData("completion")]
    public async Task Every_required_stage_rolls_back_identity_business_grants_history_and_completion(string stage)
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var connection = Connection(database); await Migrate(connection); var interceptor = new RejectStage(stage);
        var bootstrap = new HostBootstrapper(connection, options => options.AddInterceptors(interceptor), options => options.AddInterceptors(interceptor));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => bootstrap.InitializeAsync(AuthBootstrapTests.Request(), AuthBootstrapTests.Password(), Consent()));
        Assert.Equal(1, interceptor.FailureHits);
        var chain = new List<Exception>();
        for (Exception? current = failure; current is not null; current = current.InnerException) chain.Add(current);
        Assert.Contains(chain, exception => ReferenceEquals(exception, interceptor.Failure));
        await using var auth = Auth(connection);
        Assert.Empty(await auth.Users.ToListAsync()); Assert.Empty(await auth.OrganizationMemberships.ToListAsync());
        Assert.Empty(await auth.SiteMemberships.ToListAsync()); Assert.Empty(await auth.ScopedRoleAssignments.ToListAsync()); Assert.Empty(await auth.SecurityEvents.ToListAsync());
        Assert.Empty(await auth.Sessions.ToListAsync());
        Assert.Empty(await auth.Set<IdentityUserClaim<long>>().ToListAsync());
        Assert.Empty(await auth.Set<IdentityUserLogin<long>>().ToListAsync());
        Assert.Empty(await auth.Set<IdentityUserToken<long>>().ToListAsync());
        var pending = await auth.BootstrapStates.SingleAsync();
        Assert.Equal((byte)0, pending.State);
        Assert.Null(pending.OperationId); Assert.Null(pending.CompletedAtUtc); Assert.Null(pending.FirstUserId);
        Assert.Null(pending.OrganizationId); Assert.Null(pending.SiteId); Assert.Null(pending.HostOperatorReference);
        await using var business = database.Context(); Assert.Empty(await business.Organizations.ToListAsync());
        Assert.Empty(await business.Sites.ToListAsync()); Assert.Empty(await business.OrganizationChanges.ToListAsync());
    }

    private sealed class InitializationBarrier : DbCommandInterceptor
    {
        private int arrivals; private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[auth].[BootstrapState] WITH (UPDLOCK,HOLDLOCK)", StringComparison.Ordinal))
            { if (Interlocked.Increment(ref arrivals) == 2) gate.TrySetResult(); await gate.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); }
            return result;
        }
    }
    [AuthBootstrapSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_initializers_never_create_multiple_administrators(bool sameOperation)
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var connection = Connection(database); await Migrate(connection); var barrier = new InitializationBarrier();
        var request = AuthBootstrapTests.Request(); var password = AuthBootstrapTests.Password();
        var first = new HostBootstrapper(connection, options => options.AddInterceptors(barrier)).InitializeAsync(request, password, Consent());
        var second = new HostBootstrapper(connection, options => options.AddInterceptors(barrier)).InitializeAsync(request with { OperationId = sameOperation ? request.OperationId : Guid.NewGuid() }, password, Consent());
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, r => r.Code == "Auth.Initialized");
        Assert.Single(results, r => r.Code == (sameOperation ? "Auth.AlreadyInitialized" : "Auth.BootstrapClosed"));
        await using var auth = Auth(connection); Assert.Single(await auth.Users.ToListAsync()); Assert.Single(await auth.ScopedRoleAssignments.ToListAsync());
        await using var business = database.Context(); Assert.Single(await business.Organizations.ToListAsync()); Assert.Single(await business.Sites.ToListAsync());
    }

    [OrganizationSqlServerFact]
    public async Task Membership_scope_constraints_and_runtime_role_prevent_cross_owner_grants_and_setup_changes()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var connection = Connection(database); await Migrate(connection);
        var result = await new HostBootstrapper(connection).InitializeAsync(AuthBootstrapTests.Request(), AuthBootstrapTests.Password(), Consent());
        Assert.Equal("Auth.Initialized", result.Code);
        long foreignSite;
        await using (var business = database.Context())
        {
            var other = Organization.Create("OTHER", "Other company"); var site = other.CreateSite("OTHER", "Other site");
            business.Add(other); await business.SaveChangesAsync(new TrustedActor(result.UserId!.Value, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid())); foreignSite = site.Id;
        }
        await using var auth = Auth(connection);
        var fk = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => auth.Database.ExecuteSqlInterpolatedAsync($"INSERT [auth].[SiteMembership] ([UserId],[OrganizationId],[SiteId],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({result.UserId},{result.OrganizationId},{foreignSite},1,SYSUTCDATETIME(),{result.UserId})"));
        Assert.Equal(547, fk.Number);
        var scopeError = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => auth.Database.ExecuteSqlInterpolatedAsync($"INSERT [auth].[ScopedRoleAssignment] ([UserId],[RoleId],[ScopeKind],[OrganizationId],[SiteId],[IsActive],[CreatedAtUtc],[CreatedByActorId]) VALUES ({result.UserId},3,1,{result.OrganizationId},NULL,1,SYSUTCDATETIME(),{result.UserId})"));
        Assert.Equal(547, scopeError.Number);
        await auth.Database.ExecuteSqlRawAsync("CREATE USER [AuthRuntimeFixture] WITHOUT LOGIN; ALTER ROLE [ForgeFlowAuthRuntime] ADD MEMBER [AuthRuntimeFixture];");
        foreach (var statement in new[] { "UPDATE [auth].[BootstrapState] SET [State]=0", "DELETE FROM [auth].[User]", "UPDATE [auth].[SecurityEvent] SET [Outcome]=1", "DELETE FROM [auth].[ScopedRoleAssignment]" })
        {
            var sql = "EXECUTE AS USER='AuthRuntimeFixture'; BEGIN TRY " + statement + "; END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;";
            Assert.Equal(229, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => auth.Database.ExecuteSqlRawAsync(sql))).Number);
        }
    }

    private sealed class LostAcknowledgement : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
            => throw new IOException("SyntheticBootstrapCommitAcknowledgementLost");
    }
    [OrganizationSqlServerFact]
    public async Task Unknown_commit_is_resolved_by_operation_id_without_recreating_or_overwriting_records()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var connection = Connection(database); await Migrate(connection); var request = AuthBootstrapTests.Request();
        var result = await new HostBootstrapper(connection, options => options.AddInterceptors(new LostAcknowledgement()))
            .InitializeAsync(request, AuthBootstrapTests.Password(), Consent());
        Assert.Equal("Auth.BootstrapOutcomeUnknown", result.Code);
        Assert.Equal("Auth.AlreadyInitialized", (await new HostBootstrapper(connection).InitializeAsync(request, AuthBootstrapTests.Password(), Consent())).Code);
        await using var auth = Auth(connection); Assert.Single(await auth.Users.ToListAsync()); Assert.Equal(5, await auth.SecurityEvents.CountAsync());
        await using var business = database.Context(); Assert.Single(await business.Organizations.ToListAsync()); Assert.Equal(2, await business.OrganizationChanges.CountAsync());
    }
}
