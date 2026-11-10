using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;

namespace ForgeFlow.Application.Roles;

/// <summary>Secured role and granular permission use cases; enforces fail-closed authorization and anti-escalation ceilings.</summary>
public sealed class RoleUseCases(
    IRoleOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(OrganizationScope? scope, string permission, CancellationToken ct)
    {
        if (actors is null) return (null, RolePermissionErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, RolePermissionErrors.ActorRequired);
        if (scope is null) return (actor, RolePermissionErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (actor, RolePermissionErrors.AccessDenied);
        return (actor, null);
    }

    private Task<OrganizationResult<T>> RecordRejected<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long organizationId,
        long? siteId,
        string entityType,
        string? entityRef,
        string action)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result, operationRecorder, opContext, UserOperationCatalog.RoleActionRejected,
            actor, scope, organizationId, siteId, entityType, entityRef, new { Action = action });

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    private static bool OrganizationMatches(OrganizationScope? scope, long organizationId)
        => scope is OrganizationScope.Organization org && org.OrganizationId == organizationId && organizationId > 0;

    private static byte[]? DecodeVersion(string? raw)
    {
        if (raw is null) return null;
        try { var bytes = Convert.FromBase64String(raw); return bytes.Length == 8 ? bytes : null; }
        catch (FormatException) { return null; }
    }

    private static RoleListQuery Bound(RoleListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static async Task<OrganizationResult<T>> Execute<T>(Func<Task<OrganizationResult<T>>> operation)
    {
        try { return await operation(); }
        catch (RoleStoreUnavailableException) { return OrganizationResult<T>.Failure(RolePermissionErrors.PersistenceUnavailable); }
    }

    private static OrganizationResult<T> FailFromRule<T>(RoleRuleException ex)
        => OrganizationResult<T>.Failure(ex.Code, ex.Parameters);

    private async Task<bool> CallerHoldsAllAsync(
        TrustedActor actor,
        OrganizationScope scope,
        IEnumerable<string> requiredCodes,
        CancellationToken ct)
    {
        foreach (var code in requiredCodes)
        {
            if (!(await permissions!.EvaluateAsync(actor, scope, code, ct)).IsAllowed)
                return false;
        }
        return true;
    }

    public async Task<OrganizationResult<IReadOnlyList<PermissionDescriptor>>> GetPermissionCatalog(
        OrganizationScope? scope,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RolePermissions.RolesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<IReadOnlyList<PermissionDescriptor>>.Failure(auth.Error);

        if (scope is OrganizationScope.Organization)
        {
            var tenantItems = PermissionCatalog.All
                .Where(d => d.Applicability != PermissionScopeApplicability.PlatformOnly)
                .ToArray();
            return OrganizationResult<IReadOnlyList<PermissionDescriptor>>.Success(tenantItems);
        }

        if (scope is OrganizationScope.Platform)
            return OrganizationResult<IReadOnlyList<PermissionDescriptor>>.Success(PermissionCatalog.All);

        return OrganizationResult<IReadOnlyList<PermissionDescriptor>>.Failure(RolePermissionErrors.AccessDenied);
    }

    public async Task<OrganizationResult<OrganizationPage<RoleSummary>>> ListOrganizationRoles(
        OrganizationScope? scope,
        long organizationId,
        RoleListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RolePermissions.RolesRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<RoleSummary>>.Failure(auth.Error);
        if (!OrganizationMatches(scope, organizationId))
            return OrganizationResult<OrganizationPage<RoleSummary>>.Failure(RolePermissionErrors.NotFound);

        return await Execute(async () => OrganizationResult<OrganizationPage<RoleSummary>>.Success(
            await store.ListOrganizationRoles(organizationId, Bound(query), ct)));
    }

    public async Task<OrganizationResult<RoleDetails>> GetOrganizationRole(
        OrganizationScope? scope,
        long organizationId,
        long roleId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RolePermissions.RolesRead, ct);
        if (auth.Error is not null) return OrganizationResult<RoleDetails>.Failure(auth.Error);
        if (!OrganizationMatches(scope, organizationId) || roleId <= 0)
            return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound);

        return await Execute(async () =>
        {
            var value = await store.GetOrganizationRole(organizationId, roleId, ct);
            return value is null
                ? OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound)
                : OrganizationResult<RoleDetails>.Success(value);
        });
    }

    public async Task<OrganizationResult<RoleDetails>> CreateOrganizationRole(
        OrganizationScope? scope,
        CreateOrganizationRole request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.RolesManage;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<RoleDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoleDetails>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId))
            res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound);
        else if (!Enum.IsDefined(request.TargetScope))
            res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.PermissionScopeInvalid, new Dictionary<string, object> { ["field"] = "TargetScope" });
        else
        {
            try
            {
                var canonicalName = RoleDefinitionName.Parse(request.Name);
                var permissionCodes = PermissionCatalog.ValidateTenantPermissions((byte)request.TargetScope, request.PermissionCodes);
                if (!await CallerHoldsAllAsync(auth.Actor!, scope!, permissionCodes, ct))
                    res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.EscalationForbidden);
                else
                    res = await Execute(() => store.CreateOrganizationRole(
                        request.OrganizationId, canonicalName, request.TargetScope, permissionCodes, Access(auth.Actor!, scope!, permission), ct));
            }
            catch (RoleRuleException ex) { res = FailFromRule<RoleDetails>(ex); }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "Role", request.Name, "CreateOrganizationRole");
    }

    public async Task<OrganizationResult<RoleDetails>> UpdateOrganizationRole(
        OrganizationScope? scope,
        UpdateOrganizationRole request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.RolesManage;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<RoleDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoleDetails>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId) || request.RoleId <= 0)
            res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound);
        else
        {
            try
            {
                PermissionCatalog.EnsureCustomRoleMutable(request.RoleId, isSystem: false);
                var canonicalName = RoleDefinitionName.Parse(request.Name);
                // Validate as Organization-capable first; store verifies whether role's locked ScopeKind is Site and rejects org-only codes.
                var permissionCodes = PermissionCatalog.ValidateTenantPermissions((byte)RoleScopeTarget.Organization, request.PermissionCodes);
                if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
                    res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.ConcurrencyConflict);
                else if (request.IsActive && !await CallerHoldsAllAsync(auth.Actor!, scope!, permissionCodes, ct))
                    res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.EscalationForbidden);
                else
                    res = await Execute(() => store.UpdateOrganizationRole(
                        request.OrganizationId, request.RoleId, canonicalName, request.IsActive, permissionCodes, version, Access(auth.Actor!, scope!, permission), ct));
            }
            catch (RoleRuleException ex) { res = FailFromRule<RoleDetails>(ex); }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "Role", request.RoleId.ToString(CultureInfo.InvariantCulture), "UpdateOrganizationRole");
    }

    public async Task<OrganizationResult<UserPermissionMatrix>> GetUserPermissions(
        OrganizationScope? scope,
        long organizationId,
        long userId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, RolePermissions.RolesRead, ct);
        if (auth.Error is not null) return OrganizationResult<UserPermissionMatrix>.Failure(auth.Error);
        if (!OrganizationMatches(scope, organizationId) || userId <= 0)
            return OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);

        return await Execute(async () =>
        {
            var value = await store.GetUserPermissions(organizationId, userId, ct);
            return value is null
                ? OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound)
                : OrganizationResult<UserPermissionMatrix>.Success(value);
        });
    }

    public async Task<OrganizationResult<UserPermissionMatrix>> AssignUserRole(
        OrganizationScope? scope,
        AssignUserRole request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.PermissionsAssign;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserPermissionMatrix> res;
        if (auth.Error is not null) res = OrganizationResult<UserPermissionMatrix>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0 || request.RoleId <= 0)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);
        else if (!Enum.IsDefined(request.TargetScope))
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.PermissionScopeInvalid, new Dictionary<string, object> { ["field"] = "TargetScope" });
        else if (request.RoleId == PermissionCatalog.DeploymentProvisionerRoleId)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.EscalationForbidden);
        else if ((request.TargetScope == RoleScopeTarget.Organization && request.SiteId is not null)
            || (request.TargetScope == RoleScopeTarget.Site && request.SiteId is not > 0))
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.PermissionScopeInvalid, new Dictionary<string, object> { ["field"] = "SiteId" });
        else
            res = await Execute(() => store.AssignUserRole(request, Access(auth.Actor!, scope!, permission), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "ScopedRoleAssignment", $"{request.UserId}:role{request.RoleId}", "AssignUserRole");
    }

    public async Task<OrganizationResult<UserPermissionMatrix>> SetUserRoleAssignmentActive(
        OrganizationScope? scope,
        SetUserRoleAssignmentActive request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.PermissionsAssign;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserPermissionMatrix> res;
        if (auth.Error is not null) res = OrganizationResult<UserPermissionMatrix>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0 || request.AssignmentId <= 0)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.NotFound);
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.ConcurrencyConflict);
        else
            res = await Execute(() => store.SetUserRoleAssignmentActive(
                request.OrganizationId, request.UserId, request.AssignmentId, request.IsActive, version, Access(auth.Actor!, scope!, permission), ct));
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "ScopedRoleAssignment", request.AssignmentId.ToString(CultureInfo.InvariantCulture), "SetUserRoleAssignmentActive");
    }

    public async Task<OrganizationResult<UserPermissionMatrix>> SaveUserOperationGrants(
        OrganizationScope? scope,
        SaveUserOperationGrants request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.PermissionsAssign;
        var auth = await Authorize(scope, permission, ct);
        OrganizationResult<UserPermissionMatrix> res;
        if (auth.Error is not null) res = OrganizationResult<UserPermissionMatrix>.Failure(auth.Error);
        else if (!OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
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
                        expected = DecodeVersion(change.ExpectedRowVersion);
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

                        if (!await CallerHoldsAllAsync(auth.Actor!, checkScope, activeGrantsToCheck, ct))
                        {
                            res = OrganizationResult<UserPermissionMatrix>.Failure(RolePermissionErrors.EscalationForbidden);
                            return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "UserPermissionGrant", request.UserId.ToString(CultureInfo.InvariantCulture), "SaveUserOperationGrants");
                        }
                    }

                    res = await Execute(() => store.SaveUserOperationGrants(
                        request.OrganizationId, request.UserId, request.TargetScope, request.SiteId, normalized, Access(auth.Actor!, scope!, permission), ct));
                }
            }
            catch (RoleRuleException ex) { res = FailFromRule<RoleDetails>(ex) is var r ? OrganizationResult<UserPermissionMatrix>.Failure(r.Error!.Code, r.Error.Parameters) : default!; }
        }
        return await RecordRejected(res, auth.Actor, scope, request.OrganizationId, request.SiteId, "UserPermissionGrant", request.UserId.ToString(CultureInfo.InvariantCulture), "SaveUserOperationGrants");
    }
}
