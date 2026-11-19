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

public sealed partial class RuntimeSessions
{
    public async Task<string> ValidateAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty || string.IsNullOrEmpty(reference.SecurityStamp))
            return "Auth.SessionInvalid";

        var now = Now;
        if (_sessions.TryGetValue(reference.SessionId, out var cachedState))
        {
            if (cachedState.IsRevoked)
                return "Auth.SessionInvalid";
            if (cachedState.ExpiresAtUtc <= now)
            {
                cachedState.MarkRevoked("Expired");
                return "Auth.SessionInvalid";
            }
            if (cachedState.SecurityStamp != reference.SecurityStamp || cachedState.UserId != reference.UserId)
            {
                cachedState.MarkRevoked("StampChanged");
                return "Auth.SessionInvalid";
            }
            // Fast in-memory cache hit if validated within sliding window (F-02)
            if (cachedState.LastValidatedAtUtc != default && now - cachedState.LastValidatedAtUtc < ValidationCacheWindow)
            {
                cachedState.LastActivityAtUtc = now;
                return "Auth.Valid";
            }
        }

        var sessionLock = _sessionLocks.GetOrAdd(reference.SessionId, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(ct);
        try
        {
            now = Now;
            if (_sessions.TryGetValue(reference.SessionId, out var stateAfterLock))
            {
                if (stateAfterLock.IsRevoked)
                    return "Auth.SessionInvalid";
                if (stateAfterLock.LastValidatedAtUtc != default && now - stateAfterLock.LastValidatedAtUtc < ValidationCacheWindow)
                {
                    stateAfterLock.LastActivityAtUtc = now;
                    return "Auth.Valid";
                }
            }

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            // Non-blocking read committed user lookup without UPDLOCK/HOLDLOCK (F-02)
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == reference.UserId, ct);
            if (user is null)
            {
                RevokeSession(reference.SessionId, "ValidationFailed");
                return "Auth.SessionInvalid";
            }

            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == reference.SessionId && s.UserId == reference.UserId, ct);
            if (session is null)
            {
                RevokeSession(reference.SessionId, "ValidationFailed");
                return "Auth.SessionInvalid";
            }

            var correlation = Guid.NewGuid();
            var reason = !user.IsActive ? "AccountDisabled" : Locked(user, now) ? "AccountLocked" : user.SecurityStamp != session.SecurityStampSnapshot ? "StampChanged" : null;
            if (reason is not null)
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await Revoke(db, user.Id, reason, now, correlation, ct, user.SecurityStamp);
                await db.SaveChangesAsync(ct);
                await Commit(tx, ct);
                return "Auth.SessionInvalid";
            }

            if (session.RevokedAtUtc is null)
            {
                if (!_userIdleTimeoutCache.TryGetValue(user.Id, out var idleTimeoutMinutes))
                {
                    idleTimeoutMinutes = await db.UserPreferences.AsNoTracking()
                        .Where(p => p.UserId == user.Id)
                        .Select(p => p.IdleTimeoutMinutes)
                        .SingleOrDefaultAsync(ct);
                    _userIdleTimeoutCache[user.Id] = idleTimeoutMinutes;
                }

                if (idleTimeoutMinutes is 15 or 30 or 60 && now - session.LastActivityAtUtc > TimeSpan.FromMinutes(idleTimeoutMinutes.Value))
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    session.RevokedAtUtc = now < session.IssuedAtUtc ? session.IssuedAtUtc : now;
                    session.RevokeReason = "IdleTimeout";
                    Event(db, "SessionRevoked", now, correlation, user.Id, session.Id);
                    await db.SaveChangesAsync(ct);
                    await Commit(tx, ct);
                    RevokeSession(session.Id, "IdleTimeout");
                    return "Auth.SessionInvalid";
                }
            }

            var isValid = session.RevokedAtUtc is null && session.IssuedAtUtc <= now && session.ExpiresAtUtc > now
                && reference.SecurityStamp == user.SecurityStamp && reference.SecurityStamp == session.SecurityStampSnapshot;

            if (!isValid)
            {
                RevokeSession(reference.SessionId, "ValidationFailed");
                return "Auth.SessionInvalid";
            }

            // Persistence of throttled LastActivityAtUtc (>= 2 minutes elapsed)
            if (now - session.LastActivityAtUtc >= TimeSpan.FromMinutes(2))
            {
                session.LastActivityAtUtc = now < session.IssuedAtUtc ? session.IssuedAtUtc : now;
                await db.SaveChangesAsync(ct);
            }

            if (_sessions.TryGetValue(reference.SessionId, out var existingState))
            {
                existingState.LastActivityAtUtc = now;
                existingState.LastValidatedAtUtc = now;
                if (session.LastActivityAtUtc == now)
                {
                    existingState.LastPersistedActivityAtUtc = session.LastActivityAtUtc;
                }
            }
            else
            {
                RegisterSessionInMemory(reference.SessionId, reference.UserId, reference.SecurityStamp, session.IssuedAtUtc, session.ExpiresAtUtc, session.LastActivityAtUtc);
                if (_sessions.TryGetValue(reference.SessionId, out var s))
                    s.LastValidatedAtUtc = now;
            }

            return "Auth.Valid";
        }
        catch (Exception e) when (AvailableFailure(e)) { Failure("validate-session", e, reference.SecurityStamp); Unavailable(); return "Auth.ServiceUnavailable"; }
        finally
        {
            sessionLock.Release();
        }
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
                RevokeSession(session.Id, "Logout");
            }
            await db.SaveChangesAsync(ct);
            var committed = await Commit(tx,ct);
            if (committed)
            {
                RevokeSession(reference.SessionId, "Logout");
                return "Auth.SignedOut";
            }
            return "Auth.LogoutRevocationIncomplete";
        }
        catch (Exception e) when (AvailableFailure(e)) { Unavailable(); return "Auth.LogoutRevocationIncomplete"; }
    }

    public async Task<SessionLoginResult> ChangeOwnPasswordAsync(SessionReference reference, string currentPassword, string newPassword, CancellationToken ct = default, string? clientIp = null, string? userAgent = null)
    {
        if (string.IsNullOrEmpty(currentPassword) || currentPassword.Length > 128)
            return new("Auth.CurrentPasswordInvalid");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length is < 12 or > 128 || newPassword.Any(char.IsControl))
            return new("Auth.PasswordInvalid");

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var correlation = Guid.NewGuid();
            if (!await Eligible(db, reference, correlation, ct))
            {
                await db.SaveChangesAsync(ct);
                await Commit(tx, ct, [currentPassword, newPassword]);
                return new("Auth.SessionInvalid");
            }

            var user = await LockUser(db, reference.UserId, ct);
            var currentSession = await db.Sessions.SingleOrDefaultAsync(s => s.Id == reference.SessionId && s.UserId == reference.UserId, ct);
            if (user is null || currentSession is null)
                return new("Auth.SessionInvalid");

            if (!await manager.CheckPasswordAsync(user, currentPassword))
                return new("Auth.CurrentPasswordInvalid");

            foreach (var validator in manager.PasswordValidators)
            {
                var check = await validator.ValidateAsync(manager, user, newPassword);
                if (!check.Succeeded)
                    return new("Auth.PasswordInvalid");
            }

            var now = Now;
            user.PasswordHash = manager.PasswordHasher.HashPassword(user, newPassword);
            user.PasswordChangedAtUtc = now >= user.CreatedAtUtc ? now : user.CreatedAtUtc;
            user.RequirePasswordChange = false;
            user.UpdatedAtUtc = now >= user.CreatedAtUtc ? now : user.CreatedAtUtc;
            var stampResult = await manager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
                return new("Auth.ServiceUnavailable");

            await Revoke(db, user.Id, "PasswordReset", now, correlation, ct);

            var expires = currentSession.ExpiresAtUtc > now ? currentSession.ExpiresAtUtc : now.Add(Lifetime);
            var normalizedIp = NormalizeIpString(clientIp);
            var (deviceSummary, isMobile) = UserAgentDeviceParser.Parse(userAgent);
            var freshSession = new AccountSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                SecurityStampSnapshot = user.SecurityStamp!,
                IpAddress = normalizedIp,
                DeviceSummary = userAgent is null ? null : deviceSummary,
                IsMobileDevice = isMobile,
                IssuedAtUtc = now,
                LastActivityAtUtc = now,
                ExpiresAtUtc = expires
            };
            db.Sessions.Add(freshSession);
            db.SecurityEvents.Add(new SecurityEvent
            {
                EventCode = "PasswordResetCompleted",
                OccurredAtUtc = now,
                CorrelationId = correlation,
                ActorKind = 1,
                ActorUserId = user.Id,
                SubjectUserId = user.Id,
                SessionId = freshSession.Id,
                Outcome = 0,
                DataJson = "{\"action\":\"SelfPasswordChanged\"}"
            });
            Event(db, "SessionIssued", now, correlation, user.Id, freshSession.Id, true, dataJson: BuildSessionEventJson(normalizedIp, userAgent is null ? null : deviceSummary));
            var notifiedPassword = await TryQueueNotificationAsync(
                db,
                user.Id,
                NotificationEventCatalog.NewDeviceSignIn,
                NotificationCategory.Security,
                NotificationSeverity.Info,
                NotificationTemplateCatalog.PasswordChangedAlert,
                new { device = userAgent is null ? "Browser" : deviceSummary, ip = normalizedIp ?? "—" },
                "/account/sessions",
                now,
                ct);

            await db.SaveChangesAsync(ct);
            if (!await Commit(tx, ct, [currentPassword, newPassword]))
                return new("Auth.ServiceUnavailable");

            RegisterSessionInMemory(freshSession.Id, user.Id, user.SecurityStamp!, freshSession.IssuedAtUtc, freshSession.ExpiresAtUtc, freshSession.LastActivityAtUtc);
            RevokeUserSessions(reference.UserId, "PasswordReset", exceptSessionId: freshSession.Id);

            if (notifiedPassword) liveChannel?.NotifyUser(user.Id);
            return new("Auth.PasswordChanged", new(user.Id, freshSession.Id, user.SecurityStamp!, user.UserName), new DateTimeOffset(freshSession.ExpiresAtUtc));
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("change-own-password", e, currentPassword, newPassword, reference.SecurityStamp);
            Unavailable();
            return new("Auth.ServiceUnavailable");
        }
    }
}
