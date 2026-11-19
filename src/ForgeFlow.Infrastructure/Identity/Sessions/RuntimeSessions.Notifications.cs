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
            var committed = await Commit(tx, ct);
            if (committed)
            {
                RevokeSession(target.Id, "SessionRevokedByUser");
                return new("Session.Revoked", 1);
            }
            return new("Auth.ServiceUnavailable");
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
            var committed = await Commit(tx, ct);
            if (committed)
            {
                foreach (var session in others)
                    RevokeSession(session.Id, "SessionRevokedByUser");
                RevokeUserSessions(reference.UserId, "SessionRevokedByUser", exceptSessionId: reference.SessionId);
                return new("Session.OthersRevoked", others.Count);
            }
            return new("Auth.ServiceUnavailable");
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

}
