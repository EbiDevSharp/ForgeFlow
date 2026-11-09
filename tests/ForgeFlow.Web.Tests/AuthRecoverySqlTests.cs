using System.Data.Common;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuthRecoverySqlTests
{
    private static RecoveryOperatorConsent Consent() => RecoveryOperatorConsent.Confirm("RECOVER")!;
    private static AuthDbContext Auth(string connection) => new(new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth")).Options);
    private static async Task<(string Connection, long UserId, string Password)> Seed(OrganizationSqlServerTests.DisposableDatabase database)
    {
        using var business = database.Context(); var connection = business.Database.GetConnectionString()!;
        await using var auth = Auth(connection); await auth.Database.MigrateAsync();
        var password = AuthBootstrapTests.Password();
        var result = await new HostBootstrapper(connection).InitializeAsync(AuthBootstrapTests.Request(), password, HostOperatorConsent.Confirm("INITIALIZE"));
        Assert.Equal("Auth.Initialized", result.Code);
        var user = await auth.Users.SingleAsync();
        var now = DateTime.UtcNow.AddHours(-2);
        foreach (var expired in new[] { false, true })
        {
            var id = Guid.NewGuid(); var expires = expired ? now.AddHours(1) : now.AddHours(8);
            await auth.Database.ExecuteSqlInterpolatedAsync($"INSERT [auth].[Session] ([Id],[UserId],[SecurityStampSnapshot],[IssuedAtUtc],[ExpiresAtUtc]) VALUES ({id},{user.Id},{user.SecurityStamp},{now},{expires})");
        }
        return (connection, user.Id, password);
    }

    [AuthBootstrapSqlTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_rotates_credentials_revokes_sessions_and_preserves_eligibility_and_authority(bool disabled)
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection, id, oldPassword) = await Seed(database);
        await using var before = Auth(connection);
        if (disabled)
        {
            await before.Database.ExecuteSqlRawAsync("UPDATE [auth].[User] SET IsActive=0,AccessFailedCount=4,LockoutEnd=DATEADD(day,1,SYSUTCDATETIME()); UPDATE [auth].[OrganizationMembership] SET IsActive=0; UPDATE [auth].[ScopedRoleAssignment] SET IsActive=0;");
        }
        var old = await before.Users.AsNoTracking().SingleAsync();
        var request = new RecoveryRequest(Guid.NewGuid(), id); var password = AuthBootstrapTests.Password();
        Assert.Equal("Auth.PasswordResetCompleted", (await new HostRecovery(connection).ResetAsync(request, password, Consent())).Code);
        await using var verify = Auth(connection); var user = await verify.Users.SingleAsync();
        var hasher = new PasswordHasher<ApplicationAccount>();
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, user.PasswordHash!, password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, user.PasswordHash!, oldPassword));
        Assert.NotEqual(old.SecurityStamp, user.SecurityStamp);
        Assert.Equal(old.IsActive, user.IsActive); Assert.Equal(old.LockoutEnd, user.LockoutEnd); Assert.Equal(old.AccessFailedCount, user.AccessFailedCount);
        Assert.All(await verify.Sessions.ToListAsync(), s => { Assert.NotNull(s.RevokedAtUtc); Assert.Equal("PasswordReset", s.RevokeReason); Assert.NotEqual(user.SecurityStamp, s.SecurityStampSnapshot); });
        Assert.Equal(!disabled, (await verify.OrganizationMemberships.SingleAsync()).IsActive);
        var grant = await verify.ScopedRoleAssignments.SingleAsync(); Assert.Equal((byte)1, grant.RoleId); Assert.Equal(!disabled, grant.IsActive);
        var events = await verify.SecurityEvents.Where(e => e.CorrelationId == request.OperationId).ToListAsync();
        Assert.Equal(3, events.Count); Assert.Single(events, e => e.EventCode == "PasswordResetCompleted");
        Assert.All(events, e => { Assert.Equal((byte)2, e.ActorKind); Assert.Null(e.ActorUserId); Assert.Equal(id,e.SubjectUserId); Assert.Equal(Consent().Reference,e.HostOperatorReference); Assert.DoesNotContain(password,e.DataJson); });
        Assert.Equal("Auth.AlreadyRecovered", (await new HostRecovery(connection).ResetAsync(request, AuthBootstrapTests.Password(), Consent())).Code);
        await using var replay = Auth(connection); Assert.Equal(user.PasswordHash, (await replay.Users.SingleAsync()).PasswordHash); Assert.Equal(8, await replay.SecurityEvents.CountAsync());
        Assert.Equal((byte)1, (await replay.BootstrapStates.SingleAsync()).State);
        await using var business = database.Context(); Assert.Equal(2,await business.OrganizationChanges.CountAsync());
    }

    private sealed class RejectStage(string stage) : SaveChangesInterceptor
    {
        public int Hits { get; private set; }
        public Exception Failure { get; } = new InvalidOperationException("SyntheticRecoveryFailure");
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if ((stage == "sessions" && db.ChangeTracker.Entries<AccountSession>().Any(e => e.State == EntityState.Modified)) ||
                (stage == "audit" && db.ChangeTracker.Entries<SecurityEvent>().Any(e => e.State == EntityState.Added)))
            { Hits++; throw Failure; }
            return ValueTask.FromResult(result);
        }
    }
    [AuthBootstrapSqlTheory]
    [InlineData("sessions")]
    [InlineData("audit")]
    public async Task Failure_after_identity_password_save_rolls_back_password_stamp_sessions_and_events(string stage)
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection,id,_) = await Seed(database);
        await using var before = Auth(connection); var original = await before.Users.SingleAsync();
        var interceptor = new RejectStage(stage);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => new HostRecovery(connection, o => o.AddInterceptors(interceptor)).ResetAsync(new(Guid.NewGuid(),id),AuthBootstrapTests.Password(),Consent()));
        Assert.Same(interceptor.Failure,error); Assert.Equal(1,interceptor.Hits);
        await using var verify = Auth(connection); var user = await verify.Users.SingleAsync();
        Assert.Equal(original.PasswordHash,user.PasswordHash); Assert.Equal(original.SecurityStamp,user.SecurityStamp);
        Assert.All(await verify.Sessions.ToListAsync(),s => Assert.Null(s.RevokedAtUtc)); Assert.Equal(5,await verify.SecurityEvents.CountAsync());
    }

    [OrganizationSqlServerFact]
    public async Task Unknown_subject_does_not_change_any_account_or_event()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection,_,_) = await Seed(database);
        Assert.Equal("Auth.ResetRejected",(await new HostRecovery(connection).ResetAsync(new(Guid.NewGuid(),long.MaxValue),AuthBootstrapTests.Password(),Consent())).Code);
        await using var verify = Auth(connection); Assert.Equal(5,await verify.SecurityEvents.CountAsync()); Assert.All(await verify.Sessions.ToListAsync(),s=>Assert.Null(s.RevokedAtUtc));
    }
    [OrganizationSqlServerFact]
    public async Task Recovery_leaves_other_account_sessions_and_previously_revoked_sessions_unchanged()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection,id,_) = await Seed(database);
        await using var provider = HostRecovery.AddRecoveryIdentity(new ServiceCollection(),o=>o.UseSqlServer(connection,sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory","auth"))).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
        var other = new ApplicationAccount { UserName = "other.owner" };
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>(); db.Entry(other).Property("CreatedAtUtc").CurrentValue=DateTime.UtcNow;
        Assert.True((await manager.CreateAsync(other,AuthBootstrapTests.Password())).Succeeded);
        var original = await db.Users.SingleAsync(u=>u.Id==id);
        var previous = Guid.NewGuid(); var unrelated = Guid.NewGuid(); var issued = DateTime.UtcNow.AddHours(-1); var expires = issued.AddHours(8); var revoked = issued.AddMinutes(1);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [auth].[Session] ([Id],[UserId],[SecurityStampSnapshot],[IssuedAtUtc],[ExpiresAtUtc],[RevokedAtUtc],[RevokeReason]) VALUES ({previous},{id},{original.SecurityStamp},{issued},{expires},{revoked},'Logout')");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [auth].[Session] ([Id],[UserId],[SecurityStampSnapshot],[IssuedAtUtc],[ExpiresAtUtc]) VALUES ({unrelated},{other.Id},{other.SecurityStamp},{issued},{expires})");
        Assert.Equal("Auth.PasswordResetCompleted",(await new HostRecovery(connection).ResetAsync(new(Guid.NewGuid(),id),AuthBootstrapTests.Password(),Consent())).Code);
        await using var verify = Auth(connection);
        var stored = await verify.Sessions.SingleAsync(s=>s.Id==previous); Assert.Equal(revoked,stored.RevokedAtUtc); Assert.Equal("Logout",stored.RevokeReason);
        Assert.Null((await verify.Sessions.SingleAsync(s=>s.Id==unrelated)).RevokedAtUtc);
        Assert.Equal(other.SecurityStamp,(await verify.Users.SingleAsync(u=>u.Id==other.Id)).SecurityStamp);
    }

    private sealed class StartTogether : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [auth].[User] WITH (UPDLOCK,HOLDLOCK)",StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrivals)==2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(30),cancellationToken);
            }
            return result;
        }
    }
    [OrganizationSqlServerFact]
    public async Task Concurrent_same_operation_resets_once()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection,id,_) = await Seed(database); var request = new RecoveryRequest(Guid.NewGuid(),id);
        var barrier = new StartTogether();
        var results = await Task.WhenAll(Enumerable.Range(0,2).Select(_ => new HostRecovery(connection,o=>o.AddInterceptors(barrier)).ResetAsync(request,AuthBootstrapTests.Password(),Consent())));
        Assert.Single(results,r=>r.Code=="Auth.PasswordResetCompleted"); Assert.Single(results,r=>r.Code=="Auth.AlreadyRecovered");
        await using var verify = Auth(connection); Assert.Equal(8,await verify.SecurityEvents.CountAsync());
    }
    private sealed class LostAcknowledgement : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) => throw new IOException("SyntheticRecoveryAcknowledgementLost");
    }
    [OrganizationSqlServerFact]
    public async Task Sql_audit_rejection_rolls_back_already_saved_identity_and_session_updates()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection,id,_) = await Seed(database);
        await using var before = Auth(connection); var original = await before.Users.SingleAsync();
        await before.Database.ExecuteSqlRawAsync("ALTER TABLE [auth].[SecurityEvent] ADD CONSTRAINT [SyntheticRecoveryAuditFailure] CHECK (EventCode <> 'PasswordResetCompleted')");
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => new HostRecovery(connection).ResetAsync(new(Guid.NewGuid(),id),AuthBootstrapTests.Password(),Consent()));
        Assert.Equal(547,Assert.IsType<Microsoft.Data.SqlClient.SqlException>(error.InnerException).Number);
        Assert.Contains("SyntheticRecoveryAuditFailure",error.InnerException!.Message);
        await using var verify = Auth(connection); var user = await verify.Users.SingleAsync();
        Assert.Equal(original.PasswordHash,user.PasswordHash); Assert.Equal(original.SecurityStamp,user.SecurityStamp);
        Assert.All(await verify.Sessions.ToListAsync(),s=>Assert.Null(s.RevokedAtUtc)); Assert.Equal(5,await verify.SecurityEvents.CountAsync());
    }
    [OrganizationSqlServerFact]
    public async Task Uncertain_commit_is_resolved_without_changing_password_again()
    {
        await using var database = await OrganizationSqlServerTests.DisposableDatabase.Create();
        var (connection,id,_) = await Seed(database); var request = new RecoveryRequest(Guid.NewGuid(),id);
        Assert.Equal("Auth.RecoveryOutcomeUnknown",(await new HostRecovery(connection,o=>o.AddInterceptors(new LostAcknowledgement())).ResetAsync(request,AuthBootstrapTests.Password(),Consent())).Code);
        await using var before = Auth(connection); var original = await before.Users.SingleAsync();
        Assert.Equal("Auth.AlreadyRecovered",(await new HostRecovery(connection).ResetAsync(request,AuthBootstrapTests.Password(),Consent())).Code);
        await using var verify = Auth(connection); Assert.Equal(original.PasswordHash,(await verify.Users.SingleAsync()).PasswordHash); Assert.Equal(8,await verify.SecurityEvents.CountAsync());
    }
}
