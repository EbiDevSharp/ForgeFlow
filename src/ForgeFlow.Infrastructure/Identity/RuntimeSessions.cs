using System.Data;
using System.Data.Common;
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
using ForgeFlow.Application.Units;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Roles;
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
public sealed record PreviousSignInInfo(DateTime OccurredAtUtc, string? IpAddress);
public interface ICurrentAuthenticatedSession
{
    ValueTask<SessionReference?> GetAsync(CancellationToken ct);
}

/// <summary>Only short-lived operation scopes; no DbContext retained by a Blazor circuit.</summary>
public sealed class RuntimeSessions(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<RuntimeSessions> logger,
    AuthDiagnosticsOptions? diagnostics = null,
    IUserNotificationLiveChannel? liveChannel = null) : IUserNotificationPublisher
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
    public async Task<SessionLoginResult> LoginAsync(string username, string password, CancellationToken ct = default, string? clientIp = null, string? initialCulture = null, string? userAgent = null, string? deviceToken = null)
    {
        var stage = "store-open";
        var normalizedIp = NormalizeIpString(clientIp);
        var (deviceSummary, isMobile) = UserAgentDeviceParser.Parse(userAgent);
        var deviceTokenHash = ComputeDeviceTokenHash(deviceToken);
        var eventDataJson = BuildSessionEventJson(normalizedIp, userAgent is null ? null : deviceSummary);
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
                Event(db,"SignInFailed",now,correlation,user?.Id,outcome:1,dataJson:eventDataJson);
                var notifiedFailure = false;
                if (allowed && user is not null && user.AccessFailedCount >= 1)
                {
                    notifiedFailure = await TryQueueNotificationAsync(
                        db,
                        user.Id,
                        NotificationEventCatalog.FailedSignInAttempt,
                        NotificationCategory.Security,
                        NotificationSeverity.Danger,
                        NotificationTemplateCatalog.FailedSignInAttempts,
                        new { count = user.AccessFailedCount, ip = normalizedIp ?? "—" },
                        "/account/activity",
                        now,
                        ct);
                }
                await db.SaveChangesAsync(ct); if (!await Commit(tx,ct,[password, username])) return new("Auth.ServiceUnavailable");
                if (notifiedFailure && user is not null) liveChannel?.NotifyUser(user.Id);
                return new("Auth.LoginRejected");
            }
            stage = "identity-reset-failures";
            var reset = await manager.ResetAccessFailedCountAsync(user!);
            if (!reset.Succeeded) { logger.LogWarning("AUTH-LOGIN stage=identity-reset-failures outcome=unavailable codes={Codes}", string.Join(",", reset.Errors.Select(error => error.Code))); return new("Auth.ServiceUnavailable"); }
            stage = "session-save";
            await Revoke(db,user!.Id,"StampChanged",Now,correlation,ct,user.SecurityStamp);
            // Same account lock as recovery: stamp is read only after taking it.
            var issued=Now;
            var hasPriorSessions = await db.Sessions.AsNoTracking().AnyAsync(s => s.UserId == user.Id, ct);
            var isKnownDevice = !hasPriorSessions || (deviceTokenHash is not null
                ? await db.Sessions.AsNoTracking().AnyAsync(s => s.UserId == user.Id && s.DeviceTokenHash == deviceTokenHash, ct)
                : await db.Sessions.AsNoTracking().AnyAsync(s =>
                    s.UserId == user.Id
                    && s.IpAddress == normalizedIp
                    && s.DeviceSummary == (userAgent == null ? null : deviceSummary), ct));

            var session = new AccountSession { Id = Guid.NewGuid(), UserId = user!.Id, SecurityStampSnapshot = user.SecurityStamp!,
                IpAddress = normalizedIp, DeviceSummary = userAgent is null ? null : deviceSummary, DeviceTokenHash = deviceTokenHash, IsMobileDevice = isMobile,
                IssuedAtUtc = issued, LastActivityAtUtc = issued, ExpiresAtUtc = issued.Add(Lifetime) };
            db.Sessions.Add(session);
            Event(db,"SessionIssued",session.IssuedAtUtc,correlation,user.Id,session.Id,true,dataJson:eventDataJson);

            var notifiedNewDevice = false;
            if (hasPriorSessions && !isKnownDevice)
            {
                notifiedNewDevice = await TryQueueNotificationAsync(
                    db,
                    user.Id,
                    NotificationEventCatalog.NewDeviceSignIn,
                    NotificationCategory.Security,
                    NotificationSeverity.Warning,
                    NotificationTemplateCatalog.NewDeviceSignIn,
                    new { device = userAgent is null ? "Browser" : deviceSummary, ip = normalizedIp ?? "—" },
                    "/account/sessions",
                    issued,
                    ct);
            }

            var existingPref = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == user.Id, ct);
            if (existingPref is null)
            {
                var seedLanguage = UserPreferenceCatalog.NormalizeLanguage(user.PreferredCulture)
                    ?? UserPreferenceCatalog.NormalizeLanguage(initialCulture)
                    ?? UserPreferenceCatalog.PersianLanguage;
                db.UserPreferences.Add(new UserPreference
                {
                    UserId = user.Id,
                    Language = seedLanguage,
                    Theme = UserPreferenceCatalog.LightTheme,
                    CalendarSystem = UserPreferenceCatalog.DefaultCalendarForLanguage(seedLanguage),
                    TimeZoneId = UserPreferenceCatalog.DefaultTimeZoneId,
                    CreatedAtUtc = issued
                });
            }

            await db.SaveChangesAsync(ct); stage = "session-commit"; if (!await Commit(tx,ct,[password, username])) return new("Auth.ServiceUnavailable");
            if (notifiedNewDevice) liveChannel?.NotifyUser(user.Id);
            return new("Auth.SignedIn",new(user.Id,session.Id,user.SecurityStamp!,user.UserName),new DateTimeOffset(session.ExpiresAtUtc));
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure(stage, e, password, username);
            logger.LogWarning("AUTH-LOGIN stage={Stage} outcome=unavailable exceptionType={ExceptionType}", stage, e.GetType().Name);
            Unavailable(); return new("Auth.ServiceUnavailable");
        }
    }
    public async Task<PreviousSignInInfo?> GetPreviousSignInAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var row = await db.SecurityEvents.AsNoTracking()
                .Where(e => e.SubjectUserId == reference.UserId
                            && e.EventCode == "SessionIssued"
                            && e.Outcome == 0
                            && e.SessionId != null
                            && e.SessionId != reference.SessionId)
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenByDescending(e => e.Id)
                .Select(e => new { e.OccurredAtUtc, e.DataJson })
                .FirstOrDefaultAsync(ct);

            if (row is null)
                return null;

            string? ip = null;
            if (!string.IsNullOrWhiteSpace(row.DataJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.DataJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("ip", out var prop)
                        && prop.ValueKind == JsonValueKind.String)
                    {
                        ip = prop.GetString();
                    }
                }
                catch (JsonException) { }
            }

            return new PreviousSignInInfo(DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc), string.IsNullOrWhiteSpace(ip) ? null : ip);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("previous-signin-lookup", e, reference.SecurityStamp);
            return null;
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
    public async Task<string> SetPreferredCultureAsync(SessionReference reference, string culture, CancellationToken ct = default)
    {
        if (culture is not ("fa-IR" or "en-US")) return "Culture.ProfileSyncFailed";
        try
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var correlation = Guid.NewGuid();
            var eligible = await Eligible(db, reference, correlation, ct);
            if (!eligible) { await db.SaveChangesAsync(ct); await Commit(tx, ct); return "Auth.SessionInvalid"; }
            var user = await db.Users.SingleAsync(u => u.Id == reference.UserId, ct);
            var pref = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == reference.UserId, ct);
            var changed = !string.Equals(user.PreferredCulture, culture, StringComparison.Ordinal)
                || (pref is not null && !string.Equals(pref.Language, culture, StringComparison.Ordinal));
            var now = Now < user.CreatedAtUtc ? user.CreatedAtUtc : Now;
            if (pref is null)
            {
                db.UserPreferences.Add(new UserPreference
                {
                    UserId = user.Id,
                    Language = culture,
                    Theme = UserPreferenceCatalog.LightTheme,
                    CalendarSystem = UserPreferenceCatalog.DefaultCalendarForLanguage(culture),
                    TimeZoneId = UserPreferenceCatalog.DefaultTimeZoneId,
                    CreatedAtUtc = now
                });
            }
            else if (!string.Equals(pref.Language, culture, StringComparison.Ordinal))
            {
                pref.Language = culture;
                pref.UpdatedAtUtc = now < pref.CreatedAtUtc ? pref.CreatedAtUtc : now;
            }
            if (changed)
            {
                user.PreferredCulture = culture;
                user.UpdatedAtUtc = now;
                db.SecurityEvents.Add(new SecurityEvent
                {
                    EventCode = "CulturePreferenceChanged",
                    OccurredAtUtc = now,
                    CorrelationId = correlation,
                    ActorKind = 1,
                    ActorUserId = user.Id,
                    SubjectUserId = user.Id,
                    SessionId = reference.SessionId,
                    Outcome = 0,
                    DataJson = $"{{\"culture\":\"{culture}\"}}"
                });
            }
            await db.SaveChangesAsync(ct);
            return await Commit(tx, ct) ? "Culture.ProfileSaved" : "Culture.ProfileSyncFailed";
        }
        catch (Exception e) when (AvailableFailure(e)) { Failure("culture-preference", e, reference.SecurityStamp); Unavailable(); return "Culture.ProfileSyncFailed"; }
    }

    public async Task<UserPreferencesSnapshot?> GetPreferencesAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;
            var row = await (from u in db.Users.AsNoTracking()
                             join s in db.Sessions.AsNoTracking() on u.Id equals s.UserId
                             join p in db.UserPreferences.AsNoTracking() on u.Id equals p.UserId into prefs
                             from p in prefs.DefaultIfEmpty()
                             where u.Id == reference.UserId && u.IsActive
                                   && s.Id == reference.SessionId && s.RevokedAtUtc == null
                                   && s.ExpiresAtUtc > now && s.SecurityStampSnapshot == u.SecurityStamp
                             select new
                             {
                                 u.PreferredCulture,
                                 PrefLanguage = p != null ? p.Language : null,
                                 Theme = p != null ? p.Theme : null,
                                 CalendarSystem = p != null ? p.CalendarSystem : null,
                                 TimeZoneId = p != null ? p.TimeZoneId : null
                             }).SingleOrDefaultAsync(ct);
            if (row is null) return null;

            var language = UserPreferenceCatalog.NormalizeLanguage(row.PrefLanguage)
                ?? UserPreferenceCatalog.NormalizeLanguage(row.PreferredCulture)
                ?? UserPreferenceCatalog.PersianLanguage;
            var theme = UserPreferenceCatalog.NormalizeTheme(row.Theme) ?? UserPreferenceCatalog.LightTheme;
            var calendar = UserPreferenceCatalog.NormalizeCalendar(row.CalendarSystem)
                ?? UserPreferenceCatalog.DefaultCalendarForLanguage(language);
            var timeZone = UserPreferenceCatalog.NormalizeTimeZoneId(row.TimeZoneId) ?? UserPreferenceCatalog.DefaultTimeZoneId;
            return new UserPreferencesSnapshot(language, theme, calendar, timeZone);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("preferences-lookup", e, reference.SecurityStamp);
            return null;
        }
    }

    public async Task<UserPreferencesResult> SavePreferencesAsync(SessionReference reference, UpdateUserPreferencesRequest request, CancellationToken ct = default)
    {
        var language = UserPreferenceCatalog.NormalizeLanguage(request.Language);
        var theme = UserPreferenceCatalog.NormalizeTheme(request.Theme);
        var calendar = UserPreferenceCatalog.NormalizeCalendar(request.CalendarSystem);
        var timeZoneId = UserPreferenceCatalog.NormalizeTimeZoneId(request.TimeZoneId);
        if (language is null || theme is null || calendar is null || timeZoneId is null)
            return new("Preferences.InvalidInput");

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var correlation = Guid.NewGuid();
            if (!await Eligible(db, reference, correlation, ct))
            {
                await db.SaveChangesAsync(ct);
                await Commit(tx, ct);
                return new("Auth.SessionInvalid");
            }

            var user = await db.Users.SingleAsync(u => u.Id == reference.UserId, ct);
            var now = Now < user.CreatedAtUtc ? user.CreatedAtUtc : Now;
            user.PreferredCulture = language;
            user.UpdatedAtUtc = now;

            var pref = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == user.Id, ct);
            if (pref is null)
            {
                pref = new UserPreference
                {
                    UserId = user.Id,
                    Language = language,
                    Theme = theme,
                    CalendarSystem = calendar,
                    TimeZoneId = timeZoneId,
                    CreatedAtUtc = now
                };
                db.UserPreferences.Add(pref);
            }
            else
            {
                pref.Language = language;
                pref.Theme = theme;
                pref.CalendarSystem = calendar;
                pref.TimeZoneId = timeZoneId;
                pref.UpdatedAtUtc = now < pref.CreatedAtUtc ? pref.CreatedAtUtc : now;
            }

            db.SecurityEvents.Add(new SecurityEvent
            {
                EventCode = "PreferencesUpdated",
                OccurredAtUtc = now,
                CorrelationId = correlation,
                ActorKind = 1,
                ActorUserId = user.Id,
                SubjectUserId = user.Id,
                SessionId = reference.SessionId,
                Outcome = 0,
                DataJson = JsonSerializer.Serialize(new { language, theme, calendar, timeZone = timeZoneId })
            });

            await db.SaveChangesAsync(ct);
            if (!await Commit(tx, ct))
                return new("Auth.ServiceUnavailable");

            return new("Preferences.Saved", new UserPreferencesSnapshot(language, theme, calendar, timeZoneId));
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("preferences-save", e, reference.SecurityStamp);
            Unavailable();
            return new("Auth.ServiceUnavailable");
        }
    }

    public async Task<UserProfileSnapshot?> GetProfileAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;
            return await (from u in db.Users.AsNoTracking()
                          join s in db.Sessions.AsNoTracking() on u.Id equals s.UserId
                          where u.Id == reference.UserId && u.IsActive
                                && s.Id == reference.SessionId && s.RevokedAtUtc == null
                                && s.ExpiresAtUtc > now && s.SecurityStampSnapshot == u.SecurityStamp
                          select new UserProfileSnapshot(
                              u.Id,
                              u.UserName!,
                              u.DisplayName,
                              u.Email,
                              u.PhoneNumber,
                              u.AvatarContentType != null && u.AvatarData != null))
                .SingleOrDefaultAsync(ct);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("profile-lookup", e, reference.SecurityStamp);
            return null;
        }
    }

    public async Task<UserProfileResult> UpdateProfileAsync(SessionReference reference, UpdateUserProfileRequest request, CancellationToken ct = default)
    {
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (displayName is { Length: > 128 } || (displayName is not null && displayName.Any(char.IsControl)))
            return new("Profile.DisplayNameInvalid");
        if (!UserPreferenceCatalog.IsValidEmailOrEmpty(request.Email, out var email))
            return new("Profile.EmailInvalid");
        if (!UserPreferenceCatalog.IsValidPhoneOrEmpty(request.PhoneNumber, out var phone))
            return new("Profile.PhoneInvalid");
        if (request.AvatarBytes is not null && !UserPreferenceCatalog.IsValidAvatar(request.AvatarBytes, request.AvatarContentType))
            return new("Profile.AvatarInvalid");

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
                await Commit(tx, ct);
                return new("Auth.SessionInvalid");
            }

            var user = await LockUser(db, reference.UserId, ct);
            if (user is null) return new("Auth.SessionInvalid");

            var now = Now < user.CreatedAtUtc ? user.CreatedAtUtc : Now;
            user.DisplayName = displayName;
            user.Email = email;
            user.NormalizedEmail = email is null ? null : manager.NormalizeEmail(email);
            user.PhoneNumber = phone;
            if (request.RemoveAvatar)
            {
                user.AvatarContentType = null;
                user.AvatarData = null;
            }
            else if (request.AvatarBytes is not null && request.AvatarContentType is not null)
            {
                user.AvatarContentType = request.AvatarContentType;
                user.AvatarData = request.AvatarBytes;
            }
            user.UpdatedAtUtc = now;

            db.SecurityEvents.Add(new SecurityEvent
            {
                EventCode = "ProfileUpdated",
                OccurredAtUtc = now,
                CorrelationId = correlation,
                ActorKind = 1,
                ActorUserId = user.Id,
                SubjectUserId = user.Id,
                SessionId = reference.SessionId,
                Outcome = 0,
                DataJson = JsonSerializer.Serialize(new
                {
                    hasDisplayName = displayName is not null,
                    hasEmail = email is not null,
                    hasPhone = phone is not null,
                    avatarChanged = request.RemoveAvatar || request.AvatarBytes is not null
                })
            });

            await db.SaveChangesAsync(ct);
            if (!await Commit(tx, ct))
                return new("Auth.ServiceUnavailable");

            var snapshot = new UserProfileSnapshot(
                user.Id,
                user.UserName!,
                user.DisplayName,
                user.Email,
                user.PhoneNumber,
                user.AvatarContentType is not null && user.AvatarData is not null);
            return new("Profile.Saved", snapshot);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("profile-update", e, reference.SecurityStamp);
            Unavailable();
            return new("Auth.ServiceUnavailable");
        }
    }

    public async Task<(byte[] Data, string ContentType)?> GetAvatarAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;
            var avatar = await (from u in db.Users.AsNoTracking()
                                join s in db.Sessions.AsNoTracking() on u.Id equals s.UserId
                                where u.Id == reference.UserId && u.IsActive
                                      && s.Id == reference.SessionId && s.RevokedAtUtc == null
                                      && s.ExpiresAtUtc > now && s.SecurityStampSnapshot == u.SecurityStamp
                                      && u.AvatarContentType != null && u.AvatarData != null
                                select new { u.AvatarData, u.AvatarContentType }).SingleOrDefaultAsync(ct);
            return avatar is null ? null : (avatar.AvatarData!, avatar.AvatarContentType!);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("avatar-lookup", e, reference.SecurityStamp);
            return null;
        }
    }

    public async Task TouchSessionActivityAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;
            var session = await db.Sessions.SingleOrDefaultAsync(
                s => s.Id == reference.SessionId && s.UserId == reference.UserId && s.RevokedAtUtc == null && s.ExpiresAtUtc > now,
                ct);
            if (session is null || session.SecurityStampSnapshot != reference.SecurityStamp)
                return;

            if (now - session.LastActivityAtUtc >= TimeSpan.FromMinutes(2))
            {
                session.LastActivityAtUtc = now < session.IssuedAtUtc ? session.IssuedAtUtc : now;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("session-touch", e, reference.SecurityStamp);
        }
    }

    private static async Task<(IReadOnlyList<AccessibleSiteOption> Sites, string? PlatformRole)> LoadAccessibleSitesAsync(
        AuthDbContext db,
        long userId,
        CancellationToken ct)
    {
        var roleAssignments = await (from r in db.ScopedRoleAssignments.AsNoTracking()
                                     join rd in db.RoleDefinitions.AsNoTracking() on r.RoleId equals rd.Id
                                     where r.UserId == userId && r.IsActive && rd.IsActive
                                     orderby rd.IsSystem descending, r.Id
                                     select new
                                     {
                                         r.ScopeKind,
                                         r.OrganizationId,
                                         r.SiteId,
                                         RoleName = rd.Name
                                     }).ToListAsync(ct);

        var platformRole = roleAssignments.FirstOrDefault(r => r.ScopeKind == 0)?.RoleName;

        var orgMembers = await (from om in db.OrganizationMemberships.AsNoTracking()
                                join o in db.Set<ForgeFlow.Domain.Organizations.Organization>().AsNoTracking() on om.OrganizationId equals o.Id
                                where om.UserId == userId && om.IsActive && o.IsActive
                                orderby o.Code, o.Id
                                select new { o.Id, o.Code, o.Name }).ToListAsync(ct);

        if (orgMembers.Count == 0)
            return ([], platformRole);

        var activeOrgIds = orgMembers.Select(o => o.Id).ToList();
        var orgMap = orgMembers.ToDictionary(o => o.Id);

        var orgSites = await db.Set<ForgeFlow.Domain.Organizations.Site>().AsNoTracking()
            .Where(s => activeOrgIds.Contains(s.OrganizationId) && s.IsActive)
            .OrderBy(s => s.Code)
            .ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.OrganizationId, s.Code, s.Name })
            .ToListAsync(ct);

        var siteMemberships = await db.SiteMemberships.AsNoTracking()
            .Where(sm => sm.UserId == userId && activeOrgIds.Contains(sm.OrganizationId) && sm.IsActive)
            .Select(sm => new { sm.OrganizationId, sm.SiteId })
            .ToListAsync(ct);
        var siteMemberSet = siteMemberships.Select(x => (x.OrganizationId, x.SiteId)).ToHashSet();

        var directGrantSites = await db.UserPermissionGrants.AsNoTracking()
            .Where(g => g.UserId == userId && g.IsActive && g.ScopeKind == 2 && g.SiteId != null && activeOrgIds.Contains(g.OrganizationId))
            .Select(g => new { g.OrganizationId, SiteId = g.SiteId!.Value })
            .Distinct()
            .ToListAsync(ct);
        var directGrantOrgIds = await db.UserPermissionGrants.AsNoTracking()
            .Where(g => g.UserId == userId && g.IsActive && g.ScopeKind == 1 && activeOrgIds.Contains(g.OrganizationId))
            .Select(g => g.OrganizationId)
            .Distinct()
            .ToListAsync(ct);
        var directSiteSet = directGrantSites.Select(x => (x.OrganizationId, x.SiteId)).ToHashSet();

        var result = new List<AccessibleSiteOption>();
        foreach (var site in orgSites)
        {
            var org = orgMap[site.OrganizationId];
            var orgRole = roleAssignments.FirstOrDefault(r => r.ScopeKind == 1 && r.OrganizationId == site.OrganizationId)?.RoleName;
            var siteRole = roleAssignments.FirstOrDefault(r => r.ScopeKind == 2 && r.OrganizationId == site.OrganizationId && r.SiteId == site.Id)?.RoleName;

            var hasOrgWideAccess = orgRole is not null || directGrantOrgIds.Contains(site.OrganizationId);
            var hasSiteAccess = siteMemberSet.Contains((site.OrganizationId, site.Id))
                                && (siteRole is not null || directSiteSet.Contains((site.OrganizationId, site.Id)));

            if (!hasOrgWideAccess && !hasSiteAccess)
                continue;

            result.Add(new AccessibleSiteOption(
                org.Id,
                org.Code,
                org.Name,
                site.Id,
                site.Code,
                site.Name,
                siteRole ?? orgRole));
        }

        return (result, platformRole);
    }

    public async Task<UserMenuContextSnapshot?> GetUserMenuContextAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;

            var account = await (from u in db.Users.AsNoTracking()
                                 join s in db.Sessions.AsNoTracking() on u.Id equals s.UserId
                                 join p in db.UserPreferences.AsNoTracking() on u.Id equals p.UserId into prefs
                                 from p in prefs.DefaultIfEmpty()
                                 where u.Id == reference.UserId && u.IsActive
                                       && s.Id == reference.SessionId && s.RevokedAtUtc == null
                                       && s.ExpiresAtUtc > now && s.SecurityStampSnapshot == u.SecurityStamp
                                 select new
                                 {
                                     u.UserName,
                                     u.DisplayName,
                                     HasAvatar = u.AvatarContentType != null && u.AvatarData != null,
                                     u.SecurityStamp,
                                     SessionIssuedAtUtc = s.IssuedAtUtc,
                                     ActiveOrgId = p != null ? p.ActiveOrganizationId : null,
                                     ActiveSiteId = p != null ? p.ActiveSiteId : null
                                 }).SingleOrDefaultAsync(ct);

            if (account is null)
                return null;

            var (sites, platformRole) = await LoadAccessibleSitesAsync(db, reference.UserId, ct);
            var activeSite = sites.FirstOrDefault(s =>
                                 account.ActiveOrgId.HasValue
                                 && account.ActiveSiteId.HasValue
                                 && s.OrganizationId == account.ActiveOrgId.Value
                                 && s.SiteId == account.ActiveSiteId.Value)
                             ?? sites.FirstOrDefault();

            var prevRow = await db.SecurityEvents.AsNoTracking()
                .Where(e => e.SubjectUserId == reference.UserId
                            && e.EventCode == "SessionIssued"
                            && e.Outcome == 0
                            && e.SessionId != null
                            && e.SessionId != reference.SessionId
                            && e.OccurredAtUtc <= account.SessionIssuedAtUtc)
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenByDescending(e => e.Id)
                .Select(e => new { e.OccurredAtUtc, e.DataJson })
                .FirstOrDefaultAsync(ct);

            DateTime? prevUtc = prevRow is null ? null : DateTime.SpecifyKind(prevRow.OccurredAtUtc, DateTimeKind.Utc);
            var prevIp = prevRow is null ? null : ParseJsonField(prevRow.DataJson, "ip");
            var failedSincePrev = prevRow is null
                ? 0
                : await db.SecurityEvents.AsNoTracking()
                    .CountAsync(e => e.SubjectUserId == reference.UserId
                                     && e.EventCode == "SignInFailed"
                                     && e.OccurredAtUtc > prevRow.OccurredAtUtc, ct);

            var activeSessionCount = await db.Sessions.AsNoTracking()
                .CountAsync(s => s.UserId == reference.UserId
                                 && s.RevokedAtUtc == null
                                 && s.ExpiresAtUtc > now
                                 && s.SecurityStampSnapshot == account.SecurityStamp, ct);

            return new UserMenuContextSnapshot(
                account.UserName!,
                account.DisplayName,
                account.HasAvatar,
                activeSite?.RoleName ?? platformRole,
                activeSite?.SiteName,
                activeSite?.OrganizationId,
                activeSite?.SiteId,
                sites,
                prevUtc,
                prevIp,
                failedSincePrev,
                activeSessionCount);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("user-menu-context", e, reference.SecurityStamp);
            return null;
        }
    }

    public async Task<string> SwitchActiveSiteAsync(SessionReference reference, long organizationId, long siteId, CancellationToken ct = default)
    {
        if (organizationId <= 0 || siteId <= 0)
            return "Site.InvalidSelection";

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var correlation = Guid.NewGuid();
            if (!await Eligible(db, reference, correlation, ct))
            {
                await db.SaveChangesAsync(ct);
                await Commit(tx, ct);
                return "Auth.SessionInvalid";
            }

            var (sites, _) = await LoadAccessibleSitesAsync(db, reference.UserId, ct);
            if (!sites.Any(s => s.OrganizationId == organizationId && s.SiteId == siteId))
                return "Site.AccessDenied";

            var user = await db.Users.SingleAsync(u => u.Id == reference.UserId, ct);
            var now = Now < user.CreatedAtUtc ? user.CreatedAtUtc : Now;
            var pref = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == reference.UserId, ct);
            if (pref is null)
            {
                var seedLang = UserPreferenceCatalog.NormalizeLanguage(user.PreferredCulture) ?? UserPreferenceCatalog.PersianLanguage;
                pref = new UserPreference
                {
                    UserId = user.Id,
                    Language = seedLang,
                    Theme = UserPreferenceCatalog.LightTheme,
                    CalendarSystem = UserPreferenceCatalog.DefaultCalendarForLanguage(seedLang),
                    TimeZoneId = UserPreferenceCatalog.DefaultTimeZoneId,
                    ActiveOrganizationId = organizationId,
                    ActiveSiteId = siteId,
                    CreatedAtUtc = now
                };
                db.UserPreferences.Add(pref);
            }
            else
            {
                pref.ActiveOrganizationId = organizationId;
                pref.ActiveSiteId = siteId;
                pref.UpdatedAtUtc = now < pref.CreatedAtUtc ? pref.CreatedAtUtc : now;
            }

            db.SecurityEvents.Add(new SecurityEvent
            {
                EventCode = "ActiveSiteSwitched",
                OccurredAtUtc = now,
                CorrelationId = correlation,
                ActorKind = 1,
                ActorUserId = user.Id,
                SubjectUserId = user.Id,
                SessionId = reference.SessionId,
                OrganizationId = organizationId,
                SiteId = siteId,
                Outcome = 0,
                DataJson = JsonSerializer.Serialize(new { organizationId, siteId })
            });

            await db.SaveChangesAsync(ct);
            return await Commit(tx, ct) ? "Site.Switched" : "Auth.ServiceUnavailable";
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("switch-active-site", e, reference.SecurityStamp);
            Unavailable();
            return "Auth.ServiceUnavailable";
        }
    }

    public async Task<IReadOnlyList<ActiveSessionSnapshot>> ListActiveSessionsAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return [];
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;
            var userStamp = await db.Users.AsNoTracking()
                .Where(u => u.Id == reference.UserId && u.IsActive)
                .Select(u => u.SecurityStamp)
                .SingleOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(userStamp)) return [];

            var rows = await db.Sessions.AsNoTracking()
                .Where(s => s.UserId == reference.UserId
                            && s.RevokedAtUtc == null
                            && s.ExpiresAtUtc > now
                            && s.SecurityStampSnapshot == userStamp)
                .OrderByDescending(s => s.Id == reference.SessionId)
                .ThenByDescending(s => s.LastActivityAtUtc)
                .ThenByDescending(s => s.IssuedAtUtc)
                .Select(s => new ActiveSessionSnapshot(
                    s.Id,
                    s.IpAddress,
                    string.IsNullOrWhiteSpace(s.DeviceSummary) ? "Browser" : s.DeviceSummary,
                    s.IsMobileDevice,
                    DateTime.SpecifyKind(s.IssuedAtUtc, DateTimeKind.Utc),
                    DateTime.SpecifyKind(s.LastActivityAtUtc, DateTimeKind.Utc),
                    s.Id == reference.SessionId))
                .ToListAsync(ct);

            return rows;
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("list-active-sessions", e, reference.SecurityStamp);
            return [];
        }
    }

    public async Task<SessionOperationResult> RevokeSessionAsync(SessionReference reference, Guid targetSessionId, CancellationToken ct = default)
    {
        if (targetSessionId == Guid.Empty)
            return new("Session.NotFound");
        if (targetSessionId == reference.SessionId)
            return new("Session.CannotRevokeCurrent");

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var correlation = Guid.NewGuid();
            if (!await Eligible(db, reference, correlation, ct))
            {
                await db.SaveChangesAsync(ct);
                await Commit(tx, ct);
                return new("Auth.SessionInvalid");
            }

            var now = Now;
            var target = await db.Sessions.SingleOrDefaultAsync(
                s => s.Id == targetSessionId && s.UserId == reference.UserId && s.RevokedAtUtc == null,
                ct);
            if (target is null)
                return new("Session.NotFound");

            target.RevokedAtUtc = now < target.IssuedAtUtc ? target.IssuedAtUtc : now;
            target.RevokeReason = "SessionRevokedByUser";
            Event(db, "SessionRevoked", now, correlation, reference.UserId, target.Id, true);
            Event(db, "SessionRevokedByUser", now, correlation, reference.UserId, target.Id, true,
                dataJson: JsonSerializer.Serialize(new { revokedSessionId = target.Id }));

            await db.SaveChangesAsync(ct);
            return await Commit(tx, ct) ? new("Session.Revoked", 1) : new("Auth.ServiceUnavailable");
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("revoke-session", e, reference.SecurityStamp);
            Unavailable();
            return new("Auth.ServiceUnavailable");
        }
    }

    public async Task<SessionOperationResult> RevokeOtherSessionsAsync(SessionReference reference, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var correlation = Guid.NewGuid();
            if (!await Eligible(db, reference, correlation, ct))
            {
                await db.SaveChangesAsync(ct);
                await Commit(tx, ct);
                return new("Auth.SessionInvalid");
            }

            var now = Now;
            var others = await db.Sessions
                .Where(s => s.UserId == reference.UserId
                            && s.Id != reference.SessionId
                            && s.RevokedAtUtc == null)
                .ToListAsync(ct);

            foreach (var session in others)
            {
                session.RevokedAtUtc = now < session.IssuedAtUtc ? session.IssuedAtUtc : now;
                session.RevokeReason = "SessionRevokedByUser";
                Event(db, "SessionRevoked", now, correlation, reference.UserId, session.Id, true);
            }

            if (others.Count > 0)
            {
                Event(db, "OtherSessionsRevoked", now, correlation, reference.UserId, reference.SessionId, true,
                    dataJson: JsonSerializer.Serialize(new { revokedCount = others.Count }));
            }

            await db.SaveChangesAsync(ct);
            return await Commit(tx, ct) ? new("Session.OthersRevoked", others.Count) : new("Auth.ServiceUnavailable");
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("revoke-other-sessions", e, reference.SecurityStamp);
            Unavailable();
            return new("Auth.ServiceUnavailable");
        }
    }

    private static string? ParseJsonField(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static async Task<bool> TryQueueNotificationAsync(
        AuthDbContext db,
        long userId,
        string preferenceEventCode,
        NotificationCategory category,
        NotificationSeverity severity,
        string templateCode,
        object? parameters,
        string? linkUrl,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (userId <= 0)
            return false;

        var pref = await db.UserNotificationPreferences.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userId && p.EventCode == preferenceEventCode, ct);
        if (pref is not null && !pref.InAppEnabled)
            return false;

        var paramsJson = parameters is null ? "{}" : JsonSerializer.Serialize(parameters);
        if (paramsJson.Length > 1000)
            paramsJson = "{}";

        db.UserNotifications.Add(new UserNotification
        {
            UserId = userId,
            Category = (byte)category,
            Severity = (byte)severity,
            TemplateCode = templateCode,
            ParamsJson = paramsJson,
            LinkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim(),
            CreatedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)
        });
        return true;
    }

    public async Task PublishAsync(
        long userId,
        string preferenceEventCode,
        NotificationCategory category,
        NotificationSeverity severity,
        string templateCode,
        object? parameters,
        string? linkUrl,
        CancellationToken ct = default)
    {
        if (userId <= 0 || NotificationEventCatalog.Normalize(preferenceEventCode) is null)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var activeUser = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, ct);
            if (!activeUser)
                return;

            var queued = await TryQueueNotificationAsync(
                db, userId, preferenceEventCode, category, severity, templateCode, parameters, linkUrl, Now, ct);
            if (!queued)
                return;

            await db.SaveChangesAsync(ct);
            liveChannel?.NotifyUser(userId);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("notification-publish", e);
        }
    }

    public async Task<IReadOnlyList<NotificationPreferenceSnapshot>> GetNotificationPreferencesAsync(
        SessionReference reference,
        CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return [];
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var saved = await db.UserNotificationPreferences.AsNoTracking()
                .Where(p => p.UserId == reference.UserId)
                .ToListAsync(ct);
            var map = saved.ToDictionary(p => p.EventCode, StringComparer.Ordinal);
            return NotificationEventCatalog.All.Select(code => map.TryGetValue(code, out var row)
                ? new NotificationPreferenceSnapshot(code, row.InAppEnabled, row.EmailEnabled)
                : new NotificationPreferenceSnapshot(code, true, false)).ToList();
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("notification-prefs-get", e, reference.SecurityStamp);
            return [];
        }
    }

    public async Task<string> SaveNotificationPreferenceAsync(
        SessionReference reference,
        string eventCode,
        bool inAppEnabled,
        bool emailEnabled,
        CancellationToken ct = default)
    {
        var normalized = NotificationEventCatalog.Normalize(eventCode);
        if (normalized is null)
            return "NotificationPrefs.Invalid";

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var correlation = Guid.NewGuid();
            if (!await Eligible(db, reference, correlation, ct))
            {
                await db.SaveChangesAsync(ct);
                await Commit(tx, ct);
                return "Auth.SessionInvalid";
            }

            var now = Now;
            var existing = await db.UserNotificationPreferences
                .SingleOrDefaultAsync(p => p.UserId == reference.UserId && p.EventCode == normalized, ct);
            if (existing is null)
            {
                db.UserNotificationPreferences.Add(new UserNotificationPreference
                {
                    UserId = reference.UserId,
                    EventCode = normalized,
                    InAppEnabled = inAppEnabled,
                    EmailEnabled = emailEnabled,
                    CreatedAtUtc = now
                });
            }
            else
            {
                existing.InAppEnabled = inAppEnabled;
                existing.EmailEnabled = emailEnabled;
                existing.UpdatedAtUtc = now < existing.CreatedAtUtc ? existing.CreatedAtUtc : now;
            }

            await db.SaveChangesAsync(ct);
            return await Commit(tx, ct) ? "NotificationPrefs.Saved" : "Auth.ServiceUnavailable";
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("notification-prefs-save", e, reference.SecurityStamp);
            Unavailable();
            return "Auth.ServiceUnavailable";
        }
    }

    public async Task<int> GetUnreadNotificationCountAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return 0;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;
            var sessionValid = await (from u in db.Users.AsNoTracking()
                                      join s in db.Sessions.AsNoTracking() on u.Id equals s.UserId
                                      where u.Id == reference.UserId && u.IsActive
                                            && s.Id == reference.SessionId && s.RevokedAtUtc == null
                                            && s.ExpiresAtUtc > now && s.SecurityStampSnapshot == u.SecurityStamp
                                      select u.Id).AnyAsync(ct);
            if (!sessionValid) return 0;

            return await db.UserNotifications.AsNoTracking()
                .CountAsync(n => n.UserId == reference.UserId && n.ReadAtUtc == null, ct);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("notifications-unread-count", e, reference.SecurityStamp);
            return 0;
        }
    }

    public async Task<NotificationPageResult> ListNotificationsAsync(
        SessionReference reference,
        NotificationListFilter filter = NotificationListFilter.All,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty)
            return new NotificationPageResult([], 0, 0, 1, pageSize);

        var clampedPage = Math.Max(1, page);
        var clampedSize = Math.Clamp(pageSize, 1, 50);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;
            var sessionValid = await (from u in db.Users.AsNoTracking()
                                      join s in db.Sessions.AsNoTracking() on u.Id equals s.UserId
                                      where u.Id == reference.UserId && u.IsActive
                                            && s.Id == reference.SessionId && s.RevokedAtUtc == null
                                            && s.ExpiresAtUtc > now && s.SecurityStampSnapshot == u.SecurityStamp
                                      select u.Id).AnyAsync(ct);
            if (!sessionValid)
                return new NotificationPageResult([], 0, 0, clampedPage, clampedSize);

            var baseQuery = db.UserNotifications.AsNoTracking().Where(n => n.UserId == reference.UserId);
            var unreadCount = await baseQuery.CountAsync(n => n.ReadAtUtc == null, ct);

            var filtered = filter switch
            {
                NotificationListFilter.Unread => baseQuery.Where(n => n.ReadAtUtc == null),
                NotificationListFilter.Security => baseQuery.Where(n => n.Category == (byte)NotificationCategory.Security),
                NotificationListFilter.Orders => baseQuery.Where(n => n.Category == (byte)NotificationCategory.Orders),
                _ => baseQuery
            };

            var total = await filtered.CountAsync(ct);
            var items = await filtered
                .OrderByDescending(n => n.CreatedAtUtc)
                .ThenByDescending(n => n.Id)
                .Skip((clampedPage - 1) * clampedSize)
                .Take(clampedSize)
                .Select(n => new UserNotificationSnapshot(
                    n.Id,
                    n.Category,
                    n.Severity,
                    n.TemplateCode,
                    n.ParamsJson,
                    n.LinkUrl,
                    DateTime.SpecifyKind(n.CreatedAtUtc, DateTimeKind.Utc),
                    n.ReadAtUtc.HasValue ? DateTime.SpecifyKind(n.ReadAtUtc.Value, DateTimeKind.Utc) : null))
                .ToListAsync(ct);

            return new NotificationPageResult(items, total, unreadCount, clampedPage, clampedSize);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("notifications-list", e, reference.SecurityStamp);
            return new NotificationPageResult([], 0, 0, clampedPage, clampedSize);
        }
    }

    public async Task<bool> MarkNotificationReadAsync(SessionReference reference, long notificationId, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || notificationId <= 0) return false;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (!await Eligible(db, reference, Guid.NewGuid(), ct))
            {
                await db.SaveChangesAsync(ct);
                await Commit(tx, ct);
                return false;
            }

            var target = await db.UserNotifications
                .SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == reference.UserId, ct);
            if (target is null)
                return false;

            if (target.ReadAtUtc is null)
            {
                var now = Now;
                target.ReadAtUtc = now < target.CreatedAtUtc ? target.CreatedAtUtc : now;
                await db.SaveChangesAsync(ct);
            }

            if (!await Commit(tx, ct))
                return false;

            liveChannel?.NotifyUser(reference.UserId);
            return true;
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("notification-mark-read", e, reference.SecurityStamp);
            return false;
        }
    }

    public async Task<int> MarkAllNotificationsReadAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0) return 0;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (!await Eligible(db, reference, Guid.NewGuid(), ct))
            {
                await db.SaveChangesAsync(ct);
                await Commit(tx, ct);
                return 0;
            }

            var unread = await db.UserNotifications
                .Where(n => n.UserId == reference.UserId && n.ReadAtUtc == null)
                .ToListAsync(ct);
            if (unread.Count > 0)
            {
                var now = Now;
                foreach (var item in unread)
                    item.ReadAtUtc = now < item.CreatedAtUtc ? item.CreatedAtUtc : now;
                await db.SaveChangesAsync(ct);
            }

            if (!await Commit(tx, ct))
                return 0;

            liveChannel?.NotifyUser(reference.UserId);
            return unread.Count;
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("notifications-mark-all-read", e, reference.SecurityStamp);
            return 0;
        }
    }

    public async Task<string?> GetPreferredCultureAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
        try
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;
            var culture = await (from u in db.Users.AsNoTracking()
                                 join s in db.Sessions.AsNoTracking() on u.Id equals s.UserId
                                 join p in db.UserPreferences.AsNoTracking() on u.Id equals p.UserId into prefs
                                 from pref in prefs.DefaultIfEmpty()
                                 where u.Id == reference.UserId && u.IsActive
                                       && s.Id == reference.SessionId && s.RevokedAtUtc == null
                                       && s.ExpiresAtUtc > now && s.SecurityStampSnapshot == u.SecurityStamp
                                 select pref != null ? pref.Language : u.PreferredCulture).SingleOrDefaultAsync(ct);
            return culture is "fa-IR" or "en-US" ? culture : null;
        }
        catch (Exception e) when (AvailableFailure(e)) { Failure("culture-lookup", e, reference.SecurityStamp); return null; }
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
    private static async Task<bool> ScopeAllowed(AuthDbContext db, long userId, OrganizationScope scope, string permission, CancellationToken ct)
    {
        if (!PermissionCatalog.TryGet(permission, out var descriptor))
            return false;

        if (scope is OrganizationScope.Platform)
        {
            if (descriptor.Applicability != PermissionScopeApplicability.PlatformOnly
                && permission is not (UserPermissions.UsersSetActive or UserPermissions.UsersUnlock or UserPermissions.UsersResetPassword or AuditPermissions.AuditRead or AuditPermissions.AuditExport or DiagnosticPermissions.ErrorLogsRead))
                return false;

            var provisionerBuiltIn = permission is AuditPermissions.AuditRead
                or AuditPermissions.AuditExport
                or DiagnosticPermissions.ErrorLogsRead
                or DiagnosticPermissions.ErrorLogsManage;

            return await (from r in db.ScopedRoleAssignments.AsNoTracking()
                          join rd in db.RoleDefinitions.AsNoTracking() on r.RoleId equals rd.Id
                          join rp in db.RolePermissions.AsNoTracking() on rd.Id equals rp.RoleId
                          where r.UserId == userId
                                && r.IsActive
                                && r.ScopeKind == 0
                                && r.OrganizationId == null
                                && r.SiteId == null
                                && rd.IsActive
                                && rd.ScopeKind == 0
                                && rp.IsActive
                                && (rp.PermissionCode == permission
                                    || (r.RoleId == FixedAccessRoles.DeploymentProvisioner && provisionerBuiltIn))
                          select r.Id).AnyAsync(ct);
        }

        if (descriptor.Applicability == PermissionScopeApplicability.PlatformOnly)
            return false;

        var orgId = scope is OrganizationScope.Organization org ? org.OrganizationId : ((OrganizationScope.Site)scope).OrganizationId;
        if (!await db.OrganizationMemberships.AsNoTracking().AnyAsync(m => m.UserId == userId && m.OrganizationId == orgId && m.IsActive, ct))
            return false;

        var company = await db.Set<ForgeFlow.Domain.Organizations.Organization>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == orgId, ct);
        if (company is null)
            return false;

        var read = permission is OrganizationPermissions.OrganizationsRead
            or OrganizationPermissions.SitesRead
            or UserPermissions.UsersRead
            or RolePermissions.RolesRead
            or AuditPermissions.AuditRead
            or UnitPermissions.UnitsRead
            or ItemPermissions.ItemsRead
            or BomPermissions.BomsRead
            or ProductionOrderPermissions.ProductionOrdersRead
            or InventoryPermissions.InventoryRead
            or ReportingPermissions.ReportsRead
            or ReportingPermissions.ReportsExport
            or QualityPermissions.QualityRead
            or DiagnosticPermissions.ErrorLogsRead;
        if (!company.IsActive && !read && permission != OrganizationPermissions.OrganizationsSetActive)
            return false;

        if (scope is OrganizationScope.Site site)
        {
            if (descriptor.Applicability != PermissionScopeApplicability.OrganizationOrSite)
                return false;

            var target = await db.Set<ForgeFlow.Domain.Organizations.Site>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == site.SiteId && s.OrganizationId == orgId, ct);
            if (target is null || (!target.IsActive && !read && permission != OrganizationPermissions.SitesSetActive))
                return false;
        }

        var autoGrantedToOrgAdmin = PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(permission, 1);
        var orgRoleAllowed = await (from r in db.ScopedRoleAssignments.AsNoTracking()
                                    join rd in db.RoleDefinitions.AsNoTracking() on r.RoleId equals rd.Id
                                    join rp in db.RolePermissions.AsNoTracking() on rd.Id equals rp.RoleId
                                    where r.UserId == userId
                                          && r.IsActive
                                          && r.ScopeKind == 1
                                          && r.OrganizationId == orgId
                                          && rd.IsActive
                                          && (rd.OrganizationId == null || rd.OrganizationId == orgId)
                                          && ((rp.IsActive && rp.PermissionCode == permission)
                                              || (r.RoleId == FixedAccessRoles.OrganizationAdministrator && autoGrantedToOrgAdmin))
                                    select r.Id).AnyAsync(ct);
        if (orgRoleAllowed)
            return true;

        var orgDirectAllowed = await db.UserPermissionGrants.AsNoTracking()
            .AnyAsync(g => g.UserId == userId
                           && g.IsActive
                           && g.ScopeKind == 1
                           && g.OrganizationId == orgId
                           && g.SiteId == null
                           && g.PermissionCode == permission, ct);
        if (orgDirectAllowed)
            return true;

        if (scope is not OrganizationScope.Site requested)
            return false;

        if (!await db.SiteMemberships.AsNoTracking().AnyAsync(m => m.UserId == userId && m.OrganizationId == orgId && m.SiteId == requested.SiteId && m.IsActive, ct))
            return false;

        var siteRoleAllowed = await (from r in db.ScopedRoleAssignments.AsNoTracking()
                                     join rd in db.RoleDefinitions.AsNoTracking() on r.RoleId equals rd.Id
                                     join rp in db.RolePermissions.AsNoTracking() on rd.Id equals rp.RoleId
                                     where r.UserId == userId
                                           && r.IsActive
                                           && r.ScopeKind == 2
                                           && r.OrganizationId == orgId
                                           && r.SiteId == requested.SiteId
                                           && rd.IsActive
                                           && (rd.OrganizationId == null || rd.OrganizationId == orgId)
                                           && rp.IsActive
                                           && rp.PermissionCode == permission
                                     select r.Id).AnyAsync(ct);
        if (siteRoleAllowed)
            return true;

        return await db.UserPermissionGrants.AsNoTracking()
            .AnyAsync(g => g.UserId == userId
                           && g.IsActive
                           && g.ScopeKind == 2
                           && g.OrganizationId == orgId
                           && g.SiteId == requested.SiteId
                           && g.PermissionCode == permission, ct);
    }
}

public static class FixedRolePolicy
{
    public static bool Allows(long role, OrganizationScope scope, string permission) => role switch
    {
        FixedAccessRoles.DeploymentProvisioner => scope is OrganizationScope.Platform
            && permission is OrganizationPermissions.OrganizationsCreate or UserPermissions.UsersAttachOrganization or UserPermissions.UsersSetActive or UserPermissions.UsersUnlock or UserPermissions.UsersResetPassword or AuditPermissions.AuditRead or AuditPermissions.AuditExport or DiagnosticPermissions.ErrorLogsRead or DiagnosticPermissions.ErrorLogsManage,
        FixedAccessRoles.OrganizationAdministrator => scope switch {
            OrganizationScope.Organization => PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(permission, 1),
            OrganizationScope.Site => PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(permission, 2), _=>false },
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
