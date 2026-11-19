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
    public async Task<IReadOnlyList<byte[]>> GetActivePasskeyCredentialIdsAsync(SessionReference reference, CancellationToken ct = default)
    {
        if (reference.UserId <= 0 || reference.SessionId == Guid.Empty) return [];
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            return await db.Passkeys.AsNoTracking()
                .Where(p => p.UserId == reference.UserId && p.RevokedAtUtc == null)
                .Select(p => p.CredentialId)
                .ToListAsync(ct);
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("passkey-list-ids", e, reference.SecurityStamp);
            return [];
        }
    }

    public async Task<string> RegisterPasskeyAsync(
        SessionReference reference,
        string displayName,
        VerifiedPasskeyRegistration verified,
        CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "Passkey" : displayName.Trim();
        if (name.Length > 100 || name.Any(char.IsControl))
            return "Auth.PasskeyInvalid";

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

            if (await db.Passkeys.AnyAsync(p => p.CredentialId == verified.CredentialId, ct))
                return "Auth.PasskeyInvalid";

            var now = Now;
            db.Passkeys.Add(new UserPasskey
            {
                UserId = reference.UserId,
                CredentialId = verified.CredentialId,
                PublicKeyCose = verified.PublicKeyCose,
                Algorithm = verified.Algorithm,
                SignCount = verified.SignCount,
                DisplayName = name,
                CreatedAtUtc = now
            });
            Event(db, "PasskeyRegistered", now, correlation, reference.UserId, reference.SessionId, true,
                dataJson: JsonSerializer.Serialize(new { displayName = name, algorithm = verified.Algorithm }));

            await db.SaveChangesAsync(ct);
            return await Commit(tx, ct) ? "Auth.PasskeyRegistered" : "Auth.ServiceUnavailable";
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("passkey-register", e, reference.SecurityStamp);
            Unavailable();
            return "Auth.ServiceUnavailable";
        }
    }

    public async Task<string> RemovePasskeyAsync(SessionReference reference, long passkeyId, CancellationToken ct = default)
    {
        if (passkeyId <= 0) return "Auth.PasskeyNotFound";
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

            var passkey = await db.Passkeys.SingleOrDefaultAsync(
                p => p.Id == passkeyId && p.UserId == reference.UserId && p.RevokedAtUtc == null, ct);
            if (passkey is null)
                return "Auth.PasskeyNotFound";

            var now = Now;
            passkey.RevokedAtUtc = now < passkey.CreatedAtUtc ? passkey.CreatedAtUtc : now;
            Event(db, "PasskeyRemoved", now, correlation, reference.UserId, reference.SessionId, true,
                dataJson: JsonSerializer.Serialize(new { passkeyId, displayName = passkey.DisplayName }));

            await db.SaveChangesAsync(ct);
            return await Commit(tx, ct) ? "Auth.PasskeyRemoved" : "Auth.ServiceUnavailable";
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("passkey-remove", e, reference.SecurityStamp);
            Unavailable();
            return "Auth.ServiceUnavailable";
        }
    }

    public async Task<string> RevokeTrustedDevicesAsync(SessionReference reference, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var tokens = await db.UserTokens.Where(t => t.UserId == reference.UserId && t.LoginProvider == TrustedDeviceLoginProvider).ToListAsync(ct);
            if (tokens.Count > 0)
            {
                db.UserTokens.RemoveRange(tokens);
                await db.SaveChangesAsync(ct);
            }
            return "Security.TrustedDevicesRevoked";
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Unavailable();
            return "Auth.ServiceUnavailable";
        }
    }

    public async Task<SessionLoginResult> LoginWithPasskeyAsync(
        byte[] credentialId,
        byte[] clientDataJson,
        byte[] authData,
        byte[] signature,
        string expectedChallengeB64Url,
        string expectedOrigin,
        string expectedRpId,
        CancellationToken ct = default,
        string? clientIp = null,
        string? initialCulture = null,
        string? userAgent = null,
        string? deviceToken = null)
    {
        if (credentialId.Length == 0 || credentialId.Length > 512)
            return new("Auth.LoginRejected");

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

            var passkey = await db.Passkeys.SingleOrDefaultAsync(p => p.CredentialId == credentialId && p.RevokedAtUtc == null, ct);
            if (passkey is null)
                return new("Auth.LoginRejected");

            var user = await LockUser(db, passkey.UserId, ct);
            if (user is null || !user.IsActive || Locked(user, now))
                return new("Auth.LoginRejected");

            if (!WebAuthnVerifier.VerifyAssertion(
                    clientDataJson,
                    authData,
                    signature,
                    passkey.PublicKeyCose,
                    passkey.Algorithm,
                    passkey.SignCount,
                    expectedChallengeB64Url,
                    expectedOrigin,
                    expectedRpId,
                    out var newSignCount))
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
                if (!await Commit(tx, ct)) return new("Auth.ServiceUnavailable");
                return new("Auth.LoginRejected");
            }

            var reset = await manager.ResetAccessFailedCountAsync(user);
            if (!reset.Succeeded) return new("Auth.ServiceUnavailable");

            passkey.SignCount = newSignCount;
            passkey.LastUsedAtUtc = now < passkey.CreatedAtUtc ? passkey.CreatedAtUtc : now;

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
                if (!await Commit(tx, ct)) return new("Auth.ServiceUnavailable");
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
            Event(db, "PasskeySignIn", session.IssuedAtUtc, correlation, user.Id, session.Id, true,
                dataJson: JsonSerializer.Serialize(new { passkeyId = passkey.Id, displayName = passkey.DisplayName, ip = normalizedIp }));

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
            if (!await Commit(tx, ct)) return new("Auth.ServiceUnavailable");
            RegisterSessionInMemory(session.Id, user.Id, user.SecurityStamp!, session.IssuedAtUtc, session.ExpiresAtUtc, session.LastActivityAtUtc);
            if (notifiedNewDevice) liveChannel?.NotifyUser(user.Id);
            return new("Auth.SignedIn", new(user.Id, session.Id, user.SecurityStamp!, user.UserName), new DateTimeOffset(session.ExpiresAtUtc));
        }
        catch (Exception e) when (AvailableFailure(e))
        {
            Failure("passkey-login", e);
            Unavailable();
            return new("Auth.ServiceUnavailable");
        }
    }

    public async Task<bool> PermissionAsync(SessionReference reference, OrganizationScope scope, string permission, CancellationToken ct)
    {
        if (permCache is not null && permCache.TryGetGrantedPermissions(reference.UserId, scope, out var cachedGranted) && cachedGranted is not null)
        {
            return cachedGranted.Contains(permission);
        }

        try
        {
            await using var operation = scopes.CreateAsyncScope(); var db = operation.ServiceProvider.GetRequiredService<AuthDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var correlation=Guid.NewGuid(); var eligible=await Eligible(db,reference,correlation,ct);
            var allowed = eligible && await ScopeAllowed(db,reference.UserId,scope,permission,ct);
            if (eligible && !allowed) Event(db,"AccessDenied",Now,correlation,reference.UserId,reference.SessionId,true,outcome:1);
            await db.SaveChangesAsync(ct);
            var committed = await Commit(tx,ct);
            if (committed && eligible)
            {
                var granted = await LoadGrantedPermissionsAsync(db, reference.UserId, scope, ct);
                permCache?.SetGrantedPermissions(reference.UserId, scope, granted);
            }
            return committed && allowed;
        }
        catch (Exception e) when (AvailableFailure(e)) { Unavailable(); return false; }
    }

    private static async Task<HashSet<string>> LoadGrantedPermissionsAsync(AuthDbContext db, long userId, OrganizationScope scope, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        if (scope is OrganizationScope.Platform)
        {
            var codes = await (from r in db.ScopedRoleAssignments.AsNoTracking()
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
                               select new { r.RoleId, rp.PermissionCode }).ToListAsync(ct);

            foreach (var item in codes)
            {
                result.Add(item.PermissionCode);
                if (item.RoleId == FixedAccessRoles.DeploymentProvisioner)
                {
                    result.Add(AuditPermissions.AuditRead);
                    result.Add(AuditPermissions.AuditExport);
                    result.Add(DiagnosticPermissions.ErrorLogsRead);
                    result.Add(DiagnosticPermissions.ErrorLogsManage);
                }
            }
            return result;
        }

        var orgId = scope is OrganizationScope.Organization org ? org.OrganizationId : ((OrganizationScope.Site)scope).OrganizationId;
        if (!await db.OrganizationMemberships.AsNoTracking().AnyAsync(m => m.UserId == userId && m.OrganizationId == orgId && m.IsActive, ct))
            return result;

        var company = await db.Set<ForgeFlow.Domain.Organizations.Organization>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == orgId, ct);
        if (company is null)
            return result;

        if (scope is OrganizationScope.Site site)
        {
            var target = await db.Set<ForgeFlow.Domain.Organizations.Site>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == site.SiteId && s.OrganizationId == orgId, ct);
            if (target is null)
                return result;
        }

        var orgRoles = await (from r in db.ScopedRoleAssignments.AsNoTracking()
                              join rd in db.RoleDefinitions.AsNoTracking() on r.RoleId equals rd.Id
                              join rp in db.RolePermissions.AsNoTracking() on rd.Id equals rp.RoleId
                              where r.UserId == userId
                                    && r.IsActive
                                    && r.ScopeKind == 1
                                    && r.OrganizationId == orgId
                                    && rd.IsActive
                                    && (rd.OrganizationId == null || rd.OrganizationId == orgId)
                                    && rp.IsActive
                              select new { r.RoleId, rp.PermissionCode }).ToListAsync(ct);

        foreach (var r in orgRoles)
        {
            result.Add(r.PermissionCode);
            if (r.RoleId == FixedAccessRoles.OrganizationAdministrator)
            {
                foreach (var p in PermissionCatalog.All)
                {
                    if (PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(p.Code, 1))
                        result.Add(p.Code);
                }
            }
        }

        var orgDirect = await db.UserPermissionGrants.AsNoTracking()
            .Where(g => g.UserId == userId && g.IsActive && g.ScopeKind == 1 && g.OrganizationId == orgId && g.SiteId == null)
            .Select(g => g.PermissionCode)
            .ToListAsync(ct);
        foreach (var c in orgDirect) result.Add(c);

        if (scope is not OrganizationScope.Site siteScope)
            return result;

        if (!await db.SiteMemberships.AsNoTracking().AnyAsync(m => m.UserId == userId && m.OrganizationId == orgId && m.SiteId == siteScope.SiteId && m.IsActive, ct))
            return result;

        var siteRoles = await (from r in db.ScopedRoleAssignments.AsNoTracking()
                               join rd in db.RoleDefinitions.AsNoTracking() on r.RoleId equals rd.Id
                               join rp in db.RolePermissions.AsNoTracking() on rd.Id equals rp.RoleId
                               where r.UserId == userId
                                     && r.IsActive
                                     && r.ScopeKind == 2
                                     && r.OrganizationId == orgId
                                     && r.SiteId == siteScope.SiteId
                                     && rd.IsActive
                                     && (rd.OrganizationId == null || rd.OrganizationId == orgId)
                                     && rp.IsActive
                               select rp.PermissionCode).ToListAsync(ct);
        foreach (var c in siteRoles) result.Add(c);

        var siteDirect = await db.UserPermissionGrants.AsNoTracking()
            .Where(g => g.UserId == userId && g.IsActive && g.ScopeKind == 2 && g.OrganizationId == orgId && g.SiteId == siteScope.SiteId)
            .Select(g => g.PermissionCode)
            .ToListAsync(ct);
        foreach (var c in siteDirect) result.Add(c);

        return result;
    }
    private static async Task<bool> ScopeAllowed(AuthDbContext db, long userId, OrganizationScope scope, string permission, CancellationToken ct)
    {
        if (!PermissionCatalog.TryGet(permission, out var descriptor))
            return false;

        if (scope is OrganizationScope.Platform)
        {
            if (descriptor.Applicability != PermissionScopeApplicability.PlatformOnly
                && permission is not (UserPermissions.UsersSetActive or UserPermissions.UsersUnlock or UserPermissions.UsersResetPassword or UserPermissions.UsersResetTwoFactor or AuditPermissions.AuditRead or AuditPermissions.AuditExport or DiagnosticPermissions.ErrorLogsRead))
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

    public async Task<int> PurgeExpiredSessionsAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        var totalDeleted = 0;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            while (!ct.IsCancellationRequested)
            {
                var candidateIds = await db.Sessions
                    .Where(s => s.ExpiresAtUtc < cutoffUtc || (s.RevokedAtUtc != null && s.RevokedAtUtc < cutoffUtc))
                    .Select(s => s.Id)
                    .Take(1000)
                    .ToListAsync(ct);

                if (candidateIds.Count == 0)
                    break;

                // Clear FK reference in SecurityEvent before deleting AccountSession
                await db.SecurityEvents
                    .Where(e => e.SessionId != null && candidateIds.Contains(e.SessionId.Value))
                    .ExecuteUpdateAsync(e => e.SetProperty(x => x.SessionId, (Guid?)null), ct);

                var deleted = await db.Sessions
                    .Where(s => candidateIds.Contains(s.Id))
                    .ExecuteDeleteAsync(ct);

                totalDeleted += deleted;
                if (candidateIds.Count < 1000)
                    break;
            }
        }
        catch (Exception ex) when (AvailableFailure(ex))
        {
            Unavailable();
        }

        foreach (var (sId, s) in _sessions)
        {
            if (s.ExpiresAtUtc < cutoffUtc || (s.IsRevoked && s.ExpiresAtUtc < cutoffUtc))
            {
                _sessions.TryRemove(sId, out _);
                if (_userSessions.TryGetValue(s.UserId, out var set))
                {
                    set.TryRemove(sId, out _);
                }
                _sessionLocks.TryRemove(sId, out _);
            }
        }

        return totalDeleted;
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
