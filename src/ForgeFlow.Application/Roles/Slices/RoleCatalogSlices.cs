using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;

namespace ForgeFlow.Application.Roles.Slices;

/// <summary>
/// Operational vertical slice for permission catalog introspection, custom tenant role creation, and updates.
/// </summary>
public sealed class RoleCatalogSlices(
    IRoleOperationsStore store,
    RoleSliceSupport support)
{
    public async Task<OrganizationResult<IReadOnlyList<PermissionDescriptor>>> GetPermissionCatalogAsync(
        OrganizationScope? scope,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RolePermissions.RolesRead, ct);
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

    public async Task<OrganizationResult<OrganizationPage<RoleSummary>>> ListOrganizationRolesAsync(
        OrganizationScope? scope,
        long organizationId,
        RoleListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RolePermissions.RolesRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<RoleSummary>>.Failure(auth.Error);
        if (!RoleSliceSupport.OrganizationMatches(scope, organizationId))
            return OrganizationResult<OrganizationPage<RoleSummary>>.Failure(RolePermissionErrors.NotFound);

        return await RoleSliceSupport.Execute(async () => OrganizationResult<OrganizationPage<RoleSummary>>.Success(
            await store.ListOrganizationRoles(organizationId, RoleSliceSupport.Bound(query), ct)));
    }

    public async Task<OrganizationResult<RoleDetails>> GetOrganizationRoleAsync(
        OrganizationScope? scope,
        long organizationId,
        long roleId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, RolePermissions.RolesRead, ct);
        if (auth.Error is not null) return OrganizationResult<RoleDetails>.Failure(auth.Error);
        if (!RoleSliceSupport.OrganizationMatches(scope, organizationId) || roleId <= 0)
            return OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound);

        return await RoleSliceSupport.Execute(async () =>
        {
            var value = await store.GetOrganizationRole(organizationId, roleId, ct);
            return value is null
                ? OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound)
                : OrganizationResult<RoleDetails>.Success(value);
        });
    }

    public async Task<OrganizationResult<RoleDetails>> CreateOrganizationRoleAsync(
        OrganizationScope? scope,
        CreateOrganizationRole request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.RolesManage;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<RoleDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoleDetails>.Failure(auth.Error);
        else if (!RoleSliceSupport.OrganizationMatches(scope, request.OrganizationId))
            res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound);
        else if (!Enum.IsDefined(request.TargetScope))
            res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.PermissionScopeInvalid, new Dictionary<string, object> { ["field"] = "TargetScope" });
        else
        {
            try
            {
                var canonicalName = RoleDefinitionName.Parse(request.Name);
                var permissionCodes = PermissionCatalog.ValidateTenantPermissions((byte)request.TargetScope, request.PermissionCodes);
                if (!await support.CallerHoldsAllAsync(auth.Actor!, scope!, permissionCodes, ct))
                    res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.EscalationForbidden);
                else
                    res = await RoleSliceSupport.Execute(() => store.CreateOrganizationRole(
                        request.OrganizationId, canonicalName, request.TargetScope, permissionCodes, support.Access(auth.Actor!, scope!, permission), ct));
            }
            catch (RoleRuleException ex) { res = RoleSliceSupport.FailFromRule<RoleDetails>(ex); }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "Role", request.Name, "CreateOrganizationRole");
    }

    public async Task<OrganizationResult<RoleDetails>> UpdateOrganizationRoleAsync(
        OrganizationScope? scope,
        UpdateOrganizationRole request,
        CancellationToken ct = default)
    {
        const string permission = RolePermissions.RolesManage;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<RoleDetails> res;
        if (auth.Error is not null) res = OrganizationResult<RoleDetails>.Failure(auth.Error);
        else if (!RoleSliceSupport.OrganizationMatches(scope, request.OrganizationId) || request.RoleId <= 0)
            res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.NotFound);
        else
        {
            try
            {
                PermissionCatalog.EnsureCustomRoleMutable(request.RoleId, isSystem: false);
                var canonicalName = RoleDefinitionName.Parse(request.Name);
                var permissionCodes = PermissionCatalog.ValidateTenantPermissions((byte)RoleScopeTarget.Organization, request.PermissionCodes);
                if (RoleSliceSupport.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
                    res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.ConcurrencyConflict);
                else if (request.IsActive && !await support.CallerHoldsAllAsync(auth.Actor!, scope!, permissionCodes, ct))
                    res = OrganizationResult<RoleDetails>.Failure(RolePermissionErrors.EscalationForbidden);
                else
                    res = await RoleSliceSupport.Execute(() => store.UpdateOrganizationRole(
                        request.OrganizationId, request.RoleId, canonicalName, request.IsActive, permissionCodes, version, support.Access(auth.Actor!, scope!, permission), ct));
            }
            catch (RoleRuleException ex) { res = RoleSliceSupport.FailFromRule<RoleDetails>(ex); }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "Role", request.RoleId.ToString(CultureInfo.InvariantCulture), "UpdateOrganizationRole");
    }
}
