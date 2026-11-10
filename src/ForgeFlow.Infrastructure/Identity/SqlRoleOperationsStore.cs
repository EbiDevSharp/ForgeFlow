using System.Data;
using System.Data.Common;
using System.Text.Json;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Identity;

internal sealed class RoleCommitOutcomeUnknownException(Exception inner) : Exception(RolePermissionErrors.OutcomeUnknown, inner);

/// <summary>SQL Server implementation of <see cref="IRoleOperationsStore"/> with atomic security events and tenant isolation.</summary>
public sealed class SqlRoleOperationsStore(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOrganizationPermissionEvaluator? callerPermissions = null) : IRoleOperationsStore
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public Task<OrganizationPage<RoleSummary>> ListOrganizationRoles(long organizationId, RoleListQuery query, CancellationToken ct)
        => ReadAsync(async db =>
        {
            var rows = db.RoleDefinitions.AsNoTracking()
                .Where(r => (r.IsSystem && r.ScopeKind != 0) || r.OrganizationId == organizationId);

            if (!query.IncludeSystem)
                rows = rows.Where(r => !r.IsSystem);
            if (!query.IncludeInactive)
                rows = rows.Where(r => r.IsActive);
            if (!string.IsNullOrEmpty(query.Search))
                rows = rows.Where(r => r.Name.Contains(query.Search));

            var total = await rows.CountAsync(ct);
            var pageRows = await rows
                .OrderByDescending(r => r.IsSystem)
                .ThenBy(r => r.Name)
                .ThenBy(r => r.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(r => new
                {
                    r.Id,
                    r.OrganizationId,
                    r.Name,
                    r.IsSystem,
                    r.ScopeKind,
                    r.IsActive,
                    RowVersion = EF.Property<byte[]>(r, "RowVersion")
                })
                .ToListAsync(ct);

            if (pageRows.Count == 0)
                return new OrganizationPage<RoleSummary>([], total, query.Page, query.PageSize);

            var roleIds = pageRows.Select(r => r.Id).ToArray();
            var counts = await db.RolePermissions.AsNoTracking()
                .Where(p => roleIds.Contains(p.RoleId) && p.IsActive)
                .GroupBy(p => p.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);
            var orgAdminHasAuditRow = roleIds.Contains(FixedAccessRoles.OrganizationAdministrator)
                && await db.RolePermissions.AsNoTracking().AnyAsync(
                    p => p.RoleId == FixedAccessRoles.OrganizationAdministrator && p.IsActive && p.PermissionCode == AuditPermissions.AuditRead, ct);

            var items = pageRows.Select(r => new RoleSummary(
                r.Id,
                r.OrganizationId,
                r.Name,
                r.IsSystem,
                (RoleScopeTarget)r.ScopeKind,
                r.IsActive,
                counts.GetValueOrDefault(r.Id) + (r.Id == FixedAccessRoles.OrganizationAdministrator && !orgAdminHasAuditRow ? 1 : 0),
                Convert.ToBase64String(r.RowVersion))).ToArray();

            return new OrganizationPage<RoleSummary>(items, total, query.Page, query.PageSize);
        }, ct);

    public Task<RoleDetails?> GetOrganizationRole(long organizationId, long roleId, CancellationToken ct)
        => ReadAsync(db => BuildRoleDetailsAsync(db, organizationId, roleId, ct), ct);

    public Task<OrganizationResult<RoleDetails>> CreateOrganizationRole(
        long organizationId,
        RoleDefinitionName canonicalName,
        RoleScopeTarget targetScope,
        IReadOnlyList<string> permissionCodes,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => WriteAsync(access, async db =>
        {
            var org = await LockOrganizationAsync(db, organizationId, ct);
            if (org is null)
                return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound);
            if (!org.IsActive)
                return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.ParentOrganizationInactive);

            var duplicate = await db.RoleDefinitions.AnyAsync(
                r => !r.IsSystem && r.OrganizationId == organizationId && r.NormalizedName == canonicalName.Normalized, ct);
            if (duplicate)
                return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NameDuplicate);

            var now = UtcNow;
            var definition = new RoleDefinition
            {
                Name = canonicalName.Value,
                NormalizedName = canonicalName.Normalized,
                IsSystem = false,
                ScopeKind = (byte)targetScope,
                OrganizationId = organizationId,
                IsActive = true
            };
            db.RoleDefinitions.Add(definition);
            SetCreateMetadata(db, definition, now, access.Actor.ActorId);

            await db.SaveChangesAsync(ct);

            foreach (var code in permissionCodes)
            {
                var perm = new RolePermission
                {
                    RoleId = definition.Id,
                    PermissionCode = code,
                    IsActive = true
                };
                db.RolePermissions.Add(perm);
                SetCreateMetadata(db, perm, now, access.Actor.ActorId);
            }

            AddEvent(db, "RoleDefinitionChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, null, organizationId, null,
                new { action = "RoleCreated", roleId = definition.Id, targetScope = targetScope.ToString(), permissions = permissionCodes });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<RoleDetails>.Success((await BuildRoleDetailsAsync(db, organizationId, definition.Id, ct))!);
        }, ct);

    public Task<OrganizationResult<RoleDetails>> UpdateOrganizationRole(
        long organizationId,
        long roleId,
        RoleDefinitionName canonicalName,
        bool isActive,
        IReadOnlyList<string> permissionCodes,
        byte[] expectedVersion,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => WriteAsync(access, async db =>
        {
            var org = await LockOrganizationAsync(db, organizationId, ct);
            if (org is null)
                return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound);
            if (!org.IsActive)
                return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.ParentOrganizationInactive);

            var definition = await LockRoleDefinitionAsync(db, roleId, ct);
            if (definition is null || (!definition.IsSystem && definition.OrganizationId != organizationId))
                return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound);

            PermissionCatalog.EnsureCustomRoleMutable(definition.Id, definition.IsSystem);

            if (!MatchesRowVersion(db, definition, expectedVersion))
                return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.ConcurrencyConflict);

            var validatedCodes = PermissionCatalog.ValidateTenantPermissions(definition.ScopeKind, permissionCodes);

            var nameConflict = await db.RoleDefinitions.AnyAsync(
                r => !r.IsSystem && r.OrganizationId == organizationId && r.Id != roleId && r.NormalizedName == canonicalName.Normalized, ct);
            if (nameConflict)
                return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NameDuplicate);

            var existingPermissions = await db.RolePermissions
                .Where(p => p.RoleId == roleId)
                .ToListAsync(ct);

            var activeSet = existingPermissions.Where(p => p.IsActive).Select(p => p.PermissionCode).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var targetSet = validatedCodes.OrderBy(x => x, StringComparer.Ordinal).ToArray();

            var now = UtcNow;
            if (definition.Name == canonicalName.Value
                && definition.NormalizedName == canonicalName.Normalized
                && definition.IsActive == isActive
                && activeSet.SequenceEqual(targetSet, StringComparer.Ordinal))
            {
                return OrganizationResult<RoleDetails>.Success((await BuildRoleDetailsAsync(db, organizationId, roleId, ct))!);
            }

            definition.Name = canonicalName.Value;
            definition.NormalizedName = canonicalName.Normalized;
            definition.IsActive = isActive;
            SetUpdateMetadata(db, definition, now, access.Actor.ActorId, expectedVersion);

            var targetHash = targetSet.ToHashSet(StringComparer.Ordinal);
            var existingMap = existingPermissions.ToDictionary(p => p.PermissionCode, StringComparer.Ordinal);

            foreach (var existing in existingPermissions)
            {
                var desiredActive = targetHash.Contains(existing.PermissionCode);
                if (existing.IsActive != desiredActive)
                {
                    existing.IsActive = desiredActive;
                    SetUpdateMetadata(db, existing, now, access.Actor.ActorId, null);
                }
            }

            foreach (var code in targetSet)
            {
                if (!existingMap.ContainsKey(code))
                {
                    var added = new RolePermission
                    {
                        RoleId = roleId,
                        PermissionCode = code,
                        IsActive = true
                    };
                    db.RolePermissions.Add(added);
                    SetCreateMetadata(db, added, now, access.Actor.ActorId);
                }
            }

            AddEvent(db, "RoleDefinitionChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, null, organizationId, null,
                new { action = "RoleUpdated", roleId, isActive, permissions = targetSet });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<RoleDetails>.Success((await BuildRoleDetailsAsync(db, organizationId, roleId, ct))!);
        }, ct);

    public Task<UserPermissionMatrix?> GetUserPermissions(long organizationId, long userId, CancellationToken ct)
        => ReadAsync(db => BuildUserMatrixAsync(db, organizationId, userId, ct), ct);

    public Task<OrganizationResult<UserPermissionMatrix>> AssignUserRole(AssignUserRole request, OrganizationWriteAccess access, CancellationToken ct)
        => WriteAsync(access, async db =>
        {
            var org = await LockOrganizationAsync(db, request.OrganizationId, ct);
            var account = await LockUserAsync(db, request.UserId, ct);
            if (org is null || account is null)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

            var orgMembership = await db.OrganizationMemberships
                .SingleOrDefaultAsync(m => m.UserId == request.UserId && m.OrganizationId == request.OrganizationId, ct);
            if (orgMembership is null)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

            var role = await LockRoleDefinitionAsync(db, request.RoleId, ct);
            if (role is null || (!role.IsSystem && role.OrganizationId != request.OrganizationId))
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

            if (role.ScopeKind == 0 || role.ScopeKind != (byte)request.TargetScope)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.PermissionScopeInvalid, new Dictionary<string, object> { ["field"] = "TargetScope" });

            if (!org.IsActive)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ParentOrganizationInactive);
            if (!orgMembership.IsActive)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.MembershipRequired);
            if (!role.IsActive)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

            if (request.TargetScope == RoleScopeTarget.Site)
            {
                var site = await db.Set<Site>().SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Id == request.SiteId!.Value, ct);
                if (site is null)
                    return OrganizationResult<UserPermissionMatrix>.Failure(OrganizationSiteErrors.SiteNotFound);
                if (!site.IsActive)
                    return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ParentSiteInactive);

                var siteMembership = await db.SiteMemberships
                    .SingleOrDefaultAsync(m => m.UserId == request.UserId && m.OrganizationId == request.OrganizationId && m.SiteId == request.SiteId!.Value && m.IsActive, ct);
                if (siteMembership is null)
                    return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.MembershipRequired);
            }

            if (callerPermissions is not null)
            {
                var rolePermCodes = await db.RolePermissions.AsNoTracking()
                    .Where(p => p.RoleId == role.Id && p.IsActive)
                    .Select(p => p.PermissionCode)
                    .ToListAsync(ct);

                OrganizationScope evalScope = request.TargetScope == RoleScopeTarget.Organization
                    ? new OrganizationScope.Organization(request.OrganizationId)
                    : new OrganizationScope.Site(request.OrganizationId, request.SiteId!.Value);

                foreach (var code in rolePermCodes)
                {
                    if (!(await callerPermissions.EvaluateAsync(access.Actor, evalScope, code, ct)).IsAllowed)
                        return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.EscalationForbidden);
                }
            }

            var existing = await db.ScopedRoleAssignments.SingleOrDefaultAsync(
                r => r.UserId == request.UserId
                     && r.RoleId == request.RoleId
                     && r.ScopeKind == (byte)request.TargetScope
                     && r.OrganizationId == request.OrganizationId
                     && r.SiteId == request.SiteId, ct);
            if (existing is not null)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ConcurrencyConflict);

            var now = UtcNow;
            var assignment = new ScopedRoleAssignment
            {
                UserId = request.UserId,
                RoleId = request.RoleId,
                ScopeKind = (byte)request.TargetScope,
                OrganizationId = request.OrganizationId,
                SiteId = request.SiteId,
                IsActive = true
            };
            db.ScopedRoleAssignments.Add(assignment);
            SetCreateMetadata(db, assignment, now, access.Actor.ActorId);

            AddEvent(db, "ScopedRoleChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, request.UserId, request.OrganizationId, request.SiteId,
                new { action = "Granted", roleId = request.RoleId, targetScope = request.TargetScope.ToString(), siteId = request.SiteId });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserPermissionMatrix>.Success((await BuildUserMatrixAsync(db, request.OrganizationId, request.UserId, ct))!);
        }, ct);

    public Task<OrganizationResult<UserPermissionMatrix>> SetUserRoleAssignmentActive(
        long organizationId,
        long userId,
        long assignmentId,
        bool isActive,
        byte[] expectedVersion,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => WriteAsync(access, async db =>
        {
            var org = await LockOrganizationAsync(db, organizationId, ct);
            var account = await LockUserAsync(db, userId, ct);
            if (org is null || account is null)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

            var orgMembership = await db.OrganizationMemberships
                .SingleOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId, ct);
            if (orgMembership is null)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

            var assignment = await db.ScopedRoleAssignments
                .SingleOrDefaultAsync(r => r.Id == assignmentId && r.UserId == userId && r.OrganizationId == organizationId, ct);
            if (assignment is null)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

            if (!MatchesRowVersion(db, assignment, expectedVersion))
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ConcurrencyConflict);

            var now = UtcNow;
            if (assignment.IsActive == isActive)
                return OrganizationResult<UserPermissionMatrix>.Success((await BuildUserMatrixAsync(db, organizationId, userId, ct))!);

            if (isActive)
            {
                if (!org.IsActive)
                    return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ParentOrganizationInactive);
                if (!orgMembership.IsActive)
                    return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.MembershipRequired);

                var role = await db.RoleDefinitions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == assignment.RoleId, ct);
                if (role is null || !role.IsActive)
                    return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

                if (assignment.ScopeKind == 2)
                {
                    var site = await db.Set<Site>().SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == assignment.SiteId!.Value, ct);
                    if (site is null)
                        return OrganizationResult<UserPermissionMatrix>.Failure(OrganizationSiteErrors.SiteNotFound);
                    if (!site.IsActive)
                        return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ParentSiteInactive);
                    if (!await db.SiteMemberships.AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId && m.SiteId == assignment.SiteId!.Value && m.IsActive, ct))
                        return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.MembershipRequired);
                }
            }
            else if (assignment.RoleId == PermissionCatalog.OrganizationAdministratorRoleId)
            {
                PermissionCatalog.EnsureNotSelfAdminRevocation(access.Actor.ActorId, userId, assignment.RoleId);

                var remainingAdmins = await (from r in db.ScopedRoleAssignments
                                             join m in db.OrganizationMemberships on new { r.UserId, OrganizationId = r.OrganizationId!.Value } equals new { m.UserId, m.OrganizationId }
                                             join u in db.Users on r.UserId equals u.Id
                                             where r.RoleId == PermissionCatalog.OrganizationAdministratorRoleId
                                                   && r.ScopeKind == 1
                                                   && r.OrganizationId == organizationId
                                                   && r.IsActive
                                                   && m.IsActive
                                                   && u.IsActive
                                                   && r.UserId != userId
                                             select r.UserId)
                    .Distinct()
                    .CountAsync(ct);

                if (org.IsActive && remainingAdmins < 1)
                    return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.LastOrganizationAdminRequired);
            }

            assignment.IsActive = isActive;
            SetUpdateMetadata(db, assignment, now, access.Actor.ActorId, expectedVersion);

            AddEvent(db, "ScopedRoleChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, assignment.SiteId,
                new { action = isActive ? "Activated" : "Deactivated", roleId = assignment.RoleId, assignmentId, isActive });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserPermissionMatrix>.Success((await BuildUserMatrixAsync(db, organizationId, userId, ct))!);
        }, ct);

    public Task<OrganizationResult<UserPermissionMatrix>> SaveUserOperationGrants(
        long organizationId,
        long userId,
        RoleScopeTarget targetScope,
        long? siteId,
        IReadOnlyList<(string PermissionCode, bool IsActive, byte[]? ExpectedVersion)> normalizedChanges,
        OrganizationWriteAccess access,
        CancellationToken ct)
        => WriteAsync(access, async db =>
        {
            var org = await LockOrganizationAsync(db, organizationId, ct);
            var account = await LockUserAsync(db, userId, ct);
            if (org is null || account is null)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

            var orgMembership = await db.OrganizationMemberships
                .SingleOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId, ct);
            if (orgMembership is null)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

            if (!org.IsActive)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ParentOrganizationInactive);
            if (!orgMembership.IsActive)
                return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.MembershipRequired);

            if (targetScope == RoleScopeTarget.Site)
            {
                var site = await db.Set<Site>().SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Id == siteId!.Value, ct);
                if (site is null)
                    return OrganizationResult<UserPermissionMatrix>.Failure(OrganizationSiteErrors.SiteNotFound);
                if (!site.IsActive)
                    return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ParentSiteInactive);
                if (!await db.SiteMemberships.AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId && m.SiteId == siteId!.Value && m.IsActive, ct))
                    return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.MembershipRequired);
            }

            var existingGrants = await db.UserPermissionGrants
                .Where(g => g.UserId == userId
                            && g.OrganizationId == organizationId
                            && g.ScopeKind == (byte)targetScope
                            && g.SiteId == siteId)
                .ToDictionaryAsync(g => g.PermissionCode, StringComparer.Ordinal, ct);

            var now = UtcNow;
            var changedCodes = new List<string>();

            foreach (var (code, isActive, expectedVersion) in normalizedChanges)
            {
                if (existingGrants.TryGetValue(code, out var existing))
                {
                    if (expectedVersion is null || !MatchesRowVersion(db, existing, expectedVersion))
                        return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ConcurrencyConflict);

                    if (existing.IsActive == isActive)
                        continue;

                    existing.IsActive = isActive;
                    SetUpdateMetadata(db, existing, now, access.Actor.ActorId, expectedVersion);
                    changedCodes.Add($"{code}:{(isActive ? "1" : "0")}");
                }
                else
                {
                    if (expectedVersion is not null)
                        return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ConcurrencyConflict);

                    if (!isActive)
                        continue;

                    var grant = new UserPermissionGrant
                    {
                        UserId = userId,
                        ScopeKind = (byte)targetScope,
                        OrganizationId = organizationId,
                        SiteId = siteId,
                        PermissionCode = code,
                        IsActive = true
                    };
                    db.UserPermissionGrants.Add(grant);
                    SetCreateMetadata(db, grant, now, access.Actor.ActorId);
                    changedCodes.Add($"{code}:1");
                }
            }

            if (changedCodes.Count == 0)
                return OrganizationResult<UserPermissionMatrix>.Success((await BuildUserMatrixAsync(db, organizationId, userId, ct))!);

            AddEvent(db, "DirectPermissionChanged", now, access.Actor.CorrelationId, access.Actor.ActorId, userId, organizationId, siteId,
                new { targetScope = targetScope.ToString(), siteId, changes = changedCodes });

            await db.SaveChangesAsync(ct);
            return OrganizationResult<UserPermissionMatrix>.Success((await BuildUserMatrixAsync(db, organizationId, userId, ct))!);
        }, ct);

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
            throw new RoleStoreUnavailableException(ex);
        }
    }

    private async Task<OrganizationResult<T>> WriteAsync<T>(
        OrganizationWriteAccess access,
        Func<AuthDbContext, Task<OrganizationResult<T>>> operation,
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
                    return OrganizationResult<T>.Failure(RolePermissionErrors.AccessDenied);

                var commitAttempted = false;
                try
                {
                    await using var opScope = scopes.CreateAsyncScope();
                    var db = opScope.ServiceProvider.GetRequiredService<AuthDbContext>();
                    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                    var result = await operation(db);
                    if (!result.Succeeded)
                        return result;

                    commitAttempted = true;
                    await tx.CommitAsync(ct);
                    return result;
                }
                catch (Exception ex) when (commitAttempted && ex is not RoleCommitOutcomeUnknownException)
                {
                    throw new RoleCommitOutcomeUnknownException(ex);
                }
            });
        }
        catch (RoleRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (DbUpdateConcurrencyException) { return OrganizationResult<T>.Failure(RolePermissionErrors.ConcurrencyConflict); }
        catch (RoleCommitOutcomeUnknownException) { return OrganizationResult<T>.Failure(RolePermissionErrors.OutcomeUnknown); }
        catch (DbUpdateException ex) when (IsUniqueRoleNameViolation(ex))
        { return OrganizationResult<T>.Failure(RolePermissionErrors.NameDuplicate); }
        catch (DbUpdateException ex) when (IsUniqueAssignmentOrGrantViolation(ex))
        { return OrganizationResult<T>.Failure(RolePermissionErrors.ConcurrencyConflict); }
        catch (DbUpdateException ex) when (IsAuditWriteFailure(ex))
        { return OrganizationResult<T>.Failure(RolePermissionErrors.AuditUnavailable); }
        catch (SqlException sql) when (IsAuditSqlFailure(sql))
        { return OrganizationResult<T>.Failure(RolePermissionErrors.AuditUnavailable); }
        catch (Exception ex) when (IsTransientUnavailable(ex))
        { return OrganizationResult<T>.Failure(RolePermissionErrors.PersistenceUnavailable); }
    }

    private static Task<Organization?> LockOrganizationAsync(AuthDbContext db, long organizationId, CancellationToken ct)
        => db.Set<Organization>()
            .FromSqlInterpolated($"SELECT * FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={organizationId}")
            .SingleOrDefaultAsync(ct);

    private static Task<ApplicationAccount?> LockUserAsync(AuthDbContext db, long userId, CancellationToken ct)
        => db.Users
            .FromSqlInterpolated($"SELECT * FROM [auth].[User] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={userId}")
            .SingleOrDefaultAsync(ct);

    private static Task<RoleDefinition?> LockRoleDefinitionAsync(AuthDbContext db, long roleId, CancellationToken ct)
        => db.RoleDefinitions
            .FromSqlInterpolated($"SELECT * FROM [auth].[RoleDefinition] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={roleId}")
            .SingleOrDefaultAsync(ct);

    private static async Task<RoleDetails?> BuildRoleDetailsAsync(AuthDbContext db, long organizationId, long roleId, CancellationToken ct)
    {
        var role = await db.RoleDefinitions.AsNoTracking()
            .Where(r => r.Id == roleId && ((r.IsSystem && r.ScopeKind != 0) || r.OrganizationId == organizationId))
            .Select(r => new
            {
                r.Id,
                r.OrganizationId,
                r.Name,
                r.IsSystem,
                r.ScopeKind,
                r.IsActive,
                RowVersion = EF.Property<byte[]>(r, "RowVersion")
            })
            .SingleOrDefaultAsync(ct);
        if (role is null)
            return null;

        var perms = await db.RolePermissions.AsNoTracking()
            .Where(p => p.RoleId == roleId && p.IsActive)
            .OrderBy(p => p.PermissionCode)
            .Select(p => p.PermissionCode)
            .ToArrayAsync(ct);
        if (role.Id == FixedAccessRoles.OrganizationAdministrator && !perms.Contains(AuditPermissions.AuditRead, StringComparer.Ordinal))
            perms = perms.Append(AuditPermissions.AuditRead).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        return new RoleDetails(
            role.Id,
            role.OrganizationId,
            role.Name,
            role.IsSystem,
            (RoleScopeTarget)role.ScopeKind,
            role.IsActive,
            Convert.ToBase64String(role.RowVersion),
            perms);
    }

    private static async Task<UserPermissionMatrix?> BuildUserMatrixAsync(AuthDbContext db, long organizationId, long userId, CancellationToken ct)
    {
        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.UserName, u.IsActive })
            .SingleOrDefaultAsync(ct);
        if (account is null)
            return null;

        var membership = await db.OrganizationMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.OrganizationId == organizationId)
            .Select(m => new { m.IsActive })
            .SingleOrDefaultAsync(ct);
        if (membership is null)
            return null;

        var assignedRoles = await (from a in db.ScopedRoleAssignments.AsNoTracking()
                                   join r in db.RoleDefinitions.AsNoTracking() on a.RoleId equals r.Id
                                   where a.UserId == userId && a.OrganizationId == organizationId && a.ScopeKind != 0
                                   orderby r.IsSystem descending, r.Name, a.SiteId
                                   select new
                                   {
                                       a.Id,
                                       a.RoleId,
                                       r.Name,
                                       r.IsSystem,
                                       a.ScopeKind,
                                       a.SiteId,
                                       a.IsActive,
                                       RowVersion = EF.Property<byte[]>(a, "RowVersion")
                                   })
            .ToListAsync(ct);

        var directGrants = await db.UserPermissionGrants.AsNoTracking()
            .Where(g => g.UserId == userId && g.OrganizationId == organizationId)
            .OrderBy(g => g.ScopeKind)
            .ThenBy(g => g.SiteId)
            .ThenBy(g => g.PermissionCode)
            .Select(g => new
            {
                g.Id,
                g.ScopeKind,
                g.SiteId,
                g.PermissionCode,
                g.IsActive,
                RowVersion = EF.Property<byte[]>(g, "RowVersion")
            })
            .ToListAsync(ct);

        return new UserPermissionMatrix(
            account.Id,
            organizationId,
            account.UserName ?? string.Empty,
            account.IsActive,
            membership.IsActive,
            assignedRoles.Select(a => new UserAssignedRoleDetails(
                a.Id,
                a.RoleId,
                a.Name,
                a.IsSystem,
                (RoleScopeTarget)a.ScopeKind,
                a.SiteId,
                a.IsActive,
                Convert.ToBase64String(a.RowVersion))).ToArray(),
            directGrants.Select(g => new UserDirectPermissionDetails(
                g.Id,
                (RoleScopeTarget)g.ScopeKind,
                g.SiteId,
                g.PermissionCode,
                g.IsActive,
                Convert.ToBase64String(g.RowVersion))).ToArray());
    }

    private static bool MatchesRowVersion(AuthDbContext db, object entity, byte[] expected)
    {
        var current = (byte[]?)db.Entry(entity).Property("RowVersion").CurrentValue;
        return current is not null && current.SequenceEqual(expected);
    }

    private static void SetCreateMetadata(AuthDbContext db, object entity, DateTime now, long actorId)
    {
        var entry = db.Entry(entity);
        entry.Property("CreatedAtUtc").CurrentValue = now;
        entry.Property("CreatedByActorId").CurrentValue = actorId;
    }

    private static void SetUpdateMetadata(AuthDbContext db, object entity, DateTime now, long actorId, byte[]? expectedRowVersion)
    {
        var entry = db.Entry(entity);
        var created = (DateTime)entry.Property("CreatedAtUtc").CurrentValue!;
        entry.Property("UpdatedAtUtc").CurrentValue = now >= created ? now : created;
        entry.Property("UpdatedByActorId").CurrentValue = actorId;
        if (expectedRowVersion is not null)
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
            OrganizationId = organizationId,
            SiteId = siteId,
            Outcome = 0,
            DataJson = JsonSerializer.Serialize(payload)
        });
    }

    private static bool IsUniqueRoleNameViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql
           && sql.Number is 2601 or 2627
           && sql.Message.Contains("UX_RoleDefinition_Org_Name", StringComparison.Ordinal);

    private static bool IsUniqueAssignmentOrGrantViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql
           && sql.Number is 2601 or 2627
           && !sql.Message.Contains("UX_RoleDefinition_Org_Name", StringComparison.Ordinal);

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
