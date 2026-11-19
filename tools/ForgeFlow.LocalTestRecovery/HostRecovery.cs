using System.Data;
using System.Security.Principal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class RecoveryOperatorConsent
{
    private RecoveryOperatorConsent(string reference) => Reference = reference;
    public string Reference { get; }
    // The operator verifies the account owner out of band and supervises private input.
    public static RecoveryOperatorConsent? Confirm(string? confirmation)
    {
        if (confirmation != "RECOVER") return null;
        string reference;
        if (OperatingSystem.IsWindows()) { using var identity = WindowsIdentity.GetCurrent(); reference = identity.Name; }
        else reference = Environment.MachineName + "/" + Environment.UserName;
        return string.IsNullOrWhiteSpace(reference) || reference.Length > 256 ? null : new(reference);
    }
}

public sealed record RecoveryRequest(Guid OperationId, long UserId);
public sealed record RecoveryResult(string Code, Guid OperationId);

/// <summary>Host-only supervised reset. Never registered by Web; no automatic retries.</summary>
public sealed class HostRecovery(string provisioningConnection, Action<DbContextOptionsBuilder>? configureAuth = null)
{
    public static IServiceCollection AddRecoveryIdentity(IServiceCollection services, Action<DbContextOptionsBuilder> options)
    {
        services.AddBootstrapIdentity(options);
        // Tokens live only inside this operation; no key files or token delivery channel.
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        new IdentityBuilder(typeof(ApplicationAccount), services)
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationAccount>>(TokenOptions.DefaultProvider);
        return services;
    }

    public async Task<RecoveryResult> ResetAsync(RecoveryRequest request, string password,
        RecoveryOperatorConsent? consent, CancellationToken ct = default)
    {
        if (consent is null) return new("Auth.OperatorRequired", request.OperationId);
        if (request.OperationId == Guid.Empty || request.UserId <= 0 || password.Length is < 12 or > 128)
            return new("Auth.ResetRejected", request.OperationId);
        var settings = new SqlConnectionStringBuilder(provisioningConnection);
        if (string.IsNullOrWhiteSpace(settings.InitialCatalog) || new[] { "master", "model", "msdb", "tempdb" }.Contains(settings.InitialCatalog, StringComparer.OrdinalIgnoreCase))
            return new("Auth.SchemaNotReady", request.OperationId);
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(ct);
        await using (var permission = connection.CreateCommand())
        {
            permission.CommandText = "SELECT CASE WHEN (USER_NAME()='dbo' OR IS_ROLEMEMBER('db_owner')=1) AND COALESCE(IS_ROLEMEMBER('ForgeFlowOrganizationRuntime'),0)=0 AND COALESCE(IS_ROLEMEMBER('ForgeFlowAuthRuntime'),0)=0 THEN 1 ELSE 0 END";
            if ((int)(await permission.ExecuteScalarAsync(ct))! != 1) return new("Auth.OperatorRequired", request.OperationId);
        }
        var services = AddRecoveryIdentity(new ServiceCollection(), options => {
            options.UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"));
            configureAuth?.Invoke(options);
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        if (!ReferenceEquals(db.Database.GetDbConnection(), connection)) throw new InvalidOperationException("Auth.SharedConnectionRequired");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) return new("Auth.SchemaNotReady", request.OperationId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Serialize resets for this account; future session issuance must acquire the same user lock.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM [auth].[User] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={request.UserId}").SingleOrDefaultAsync(ct);
        if (user is null || !await db.BootstrapStates.AnyAsync(s => s.State == 1, ct)) return new("Auth.ResetRejected", request.OperationId);
        if (await db.SecurityEvents.AnyAsync(e => e.SubjectUserId == request.UserId && e.CorrelationId == request.OperationId && e.EventCode == "PasswordResetCompleted", ct))
            return new("Auth.AlreadyRecovered", request.OperationId);
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
        var token = await manager.GeneratePasswordResetTokenAsync(user);
        var now = DateTime.UtcNow;
        user.UpdatedAtUtc = now;
        var reset = await manager.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded) return new("Auth.ResetRejected", request.OperationId);
        // Identity rotates SecurityStamp; eligibility/lockout/memberships/grants are untouched.
        var sessions = await db.Sessions.Where(s => s.UserId == request.UserId && s.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var session in sessions)
        {
            session.RevokedAtUtc = now; session.RevokeReason = "PasswordReset";
            AddEvent("SessionRevoked", session.Id, "{\"reason\":\"PasswordReset\"}");
        }
        AddEvent("PasswordResetCompleted", null, "{}");
        await db.SaveChangesAsync(ct);
        try { await transaction.CommitAsync(ct); }
        catch (Exception) { return new("Auth.RecoveryOutcomeUnknown", request.OperationId); }
        return new("Auth.PasswordResetCompleted", request.OperationId);

        void AddEvent(string code, Guid? sessionId, string data) => db.SecurityEvents.Add(new SecurityEvent {
            EventCode = code, SubjectUserId = user.Id, ActorKind = 2, HostOperatorReference = consent.Reference,
            SessionId = sessionId, OccurredAtUtc = now, CorrelationId = request.OperationId, Outcome = 0, DataJson = data });
    }
}
