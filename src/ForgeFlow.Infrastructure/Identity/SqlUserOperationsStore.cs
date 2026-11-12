using System.Data;
using System.Data.Common;
using System.Text.Json;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Identity;

internal sealed class UserCommitOutcomeUnknownException(Exception inner) : Exception(UserManagementErrors.OutcomeUnknown, inner);

/// <summary>SQL Server implementation of <see cref="IUserOperationsStore"/> using operation-scoped AuthDbContext and serialized locks.</summary>
public sealed class SqlUserOperationsStore(IServiceScopeFactory scopes, TimeProvider clock, IUserNotificationPublisher? notifications = null) : IUserOperationsStore
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public Task<OrganizationPage<UserSummary>> ListOrganizationUsers(long organizationId, UserListQuery query, CancellationToken ct)
        => ReadAsync(async db =>
        {
            var rows = from m in db.OrganizationMemberships.AsNoTracking()
                       join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                       where m.OrganizationId == organizationId
                             && (query.IncludeInactive || (m.IsActive && u.IsActive))
                       select new
                       {
                           u.Id,
                           u.UserName,
                           AccountIsActive = u.IsActive,
                           MembershipIsActive = m.IsActive,
                           u.LockoutEnabled,
                           u.LockoutEnd,
                           AccountRowVersion = EF.Property<byte[]>(u, "RowVersion"),
                           MembershipRowVersion = EF.Property<byte[]>(m, "RowVersion")
                       };

            if (!string.IsNullOrEmpty(query.Search))
            {
                var term = query.Search;
                rows = rows.Where(x => x.UserName != null && x.UserName.Contains(term));
            }

            var total = await rows.CountAsync(ct);
            var pageRows = await rows
                .OrderBy(x => x.UserName)
                .ThenBy(x => x.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct);

            if (pageRows.Count == 0)
                return new OrganizationPage<UserSummary>([], total, query.Page, query.PageSize);

            var userIds = pageRows.Select(x => x.Id).ToArray();
            var adminUserIds = (await db.ScopedRoleAssignments.AsNoTracking()
                .Where(r => r.OrganizationId == organizationId
                            && r.ScopeKind == 1
                            && r.RoleId == FixedAccessRoles.OrganizationAdministrator
                            && r.IsActive
                            && userIds.Contains(r.UserId))
                .Select(r => r.UserId)
                .ToListAsync(ct))
                .ToHashSet();

            var siteCounts = await db.SiteMemberships.AsNoTracking()
                .Where(s => s.OrganizationId == organizationId && s.IsActive && userIds.Contains(s.UserId))
                .GroupBy(s => s.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

            var now = new DateTimeOffset(UtcNow);
            var items = pageRows.Select(x =>
            {
                var accountVersion = Convert.ToBase64String(x.AccountRowVersion);
                var membershipVersion = Convert.ToBase64String(x.MembershipRowVersion);
                var locked = x.LockoutEnabled && x.LockoutEnd > now;
                return new UserSummary(
                    x.Id,
                    x.UserName ?? string.Empty,
                    x.AccountIsActive,
                    x.MembershipIsActive,
                    locked,
                    adminUserIds.Contains(x.Id),
                    siteCounts.GetValueOrDefault(x.Id),
                    accountVersion,
                    membershipVersion);
            }).ToArray();

            return new OrganizationPage<UserSummary>(items, total, query.Page, query.PageSize);
        }, ct);

    public Task<UserDetails?> GetOrganizationUser(long organizationId, long userId, CancellationToken ct)
        => ReadAsync(db => BuildUserDetailsAsync(db, organizationId, userId, UtcNow, ct), ct);

    public Task<OrganizationResult<UserDetails>> CreateOrganizationUser(
        CreateOrganizationUser request,
        UserAccountName canonicalUserName,
        IReadOnlyList<long> distinctSiteIds,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => WriteAsync(access, async (db, manager) =>
        {
            var org = await LockOrganizationAsync(db, request.OrganizationId, ct);
            if (org is null)
                return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.OrganizationNotFound);
            if (!org.IsActive)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ParentOrganizationInactive);

            var siteError = await ValidateActiveSitesAsync(db, request.OrganizationId, distinctSiteIds, ct);
            if (siteError is not null)
                return OrganizationResult<UserDetails>.Failure(siteError);

            var now = UtcNow;
            var account = new ApplicationAccount
            {
                UserName = canonicalUserName.Value,
                IsActive = true,
                LockoutEnabled = true,
                RequirePasswordChange = request.RequirePasswordChange,
                CreatedAtUtc = now
            };

            var createResult = await manager.CreateAsync(account, request.InitialPassword!);
            if (!createResult.Succeeded)
                return MapIdentityFailure<UserDetails>(createResult);

            var membership = new OrganizationMembership
            {
                UserId = account.Id,
                OrganizationId = request.OrganizationId,
                IsActive = true
            };
            db.OrganizationMemberships.Add(membership);
            SetCreateMetadata(db, membership, now, access.Actor.ActorId);

            if (request.AssignOrganizationAdmin)
            {
                var adminRole = new ScopedRoleAssignment
                {
                    UserId = account.Id,
                    RoleId = FixedAccessRoles.OrganizationAdministrator,
                    ScopeKind = 1,
                    OrganizationId = request.OrganizationId,
                    SiteId = null,
                    IsActive = true
                };
                db.ScopedRoleAssignments.Add(adminRole);
                SetCreateMetadata(db, adminRole, now, access.Actor.ActorId);
            }

            AddSiteMembershipsAndRoles(db, account.Id, request.OrganizationId, distinctSiteIds, request.AssignSiteOperatorToSites, now, access.Actor.ActorId);

            var correlation = access.Actor.CorrelationId;
            AddEvent(db, "AccountCreated", now, correlation, access.Actor.ActorId, account.Id, request.OrganizationId, null, null,
                new { scope = "organization" });
            AddEvent(db, "MembershipChanged", now, correlation, access.Actor.ActorId, account.Id, request.OrganizationId, null, null,
                new { action = "OrganizationMembershipCreated", siteCount = distinctSiteIds.Count });
            if (request.AssignOrganizationAdmin)
            {
                AddEvent(db, "ScopedRoleChanged", now, correlation, access.Actor.ActorId, account.Id, request.OrganizationId, null, null,
                    new { action = "Granted", role = nameof(TenantRoleKind.OrganizationAdministrator) });
            }
            if (request.AssignSiteOperatorToSites)
            {
                foreach (var siteId in distinctSiteIds)
                {
                    AddEvent(db, "ScopedRoleChanged", now, correlation, access.Actor.ActorId, account.Id, request.OrganizationId, siteId, null,
                        new { action = "Granted", role = nameof(TenantRoleKind.SiteOperator), siteId });
                }
            }

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, request.OrganizationId, account.Id, now, ct))!);
        }, ct);

    public Task<OrganizationResult<UserDetails>> AttachExistingUserToOrganization(
        AttachExistingUserToOrganization request,
        UserAccountName canonicalUserName,
        IReadOnlyList<long> distinctSiteIds,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => WriteAsync(access, async (db, _) =>
        {
            var org = await LockOrganizationAsync(db, request.OrganizationId, ct);
            if (org is null)
                return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.OrganizationNotFound);
            if (!org.IsActive)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ParentOrganizationInactive);

            var account = await LockUserByNormalizedNameAsync(db, canonicalUserName.Normalized, ct);
            if (account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            if (await db.OrganizationMemberships.AnyAsync(m => m.UserId == account.Id && m.OrganizationId == request.OrganizationId, ct))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var siteError = await ValidateActiveSitesAsync(db, request.OrganizationId, distinctSiteIds, ct);
            if (siteError is not null)
                return OrganizationResult<UserDetails>.Failure(siteError);

            var now = UtcNow;
            var membership = new OrganizationMembership
            {
                UserId = account.Id,
                OrganizationId = request.OrganizationId,
                IsActive = true
            };
            db.OrganizationMemberships.Add(membership);
            SetCreateMetadata(db, membership, now, access.Actor.ActorId);

            if (request.AssignOrganizationAdmin)
            {
                var adminRole = new ScopedRoleAssignment
                {
                    UserId = account.Id,
                    RoleId = FixedAccessRoles.OrganizationAdministrator,
                    ScopeKind = 1,
                    OrganizationId = request.OrganizationId,
                    SiteId = null,
                    IsActive = true
                };
                db.ScopedRoleAssignments.Add(adminRole);
                SetCreateMetadata(db, adminRole, now, access.Actor.ActorId);
            }

            AddSiteMembershipsAndRoles(db, account.Id, request.OrganizationId, distinctSiteIds, request.AssignSiteOperatorToSites, now, access.Actor.ActorId);

            var correlation = access.Actor.CorrelationId;
            AddEvent(db, "MembershipChanged", now, correlation, access.Actor.ActorId, account.Id, request.OrganizationId, null, null,
                new { action = "OrganizationMembershipAttached", siteCount = distinctSiteIds.Count });
            if (request.AssignOrganizationAdmin)
            {
                AddEvent(db, "ScopedRoleChanged", now, correlation, access.Actor.ActorId, account.Id, request.OrganizationId, null, null,
                    new { action = "Granted", role = nameof(TenantRoleKind.OrganizationAdministrator) });
            }
            if (request.AssignSiteOperatorToSites)
            {
                foreach (var siteId in distinctSiteIds)
                {
                    AddEvent(db, "ScopedRoleChanged", now, correlation, access.Actor.ActorId, account.Id, request.OrganizationId, siteId, null,
                        new { action = "Granted", role = nameof(TenantRoleKind.SiteOperator), siteId });
                }
            }

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, request.OrganizationId, account.Id, now, ct))!);
        }, ct);

    public Task<OrganizationResult<UserDetails>> SetUserActive(
        long? organizationId,
        long userId,
        bool isActive,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => WriteAsync(access, async (db, manager) =>
        {
            var orgIdsToLock = await ResolveOrganizationLocksAsync(db, organizationId, userId, ct);
            Dictionary<long, Organization> lockedOrgs = [];
            foreach (var lockOrgId in orgIdsToLock)
            {
                var lockedOrg = await LockOrganizationAsync(db, lockOrgId, ct);
                if (lockedOrg is not null) lockedOrgs[lockOrgId] = lockedOrg;
            }

            var account = await LockUserByIdAsync(db, userId, ct);
            if (account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            if (organizationId is long orgScopeId)
            {
                if (!lockedOrgs.TryGetValue(orgScopeId, out var scopedOrg))
                    return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

                var isolationError = await EnsureOrganizationExclusiveUserAsync(db, orgScopeId, userId, ct);
                if (isolationError is not null)
                    return OrganizationResult<UserDetails>.Failure(isolationError);

                if (!scopedOrg.IsActive)
                    return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ParentOrganizationInactive);
            }

            if (!MatchesRowVersion(db, account, expectedAccountVersion))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var now = UtcNow;
            if (account.IsActive == isActive)
                return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);

            if (!isActive)
            {
                TenantAccessPolicy.EnsureNotSelfDeactivation(access.Actor.ActorId, userId);

                foreach (var (orgId, org) in lockedOrgs)
                {
                    if (!org.IsActive) continue;
                    var isOrgAdmin = await db.ScopedRoleAssignments.AnyAsync(
                        r => r.UserId == userId
                             && r.OrganizationId == orgId
                             && r.ScopeKind == 1
                             && r.RoleId == FixedAccessRoles.OrganizationAdministrator
                             && r.IsActive, ct);
                    if (isOrgAdmin)
                    {
                        var remainingAdmins = await CountActiveOrgAdminsExcludingAsync(db, orgId, userId, ct);
                        TenantAccessPolicy.EnsureOrganizationRetainsActiveAdmin(org.IsActive, remainingAdmins);
                    }
                }

                var isPlatformProvisioner = await db.ScopedRoleAssignments.AnyAsync(
                    r => r.UserId == userId && r.ScopeKind == 0 && r.RoleId == FixedAccessRoles.DeploymentProvisioner && r.IsActive, ct);
                if (isPlatformProvisioner)
                {
                    var remainingProvisioners = await CountActiveProvisionersExcludingAsync(db, userId, ct);
                    TenantAccessPolicy.EnsurePlatformRetainsActiveProvisioner(remainingProvisioners);
                }
            }

            account.IsActive = isActive;
            account.UpdatedAtUtc = SafeUpdatedAt(account.CreatedAtUtc, now);
            db.Entry(account).Property("RowVersion").OriginalValue = expectedAccountVersion;

            if (!isActive)
            {
                // Rotate SecurityStamp via Identity and revoke open sessions atomically in the same SQL transaction.
                var stampResult = await manager.UpdateSecurityStampAsync(account);
                if (!stampResult.Succeeded)
                {
                    var code = stampResult.Errors.Any(e => e.Code == "ConcurrencyFailure")
                        ? UserManagementErrors.ConcurrencyConflict
                        : UserManagementErrors.PersistenceUnavailable;
                    return OrganizationResult<UserDetails>.Failure(code);
                }

                var openSessions = await db.Sessions
                    .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
                    .ToListAsync(ct);

                foreach (var session in openSessions)
                {
                    session.RevokedAtUtc = now >= session.IssuedAtUtc ? now : session.IssuedAtUtc;
                    session.RevokeReason = "AccountDisabled";
                    AddEvent(db, "SessionRevoked", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, session.Id,
                        new { reason = "AccountDisabled" });
                }
            }

            AddEvent(db, "AccountEligibilityChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, null,
                new { action = isActive ? "UserActivated" : "UserDeactivated", isActive });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);
        }, ct);


    public Task<OrganizationResult<UserDetails>> UnlockUser(
        long? organizationId,
        long userId,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => WriteAsync(access, async (db, _) =>
        {
            Organization? scopedOrg = null;
            if (organizationId is long orgId)
                scopedOrg = await LockOrganizationAsync(db, orgId, ct);

            var account = await LockUserByIdAsync(db, userId, ct);
            if (account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            if (organizationId is long orgScopeId)
            {
                if (scopedOrg is null)
                    return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

                var isolationError = await EnsureOrganizationExclusiveUserAsync(db, orgScopeId, userId, ct);
                if (isolationError is not null)
                    return OrganizationResult<UserDetails>.Failure(isolationError);

                if (!scopedOrg.IsActive)
                    return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ParentOrganizationInactive);
            }

            if (!MatchesRowVersion(db, account, expectedAccountVersion))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var now = UtcNow;
            if (account.LockoutEnd is null && account.AccessFailedCount == 0)
                return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);

            account.LockoutEnd = null;
            account.AccessFailedCount = 0;
            account.UpdatedAtUtc = SafeUpdatedAt(account.CreatedAtUtc, now);
            db.Entry(account).Property("RowVersion").OriginalValue = expectedAccountVersion;

            AddEvent(db, "AccountEligibilityChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, null,
                new { action = "UserUnlocked" });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);
        }, ct);


    public Task<OrganizationResult<UserDetails>> ResetUserPassword(
        long? organizationId,
        long userId,
        string newPassword,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct,
        bool requirePasswordChange = true)
        => WriteAsync(access, async (db, manager) =>
        {
            Organization? scopedOrg = null;
            if (organizationId is long orgId)
                scopedOrg = await LockOrganizationAsync(db, orgId, ct);

            var account = await LockUserByIdAsync(db, userId, ct);
            if (account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            if (organizationId is long orgScopeId)
            {
                if (scopedOrg is null)
                    return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

                var isolationError = await EnsureOrganizationExclusiveUserAsync(db, orgScopeId, userId, ct);
                if (isolationError is not null)
                    return OrganizationResult<UserDetails>.Failure(isolationError);

                if (!scopedOrg.IsActive)
                    return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ParentOrganizationInactive);
            }

            if (!MatchesRowVersion(db, account, expectedAccountVersion))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            foreach (var validator in manager.PasswordValidators)
            {
                var validation = await validator.ValidateAsync(manager, account, newPassword);
                if (!validation.Succeeded)
                    return MapIdentityFailure<UserDetails>(validation);
            }

            var now = UtcNow;
            account.PasswordHash = manager.PasswordHasher.HashPassword(account, newPassword);
            account.RequirePasswordChange = requirePasswordChange;
            account.UpdatedAtUtc = SafeUpdatedAt(account.CreatedAtUtc, now);
            db.Entry(account).Property("RowVersion").OriginalValue = expectedAccountVersion;

            var stampResult = await manager.UpdateSecurityStampAsync(account);
            if (!stampResult.Succeeded)
            {
                var code = stampResult.Errors.Any(e => e.Code == "ConcurrencyFailure")
                    ? UserManagementErrors.ConcurrencyConflict
                    : UserManagementErrors.PersistenceUnavailable;
                return OrganizationResult<UserDetails>.Failure(code);
            }

            var openSessions = await db.Sessions
                .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
                .ToListAsync(ct);

            foreach (var session in openSessions)
            {
                session.RevokedAtUtc = now >= session.IssuedAtUtc ? now : session.IssuedAtUtc;
                session.RevokeReason = "PasswordReset";
                AddEvent(db, "SessionRevoked", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, session.Id,
                    new { reason = "PasswordReset" });
            }

            AddEvent(db, "PasswordResetCompleted", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, null,
                new { action = "AdminPasswordReset" });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);
        }, ct);

    public async Task<OrganizationResult<UserDetails>> ResetUserTwoFactor(
        long? organizationId,
        long userId,
        string reason,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct)
    {
        var notifyIds = new List<long>();
        var targetName = string.Empty;
        var res = await WriteAsync(access, async (db, manager) =>
        {
            if (access.Actor.ActorId == userId)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.CannotResetOwnTwoFactor);

            Organization? scopedOrg = null;
            if (organizationId is long orgId)
                scopedOrg = await LockOrganizationAsync(db, orgId, ct);

            var account = await LockUserByIdAsync(db, userId, ct);
            if (account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            if (organizationId is long orgScopeId)
            {
                if (scopedOrg is null)
                    return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

                var isolationError = await EnsureOrganizationExclusiveUserAsync(db, orgScopeId, userId, ct);
                if (isolationError is not null)
                    return OrganizationResult<UserDetails>.Failure(isolationError);

                if (!scopedOrg.IsActive)
                    return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ParentOrganizationInactive);
            }

            if (!MatchesRowVersion(db, account, expectedAccountVersion))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var now = UtcNow;
            await manager.SetTwoFactorEnabledAsync(account, false);
            await manager.ResetAuthenticatorKeyAsync(account);
            await manager.RemoveAuthenticationTokenAsync(account, "[AspNetUserStore]", "RecoveryCodes");
            account.TwoFactorEnabledAtUtc = null;
            account.RequireTwoFactorSetup = true;
            account.UpdatedAtUtc = SafeUpdatedAt(account.CreatedAtUtc, now);
            db.Entry(account).Property("RowVersion").OriginalValue = expectedAccountVersion;

            var stampResult = await manager.UpdateSecurityStampAsync(account);
            if (!stampResult.Succeeded)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.PersistenceUnavailable);

            var openSessions = await db.Sessions
                .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
                .ToListAsync(ct);

            foreach (var session in openSessions)
            {
                session.RevokedAtUtc = now >= session.IssuedAtUtc ? now : session.IssuedAtUtc;
                session.RevokeReason = "TwoFactorReset";
                AddEvent(db, "SessionRevoked", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, session.Id,
                    new { reason = "TwoFactorReset" });
            }

            AddEvent(db, "TwoFactorReset", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, null,
                new { reason, targetUserName = account.UserName });

            await db.SaveChangesAsync(ct);

            targetName = account.UserName ?? string.Empty;
            notifyIds.Add(userId);
            if (organizationId is long orgIdNotify)
            {
                var targetIsAdmin = await db.ScopedRoleAssignments.AsNoTracking()
                    .AnyAsync(r => r.UserId == userId && r.OrganizationId == orgIdNotify && r.RoleId == FixedAccessRoles.OrganizationAdministrator && r.IsActive, ct);
                if (targetIsAdmin)
                {
                    var peerAdmins = await db.ScopedRoleAssignments.AsNoTracking()
                        .Where(r => r.OrganizationId == orgIdNotify && r.RoleId == FixedAccessRoles.OrganizationAdministrator && r.IsActive && r.UserId != userId && r.UserId != access.Actor.ActorId)
                        .Select(r => r.UserId)
                        .Distinct()
                        .ToListAsync(ct);
                    notifyIds.AddRange(peerAdmins);
                }
            }

            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);
        }, ct);

        if (res.Succeeded && notifications is not null)
        {
            foreach (var uid in notifyIds.Distinct())
            {
                await notifications.PublishAsync(
                    uid,
                    NotificationEventCatalog.NewDeviceSignIn,
                    NotificationCategory.Security,
                    NotificationSeverity.Danger,
                    NotificationTemplateCatalog.TwoFactorResetAlert,
                    new { user = targetName },
                    "/account/security?setup2fa=1",
                    ct);
            }
        }
        return res;
    }

    public Task<OrganizationResult<UserDetails>> RevokeUserSessions(
        long? organizationId,
        long userId,
        byte[] expectedAccountVersion,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => WriteAsync(access, async (db, manager) =>
        {
            Organization? scopedOrg = null;
            if (organizationId is long orgId)
                scopedOrg = await LockOrganizationAsync(db, orgId, ct);

            var account = await LockUserByIdAsync(db, userId, ct);
            if (account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            if (organizationId is long orgScopeId)
            {
                if (scopedOrg is null)
                    return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

                var isolationError = await EnsureOrganizationExclusiveUserAsync(db, orgScopeId, userId, ct);
                if (isolationError is not null)
                    return OrganizationResult<UserDetails>.Failure(isolationError);
            }

            if (!MatchesRowVersion(db, account, expectedAccountVersion))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var now = UtcNow;
            var openSessions = await db.Sessions
                .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
                .ToListAsync(ct);

            foreach (var session in openSessions)
            {
                session.RevokedAtUtc = now >= session.IssuedAtUtc ? now : session.IssuedAtUtc;
                session.RevokeReason = "AdminRevoked";
                AddEvent(db, "SessionRevoked", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, session.Id,
                    new { reason = "AdminRevoked" });
            }

            AddEvent(db, "SessionsRevokedByAdmin", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, null,
                new { count = openSessions.Count });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);
        }, ct);

    public async Task<OrganizationResult<UserDetails>> SetOrganizationMembershipActive(
        long organizationId,
        long userId,
        bool isActive,
        byte[] expectedMembershipVersion,
        OrganizationWriteAccess access,
        CancellationToken ct)
    {
        var changed = false;
        var scopeLabel = string.Empty;
        var res = await WriteAsync(access, async (db, _) =>
        {
            var org = await LockOrganizationAsync(db, organizationId, ct);
            var account = await LockUserByIdAsync(db, userId, ct);
            if (org is null || account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            var membership = await db.OrganizationMemberships
                .SingleOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId, ct);
            if (membership is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            if (!MatchesRowVersion(db, membership, expectedMembershipVersion))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var now = UtcNow;
            if (membership.IsActive == isActive)
                return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);

            if (isActive && !org.IsActive)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ParentOrganizationInactive);

            if (!isActive)
            {
                TenantAccessPolicy.EnsureNotSelfDeactivation(access.Actor.ActorId, userId);

                var isOrgAdmin = await db.ScopedRoleAssignments.AnyAsync(
                    r => r.UserId == userId
                         && r.OrganizationId == organizationId
                         && r.ScopeKind == 1
                         && r.RoleId == FixedAccessRoles.OrganizationAdministrator
                         && r.IsActive, ct);
                if (isOrgAdmin)
                {
                    var remainingAdmins = await CountActiveOrgAdminsExcludingAsync(db, organizationId, userId, ct);
                    TenantAccessPolicy.EnsureOrganizationRetainsActiveAdmin(org.IsActive, remainingAdmins);
                }
            }

            membership.IsActive = isActive;
            SetUpdateMetadata(db, membership, now, access.Actor.ActorId, expectedMembershipVersion);

            AddEvent(db, "MembershipChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, null, null,
                new { action = isActive ? "OrganizationMembershipActivated" : "OrganizationMembershipDeactivated", isActive });

            await db.SaveChangesAsync(ct);
            changed = true;
            scopeLabel = org.Name;
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);
        }, ct);

        if (res.Succeeded && changed && notifications is not null)
            await PublishAccessChangedAsync(userId, "OrganizationMembership", scopeLabel, ct);
        return res;
    }

    public async Task<OrganizationResult<UserDetails>> AssignSiteMembership(AssignSiteMembership request, OrganizationWriteAccess access, CancellationToken ct)
    {
        var scopeLabel = string.Empty;
        var res = await WriteAsync(access, async (db, _) =>
        {
            var org = await LockOrganizationAsync(db, request.OrganizationId, ct);
            var account = await LockUserByIdAsync(db, request.UserId, ct);
            if (org is null || account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            var membership = await db.OrganizationMemberships
                .SingleOrDefaultAsync(m => m.UserId == request.UserId && m.OrganizationId == request.OrganizationId, ct);
            if (membership is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            var site = await db.Set<Site>()
                .SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SiteId, ct);
            if (site is null)
                return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);

            TenantAccessPolicy.EnsureSiteMembershipPrerequisites(org.IsActive, site.IsActive, membership.IsActive);

            if (await db.SiteMemberships.AnyAsync(s => s.UserId == request.UserId && s.OrganizationId == request.OrganizationId && s.SiteId == request.SiteId, ct))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var now = UtcNow;
            var siteMembership = new SiteMembership
            {
                UserId = request.UserId,
                OrganizationId = request.OrganizationId,
                SiteId = request.SiteId,
                IsActive = true
            };
            db.SiteMemberships.Add(siteMembership);
            SetCreateMetadata(db, siteMembership, now, access.Actor.ActorId);

            if (request.AssignSiteOperator)
            {
                var role = new ScopedRoleAssignment
                {
                    UserId = request.UserId,
                    RoleId = FixedAccessRoles.SiteOperator,
                    ScopeKind = 2,
                    OrganizationId = request.OrganizationId,
                    SiteId = request.SiteId,
                    IsActive = true
                };
                db.ScopedRoleAssignments.Add(role);
                SetCreateMetadata(db, role, now, access.Actor.ActorId);
            }

            AddEvent(db, "MembershipChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, request.UserId, request.OrganizationId, request.SiteId, null,
                new { action = "SiteMembershipCreated", siteId = request.SiteId });
            if (request.AssignSiteOperator)
            {
                AddEvent(db, "ScopedRoleChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, request.UserId, request.OrganizationId, request.SiteId, null,
                    new { action = "Granted", role = nameof(TenantRoleKind.SiteOperator), siteId = request.SiteId });
            }

            await db.SaveChangesAsync(ct);
            scopeLabel = site.Name;
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, request.OrganizationId, request.UserId, now, ct))!);
        }, ct);

        if (res.Succeeded && notifications is not null)
        {
            await PublishAccessChangedAsync(
                request.UserId,
                request.AssignSiteOperator ? nameof(TenantRoleKind.SiteOperator) : "SiteMembership",
                scopeLabel,
                ct);
        }
        return res;
    }

    public async Task<OrganizationResult<UserDetails>> SetSiteMembershipActive(
        long organizationId,
        long userId,
        long siteId,
        bool isActive,
        byte[] expectedMembershipVersion,
        OrganizationWriteAccess access,
        CancellationToken ct)
    {
        var changed = false;
        var scopeLabel = string.Empty;
        var res = await WriteAsync(access, async (db, _) =>
        {
            var org = await LockOrganizationAsync(db, organizationId, ct);
            var account = await LockUserByIdAsync(db, userId, ct);
            if (org is null || account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            var orgMembership = await db.OrganizationMemberships
                .SingleOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId, ct);
            if (orgMembership is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            var site = await db.Set<Site>().SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == siteId, ct);
            if (site is null)
                return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);

            var siteMembership = await db.SiteMemberships
                .SingleOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId && m.SiteId == siteId, ct);
            if (siteMembership is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            if (!MatchesRowVersion(db, siteMembership, expectedMembershipVersion))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var now = UtcNow;
            if (siteMembership.IsActive == isActive)
                return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);

            if (isActive)
                TenantAccessPolicy.EnsureSiteMembershipPrerequisites(org.IsActive, site.IsActive, orgMembership.IsActive);

            siteMembership.IsActive = isActive;
            SetUpdateMetadata(db, siteMembership, now, access.Actor.ActorId, expectedMembershipVersion);

            AddEvent(db, "MembershipChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, siteId, null,
                new { action = isActive ? "SiteMembershipActivated" : "SiteMembershipDeactivated", siteId, isActive });

            await db.SaveChangesAsync(ct);
            changed = true;
            scopeLabel = site.Name;
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);
        }, ct);

        if (res.Succeeded && changed && notifications is not null)
            await PublishAccessChangedAsync(userId, "SiteMembership", scopeLabel, ct);
        return res;
    }

    public async Task<OrganizationResult<UserDetails>> AssignTenantRole(AssignTenantRole request, OrganizationWriteAccess access, CancellationToken ct)
    {
        var scopeLabel = string.Empty;
        var res = await WriteAsync(access, async (db, _) =>
        {
            TenantAccessPolicy.ValidateTenantRoleScope(request.Role, request.OrganizationId, request.SiteId);

            var org = await LockOrganizationAsync(db, request.OrganizationId, ct);
            var account = await LockUserByIdAsync(db, request.UserId, ct);
            if (org is null || account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            var orgMembership = await db.OrganizationMemberships
                .SingleOrDefaultAsync(m => m.UserId == request.UserId && m.OrganizationId == request.OrganizationId, ct);
            if (orgMembership is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            scopeLabel = org.Name;
            var siteIsActive = false;
            var siteMembershipIsActive = false;
            if (request.Role == TenantRoleKind.SiteOperator)
            {
                var site = await db.Set<Site>().SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SiteId!.Value, ct);
                if (site is null)
                    return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);

                var siteMembership = await db.SiteMemberships
                    .SingleOrDefaultAsync(m => m.UserId == request.UserId && m.OrganizationId == request.OrganizationId && m.SiteId == request.SiteId!.Value, ct);
                siteIsActive = site.IsActive;
                siteMembershipIsActive = siteMembership?.IsActive == true;
                scopeLabel = site.Name;
            }

            TenantAccessPolicy.EnsureTenantRolePrerequisites(request.Role, org.IsActive, orgMembership.IsActive, siteIsActive, siteMembershipIsActive);

            var roleId = (byte)request.Role;
            var scopeKind = request.Role == TenantRoleKind.OrganizationAdministrator ? (byte)1 : (byte)2;
            var exists = await db.ScopedRoleAssignments.AnyAsync(
                r => r.UserId == request.UserId
                     && r.RoleId == roleId
                     && r.ScopeKind == scopeKind
                     && r.OrganizationId == request.OrganizationId
                     && r.SiteId == request.SiteId, ct);
            if (exists)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var now = UtcNow;
            var assignment = new ScopedRoleAssignment
            {
                UserId = request.UserId,
                RoleId = roleId,
                ScopeKind = scopeKind,
                OrganizationId = request.OrganizationId,
                SiteId = request.SiteId,
                IsActive = true
            };
            db.ScopedRoleAssignments.Add(assignment);
            SetCreateMetadata(db, assignment, now, access.Actor.ActorId);

            AddEvent(db, "ScopedRoleChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, request.UserId, request.OrganizationId, request.SiteId, null,
                new { action = "Granted", role = request.Role.ToString(), siteId = request.SiteId });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, request.OrganizationId, request.UserId, now, ct))!);
        }, ct);

        if (res.Succeeded && notifications is not null)
        {
            await notifications.PublishAsync(
                request.UserId,
                NotificationEventCatalog.RoleOrAccessChanged,
                NotificationCategory.Security,
                NotificationSeverity.Info,
                NotificationTemplateCatalog.RoleOrAccessChanged,
                new { role = request.Role.ToString(), scope = scopeLabel },
                "/account/activity",
                ct);
        }
        return res;
    }

    public async Task<OrganizationResult<UserDetails>> SetTenantRoleActive(
        long organizationId,
        long userId,
        TenantRoleKind role,
        long? siteId,
        bool isActive,
        byte[] expectedRoleVersion,
        OrganizationWriteAccess access,
        CancellationToken ct)
    {
        var changed = false;
        var scopeLabel = string.Empty;
        var res = await WriteAsync(access, async (db, _) =>
        {
            TenantAccessPolicy.ValidateTenantRoleScope(role, organizationId, siteId);

            var org = await LockOrganizationAsync(db, organizationId, ct);
            var account = await LockUserByIdAsync(db, userId, ct);
            if (org is null || account is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            var orgMembership = await db.OrganizationMemberships
                .SingleOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId, ct);
            if (orgMembership is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            var roleId = (byte)role;
            var scopeKind = role == TenantRoleKind.OrganizationAdministrator ? (byte)1 : (byte)2;
            var assignment = await db.ScopedRoleAssignments.SingleOrDefaultAsync(
                r => r.UserId == userId
                     && r.RoleId == roleId
                     && r.ScopeKind == scopeKind
                     && r.OrganizationId == organizationId
                     && r.SiteId == siteId, ct);
            if (assignment is null)
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);

            if (!MatchesRowVersion(db, assignment, expectedRoleVersion))
                return OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);

            var now = UtcNow;
            if (assignment.IsActive == isActive)
                return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);

            scopeLabel = org.Name;
            if (role == TenantRoleKind.SiteOperator && siteId.HasValue)
            {
                var site = await db.Set<Site>().SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == siteId.Value, ct);
                if (site is not null)
                    scopeLabel = site.Name;

                if (isActive)
                {
                    if (site is null)
                        return OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
                    var siteMembership = await db.SiteMemberships
                        .SingleOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId && m.SiteId == siteId.Value, ct);
                    TenantAccessPolicy.EnsureTenantRolePrerequisites(role, org.IsActive, orgMembership.IsActive, site.IsActive, siteMembership?.IsActive == true);
                }
            }
            else if (isActive)
            {
                TenantAccessPolicy.EnsureTenantRolePrerequisites(role, org.IsActive, orgMembership.IsActive, false, false);
            }
            else if (role == TenantRoleKind.OrganizationAdministrator)
            {
                TenantAccessPolicy.EnsureNotSelfDeactivation(access.Actor.ActorId, userId);
                var remainingAdmins = await CountActiveOrgAdminsExcludingAsync(db, organizationId, userId, ct);
                TenantAccessPolicy.EnsureOrganizationRetainsActiveAdmin(org.IsActive, remainingAdmins);
            }

            assignment.IsActive = isActive;
            SetUpdateMetadata(db, assignment, now, access.Actor.ActorId, expectedRoleVersion);

            AddEvent(db, "ScopedRoleChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, siteId, null,
                new { action = isActive ? "Activated" : "Deactivated", role = role.ToString(), siteId, isActive });

            await db.SaveChangesAsync(ct);
            changed = true;
            return OrganizationResult<UserDetails>.Success((await BuildUserDetailsAsync(db, organizationId, userId, now, ct))!);
        }, ct);

        if (res.Succeeded && changed && notifications is not null)
            await PublishAccessChangedAsync(userId, role.ToString(), scopeLabel, ct);
        return res;
    }

    private Task PublishAccessChangedAsync(long userId, string role, string scope, CancellationToken ct)
        => notifications!.PublishAsync(
            userId,
            NotificationEventCatalog.RoleOrAccessChanged,
            NotificationCategory.Security,
            NotificationSeverity.Info,
            NotificationTemplateCatalog.RoleOrAccessChanged,
            new { role, scope },
            "/account/activity",
            ct);

    private async Task<T> ReadAsync<T>(Func<AuthDbContext, Task<T>> query, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            return await query(db);
        }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        {
            throw new UserStoreUnavailableException(ex);
        }
    }

    private async Task<OrganizationResult<T>> WriteAsync<T>(
        OrganizationWriteAccess access,
        Func<AuthDbContext, UserManager<ApplicationAccount>, Task<OrganizationResult<T>>> operation,
        CancellationToken ct)
    {
        try
        {
            await using var strategyScope = scopes.CreateAsyncScope();
            var strategyContext = strategyScope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                if (!await access.Revalidate(ct))
                    return OrganizationResult<T>.Failure(UserManagementErrors.AccessDenied);

                var commitAttempted = false;
                try
                {
                    await using var opScope = scopes.CreateAsyncScope();
                    var db = opScope.ServiceProvider.GetRequiredService<AuthDbContext>();
                    var manager = opScope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
                    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                    var result = await operation(db, manager);
                    if (!result.Succeeded)
                        return result;

                    commitAttempted = true;
                    await tx.CommitAsync(ct);
                    return result;
                }
                catch (Exception ex) when (commitAttempted && ex is not UserCommitOutcomeUnknownException)
                {
                    throw new UserCommitOutcomeUnknownException(ex);
                }
            });
        }
        catch (UserRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(UserManagementErrors.ConcurrencyConflict); }
        catch (UserCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(UserManagementErrors.OutcomeUnknown); }
        catch (DbUpdateException ex) when (IsUniqueUserNameViolation(ex))
        { return OrganizationResult<T>.Failure(UserManagementErrors.UserNameDuplicate); }
        catch (DbUpdateException ex) when (IsUniqueMembershipOrRoleViolation(ex))
        { return OrganizationResult<T>.Failure(UserManagementErrors.ConcurrencyConflict); }
        catch (DbUpdateException ex) when (IsAuditWriteFailure(ex))
        { return OrganizationResult<T>.Failure(UserManagementErrors.AuditUnavailable); }
        catch (SqlException sql) when (IsAuditSqlFailure(sql))
        { return OrganizationResult<T>.Failure(UserManagementErrors.AuditUnavailable); }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        { return OrganizationResult<T>.Failure(UserManagementErrors.PersistenceUnavailable); }
    }

    private static Task<Organization?> LockOrganizationAsync(AuthDbContext db, long organizationId, CancellationToken ct)
        => db.Set<Organization>()
            .FromSqlInterpolated($"SELECT * FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={organizationId}")
            .SingleOrDefaultAsync(ct);

    private static Task<ApplicationAccount?> LockUserByIdAsync(AuthDbContext db, long userId, CancellationToken ct)
        => db.Users
            .FromSqlInterpolated($"SELECT * FROM [auth].[User] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={userId}")
            .SingleOrDefaultAsync(ct);

    private static Task<ApplicationAccount?> LockUserByNormalizedNameAsync(AuthDbContext db, string normalizedUserName, CancellationToken ct)
        => db.Users
            .FromSqlInterpolated($"SELECT * FROM [auth].[User] WITH (UPDLOCK, HOLDLOCK) WHERE [NormalizedUserName]={normalizedUserName}")
            .SingleOrDefaultAsync(ct);

    private static async Task<IReadOnlyList<long>> ResolveOrganizationLocksAsync(AuthDbContext db, long? organizationId, long userId, CancellationToken ct)
    {
        if (organizationId is long orgId)
            return [orgId];

        return await db.OrganizationMemberships
            .Where(m => m.UserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct);
    }

    private static async Task<string?> EnsureOrganizationExclusiveUserAsync(AuthDbContext db, long organizationId, long userId, CancellationToken ct)
    {
        var hasScopedMembership = await db.OrganizationMemberships
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId, ct);
        if (!hasScopedMembership)
            return UserManagementErrors.NotFound;

        var hasOtherMembership = await db.OrganizationMemberships
            .AnyAsync(m => m.UserId == userId && m.OrganizationId != organizationId, ct);
        var hasPlatformGrant = await db.ScopedRoleAssignments
            .AnyAsync(r => r.UserId == userId && r.ScopeKind == 0, ct);

        return hasOtherMembership || hasPlatformGrant ? UserManagementErrors.AccessDenied : null;
    }

    private static async Task<string?> ValidateActiveSitesAsync(
        AuthDbContext db,
        long organizationId,
        IReadOnlyList<long> distinctSiteIds,
        CancellationToken ct)
    {
        if (distinctSiteIds.Count == 0)
            return null;

        var sites = await db.Set<Site>()
            .AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && distinctSiteIds.Contains(s.Id))
            .Select(s => new { s.Id, s.IsActive })
            .ToListAsync(ct);

        if (sites.Count != distinctSiteIds.Count)
            return OrganizationSiteErrors.SiteNotFound;

        return sites.Any(s => !s.IsActive) ? UserManagementErrors.ParentSiteInactive : null;
    }

    private static void AddSiteMembershipsAndRoles(
        AuthDbContext db,
        long userId,
        long organizationId,
        IReadOnlyList<long> distinctSiteIds,
        bool assignSiteOperator,
        DateTime now,
        long actorId)
    {
        foreach (var siteId in distinctSiteIds)
        {
            var siteMembership = new SiteMembership
            {
                UserId = userId,
                OrganizationId = organizationId,
                SiteId = siteId,
                IsActive = true
            };
            db.SiteMemberships.Add(siteMembership);
            SetCreateMetadata(db, siteMembership, now, actorId);

            if (assignSiteOperator)
            {
                var siteRole = new ScopedRoleAssignment
                {
                    UserId = userId,
                    RoleId = FixedAccessRoles.SiteOperator,
                    ScopeKind = 2,
                    OrganizationId = organizationId,
                    SiteId = siteId,
                    IsActive = true
                };
                db.ScopedRoleAssignments.Add(siteRole);
                SetCreateMetadata(db, siteRole, now, actorId);
            }
        }
    }

    private static Task<int> CountActiveOrgAdminsExcludingAsync(AuthDbContext db, long organizationId, long excludedUserId, CancellationToken ct)
        => (from r in db.ScopedRoleAssignments
            join m in db.OrganizationMemberships on new { r.UserId, OrganizationId = r.OrganizationId!.Value } equals new { m.UserId, m.OrganizationId }
            join u in db.Users on r.UserId equals u.Id
            where r.RoleId == FixedAccessRoles.OrganizationAdministrator
                  && r.ScopeKind == 1
                  && r.OrganizationId == organizationId
                  && r.IsActive
                  && m.IsActive
                  && u.IsActive
                  && r.UserId != excludedUserId
            select r.UserId)
            .Distinct()
            .CountAsync(ct);

    private static Task<int> CountActiveProvisionersExcludingAsync(AuthDbContext db, long excludedUserId, CancellationToken ct)
        => (from r in db.ScopedRoleAssignments
            join u in db.Users on r.UserId equals u.Id
            where r.RoleId == FixedAccessRoles.DeploymentProvisioner
                  && r.ScopeKind == 0
                  && r.IsActive
                  && u.IsActive
                  && r.UserId != excludedUserId
            select r.UserId)
            .Distinct()
            .CountAsync(ct);

    private static async Task<UserDetails?> BuildUserDetailsAsync(
        AuthDbContext db,
        long? organizationId,
        long userId,
        DateTime utcNow,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.IsActive,
                u.LockoutEnabled,
                u.LockoutEnd,
                u.PreferredCulture,
                u.SecurityStamp,
                u.TwoFactorEnabled,
                u.TwoFactorEnabledAtUtc,
                RowVersion = EF.Property<byte[]>(u, "RowVersion")
            })
            .SingleOrDefaultAsync(ct);
        if (user is null)
            return null;

        var totalOrgCount = await db.OrganizationMemberships.AsNoTracking()
            .CountAsync(m => m.UserId == userId, ct);
        var hasPlatformGrant = await db.ScopedRoleAssignments.AsNoTracking()
            .AnyAsync(r => r.UserId == userId && r.ScopeKind == 0, ct);
        var belongsToSingleOrg = totalOrgCount == 1 && !hasPlatformGrant;

        var accountVersion = Convert.ToBase64String(user.RowVersion);
        var locked = user.LockoutEnabled && user.LockoutEnd > new DateTimeOffset(utcNow);
        var activeSessions = await db.Sessions.AsNoTracking()
            .CountAsync(s => s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > utcNow && s.SecurityStampSnapshot == user.SecurityStamp, ct);
        var recoveryRaw = user.TwoFactorEnabled
            ? await db.UserTokens.AsNoTracking()
                .Where(t => t.UserId == userId && t.LoginProvider == "[AspNetUserStore]" && t.Name == "RecoveryCodes")
                .Select(t => t.Value)
                .SingleOrDefaultAsync(ct)
            : null;
        var recoveryCount = string.IsNullOrWhiteSpace(recoveryRaw)
            ? 0
            : recoveryRaw.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;

        if (organizationId is not long orgId)
        {
            return new UserDetails(
                user.Id,
                0,
                user.UserName ?? string.Empty,
                user.IsActive,
                false,
                locked,
                user.PreferredCulture,
                belongsToSingleOrg,
                false,
                null,
                accountVersion,
                string.Empty,
                [],
                user.TwoFactorEnabled,
                user.TwoFactorEnabledAtUtc.HasValue ? DateTime.SpecifyKind(user.TwoFactorEnabledAtUtc.Value, DateTimeKind.Utc) : null,
                recoveryCount,
                activeSessions);
        }

        var orgMembership = await db.OrganizationMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.OrganizationId == orgId)
            .Select(m => new { m.IsActive, RowVersion = EF.Property<byte[]>(m, "RowVersion") })
            .SingleOrDefaultAsync(ct);
        if (orgMembership is null)
            return null;

        var orgAdminRole = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(r => r.UserId == userId
                        && r.RoleId == FixedAccessRoles.OrganizationAdministrator
                        && r.ScopeKind == 1
                        && r.OrganizationId == orgId)
            .Select(r => new { r.IsActive, RowVersion = EF.Property<byte[]>(r, "RowVersion") })
            .SingleOrDefaultAsync(ct);

        var siteRows = await (from sm in db.SiteMemberships.AsNoTracking()
                              join s in db.Set<Site>().AsNoTracking()
                                  on new { sm.OrganizationId, sm.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                              where sm.UserId == userId && sm.OrganizationId == orgId
                              orderby s.Code, s.Id
                              select new
                              {
                                  s.Id,
                                  s.Code,
                                  s.Name,
                                  SiteIsActive = s.IsActive,
                                  MembershipIsActive = sm.IsActive,
                                  RowVersion = EF.Property<byte[]>(sm, "RowVersion")
                              })
            .ToListAsync(ct);

        var siteRoles = await db.ScopedRoleAssignments.AsNoTracking()
            .Where(r => r.UserId == userId
                        && r.RoleId == FixedAccessRoles.SiteOperator
                        && r.ScopeKind == 2
                        && r.OrganizationId == orgId
                        && r.SiteId != null)
            .Select(r => new { SiteId = r.SiteId!.Value, r.IsActive, RowVersion = EF.Property<byte[]>(r, "RowVersion") })
            .ToDictionaryAsync(r => r.SiteId, ct);

        var sites = siteRows.Select(x =>
        {
            siteRoles.TryGetValue(x.Id, out var role);
            return new UserSiteMembershipDetails(
                x.Id,
                x.Code,
                x.Name,
                x.SiteIsActive,
                x.MembershipIsActive,
                role?.IsActive == true,
                Convert.ToBase64String(x.RowVersion),
                role is null ? null : Convert.ToBase64String(role.RowVersion));
        }).ToArray();

        return new UserDetails(
            user.Id,
            orgId,
            user.UserName ?? string.Empty,
            user.IsActive,
            orgMembership.IsActive,
            locked,
            user.PreferredCulture,
            belongsToSingleOrg,
            orgAdminRole?.IsActive == true,
            orgAdminRole is null ? null : Convert.ToBase64String(orgAdminRole.RowVersion),
            accountVersion,
            Convert.ToBase64String(orgMembership.RowVersion),
            sites,
            user.TwoFactorEnabled,
            user.TwoFactorEnabledAtUtc.HasValue ? DateTime.SpecifyKind(user.TwoFactorEnabledAtUtc.Value, DateTimeKind.Utc) : null,
            recoveryCount,
            activeSessions);
    }

    private static bool MatchesRowVersion(AuthDbContext db, object entity, byte[] expected)
    {
        var current = (byte[]?)db.Entry(entity).Property("RowVersion").CurrentValue;
        return current is not null && current.SequenceEqual(expected);
    }

    private static DateTime SafeUpdatedAt(DateTime createdAtUtc, DateTime now)
        => now >= createdAtUtc ? now : createdAtUtc;

    private static void SetCreateMetadata(AuthDbContext db, object entity, DateTime now, long actorId)
    {
        var entry = db.Entry(entity);
        entry.Property("CreatedAtUtc").CurrentValue = now;
        entry.Property("CreatedByActorId").CurrentValue = actorId;
    }

    private static void SetUpdateMetadata(AuthDbContext db, object entity, DateTime now, long actorId, byte[] expectedRowVersion)
    {
        var entry = db.Entry(entity);
        var created = (DateTime)entry.Property("CreatedAtUtc").CurrentValue!;
        entry.Property("UpdatedAtUtc").CurrentValue = SafeUpdatedAt(created, now);
        entry.Property("UpdatedByActorId").CurrentValue = actorId;
        entry.Property("RowVersion").OriginalValue = expectedRowVersion;
    }

    private static void AddEvent(
        AuthDbContext db,
        string code,
        DateTime occurredAtUtc,
        Guid correlationId,
        long actorUserId,
        long? subjectUserId,
        long? organizationId,
        long? siteId,
        Guid? sessionId,
        object payload)
    {
        db.SecurityEvents.Add(new SecurityEvent
        {
            EventCode = code,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId,
            ActorKind = 1,
            ActorUserId = actorUserId,
            SubjectUserId = subjectUserId,
            SessionId = sessionId,
            OrganizationId = organizationId,
            SiteId = siteId,
            Outcome = 0,
            DataJson = JsonSerializer.Serialize(payload)
        });
    }

    private static OrganizationResult<T> MapIdentityFailure<T>(IdentityResult result)
    {
        var codes = result.Errors.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);
        if (codes.Contains("DuplicateUserName"))
            return OrganizationResult<T>.Failure(UserManagementErrors.UserNameDuplicate);
        if (codes.Contains("InvalidUserName"))
            return OrganizationResult<T>.Failure(UserManagementErrors.UserNameInvalid, new Dictionary<string, object> { ["field"] = "UserName", ["maxLength"] = UserAccountName.MaxLength });
        return OrganizationResult<T>.Failure(UserManagementErrors.PasswordInvalid, new Dictionary<string, object> { ["field"] = "InitialPassword" });
    }

    private static bool IsUniqueUserNameViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql
           && sql.Number is 2601 or 2627
           && sql.Message.Contains("UX_User_NormalizedUserName", StringComparison.Ordinal);

    private static bool IsUniqueMembershipOrRoleViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql
           && sql.Number is 2601 or 2627
           && !sql.Message.Contains("UX_User_NormalizedUserName", StringComparison.Ordinal);

    private static bool IsAuditWriteFailure(DbUpdateException ex)
        => ex.Entries.Any(e => e.Entity is SecurityEvent)
           || (ex.InnerException is SqlException sql && IsAuditSqlFailure(sql));

    private static bool IsAuditSqlFailure(SqlException sql)
        => sql.Number == 229 || sql.Number >= 50000;

    private static bool IsTransientUnavailable(Exception ex) => ex switch
    {
        SqlException sql => sql.Number is -2 or 53 or 64 or 233 or 1205 or 4060 or 18456 or 40197 or 40501 or 40613,
        TimeoutException => true,
        DbUpdateException or RetryLimitExceededException when ex.InnerException is not null => IsTransientUnavailable(ex.InnerException),
        DbException => true,
        _ => false
    };
}
