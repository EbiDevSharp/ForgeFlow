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
            var committed = await Commit(tx, ct);
            if (committed)
            {
                _userPreferencesCache.TryRemove(reference.UserId, out _);
            }
            return committed ? "Culture.ProfileSaved" : "Culture.ProfileSyncFailed";
        }
        catch (Exception e) when (AvailableFailure(e)) { Failure("culture-preference", e, reference.SecurityStamp); Unavailable(); return "Culture.ProfileSyncFailed"; }
    }

    public async Task<UserPreferencesSnapshot?> GetPreferencesAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return null;
        if (_userPreferencesCache.TryGetValue(reference.UserId, out var cached))
            return cached;

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
            var snapshot = new UserPreferencesSnapshot(language, theme, calendar, timeZone);
            _userPreferencesCache[reference.UserId] = snapshot;
            return snapshot;
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

            var snapshot = new UserPreferencesSnapshot(language, theme, calendar, timeZoneId);
            _userPreferencesCache[reference.UserId] = snapshot;
            return new("Preferences.Saved", snapshot);
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

        if (_sessions.TryGetValue(reference.SessionId, out var state))
        {
            if (state.IsRevoked) return;
            var now = Now;
            state.LastActivityAtUtc = now;

            if (state.LastPersistedActivityAtUtc.HasValue && now - state.LastPersistedActivityAtUtc.Value < TimeSpan.FromMinutes(2))
                return;
        }

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
                if (_sessions.TryGetValue(reference.SessionId, out var s))
                {
                    s.LastPersistedActivityAtUtc = session.LastActivityAtUtc;
                }
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
                                     u.TwoFactorEnabled,
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
                activeSessionCount,
                account.TwoFactorEnabled);
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
            var committed = await Commit(tx, ct);
            if (committed)
            {
                _userPreferencesCache.TryRemove(reference.UserId, out _);
            }
            return committed ? "Site.Switched" : "Auth.ServiceUnavailable";
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("switch-active-site", e, reference.SecurityStamp);
            Unavailable();
            return "Auth.ServiceUnavailable";
        }
    }

}
