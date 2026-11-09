using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace ForgeFlow.LocalTestRecovery;

public sealed class RecoveryText;

// Separate host-only local helper. No reference from Web and no configurable target.
public static class LocalTestRecoveryProgram
{
    public const string Connection = "Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5;";
    public static readonly Guid PreviousOperation = new("80c2921b-bec8-4200-86af-bd16b522fc3a");
    private const string Account = "EBRAHIM";

    public static bool ExecutionAllowed(string[] args, string? environment, bool inputRedirected, bool outputRedirected)
        => args.SequenceEqual(["--execute"]) && environment == "Development" && !inputRedirected && !outputRedirected;

    public static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        using var textProvider = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider();
        var text = textProvider.GetRequiredService<IStringLocalizer<RecoveryText>>();
        if (!ExecutionAllowed(args, Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), Console.IsInputRedirected, Console.IsOutputRedirected))
        { Console.Error.WriteLine(text["ExecutionRequired"]); return 2; }
        Console.WriteLine(text["Target"]);
        Console.Write(text["Consent"]);
        var consent = RecoveryOperatorConsent.Confirm(Console.ReadLine());
        if (consent is null) { Console.Error.WriteLine(text["Cancelled"]); return 2; }
        var stage = "resolve-previous-operation";
        var operation = Guid.Empty;
        try
        {
            using var services = HostRecovery.AddRecoveryIdentity(new ServiceCollection(), options =>
                options.UseSqlServer(Connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"))).BuildServiceProvider();
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Database.OpenConnectionAsync();
            // Match A2's trusted host capability before resolving the subject/ledger.
            using (var permission = db.Database.GetDbConnection().CreateCommand())
            {
                permission.CommandText = "SELECT CASE WHEN (USER_NAME()='dbo' OR IS_ROLEMEMBER('db_owner')=1) AND COALESCE(IS_ROLEMEMBER('ForgeFlowOrganizationRuntime'),0)=0 AND COALESCE(IS_ROLEMEMBER('ForgeFlowAuthRuntime'),0)=0 THEN 1 ELSE 0 END";
                if ((int)(await permission.ExecuteScalarAsync())! != 1)
                { Console.Error.WriteLine(text["Result", "Auth.OperatorRequired"]); return 1; }
            }
            Snapshot before;
            // Same account lock as A2: wait for an in-flight prior operation before resolving its committed marker.
            await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                var user = await db.Users.FromSqlInterpolated($"SELECT * FROM [auth].[User] WITH (UPDLOCK,HOLDLOCK) WHERE [NormalizedUserName]={Account}").SingleOrDefaultAsync();
                if (user is null || user.UserName != "ebrahim")
                { Console.Error.WriteLine(text["Result", "Auth.ResetRejected"]); return 1; }
                var prior = await db.SecurityEvents.Where(e => e.CorrelationId == PreviousOperation && e.EventCode == "PasswordResetCompleted").Select(e => e.SubjectUserId).ToListAsync();
                if (prior.Count > 1 || prior.Any(id => id != user.Id))
                { Console.Error.WriteLine(text["Result", "Auth.PreviousOperationMismatch"]); return 1; }
                Console.WriteLine(text[prior.Count == 1 ? "PreviousCommitted" : "PreviousNotCommitted", PreviousOperation]);
                before = new Snapshot
                {
                    UserId = user.Id, Stamp = user.SecurityStamp!, Eligibility = Eligibility(user),
                    Access = await AccessSnapshot(db, user.Id),
                    Sessions = await db.Sessions.Where(s => s.UserId == user.Id && s.RevokedAtUtc == null).Select(s => s.Id).ToArrayAsync()
                };
                await transaction.CommitAsync();
            }
            // End the read transaction before calling A2, which owns its own transaction.
            operation = Guid.NewGuid();
            Console.WriteLine(text["Operation", operation]);
            var password = GenerateTemporaryPassword();
            stage = "a2-recovery";
            var result = await new HostRecovery(Connection).ResetAsync(new(operation, before.UserId), password, consent);
            Console.WriteLine(text["Result", result.Code]);
            if (result.Code is not ("Auth.PasswordResetCompleted" or "Auth.RecoveryOutcomeUnknown")) return 1;
            stage = "verify-committed-recovery";
            db.ChangeTracker.Clear();
            var after = await db.Users.AsNoTracking().SingleAsync(u => u.Id == before.UserId && u.NormalizedUserName == Account);
            var events = await db.SecurityEvents.AsNoTracking().Where(e => e.SubjectUserId == before.UserId && e.CorrelationId == operation).ToListAsync();
            var sessions = await db.Sessions.AsNoTracking().Where(s => before.Sessions.Contains(s.Id)).ToListAsync();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationAccount>>();
            var verified = events.Count(e => e.EventCode == "PasswordResetCompleted" && e.ActorKind == 2) == 1
                && after.SecurityStamp != before.Stamp && Eligibility(after) == before.Eligibility
                && await AccessSnapshot(db, before.UserId) == before.Access
                && sessions.Count == before.Sessions.Length && sessions.All(s => s.RevokedAtUtc is not null && s.RevokeReason == "PasswordReset"
                    && events.Any(e => e.EventCode == "SessionRevoked" && e.SessionId == s.Id))
                && hasher.VerifyHashedPassword(after, after.PasswordHash!, password) != PasswordVerificationResult.Failed;
            if (!verified) { Console.Error.WriteLine(text["Unconfirmed", operation]); return 1; }
            Console.WriteLine(text["Verified"]);
            // Deliberate one-time private terminal disclosure, only after verified commit. Never persist or log.
            Console.WriteLine(text["TemporaryPassword", password]);
            return 0;
        }
        catch (Exception exception)
        {
            // No exception message, connection, hash, stamp, token or password in diagnostics.
            Console.Error.WriteLine(text["Failure", stage, operation, exception.GetType().Name]);
            return 1;
        }
    }

    public static string GenerateTemporaryPassword()
    {
        string[] groups = ["ABCDEFGHJKLMNPQRSTUVWXYZ", "abcdefghijkmnopqrstuvwxyz", "23456789", "!@#$%*-_+"];
        var alphabet = string.Concat(groups);
        var characters = new char[28];
        for (var index = 0; index < characters.Length; index++)
        {
            var choices = index < groups.Length ? groups[index] : alphabet;
            characters[index] = choices[RandomNumberGenerator.GetInt32(choices.Length)];
        }
        for (var index = characters.Length - 1; index > 0; index--)
        { var other = RandomNumberGenerator.GetInt32(index + 1); (characters[index], characters[other]) = (characters[other], characters[index]); }
        var password = new string(characters); Array.Clear(characters); return password;
    }

    private static string Eligibility(ApplicationAccount user) => JsonSerializer.Serialize(new { user.IsActive, user.LockoutEnd, user.LockoutEnabled, user.AccessFailedCount, user.PreferredCulture });
    private static async Task<string> AccessSnapshot(AuthDbContext db, long id) => JsonSerializer.Serialize(new
    {
        Organizations = await db.OrganizationMemberships.AsNoTracking().Where(m => m.UserId == id).OrderBy(m => m.OrganizationId).ToListAsync(),
        Sites = await db.SiteMemberships.AsNoTracking().Where(m => m.UserId == id).OrderBy(m => m.OrganizationId).ThenBy(m => m.SiteId).ToListAsync(),
        Grants = await db.ScopedRoleAssignments.AsNoTracking().Where(g => g.UserId == id).OrderBy(g => g.Id).ToListAsync()
    });
    private sealed class Snapshot
    {
        public required long UserId { get; init; }
        public required string Stamp { get; init; }
        public required string Eligibility { get; init; }
        public required string Access { get; init; }
        public required Guid[] Sessions { get; init; }
        public override string ToString() => nameof(Snapshot);
    }
}
