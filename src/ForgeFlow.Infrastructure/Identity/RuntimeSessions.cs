using System.Data;
using System.Data.Common;
using ForgeFlow.Application.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ForgeFlow.Infrastructure.Identity;

// Contains a stamp in memory for protected tickets. Never use record-generated logging.
public sealed class SessionReference(long userId, Guid sessionId, string securityStamp, string? displayName = null)
{
    public long UserId { get; } = userId;
    public Guid SessionId { get; } = sessionId;
    public string SecurityStamp { get; } = securityStamp;
    public string? DisplayName { get; } = displayName;
    public override string ToString() => nameof(SessionReference);
}
public sealed record SessionLoginResult(string Code, SessionReference? Session = null, DateTimeOffset? Expires = null);
public interface ICurrentAuthenticatedSession
{
    ValueTask<SessionReference?> GetAsync(CancellationToken ct);
}

/// <summary>Only short-lived operation scopes; no DbContext retained by a Blazor circuit.</summary>
public sealed class RuntimeSessions(IServiceScopeFactory scopes, TimeProvider clock, ILogger<RuntimeSessions> logger, AuthDiagnosticsOptions? diagnostics = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static bool AvailableFailure(Exception e) => e is DbException or DbUpdateException or InvalidOperationException;
    private void Unavailable() => logger.LogError("AUTH-STORE-UNAVAILABLE"); // No exception/credentials/claims payload.
    private void Failure(string stage, Exception exception, params string[] secrets)
    {
        if (diagnostics?.Enabled == true)
            logger.LogError("AUTH-DIAGNOSTIC stage={Stage} details={Details}", stage, AuthFailureDiagnostics.Describe(exception, secrets));
    }
    private async Task<bool> Commit(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,CancellationToken ct, string[]? redactions = null)
    {
        try { await tx.CommitAsync(ct); return true; }
        catch (Exception exception) { Failure("transaction-commit", exception, redactions ?? []); Unavailable(); return false; } // Never replay uncertain commit.
    }
    private static Task<ApplicationAccount?> LockUser(AuthDbContext db, long id, CancellationToken ct) => db.Users
        .FromSqlInterpolated($"SELECT * FROM [auth].[User] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(ct);
    private static void Event(AuthDbContext db, string code, DateTime now, Guid correlation, long? subject = null, Guid? session = null, bool actor = false, byte outcome = 0)
        => db.SecurityEvents.Add(new SecurityEvent { EventCode = code, OccurredAtUtc = now, CorrelationId = correlation,
            ActorKind = actor ? (byte)1 : (byte)0, ActorUserId = actor ? subject : null, SubjectUserId = subject,
            SessionId = session, Outcome = outcome, DataJson = "{}" });
    private static async Task Revoke(AuthDbContext db, long userId, string reason, DateTime now, Guid correlation, CancellationToken ct, string? currentStamp = null)
    {
        foreach (var session in await db.Sessions.Where(s => s.UserId == userId && s.RevokedAtUtc == null && (reason != "StampChanged" || s.SecurityStampSnapshot != currentStamp)).ToListAsync(ct))
        {
            session.RevokedAtUtc = now; session.RevokeReason = reason;
            Event(db,"SessionRevoked",now,correlation,userId,session.Id);
        }
    }
    private static bool Locked(ApplicationAccount user, DateTime now) => user.LockoutEnabled && user.LockoutEnd > new DateTimeOffset(now);
    private async Task<bool> Eligible(AuthDbContext db, SessionReference reference, Guid correlation, CancellationToken ct)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty || string.IsNullOrEmpty(reference.SecurityStamp)) return false;
        var user = await LockUser(db, reference.UserId, ct); if (user is null) return false;
        var now = Now;
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == reference.SessionId && s.UserId == reference.UserId, ct);
        if (session is null) return false;
        var reason = !user.IsActive ? "AccountDisabled" : Locked(user,now) ? "AccountLocked" : user.SecurityStamp != session.SecurityStampSnapshot ? "StampChanged" : null;
        if (reason is not null) { await Revoke(db,user.Id,reason,now,correlation,ct,user.SecurityStamp); return false; }
        return session.RevokedAtUtc is null && session.IssuedAtUtc <= now && session.ExpiresAtUtc > now
            && reference.SecurityStamp == user.SecurityStamp && reference.SecurityStamp == session.SecurityStampSnapshot;
    }
    public async Task<SessionLoginResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var stage = "store-open";
        try
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            stage = "account-lookup";
            var correlation = Guid.NewGuid(); var now = Now;
            var validInput = username.Length is > 0 and <= 128 && password.Length is > 0 and <= 128
                && username.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-' or '@' or '+');
            var normalized = validInput ? manager.NormalizeName(username) : null;
            var user = normalized is null ? null : await db.Users.FromSqlInterpolated($"SELECT * FROM [auth].[User] WITH (UPDLOCK,HOLDLOCK) WHERE [NormalizedUserName]={normalized}").SingleOrDefaultAsync(ct);
            var allowed = user is not null && user.IsActive && !Locked(user,now);
            stage = "credential-validation";
            if (!allowed || !await manager.CheckPasswordAsync(user!,password))
            {
                logger.LogWarning("AUTH-LOGIN stage={Stage} outcome=rejected", stage);
                if (allowed && user!.LockoutEnabled)
                {
                    stage = "identity-lockout-update";
                    var update = await manager.AccessFailedAsync(user);
                    if (!update.Succeeded) { logger.LogWarning("AUTH-LOGIN stage=identity-lockout-update outcome=unavailable codes={Codes}", string.Join(",", update.Errors.Select(error => error.Code))); return new("Auth.ServiceUnavailable"); }
                    if (Locked(user,Now))
                    { await Revoke(db,user.Id,"AccountLocked",Now,correlation,ct); Event(db,"AccountLocked",Now,correlation,user.Id); }
                }
                else if (user is not null && (!user.IsActive || Locked(user,now)))
                    await Revoke(db,user.Id,user.IsActive ? "AccountLocked" : "AccountDisabled",now,correlation,ct);
                Event(db,"SignInFailed",now,correlation,user?.Id,outcome:1);
                await db.SaveChangesAsync(ct); if (!await Commit(tx,ct,[password, username])) return new("Auth.ServiceUnavailable"); return new("Auth.LoginRejected");
            }
            stage = "identity-reset-failures";
            var reset = await manager.ResetAccessFailedCountAsync(user!);
            if (!reset.Succeeded) { logger.LogWarning("AUTH-LOGIN stage=identity-reset-failures outcome=unavailable codes={Codes}", string.Join(",", reset.Errors.Select(error => error.Code))); return new("Auth.ServiceUnavailable"); }
            stage = "session-save";
            await Revoke(db,user!.Id,"StampChanged",Now,correlation,ct,user.SecurityStamp);
            // Same account lock as recovery: stamp is read only after taking it.
            var issued=Now;
            var session = new AccountSession { Id = Guid.NewGuid(), UserId = user!.Id, SecurityStampSnapshot = user.SecurityStamp!,
                IssuedAtUtc = issued, ExpiresAtUtc = issued.Add(Lifetime) };
            db.Sessions.Add(session); Event(db,"SessionIssued",session.IssuedAtUtc,correlation,user.Id,session.Id,true);
            await db.SaveChangesAsync(ct); stage = "session-commit"; if (!await Commit(tx,ct,[password, username])) return new("Auth.ServiceUnavailable");
            return new("Auth.SignedIn",new(user.Id,session.Id,user.SecurityStamp!,user.UserName),new DateTimeOffset(session.ExpiresAtUtc));
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure(stage, e, password, username);
            logger.LogWarning("AUTH-LOGIN stage={Stage} outcome=unavailable exceptionType={ExceptionType}", stage, e.GetType().Name);
            Unavailable(); return new("Auth.ServiceUnavailable");
        }
    }
    public async Task<string> ValidateAsync(SessionReference reference, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var valid = await Eligible(db,reference,Guid.NewGuid(),ct);
            await db.SaveChangesAsync(ct); if (!await Commit(tx,ct)) return "Auth.ServiceUnavailable";
            return valid ? "Auth.Valid" : "Auth.SessionInvalid";
        }
        catch (Exception e) when (AvailableFailure(e)) { Failure("validate-session", e, reference.SecurityStamp); Unavailable(); return "Auth.ServiceUnavailable"; }
    }
    public async Task<string> LogoutAsync(SessionReference reference, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var user = await LockUser(db,reference.UserId,ct);
            var session = await db.Sessions.SingleOrDefaultAsync(s=>s.Id==reference.SessionId && s.UserId==reference.UserId,ct);
            if (user is not null && session is not null && reference.SecurityStamp == session.SecurityStampSnapshot && session.RevokedAtUtc is null)
            {
                session.RevokedAtUtc = Now; session.RevokeReason = "Logout";
                Event(db,"SessionRevoked",Now,Guid.NewGuid(),user.Id,session.Id,true);
            }
            await db.SaveChangesAsync(ct); return await Commit(tx,ct) ? "Auth.SignedOut" : "Auth.LogoutRevocationIncomplete";
        }
        catch (Exception e) when (AvailableFailure(e)) { Unavailable(); return "Auth.LogoutRevocationIncomplete"; }
    }
    public async Task<bool> PermissionAsync(SessionReference reference, OrganizationScope scope, string permission, CancellationToken ct)
    {
        try
        {
            await using var operation = scopes.CreateAsyncScope(); var db = operation.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var correlation=Guid.NewGuid(); var eligible=await Eligible(db,reference,correlation,ct);
            var allowed = eligible && await ScopeAllowed(db,reference.UserId,scope,permission,ct);
            if (eligible && !allowed) Event(db,"AccessDenied",Now,correlation,reference.UserId,reference.SessionId,true,outcome:1);
            await db.SaveChangesAsync(ct); return await Commit(tx,ct) && allowed;
        }
        catch (Exception e) when (AvailableFailure(e)) { Unavailable(); return false; }
    }
    private static async Task<bool> ScopeAllowed(AuthDbContext db,long userId,OrganizationScope scope,string permission,CancellationToken ct)
    {
        if (scope is OrganizationScope.Platform)
            return permission==OrganizationPermissions.OrganizationsCreate && await db.ScopedRoleAssignments.AnyAsync(r=>r.UserId==userId && r.IsActive && r.RoleId==FixedAccessRoles.DeploymentProvisioner && r.ScopeKind==0 && r.OrganizationId==null && r.SiteId==null,ct);
        var orgId = scope is OrganizationScope.Organization org ? org.OrganizationId : ((OrganizationScope.Site)scope).OrganizationId;
        if (!await db.OrganizationMemberships.AnyAsync(m=>m.UserId==userId && m.OrganizationId==orgId && m.IsActive,ct)) return false;
        var company = await db.Set<ForgeFlow.Domain.Organizations.Organization>().AsNoTracking().SingleOrDefaultAsync(o=>o.Id==orgId,ct);
        if (company is null) return false;
        var read = permission is OrganizationPermissions.OrganizationsRead or OrganizationPermissions.SitesRead;
        if (!company.IsActive && !read && permission!=OrganizationPermissions.OrganizationsSetActive) return false;
        if (scope is OrganizationScope.Site site)
        {
            var target = await db.Set<ForgeFlow.Domain.Organizations.Site>().AsNoTracking().SingleOrDefaultAsync(s=>s.Id==site.SiteId && s.OrganizationId==orgId,ct);
            if (target is null || (!target.IsActive && !read && permission!=OrganizationPermissions.SitesSetActive)) return false;
        }
        var admin = FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator,scope,permission)
            && await db.ScopedRoleAssignments.AnyAsync(r=>r.UserId==userId && r.IsActive && r.RoleId==FixedAccessRoles.OrganizationAdministrator && r.ScopeKind==1 && r.OrganizationId==orgId,ct);
        if (admin) return true;
        if (scope is not OrganizationScope.Site requested || !FixedRolePolicy.Allows(FixedAccessRoles.SiteOperator,scope,permission)) return false;
        return await db.SiteMemberships.AnyAsync(m=>m.UserId==userId && m.OrganizationId==orgId && m.SiteId==requested.SiteId && m.IsActive,ct)
            && await db.ScopedRoleAssignments.AnyAsync(r=>r.UserId==userId && r.IsActive && r.RoleId==FixedAccessRoles.SiteOperator && r.ScopeKind==2 && r.OrganizationId==orgId && r.SiteId==requested.SiteId,ct);
    }
}

public static class FixedRolePolicy
{
    public static bool Allows(byte role, OrganizationScope scope, string permission) => role switch
    {
        FixedAccessRoles.DeploymentProvisioner => scope is OrganizationScope.Platform && permission==OrganizationPermissions.OrganizationsCreate,
        FixedAccessRoles.OrganizationAdministrator => scope switch {
            OrganizationScope.Organization => permission is OrganizationPermissions.OrganizationsRead or OrganizationPermissions.OrganizationsUpdate or OrganizationPermissions.OrganizationsSetActive or OrganizationPermissions.SitesRead or OrganizationPermissions.SitesCreate or OrganizationPermissions.SitesUpdate or OrganizationPermissions.SitesSetActive,
            OrganizationScope.Site => permission is OrganizationPermissions.SitesRead or OrganizationPermissions.SitesUpdate or OrganizationPermissions.SitesSetActive, _=>false },
        FixedAccessRoles.SiteOperator => scope is OrganizationScope.Site && permission is OrganizationPermissions.SitesRead or OrganizationPermissions.SitesUpdate or OrganizationPermissions.SitesSetActive,
        _=>false
    };
}

public sealed class SessionActorAccessor(ICurrentAuthenticatedSession source, RuntimeSessions sessions) : ITrustedActorAccessor
{
    public async ValueTask<TrustedActor?> ResolveAsync(CancellationToken cancellationToken)
    {
        var current=await source.GetAsync(cancellationToken);
        return current is not null && await sessions.ValidateAsync(current,cancellationToken)=="Auth.Valid"
            ? new(current.UserId,ActorProvenance.AuthenticatedSession,current.SessionId,Guid.NewGuid()) : null;
    }
}
public sealed class SessionPermissionEvaluator(ICurrentAuthenticatedSession source, RuntimeSessions sessions) : IOrganizationPermissionEvaluator
{
    public async ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor,OrganizationScope requestedScope,string permission,CancellationToken cancellationToken)
    {
        var current=await source.GetAsync(cancellationToken);
        if (current is null || actor.Provenance!=ActorProvenance.AuthenticatedSession || actor.ActorId!=current.UserId || actor.ProvenanceReference!=current.SessionId) return default;
        return new(await sessions.PermissionAsync(current,requestedScope,permission,cancellationToken));
    }
}
