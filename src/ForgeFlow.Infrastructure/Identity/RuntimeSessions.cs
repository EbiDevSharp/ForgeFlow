using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Security;
using ForgeFlow.Application.Units;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Roles;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
public sealed record SessionLoginResult(
    string Code,
    SessionReference? Session = null,
    DateTimeOffset? Expires = null,
    long? PendingUserId = null,
    string? PendingSecurityStamp = null,
    bool RequireTwoFactorSetup = false,
    bool RequirePasswordChange = false);
public sealed record PreviousSignInInfo(DateTime OccurredAtUtc, string? IpAddress);
public interface ICurrentAuthenticatedSession
{
    ValueTask<SessionReference?> GetAsync(CancellationToken ct);
}

/// <summary>Only short-lived operation scopes; no DbContext retained by a Blazor circuit.</summary>
public sealed partial class RuntimeSessions(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<RuntimeSessions> logger,
    AuthDiagnosticsOptions? diagnostics = null,
    IUserNotificationLiveChannel? liveChannel = null,
    ForgeFlow.Application.Security.IOrganizationPermissionCache? permCache = null,
    IOptions<RuntimeSessionOptions>? sessionOptions = null) : IUserNotificationPublisher, ISessionInvalidator
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private readonly ConcurrentDictionary<Guid, RuntimeSessionState> _sessions = new();
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, byte>> _userSessions = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sessionLocks = new();
    private readonly ConcurrentDictionary<long, object> _userSyncRoots = new();
    private readonly ConcurrentDictionary<long, UserPreferencesSnapshot> _userPreferencesCache = new();
    private readonly ConcurrentDictionary<long, int?> _userIdleTimeoutCache = new();
    private static readonly TimeSpan ValidationCacheWindow = TimeSpan.FromSeconds(20);

    public event Action<Guid, string>? SessionRevoked;
    public event Action<long, string>? UserSessionsRevoked;

    public bool TryEnforceLimitInMemory(long userId, out Guid? evictedSessionId)
    {
        evictedSessionId = null;
        var max = sessionOptions?.Value.MaxConcurrentSessionsPerUser ?? 0;
        if (max <= 0) return true;

        var sync = _userSyncRoots.GetOrAdd(userId, _ => new object());
        lock (sync)
        {
            if (!_userSessions.TryGetValue(userId, out var userSet))
                return true;

            var now = Now;
            var activeList = userSet.Keys
                .Select(id => _sessions.TryGetValue(id, out var s) ? s : null)
                .Where(s => s is not null && !s.IsRevoked && s.ExpiresAtUtc > now)
                .OrderBy(s => s!.IssuedAtUtc)
                .ToList();

            if (activeList.Count >= max)
            {
                if (sessionOptions?.Value.EvictOldestOnLimitReached != false)
                {
                    var evictCount = activeList.Count - max + 1;
                    for (var i = 0; i < evictCount && i < activeList.Count; i++)
                    {
                        var oldest = activeList[i]!;
                        oldest.MarkRevoked("ConcurrentLimitExceeded");
                        SessionRevoked?.Invoke(oldest.SessionId, "ConcurrentLimitExceeded");
                        evictedSessionId = oldest.SessionId;
                    }
                    return true;
                }
                return false;
            }
            return true;
        }
    }

    public void RegisterSessionInMemory(Guid sessionId, long userId, string securityStamp, DateTime issuedAtUtc, DateTime expiresAtUtc, DateTime lastActivityAtUtc)
    {
        var sync = _userSyncRoots.GetOrAdd(userId, _ => new object());
        lock (sync)
        {
            var max = sessionOptions?.Value.MaxConcurrentSessionsPerUser ?? 0;
            if (max > 0 && sessionOptions?.Value.EvictOldestOnLimitReached != false)
            {
                if (_userSessions.TryGetValue(userId, out var existingSet))
                {
                    var now = Now;
                    var activeList = existingSet.Keys
                        .Select(id => _sessions.TryGetValue(id, out var s) ? s : null)
                        .Where(s => s is not null && !s.IsRevoked && s.ExpiresAtUtc > now)
                        .OrderBy(s => s!.IssuedAtUtc)
                        .ToList();

                    if (activeList.Count >= max)
                    {
                        var evictCount = activeList.Count - max + 1;
                        for (var i = 0; i < evictCount && i < activeList.Count; i++)
                        {
                            var oldest = activeList[i]!;
                            oldest.MarkRevoked("ConcurrentLimitExceeded");
                            SessionRevoked?.Invoke(oldest.SessionId, "ConcurrentLimitExceeded");
                        }
                    }
                }
            }

            var state = new RuntimeSessionState(sessionId, userId, securityStamp, issuedAtUtc, expiresAtUtc, lastActivityAtUtc);
            _sessions[sessionId] = state;
            var userSet = _userSessions.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, byte>());
            userSet[sessionId] = 0;
        }
    }

    public void RevokeSession(Guid sessionId, string reason)
    {
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            state.MarkRevoked(reason);
        }
        SessionRevoked?.Invoke(sessionId, reason);
    }

    public void RevokeUserSessions(long userId, string reason, string? exceptSecurityStamp = null, Guid? exceptSessionId = null)
    {
        permCache?.InvalidateUser(userId);
        _userPreferencesCache.TryRemove(userId, out _);
        _userIdleTimeoutCache.TryRemove(userId, out _);
        if (_userSessions.TryGetValue(userId, out var userSet))
        {
            foreach (var sId in userSet.Keys)
            {
                if (exceptSessionId.HasValue && sId == exceptSessionId.Value)
                    continue;

                if (_sessions.TryGetValue(sId, out var state))
                {
                    if (exceptSecurityStamp is not null && state.SecurityStamp == exceptSecurityStamp)
                        continue;

                    state.MarkRevoked(reason);
                    SessionRevoked?.Invoke(sId, reason);
                }
            }
        }
        if (!exceptSessionId.HasValue && exceptSecurityStamp is null)
        {
            UserSessionsRevoked?.Invoke(userId, reason);
        }
    }

    public void RegisterCircuit(Guid sessionId, Guid circuitId)
    {
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            state.IncrementCircuits();
        }
    }

    public void UnregisterCircuit(Guid sessionId, Guid circuitId)
    {
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            state.DecrementCircuits();
        }
    }

    public bool TryGetSessionState(Guid sessionId, out RuntimeSessionState? state)
        => _sessions.TryGetValue(sessionId, out state);

    public int GetActiveSessionCount(long userId)
    {
        if (!_userSessions.TryGetValue(userId, out var userSet))
            return 0;

        var now = Now;
        var count = 0;
        foreach (var sId in userSet.Keys)
        {
            if (_sessions.TryGetValue(sId, out var state) && !state.IsRevoked && state.ExpiresAtUtc > now)
                count++;
        }
        return count;
    }
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
    private static void Event(AuthDbContext db, string code, DateTime now, Guid correlation, long? subject = null, Guid? session = null, bool actor = false, byte outcome = 0, string dataJson = "{}")
        => db.SecurityEvents.Add(new SecurityEvent { EventCode = code, OccurredAtUtc = now, CorrelationId = correlation,
            ActorKind = actor ? (byte)1 : (byte)0, ActorUserId = actor ? subject : null, SubjectUserId = subject,
            SessionId = session, Outcome = outcome, DataJson = dataJson });
    private static string? NormalizeIpString(string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
            return null;
        if (!IPAddress.TryParse(clientIp.Trim(), out var parsed))
            return null;
        return parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4().ToString() : parsed.ToString();
    }

    private static string? ComputeDeviceTokenHash(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;
        var trimmed = rawToken.Trim();
        if (trimmed.Length is < 16 or > 128)
            return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)));
    }

    private static string BuildSessionEventJson(string? clientIp, string? deviceSummary = null)
    {
        var normalized = NormalizeIpString(clientIp);
        var device = string.IsNullOrWhiteSpace(deviceSummary) ? null : deviceSummary.Trim();
        if (normalized is null && device is null)
            return "{}";
        if (normalized is not null && device is not null)
            return JsonSerializer.Serialize(new { ip = normalized, device });
        return normalized is not null
            ? JsonSerializer.Serialize(new { ip = normalized })
            : JsonSerializer.Serialize(new { device });
    }
    private async Task Revoke(AuthDbContext db, long userId, string reason, DateTime now, Guid correlation, CancellationToken ct, string? currentStamp = null)
    {
        permCache?.InvalidateUser(userId);
        foreach (var session in await db.Sessions.Where(s => s.UserId == userId && s.RevokedAtUtc == null && (reason != "StampChanged" || s.SecurityStampSnapshot != currentStamp)).ToListAsync(ct))
        {
            session.RevokedAtUtc = now; session.RevokeReason = reason;
            Event(db,"SessionRevoked",now,correlation,userId,session.Id);
            RevokeSession(session.Id, reason);
        }
        RevokeUserSessions(userId, reason, currentStamp);
    }

    private async Task<string?> EnforceConcurrentSessionLimitAsync(AuthDbContext db, ApplicationAccount user, DateTime now, Guid correlation, CancellationToken ct)
    {
        var maxSessions = sessionOptions?.Value.MaxConcurrentSessionsPerUser ?? 0;
        if (maxSessions <= 0) return null;

        var activeSessions = await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .OrderBy(s => s.IssuedAtUtc)
            .ToListAsync(ct);

        if (activeSessions.Count >= maxSessions)
        {
            if (sessionOptions?.Value.EvictOldestOnLimitReached != false)
            {
                var evictCount = activeSessions.Count - maxSessions + 1;
                for (var i = 0; i < evictCount && i < activeSessions.Count; i++)
                {
                    var sToEvict = activeSessions[i];
                    sToEvict.RevokedAtUtc = now < sToEvict.IssuedAtUtc ? sToEvict.IssuedAtUtc : now;
                    sToEvict.RevokeReason = "ConcurrentLimitExceeded";
                    Event(db, "SessionRevoked", now, correlation, user.Id, sToEvict.Id, true,
                        dataJson: JsonSerializer.Serialize(new { reason = "ConcurrentLimitExceeded" }));
                    RevokeSession(sToEvict.Id, "ConcurrentLimitExceeded");
                }
            }
            else
            {
                Event(db, "SignInFailed", now, correlation, user.Id, outcome: 1,
                    dataJson: JsonSerializer.Serialize(new { reason = "ConcurrentLimitReached" }));
                return "Auth.ConcurrentSessionLimitReached";
            }
        }
        return null;
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
        if (session.RevokedAtUtc is null)
        {
            var idleTimeoutMinutes = await db.UserPreferences.AsNoTracking()
                .Where(p => p.UserId == user.Id)
                .Select(p => p.IdleTimeoutMinutes)
                .SingleOrDefaultAsync(ct);
            if (idleTimeoutMinutes is 15 or 30 or 60 && now - session.LastActivityAtUtc > TimeSpan.FromMinutes(idleTimeoutMinutes.Value))
            {
                session.RevokedAtUtc = now < session.IssuedAtUtc ? session.IssuedAtUtc : now;
                session.RevokeReason = "IdleTimeout";
                Event(db, "SessionRevoked", now, correlation, user.Id, session.Id);
                return false;
            }
        }
        var isValid = session.RevokedAtUtc is null && session.IssuedAtUtc <= now && session.ExpiresAtUtc > now
            && reference.SecurityStamp == user.SecurityStamp && reference.SecurityStamp == session.SecurityStampSnapshot;

        if (isValid)
        {
            if (_sessions.TryGetValue(session.Id, out var state))
            {
                state.LastActivityAtUtc = now;
            }

            if (now - session.LastActivityAtUtc >= TimeSpan.FromMinutes(2))
            {
                session.LastActivityAtUtc = now < session.IssuedAtUtc ? session.IssuedAtUtc : now;
                if (_sessions.TryGetValue(session.Id, out var s))
                {
                    s.LastPersistedActivityAtUtc = session.LastActivityAtUtc;
                }
            }
        }
        return isValid;
    }

    private static readonly ApplicationAccount s_dummyAccount = new();
    private static readonly string s_dummyHash = new PasswordHasher<ApplicationAccount>().HashPassword(s_dummyAccount, "ForgeFlow#DummyPass123!");

    private sealed class LoginThrottleState
    {
        public int Failures;
        public DateTime LastFailureUtc;
    }

    private static readonly MemoryCache s_loginThrottles = new(new MemoryCacheOptions
    {
        SizeLimit = 10_000,
        ExpirationScanFrequency = TimeSpan.FromMinutes(5)
    });

    private static string BuildThrottleKey(string normalizedUser, string? clientIp)
        => $"{normalizedUser}:{clientIp ?? "unknown"}";

    private static async Task ApplyLoginThrottlingAsync(string normalizedUser, string? clientIp, CancellationToken ct)
    {
        var key = BuildThrottleKey(normalizedUser, clientIp);
        if (s_loginThrottles.TryGetValue<LoginThrottleState>(key, out var state) && state is not null)
        {
            var elapsed = DateTime.UtcNow - state.LastFailureUtc;
            if (elapsed < TimeSpan.FromMinutes(5) && state.Failures >= 3)
            {
                var delayMs = Math.Min(2000, 250 * (1 << Math.Min(state.Failures - 3, 3)));
                await Task.Delay(delayMs, ct);
            }
        }
    }

    private static void RecordLoginFailure(string normalizedUser, string? clientIp)
    {
        var key = BuildThrottleKey(normalizedUser, clientIp);
        var now = DateTime.UtcNow;
        var nextState = s_loginThrottles.TryGetValue<LoginThrottleState>(key, out var existing) && existing is not null
            && (now - existing.LastFailureUtc) <= TimeSpan.FromMinutes(15)
            ? new LoginThrottleState { Failures = existing.Failures + 1, LastFailureUtc = now }
            : new LoginThrottleState { Failures = 1, LastFailureUtc = now };

        s_loginThrottles.Set(key, nextState, new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = TimeSpan.FromMinutes(15)
        });
    }

    private static bool IsIpThrottledForAccount(string normalizedUser, string? clientIp)
    {
        var key = BuildThrottleKey(normalizedUser, clientIp);
        return s_loginThrottles.TryGetValue<LoginThrottleState>(key, out var state)
            && state is not null
            && state.Failures >= 5
            && (DateTime.UtcNow - state.LastFailureUtc) < TimeSpan.FromMinutes(5);
    }

    private static void ClearLoginFailure(string normalizedUser, string? clientIp)
    {
        s_loginThrottles.Remove(BuildThrottleKey(normalizedUser, clientIp));
    }

    public const string TrustedDeviceLoginProvider = "ForgeFlow.2FA.TrustedDevice";

    private static async Task<bool> IsDeviceTrustedAsync(AuthDbContext db, long userId, string? deviceToken, string? securityStamp, CancellationToken ct)
    {
        var tokenHash = ComputeDeviceTokenHash(deviceToken);
        if (tokenHash is null || string.IsNullOrEmpty(securityStamp))
            return false;

        var record = await db.UserTokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.UserId == userId && t.LoginProvider == TrustedDeviceLoginProvider && t.Name == tokenHash, ct);
        if (record?.Value is null)
            return false;

        var parts = record.Value.Split('|');
        if (parts.Length != 2)
            return false;

        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expTicks))
            return false;

        if (DateTimeOffset.UtcNow.UtcTicks > expTicks)
            return false;

        return string.Equals(parts[1], securityStamp, StringComparison.Ordinal);
    }

    private static async Task SaveTrustedDeviceAsync(AuthDbContext db, long userId, string deviceToken, string securityStamp, CancellationToken ct)
    {
        var tokenHash = ComputeDeviceTokenHash(deviceToken);
        if (tokenHash is null || string.IsNullOrEmpty(securityStamp))
            return;

        var expTicks = DateTimeOffset.UtcNow.AddDays(30).UtcTicks;
        var value = $"{expTicks}|{securityStamp}";

        var existing = await db.UserTokens
            .SingleOrDefaultAsync(t => t.UserId == userId && t.LoginProvider == TrustedDeviceLoginProvider && t.Name == tokenHash, ct);

        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            db.UserTokens.Add(new IdentityUserToken<long>
            {
                UserId = userId,
                LoginProvider = TrustedDeviceLoginProvider,
                Name = tokenHash,
                Value = value
            });
        }
    }

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
public sealed class SessionPermissionEvaluator(
    ICurrentAuthenticatedSession source,
    RuntimeSessions sessions,
    ForgeFlow.Application.Security.IOrganizationPermissionCache? cache = null) : IOrganizationPermissionEvaluator
{
    public async ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope requestedScope, string permission, CancellationToken cancellationToken)
    {
        var current = await source.GetAsync(cancellationToken);
        if (current is null || actor.Provenance != ActorProvenance.AuthenticatedSession || actor.ActorId != current.UserId || actor.ProvenanceReference != current.SessionId) return default;
        if (cache is not null && cache.TryGetGrantedPermissions(current.UserId, requestedScope, out var granted) && granted is not null)
        {
            return new(granted.Contains(permission));
        }
        return new(await sessions.PermissionAsync(current, requestedScope, permission, cancellationToken));
    }
}
