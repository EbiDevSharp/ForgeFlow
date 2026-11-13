using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;

namespace ForgeFlow.Application.Roles.Slices;

/// <summary>
/// Operational vertical slice for granular user role assignments, activation, and operation-level permission grants.
/// </summary>
public sealed class RoleAssignmentSlices(
    IRoleOperationsStore store,
    RoleSliceSupport support)
{
    public async Task<OrganizationResult<UserPermissionMatrix>> GetUserPermissionsAsync(
        OrganizationScope? scope,
        long organizationId,
        long userId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RolePermissions.RolesRead, ct);
        if (auth.Error is not null) return OrganizationResult<UserPermissionMatrix>.Failure(auth.Error);
        if (!RoleSliceSupport.OrganizationMatches(scope, organizationId) || userId <= 0)
            return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

        return await RoleSliceSupport.Execute(async () =>
        {
            var value = await store.GetUserPermissions(organizationId, userId, ct);
            return value is null
                ? OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound)
                : OrganizationResult<UserPermissionMatrix>.Success(value);
        });
    }

    public async Task<OrganizationResult<UserPermissionMatrix>> AssignUserRoleAsync(
        OrganizationScope? scope,
        AssignUserRole request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.PermissionsAssign;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserPermissionMatrix> res;
        if (auth.Error is not null) res = OrganizationResult<UserPermissionMatrix>.Failure(auth.Error);
        else if (!RoleSliceSupport.OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0 || request.RoleId <= 0)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);
        else if (!Enum.IsDefined(request.TargetScope))
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.PermissionScopeInvalid, new Dictionary<string, object> { ["field"] = "TargetScope" });
        else if (request.RoleId == PermissionCatalog.DeploymentProvisionerRoleId)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.EscalationForbidden);
        else if ((request.TargetScope == RoleScopeTarget.Organization && request.SiteId is not null)
            || (request.TargetScope == RoleScopeTarget.Site && request.SiteId is not > 0))
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.PermissionScopeInvalid, new Dictionary<string, object> { ["field"] = "SiteId" });
        else
            res = await RoleSliceSupport.Execute(() => store.AssignUserRole(request, support.Access(auth.Actor!, scope!, permission), ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "ScopedRoleAssignment", $"{request.UserId}:role{request.RoleId}", "AssignUserRole");
    }

    public async Task<OrganizationResult<UserPermissionMatrix>> SetUserRoleAssignmentActiveAsync(
        OrganizationScope? scope,
        SetUserRoleAssignmentActive request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.PermissionsAssign;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserPermissionMatrix> res;
        if (auth.Error is not null) res = OrganizationResult<UserPermissionMatrix>.Failure(auth.Error);
        else if (!RoleSliceSupport.OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0 || request.AssignmentId <= 0)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);
        else if (RoleSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ConcurrencyConflict);
        else
            res = await RoleSliceSupport.Execute(() => store.SetUserRoleAssignmentActive(
                request.OrganizationId, request.UserId, request.AssignmentId, request.IsActive, version, support.Access(auth.Actor!, scope!, permission), ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "ScopedRoleAssignment", request.AssignmentId.ToString(CultureInfo.InvariantCulture), "SetUserRoleAssignmentActive");
    }

    public async Task<OrganizationResult<UserPermissionMatrix>> SaveUserOperationGrantsAsync(
        OrganizationScope? scope,
        SaveUserOperationGrants request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.PermissionsAssign;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserPermissionMatrix> res;
        if (auth.Error is not null) res = OrganizationResult<UserPermissionMatrix>.Failure(auth.Error);
        else if (!RoleSliceSupport.OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);
        else if (!Enum.IsDefined(request.TargetScope)
            || (request.TargetScope == RoleScopeTarget.Organization && request.SiteId is not null)
            || (request.TargetScope == RoleScopeTarget.Site && request.SiteId is not > 0))
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.PermissionScopeInvalid, new Dictionary<string, object> { ["field"] = "TargetScope" });
        else if (request.Changes is null || request.Changes.Count == 0)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.PermissionUnknown, new Dictionary<string, object> { ["field"] = "Changes" });
        else
        {
            try
            {
                _ = PermissionCatalog.ValidateTenantPermissions(
                    (byte)request.TargetScope,
                    request.Changes.Select(c => c.PermissionCode ?? string.Empty));

                var normalized = new List<(string PermissionCode, bool IsActive, byte[]? ExpectedVersion)>(request.Changes.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var activeGrantsToCheck = new List<string>();
                OrganizationResult<UserPermissionMatrix>? invalidChange = null;

                foreach (var change in request.Changes)
                {
                    if (!PermissionCatalog.TryGet(change.PermissionCode, out var desc) || !seen.Add(desc.Code))
                    {
                        invalidChange = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.PermissionUnknown, new Dictionary<string, object> { ["field"] = "Changes" });
                        break;
                    }

                    byte[]? expected = null;
                    if (change.ExpectedRowVersion is not null)
                    {
                        expected = RoleSliceSupport.DecodeVersion(change.ExpectedRowVersion);
                        if (expected is null)
                        {
                            invalidChange = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ConcurrencyConflict);
                            break;
                        }
                    }

                    if (change.IsActive)
                        activeGrantsToCheck.Add(desc.Code);

                    normalized.Add((desc.Code, change.IsActive, expected));
                }

                if (invalidChange is not null)
                {
                    res = invalidChange;
                }
                else
                {
                    if (activeGrantsToCheck.Count > 0)
                    {
                        OrganizationScope checkScope = request.TargetScope == RoleScopeTarget.Organization
                            ? scope!
                            : new OrganizationScope.Site(request.OrganizationId, request.SiteId!.Value);

                        if (!await support.CallerHoldsAllAsync(auth.Actor!, checkScope, activeGrantsToCheck, ct))
                        {
                            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.EscalationForbidden);
                            return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "UserPermissionGrant", request.UserId.ToString(CultureInfo.InvariantCulture), "SaveUserOperationGrants");
                        }
                    }

                    res = await RoleSliceSupport.Execute(() => store.SaveUserOperationGrants(
                        request.OrganizationId, request.UserId, request.TargetScope, request.SiteId, normalized, support.Access(auth.Actor!, scope!, permission), ct));
                }
            }
            catch (RoleRuleException ex) { res = RoleSliceSupport.FailFromRule<RoleDetails>(ex) is var r ? OrganizationResult<UserPermissionMatrix>.Failure(r.Error!.Code, r.Error.Parameters) : default!; }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "UserPermissionGrant", request.UserId.ToString(CultureInfo.InvariantCulture), "SaveUserOperationGrants");
    }
}
