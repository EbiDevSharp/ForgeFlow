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
    public async Task<SessionLoginResult> LoginAsync(string username, string password, CancellationToken ct = default, string? clientIp = null, string? initialCulture = null, string? userAgent = null, string? deviceToken = null, bool twoFactorDeviceTrusted = false)
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

            await db.Database.OpenConnectionAsync(ct);
            stage = "account-lookup";
            var correlation = Guid.NewGuid(); var now = Now;
            var validInput = username.Length is > 0 and <= 128 && password.Length is > 0 and <= 128
                && username.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-' or '@' or '+');
            var normalized = validInput ? manager.NormalizeName(username) : null;

            if (normalized is not null)
            {
                await ApplyLoginThrottlingAsync(normalized, normalizedIp, ct);
            }

            var userCandidate = normalized is null
                ? null
                : await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
            var allowed = userCandidate is not null && userCandidate.IsActive && !Locked(userCandidate, now);

            stage = "credential-validation";
            bool passwordValid;
            if (allowed && !string.IsNullOrEmpty(userCandidate!.PasswordHash))
            {
                passwordValid = manager.PasswordHasher.VerifyHashedPassword(userCandidate, userCandidate.PasswordHash, password) == PasswordVerificationResult.Success;
            }
            else
            {
                // Dummy PBKDF2 hash verification ensures constant execution time regardless of account existence/status
                manager.PasswordHasher.VerifyHashedPassword(s_dummyAccount, s_dummyHash, password);
                passwordValid = false;
            }

            if (!allowed || !passwordValid)
            {
                logger.LogWarning("AUTH-LOGIN stage={Stage} outcome=rejected", stage);
                if (normalized is not null)
                {
                    RecordLoginFailure(normalized, normalizedIp);
                }

                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var user = userCandidate is null
                    ? null
                    : await db.Users.FromSqlInterpolated($"SELECT * FROM [auth].[User] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={userCandidate.Id}").SingleOrDefaultAsync(ct);

                var isThrottled = normalized is not null && IsIpThrottledForAccount(normalized, normalizedIp);
                if (allowed && user is not null && user.LockoutEnabled)
                {
                    stage = "identity-lockout-update";
                    var update = await manager.AccessFailedAsync(user);
                    if (!update.Succeeded) { logger.LogWarning("AUTH-LOGIN stage=identity-lockout-update outcome=unavailable codes={Codes}", string.Join(",", update.Errors.Select(error => error.Code))); return new("Auth.ServiceUnavailable"); }
                    if (Locked(user, Now))
                    {
                        await Revoke(db, user.Id, "AccountLocked", Now, correlation, ct);
                        Event(db, "AccountLocked", Now, correlation, user.Id);
                    }
                }
                else if (user is not null && (!user.IsActive || Locked(user, now)))
                    await Revoke(db, user.Id, user.IsActive ? "AccountLocked" : "AccountDisabled", now, correlation, ct);
                Event(db, "SignInFailed", now, correlation, user?.Id, outcome: 1, dataJson: eventDataJson);
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
                await db.SaveChangesAsync(ct); if (!await Commit(tx, ct, [password, username])) return new("Auth.ServiceUnavailable");
                if (notifiedFailure && user is not null) liveChannel?.NotifyUser(user.Id);
                return new("Auth.LoginRejected");
            }

            if (normalized is not null)
            {
                ClearLoginFailure(normalized, normalizedIp);
            }

            await using var successTx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var lockedUser = await db.Users.FromSqlInterpolated($"SELECT * FROM [auth].[User] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={userCandidate!.Id}").SingleOrDefaultAsync(ct);
            if (lockedUser is null || !lockedUser.IsActive || Locked(lockedUser, now))
            {
                Event(db, "SignInFailed", now, correlation, lockedUser?.Id, outcome: 1, dataJson: eventDataJson);
                await db.SaveChangesAsync(ct);
                await Commit(successTx, ct, [password, username]);
                return new("Auth.LoginRejected");
            }

            stage = "identity-reset-failures";
            var reset = await manager.ResetAccessFailedCountAsync(lockedUser);
            if (!reset.Succeeded) { logger.LogWarning("AUTH-LOGIN stage=identity-reset-failures outcome=unavailable codes={Codes}", string.Join(",", reset.Errors.Select(error => error.Code))); return new("Auth.ServiceUnavailable"); }

            if (lockedUser.TwoFactorEnabled)
            {
                var isTrusted = twoFactorDeviceTrusted && await IsDeviceTrustedAsync(db, lockedUser.Id, deviceToken, lockedUser.SecurityStamp, ct);
                if (!isTrusted)
                {
                    await db.SaveChangesAsync(ct);
                    if (!await Commit(successTx, ct, [password, username])) return new("Auth.ServiceUnavailable");
                    return new("Auth.TwoFactorRequired", PendingUserId: lockedUser.Id, PendingSecurityStamp: lockedUser.SecurityStamp);
                }
            }

            stage = "session-save";
            await Revoke(db, lockedUser.Id, "StampChanged", Now, correlation, ct, lockedUser.SecurityStamp);
            var issued = Now;
            var hasPriorSessions = await db.Sessions.AsNoTracking().AnyAsync(s => s.UserId == lockedUser.Id, ct);
            var isKnownDevice = !hasPriorSessions || (deviceTokenHash is not null
                ? await db.Sessions.AsNoTracking().AnyAsync(s => s.UserId == lockedUser.Id && s.DeviceTokenHash == deviceTokenHash, ct)
                : await db.Sessions.AsNoTracking().AnyAsync(s =>
                    s.UserId == lockedUser.Id
                    && s.IpAddress == normalizedIp
                    && s.DeviceSummary == (userAgent == null ? null : deviceSummary), ct));

            var limitError = await EnforceConcurrentSessionLimitAsync(db, lockedUser, issued, correlation, ct);
            if (limitError is not null)
            {
                await db.SaveChangesAsync(ct);
                if (!await Commit(successTx, ct, [password, username])) return new("Auth.ServiceUnavailable");
                return new(limitError);
            }

            var session = new AccountSession
            {
                Id = Guid.NewGuid(),
                UserId = lockedUser.Id,
                SecurityStampSnapshot = lockedUser.SecurityStamp!,
                IpAddress = normalizedIp,
                DeviceSummary = userAgent is null ? null : deviceSummary,
                DeviceTokenHash = deviceTokenHash,
                IsMobileDevice = isMobile,
                IssuedAtUtc = issued,
                LastActivityAtUtc = issued,
                ExpiresAtUtc = issued.Add(Lifetime)
            };
            db.Sessions.Add(session);
            Event(db, "SessionIssued", session.IssuedAtUtc, correlation, lockedUser.Id, session.Id, true, dataJson: eventDataJson);

            var notifiedNewDevice = false;
            if (hasPriorSessions && !isKnownDevice)
            {
                notifiedNewDevice = await TryQueueNotificationAsync(
                    db,
                    lockedUser.Id,
                    NotificationEventCatalog.NewDeviceSignIn,
                    NotificationCategory.Security,
                    NotificationSeverity.Warning,
                    NotificationTemplateCatalog.NewDeviceSignIn,
                    new { device = userAgent is null ? "Browser" : deviceSummary, ip = normalizedIp ?? "—" },
                    "/account/sessions",
                    issued,
                    ct);
            }

            var existingPref = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == lockedUser.Id, ct);
            if (existingPref is null)
            {
                var seedLanguage = UserPreferenceCatalog.NormalizeLanguage(lockedUser.PreferredCulture)
                    ?? UserPreferenceCatalog.NormalizeLanguage(initialCulture)
                    ?? UserPreferenceCatalog.PersianLanguage;
                existingPref = new UserPreference
                {
                    UserId = lockedUser.Id,
                    Language = seedLanguage,
                    Theme = UserPreferenceCatalog.LightTheme,
                    CalendarSystem = UserPreferenceCatalog.DefaultCalendarForLanguage(seedLanguage),
                    TimeZoneId = UserPreferenceCatalog.DefaultTimeZoneId,
                    CreatedAtUtc = issued
                };
                db.UserPreferences.Add(existingPref);
            }

            var notifiedTwoFactorReminder = false;
            if (lockedUser.RequireTwoFactorSetup && !lockedUser.TwoFactorEnabled && existingPref.TwoFactorOneTimeNotifiedAtUtc is null)
            {
                existingPref.TwoFactorOneTimeNotifiedAtUtc = issued;
                existingPref.UpdatedAtUtc = issued < existingPref.CreatedAtUtc ? existingPref.CreatedAtUtc : issued;
                notifiedTwoFactorReminder = await TryQueueNotificationAsync(
                    db,
                    lockedUser.Id,
                    NotificationEventCatalog.TwoFactorSetupReminder,
                    NotificationCategory.Security,
                    NotificationSeverity.Info,
                    NotificationTemplateCatalog.TwoFactorReminder,
                    null,
                    "/account/security",
                    issued,
                    ct);
            }

            await db.SaveChangesAsync(ct); stage = "session-commit"; if (!await Commit(successTx, ct, [password, username])) return new("Auth.ServiceUnavailable");
            RegisterSessionInMemory(session.Id, lockedUser.Id, lockedUser.SecurityStamp!, session.IssuedAtUtc, session.ExpiresAtUtc, session.LastActivityAtUtc);
            if (notifiedNewDevice || notifiedTwoFactorReminder) liveChannel?.NotifyUser(lockedUser.Id);
            return new("Auth.SignedIn", new(lockedUser.Id, session.Id, lockedUser.SecurityStamp!, lockedUser.UserName), new DateTimeOffset(session.ExpiresAtUtc), RequireTwoFactorSetup: lockedUser.RequireTwoFactorSetup, RequirePasswordChange: lockedUser.RequirePasswordChange);
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
}
