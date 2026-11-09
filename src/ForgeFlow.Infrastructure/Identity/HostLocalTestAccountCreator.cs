using System.Data;
using System.Security.Principal;
using ForgeFlow.Domain.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class LocalTestAccountConsent
{
    private LocalTestAccountConsent(string reference) => Reference = reference;
    public string Reference { get; }
    public static LocalTestAccountConsent? Confirm(string? value)
    {
        if (value != "CREATE TEST ACCOUNT" || !OperatingSystem.IsWindows()) return null;
        using var identity = WindowsIdentity.GetCurrent();
        return string.IsNullOrWhiteSpace(identity.Name) || identity.Name.Length > 256 ? null : new(identity.Name);
    }
}

public sealed record LocalTestAccountResult(string Code, Guid OperationId, long? UserId = null, long? OrganizationId = null, string? UserName = null);

// Explicitly authorized local host tool only. Never registered by Web; no configurable target.
public sealed class HostLocalTestAccountCreator
{
    public const string Connection = "Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5;";
    public static string UserNameFor(Guid operation) => "localtest-" + operation.ToString("N");

    public async Task<LocalTestAccountResult> CreateAsync(Guid operation, string password, LocalTestAccountConsent? consent, CancellationToken ct = default)
    {
        if (consent is null) return new("Auth.OperatorRequired", operation);
        if (operation == Guid.Empty || password.Length is < 12 or > 128) return new("Auth.IdentityRejected", operation);
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync(ct);
        await using (var permission = connection.CreateCommand())
        {
            permission.CommandText = "SELECT CASE WHEN (USER_NAME()='dbo' OR IS_ROLEMEMBER('db_owner')=1) AND COALESCE(IS_ROLEMEMBER('ForgeFlowOrganizationRuntime'),0)=0 AND COALESCE(IS_ROLEMEMBER('ForgeFlowAuthRuntime'),0)=0 THEN 1 ELSE 0 END";
            if ((int)(await permission.ExecuteScalarAsync(ct))! != 1) return new("Auth.OperatorRequired", operation);
        }
        using var services = new ServiceCollection().AddBootstrapIdentity(options =>
            options.UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"))).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) return new("Auth.SchemaNotReady", operation);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var organization = await db.Set<Organization>().FromSqlRaw("SELECT * FROM [organization].[Organization] WITH (UPDLOCK,HOLDLOCK) WHERE [Code]=N'ORG1'").SingleOrDefaultAsync(ct);
        if (organization is null || !organization.IsActive || !await db.BootstrapStates.AnyAsync(state => state.State == 1, ct))
            return new("Auth.LocalTestTargetNotReady", operation);
        var username = UserNameFor(operation);
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == username.ToUpperInvariant(), ct))
            return new("Auth.LocalTestAccountExists", operation); // Never adopt or overwrite an existing account.
        var now = DateTime.UtcNow;
        var account = new ApplicationAccount { UserName = username, IsActive = true, LockoutEnabled = true, CreatedAtUtc = now };
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
        if (!(await manager.CreateAsync(account, password)).Succeeded) return new("Auth.IdentityRejected", operation);
        var membership = new OrganizationMembership { UserId = account.Id, OrganizationId = organization.Id, IsActive = true };
        var role = new ScopedRoleAssignment { UserId = account.Id, OrganizationId = organization.Id,
            RoleId = FixedAccessRoles.OrganizationAdministrator, ScopeKind = 1, SiteId = null, IsActive = true };
        db.AddRange(membership, role);
        foreach (var entity in new object[] { membership, role })
        {
            db.Entry(entity).Property("CreatedAtUtc").CurrentValue = now;
            // Same subject-attribution convention as existing host provisioning; host identity is explicit in audit.
            db.Entry(entity).Property("CreatedByActorId").CurrentValue = account.Id;
        }
        foreach (var (code, data) in new[] { ("AccountCreated", "{}"), ("MembershipChanged", "{\"scope\":\"organization\"}"), ("ScopedRoleChanged", "{\"role\":\"OrganizationAdministrator\"}") })
            db.SecurityEvents.Add(new SecurityEvent { EventCode = code, DataJson = data, ActorKind = 2,
                HostOperatorReference = consent.Reference, SubjectUserId = account.Id, OrganizationId = organization.Id,
                CorrelationId = operation, OccurredAtUtc = now, Outcome = 0 });
        await db.SaveChangesAsync(ct);
        try { await transaction.CommitAsync(ct); }
        catch (Exception) { return new("Auth.LocalTestAccountOutcomeUnknown", operation, account.Id, organization.Id, username); }
        return new("Auth.LocalTestAccountCreated", operation, account.Id, organization.Id, username);
    }
}
