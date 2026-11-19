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
    private static string NormalizeOtpInput(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();

    private static string FormatSharedKey(string unformattedKey)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < unformattedKey.Length; i += 4)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(unformattedKey.AsSpan(i, Math.Min(4, unformattedKey.Length - i)));
        }
        return sb.ToString().ToUpperInvariant();
    }

    public async Task<SessionLoginResult> CompleteTwoFactorLoginAsync(
        long userId,
        string securityStamp,
        string code,
        bool isRecoveryCode,
        bool rememberDevice,
        CancellationToken ct = default,
        string? clientIp = null,
        string? initialCulture = null,
        string? userAgent = null,
        string? deviceToken = null)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(securityStamp))
            return new("Auth.TwoFactorPendingExpired");

        var normalizedCode = isRecoveryCode ? code?.Trim() ?? string.Empty : NormalizeOtpInput(code);
        if (string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 64)
            return new("Auth.TwoFactorInvalidCode");

        var normalizedIp = NormalizeIpString(clientIp);
        var (deviceSummary, isMobile) = UserAgentDeviceParser.Parse(userAgent);
        var deviceTokenHash = ComputeDeviceTokenHash(deviceToken);
        var eventDataJson = BuildSessionEventJson(normalizedIp, userAgent is null ? null : deviceSummary);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var correlation = Guid.NewGuid();
            var now = Now;

            var user = await LockUser(db, userId, ct);
            if (user is null || !user.IsActive || Locked(user, now) || !user.TwoFactorEnabled || !string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal))
                return new("Auth.TwoFactorPendingExpired");

            bool verified;
            if (isRecoveryCode)
            {
                var redeem = await manager.RedeemTwoFactorRecoveryCodeAsync(user, normalizedCode);
                verified = redeem.Succeeded;
                if (verified)
                    Event(db, "RecoveryCodeRedeemed", now, correlation, user.Id, outcome: 0, dataJson: eventDataJson);
            }
            else
            {
                verified = await manager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, normalizedCode);
            }

            if (!verified)
            {
                if (user.LockoutEnabled)
                {
                    var update = await manager.AccessFailedAsync(user);
                    if (!update.Succeeded) return new("Auth.ServiceUnavailable");
                    if (Locked(user, Now))
                    {
                        await Revoke(db, user.Id, "AccountLocked", Now, correlation, ct);
                        Event(db, "AccountLocked", Now, correlation, user.Id);
                    }
                }
                Event(db, "SignInFailed", now, correlation, user.Id, outcome: 1, dataJson: eventDataJson);
                await db.SaveChangesAsync(ct);
                if (!await Commit(tx, ct, [normalizedCode, securityStamp])) return new("Auth.ServiceUnavailable");
                return new("Auth.TwoFactorInvalidCode");
            }

            var reset = await manager.ResetAccessFailedCountAsync(user);
            if (!reset.Succeeded) return new("Auth.ServiceUnavailable");

            await Revoke(db, user.Id, "StampChanged", Now, correlation, ct, user.SecurityStamp);
            var issued = Now;
            var hasPriorSessions = await db.Sessions.AsNoTracking().AnyAsync(s => s.UserId == user.Id, ct);
            var isKnownDevice = !hasPriorSessions || (deviceTokenHash is not null
                ? await db.Sessions.AsNoTracking().AnyAsync(s => s.UserId == user.Id && s.DeviceTokenHash == deviceTokenHash, ct)
                : await db.Sessions.AsNoTracking().AnyAsync(s =>
                    s.UserId == user.Id
                    && s.IpAddress == normalizedIp
                    && s.DeviceSummary == (userAgent == null ? null : deviceSummary), ct));

            var limitError = await EnforceConcurrentSessionLimitAsync(db, user, issued, correlation, ct);
            if (limitError is not null)
            {
                await db.SaveChangesAsync(ct);
                if (!await Commit(tx, ct, [normalizedCode, securityStamp])) return new("Auth.ServiceUnavailable");
                return new(limitError);
            }

            var session = new AccountSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                SecurityStampSnapshot = user.SecurityStamp!,
                IpAddress = normalizedIp,
                DeviceSummary = userAgent is null ? null : deviceSummary,
                DeviceTokenHash = deviceTokenHash,
                IsMobileDevice = isMobile,
                IssuedAtUtc = issued,
                LastActivityAtUtc = issued,
                ExpiresAtUtc = issued.Add(Lifetime)
            };
            db.Sessions.Add(session);
            Event(db, "SessionIssued", session.IssuedAtUtc, correlation, user.Id, session.Id, true, dataJson: eventDataJson);
            if (rememberDevice)
            {
                if (!string.IsNullOrWhiteSpace(deviceToken))
                    await SaveTrustedDeviceAsync(db, user.Id, deviceToken, user.SecurityStamp!, ct);
                Event(db, "DeviceTrusted", session.IssuedAtUtc, correlation, user.Id, session.Id, true, dataJson: eventDataJson);
            }

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
                    "/account/security",
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

            await db.SaveChangesAsync(ct);
            if (!await Commit(tx, ct, [normalizedCode, securityStamp])) return new("Auth.ServiceUnavailable");
            RegisterSessionInMemory(session.Id, user.Id, user.SecurityStamp!, session.IssuedAtUtc, session.ExpiresAtUtc, session.LastActivityAtUtc);
            if (notifiedNewDevice) liveChannel?.NotifyUser(user.Id);
            return new("Auth.SignedIn", new(user.Id, session.Id, user.SecurityStamp!, user.UserName), new DateTimeOffset(session.ExpiresAtUtc));
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("complete-2fa-login", e, normalizedCode, securityStamp);
            Unavailable();
            return new("Auth.ServiceUnavailable");
        }
    }

    public async Task<AccountSecuritySnapshot?> GetAccountSecurityAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
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
                                 u,
                                 IdleTimeoutMinutes = p != null ? p.IdleTimeoutMinutes : null,
                                 SnoozedUntil = p != null ? p.TwoFactorReminderSnoozedUntilUtc : null
                             }).SingleOrDefaultAsync(ct);
            if (row is null) return null;

            var recoveryRemaining = row.u.TwoFactorEnabled
                ? await manager.CountRecoveryCodesAsync(row.u)
                : 0;
            var activeSessions = await db.Sessions.AsNoTracking()
                .CountAsync(s => s.UserId == reference.UserId && s.RevokedAtUtc == null && s.ExpiresAtUtc > now && s.SecurityStampSnapshot == row.u.SecurityStamp, ct);
            var newDeviceAlert = await db.UserNotificationPreferences.AsNoTracking()
                .Where(np => np.UserId == reference.UserId && np.EventCode == NotificationEventCatalog.NewDeviceSignIn)
                .Select(np => (bool?)np.InAppEnabled)
                .SingleOrDefaultAsync(ct) ?? true;

            var passkeys = await db.Passkeys.AsNoTracking()
                .Where(pk => pk.UserId == reference.UserId && pk.RevokedAtUtc == null)
                .OrderByDescending(pk => pk.CreatedAtUtc)
                .Select(pk => new UserPasskeySnapshot(
                    pk.Id,
                    pk.DisplayName,
                    DateTime.SpecifyKind(pk.CreatedAtUtc, DateTimeKind.Utc),
                    pk.LastUsedAtUtc.HasValue ? DateTime.SpecifyKind(pk.LastUsedAtUtc.Value, DateTimeKind.Utc) : null))
                .ToListAsync(ct);

            var pwdChanged = row.u.PasswordChangedAtUtc ?? row.u.CreatedAtUtc;
            return new AccountSecuritySnapshot(
                DateTime.SpecifyKind(pwdChanged, DateTimeKind.Utc),
                row.u.TwoFactorEnabled,
                recoveryRemaining,
                row.IdleTimeoutMinutes,
                activeSessions,
                newDeviceAlert,
                row.SnoozedUntil.HasValue ? DateTime.SpecifyKind(row.SnoozedUntil.Value, DateTimeKind.Utc) : null,
                passkeys);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("account-security-lookup", e, reference.SecurityStamp);
            return null;
        }
    }

    public async Task<TotpSetupInfo?> BeginTotpSetupAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
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
                return null;
            }

            var user = await LockUser(db, reference.UserId, ct);
            if (user is null) return null;

            var key = await manager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await manager.ResetAuthenticatorKeyAsync(user);
                key = await manager.GetAuthenticatorKeyAsync(user);
            }
            if (string.IsNullOrEmpty(key)) return null;

            await db.SaveChangesAsync(ct);
            if (!await Commit(tx, ct)) return null;

            var label = Uri.EscapeDataString($"ForgeFlow:{user.UserName}");
            var uri = $"otpauth://totp/{label}?secret={key}&issuer=ForgeFlow&digits=6";
            return new TotpSetupInfo(FormatSharedKey(key), uri);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("begin-totp-setup", e, reference.SecurityStamp);
            return null;
        }
    }

    public async Task<TotpConfirmResult> ConfirmTotpSetupAsync(SessionReference reference, string code, CancellationToken ct = default)
    {
        var cleanCode = NormalizeOtpInput(code);
        if (cleanCode.Length != 6 || !cleanCode.All(char.IsDigit))
            return new("Auth.TwoFactorInvalidCode", []);

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
                return new("Auth.SessionInvalid", []);
            }

            var user = await LockUser(db, reference.UserId, ct);
            if (user is null) return new("Auth.SessionInvalid", []);

            var valid = await manager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, cleanCode);
            if (!valid)
                return new("Auth.TwoFactorInvalidCode", []);

            var enabled = await manager.SetTwoFactorEnabledAsync(user, true);
            if (!enabled.Succeeded) return new("Auth.ServiceUnavailable", []);

            var now = Now;
            user.TwoFactorEnabledAtUtc = now;
            user.RequireTwoFactorSetup = false;
            var codes = (await manager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToList() ?? [];
            Event(db, "TwoFactorEnabled", now, correlation, user.Id, reference.SessionId, true);
            Event(db, "RecoveryCodesGenerated", now, correlation, user.Id, reference.SessionId, true, dataJson: JsonSerializer.Serialize(new { count = codes.Count }));

            await db.SaveChangesAsync(ct);
            if (!await Commit(tx, ct))
                return new("Auth.ServiceUnavailable", []);

            return new("Auth.TwoFactorEnabled", codes);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("confirm-totp-setup", e, reference.SecurityStamp);
            Unavailable();
            return new("Auth.ServiceUnavailable", []);
        }
    }

    public async Task<TotpConfirmResult> RegenerateRecoveryCodesAsync(SessionReference reference, string currentPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(currentPassword) || currentPassword.Length > 128)
            return new("Auth.CurrentPasswordInvalid", []);

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
                await Commit(tx, ct, [currentPassword]);
                return new("Auth.SessionInvalid", []);
            }

            var user = await LockUser(db, reference.UserId, ct);
            if (user is null) return new("Auth.SessionInvalid", []);
            if (!user.TwoFactorEnabled) return new("Auth.TwoFactorNotEnabled", []);
            if (!await manager.CheckPasswordAsync(user, currentPassword))
                return new("Auth.CurrentPasswordInvalid", []);

            var codes = (await manager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToList() ?? [];
            Event(db, "RecoveryCodesGenerated", Now, correlation, user.Id, reference.SessionId, true, dataJson: JsonSerializer.Serialize(new { count = codes.Count }));

            await db.SaveChangesAsync(ct);
            if (!await Commit(tx, ct, [currentPassword]))
                return new("Auth.ServiceUnavailable", []);

            return new("Auth.RecoveryCodesRegenerated", codes);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("regenerate-recovery-codes", e, currentPassword, reference.SecurityStamp);
            Unavailable();
            return new("Auth.ServiceUnavailable", []);
        }
    }

    public async Task<string> DisableTotpAsync(SessionReference reference, string currentPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(currentPassword) || currentPassword.Length > 128)
            return "Auth.CurrentPasswordInvalid";

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
                await Commit(tx, ct, [currentPassword]);
                return "Auth.SessionInvalid";
            }

            var user = await LockUser(db, reference.UserId, ct);
            if (user is null) return "Auth.SessionInvalid";
            if (!await manager.CheckPasswordAsync(user, currentPassword))
                return "Auth.CurrentPasswordInvalid";

            await manager.SetTwoFactorEnabledAsync(user, false);
            await manager.ResetAuthenticatorKeyAsync(user);
            user.TwoFactorEnabledAtUtc = null;
            Event(db, "TwoFactorDisabled", Now, correlation, user.Id, reference.SessionId, true);

            await db.SaveChangesAsync(ct);
            return await Commit(tx, ct, [currentPassword]) ? "Auth.TwoFactorDisabled" : "Auth.ServiceUnavailable";
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("disable-totp", e, currentPassword, reference.SecurityStamp);
            Unavailable();
            return "Auth.ServiceUnavailable";
        }
    }

    public async Task<string> SetIdleTimeoutAsync(SessionReference reference, int? minutes, CancellationToken ct = default)
    {
        if (!UserPreferenceCatalog.IsValidIdleTimeout(minutes))
            return "Preferences.InvalidInput";

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

            var user = await db.Users.SingleAsync(u => u.Id == reference.UserId, ct);
            var now = Now < user.CreatedAtUtc ? user.CreatedAtUtc : Now;
            var pref = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == user.Id, ct);
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
                    IdleTimeoutMinutes = minutes,
                    CreatedAtUtc = now
                };
                db.UserPreferences.Add(pref);
            }
            else
            {
                pref.IdleTimeoutMinutes = minutes;
                pref.UpdatedAtUtc = now < pref.CreatedAtUtc ? pref.CreatedAtUtc : now;
            }

            Event(db, "IdleTimeoutUpdated", now, correlation, user.Id, reference.SessionId, true,
                dataJson: JsonSerializer.Serialize(new { minutes }));

            await db.SaveChangesAsync(ct);
            var committed = await Commit(tx, ct);
            if (committed)
            {
                _userIdleTimeoutCache[reference.UserId] = minutes;
                return "Security.IdleTimeoutSaved";
            }
            return "Auth.ServiceUnavailable";
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("idle-timeout-save", e, reference.SecurityStamp);
            Unavailable();
            return "Auth.ServiceUnavailable";
        }
    }

    public async Task<TwoFactorReminderStatus?> GetTwoFactorReminderStatusAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var now = Now;
            return await (from u in db.Users.AsNoTracking()
                          join s in db.Sessions.AsNoTracking() on u.Id equals s.UserId
                          join p in db.UserPreferences.AsNoTracking() on u.Id equals p.UserId into prefs
                          from p in prefs.DefaultIfEmpty()
                          where u.Id == reference.UserId && u.IsActive
                                && s.Id == reference.SessionId && s.RevokedAtUtc == null
                                && s.ExpiresAtUtc > now && s.SecurityStampSnapshot == u.SecurityStamp
                          select new TwoFactorReminderStatus(
                              u.TwoFactorEnabled,
                              p != null ? p.TwoFactorReminderSnoozedUntilUtc : null))
                .SingleOrDefaultAsync(ct);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("2fa-reminder-lookup", e, reference.SecurityStamp);
            return null;
        }
    }

    public async Task<bool> SnoozeTwoFactorReminderAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return false;
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

            var user = await db.Users.SingleAsync(u => u.Id == reference.UserId, ct);
            var now = Now < user.CreatedAtUtc ? user.CreatedAtUtc : Now;
            var snoozeUntil = now.AddDays(7);
            var pref = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == user.Id, ct);
            if (pref is null)
            {
                var seedLang = UserPreferenceCatalog.NormalizeLanguage(user.PreferredCulture) ?? UserPreferenceCatalog.PersianLanguage;
                db.UserPreferences.Add(new UserPreference
                {
                    UserId = user.Id,
                    Language = seedLang,
                    Theme = UserPreferenceCatalog.LightTheme,
                    CalendarSystem = UserPreferenceCatalog.DefaultCalendarForLanguage(seedLang),
                    TimeZoneId = UserPreferenceCatalog.DefaultTimeZoneId,
                    TwoFactorReminderSnoozedUntilUtc = snoozeUntil,
                    CreatedAtUtc = now
                });
            }
            else
            {
                pref.TwoFactorReminderSnoozedUntilUtc = snoozeUntil;
                pref.UpdatedAtUtc = now < pref.CreatedAtUtc ? pref.CreatedAtUtc : now;
            }

            await db.SaveChangesAsync(ct);
            return await Commit(tx, ct);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("2fa-reminder-snooze", e, reference.SecurityStamp);
            return false;
        }
    }

}
