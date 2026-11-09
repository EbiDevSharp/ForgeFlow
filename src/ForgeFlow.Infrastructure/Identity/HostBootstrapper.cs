using System.Data;
using System.Security.Principal;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Identity;

/// <summary>Explicit host attestation, not an application permission or a substitute for provisioning SQL rights.</summary>
public sealed class HostOperatorConsent
{
    private HostOperatorConsent(string reference) => Reference = reference;
    public string Reference { get; }
    public static HostOperatorConsent? Confirm(string? confirmation)
    {
        if (!string.Equals(confirmation, "INITIALIZE", StringComparison.Ordinal)) return null;
        string reference;
        if (OperatingSystem.IsWindows()) { using var identity = WindowsIdentity.GetCurrent(); reference = identity.Name; }
        else reference = Environment.MachineName + "/" + Environment.UserName;
        return string.IsNullOrWhiteSpace(reference) || reference.Length > 256 ? null : new(reference);
    }
}

public sealed record BootstrapRequest(Guid OperationId, string UserName, string? OrganizationCode,
    string? OrganizationName, string? SiteCode, string? SiteName);
public sealed record BootstrapResult(string Code, Guid OperationId, long? UserId = null, long? OrganizationId = null,
    long? SiteId = null, IReadOnlyList<string>? IdentityErrorCodes = null);

/// <summary>
/// Host-only, not registered in Web. Password is a separate transient parameter, never request/log/receipt data.
/// SQL capability plus explicit deployment-owner consent is required; no runtime permission bypass is installed.
/// </summary>
public sealed class HostBootstrapper(string provisioningConnection,
    Action<DbContextOptionsBuilder>? configureAuth = null,
    Action<DbContextOptionsBuilder<OrganizationDbContext>>? configureOrganizations = null)
{
    public async Task<BootstrapResult> InitializeAsync(BootstrapRequest request, string password, HostOperatorConsent? consent, CancellationToken ct = default)
    {
        if (consent is null) return new("Auth.OperatorRequired", request.OperationId);
        if (request.OperationId == Guid.Empty || string.IsNullOrEmpty(request.UserName) || request.UserName.Length > 128
            || request.UserName.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-' or '@' or '+'))
            || password.Length is < 12 or > 128)
            return new("Auth.BootstrapInputInvalid", request.OperationId);
        Organization organization;
        try { organization = Organization.Create(request.OrganizationCode, request.OrganizationName); organization.CreateSite(request.SiteCode, request.SiteName); }
        catch (OrganizationSiteRuleException ex) { return new(ex.Code, request.OperationId); }

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
        var services = new ServiceCollection().AddBootstrapIdentity(options => {
            options.UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth")); configureAuth?.Invoke(options);
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(connection);
        configureOrganizations?.Invoke(orgOptions);
        await using var business = new OrganizationDbContext(orgOptions.Options);
        if (!ReferenceEquals(auth.Database.GetDbConnection(), connection) || !ReferenceEquals(business.Database.GetDbConnection(), connection))
            throw new InvalidOperationException("Auth.SharedConnectionRequired");
        if ((await auth.Database.GetPendingMigrationsAsync(ct)).Any() || (await business.Database.GetPendingMigrationsAsync(ct)).Any())
            return new("Auth.SchemaNotReady", request.OperationId);

        await using var transaction = await auth.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await business.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        var state = await auth.BootstrapStates.FromSqlRaw("SELECT * FROM [auth].[BootstrapState] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=1").SingleOrDefaultAsync(ct);
        if (state is null) return new("Auth.BootstrapPartial", request.OperationId);
        if (state.State == 1)
        {
            var consistent = await auth.Users.AnyAsync(u => u.Id == state.FirstUserId, ct)
                && await business.Sites.AnyAsync(s => s.Id == state.SiteId && s.OrganizationId == state.OrganizationId, ct)
                && await auth.OrganizationMemberships.AnyAsync(m => m.UserId == state.FirstUserId && m.OrganizationId == state.OrganizationId, ct)
                && await auth.SiteMemberships.AnyAsync(m => m.UserId == state.FirstUserId && m.OrganizationId == state.OrganizationId && m.SiteId == state.SiteId, ct)
                && await auth.ScopedRoleAssignments.AnyAsync(r => r.UserId == state.FirstUserId && r.OrganizationId == state.OrganizationId && r.RoleId == FixedAccessRoles.OrganizationAdministrator, ct);
            if (!consistent) return new("Auth.BootstrapPartial", request.OperationId);
            if (state.OperationId == request.OperationId &&
                (!await auth.Users.AnyAsync(u => u.Id == state.FirstUserId && u.NormalizedUserName == request.UserName.ToUpperInvariant(), ct)
                || !await business.Organizations.AnyAsync(o => o.Id == state.OrganizationId && o.Code == organization.Code, ct)
                || !await business.Sites.AnyAsync(s => s.Id == state.SiteId && s.Code == organization.Sites.Single().Code, ct)))
                return new("Auth.BootstrapClosed", request.OperationId);
            return state.OperationId == request.OperationId
                ? new("Auth.AlreadyInitialized", request.OperationId, state.FirstUserId, state.OrganizationId, state.SiteId)
                : new("Auth.BootstrapClosed", request.OperationId);
        }
        // Do not adopt any pre-existing data, including orphan security/support records.
        if (state.State != 0 || await auth.Users.AnyAsync(ct) || await business.Organizations.AnyAsync(ct)
            || await business.Sites.AnyAsync(ct) || await business.OrganizationChanges.AnyAsync(ct)
            || await auth.OrganizationMemberships.AnyAsync(ct) || await auth.SiteMemberships.AnyAsync(ct)
            || await auth.ScopedRoleAssignments.AnyAsync(ct) || await auth.Sessions.AnyAsync(ct) || await auth.SecurityEvents.AnyAsync(ct)
            || await auth.Set<IdentityUserClaim<long>>().AnyAsync(ct) || await auth.Set<IdentityUserLogin<long>>().AnyAsync(ct) || await auth.Set<IdentityUserToken<long>>().AnyAsync(ct))
            return new("Auth.BootstrapPartial", request.OperationId);

        var now = DateTime.UtcNow;
        var account = new ApplicationAccount { UserName = request.UserName, IsActive = true, CreatedAtUtc = now, LockoutEnabled = true };
        var identity = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>().CreateAsync(account, password);
        if (!identity.Succeeded) return new("Auth.IdentityRejected", request.OperationId, IdentityErrorCodes: identity.Errors.Select(e => e.Code).ToArray());
        var actor = new TrustedActor(account.Id, ActorProvenance.ControlledBootstrap, request.OperationId, Guid.NewGuid());
        business.Organizations.Add(organization);
        await business.SaveInOperation(actor, ct);
        var site = organization.Sites.Single();
        var membership = new OrganizationMembership { UserId = account.Id, OrganizationId = organization.Id, IsActive = true };
        var siteMembership = new SiteMembership { UserId = account.Id, OrganizationId = organization.Id, SiteId = site.Id, IsActive = true };
        // Company-only by construction. No platform-grant flag or assignment is accepted.
        var role = new ScopedRoleAssignment { UserId = account.Id, OrganizationId = organization.Id,
            RoleId = FixedAccessRoles.OrganizationAdministrator, ScopeKind = 1, IsActive = true };
        auth.AddRange(membership, siteMembership, role);
        foreach (var row in new object[] { membership, siteMembership, role })
        {
            auth.Entry(row).Property("CreatedAtUtc").CurrentValue = now;
            auth.Entry(row).Property("CreatedByActorId").CurrentValue = account.Id;
        }
        foreach (var (code, data, eventSite) in new[] {
            ("AccountCreated", "{}", (long?)null),
            ("MembershipChanged", "{\"scope\":\"organization\"}", (long?)null),
            ("MembershipChanged", "{\"scope\":\"site\"}", (long?)site.Id),
            ("ScopedRoleChanged", "{\"role\":\"OrganizationAdministrator\"}", (long?)null),
            ("BootstrapCompleted", "{}", (long?)site.Id) })
            auth.SecurityEvents.Add(new SecurityEvent { EventCode = code, DataJson = data, ActorKind = 2,
                HostOperatorReference = consent.Reference, SubjectUserId = account.Id, OrganizationId = organization.Id,
                SiteId = eventSite, OccurredAtUtc = now, CorrelationId = actor.CorrelationId, Outcome = 0 });
        state.State = 1; state.OperationId = request.OperationId; state.FirstUserId = account.Id;
        state.OrganizationId = organization.Id; state.SiteId = site.Id; state.CompletedAtUtc = now; state.HostOperatorReference = consent.Reference;
        await auth.SaveChangesAsync(ct);
        try { await transaction.CommitAsync(ct); }
        catch (Exception) { return new("Auth.BootstrapOutcomeUnknown", request.OperationId); }
        return new("Auth.Initialized", request.OperationId, account.Id, organization.Id, site.Id);
    }
}
