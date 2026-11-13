using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles.Slices;
using ForgeFlow.Domain.Roles;

namespace ForgeFlow.Application.Roles;

/// <summary>
/// Secured facade for role management, permission catalog introspection, and granular permission assignments.
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Roles.Slices"/>.
/// </summary>
public sealed class RoleUseCases
{
    private readonly RoleCatalogSlices catalogSlices;
    private readonly RoleAssignmentSlices assignmentSlices;

    public RoleUseCases(
        IRoleOperationsStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null)
    {
        var support = new RoleSliceSupport(actors, permissions, operationRecorder, opContext);
        catalogSlices = new RoleCatalogSlices(store, support);
        assignmentSlices = new RoleAssignmentSlices(store, support);
    }

    public Task<OrganizationResult<IReadOnlyList<PermissionDescriptor>>> GetPermissionCatalog(
        OrganizationScope? scope,
        CancellationToken ct = default)
        => catalogSlices.GetPermissionCatalogAsync(scope, ct);

    public Task<OrganizationResult<OrganizationPage<RoleSummary>>> ListOrganizationRoles(
        OrganizationScope? scope,
        long organizationId,
        RoleListQuery query,
        CancellationToken ct = default)
        => catalogSlices.ListOrganizationRolesAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<RoleDetails>> GetOrganizationRole(
        OrganizationScope? scope,
        long organizationId,
        long roleId,
        CancellationToken ct = default)
        => catalogSlices.GetOrganizationRoleAsync(scope, organizationId, roleId, ct);

    public Task<OrganizationResult<RoleDetails>> CreateOrganizationRole(
        OrganizationScope? scope,
        CreateOrganizationRole request,
        CancellationToken ct = default)
        => catalogSlices.CreateOrganizationRoleAsync(scope, request, ct);

    public Task<OrganizationResult<RoleDetails>> UpdateOrganizationRole(
        OrganizationScope? scope,
        UpdateOrganizationRole request,
        CancellationToken ct = default)
        => catalogSlices.UpdateOrganizationRoleAsync(scope, request, ct);

    public Task<OrganizationResult<UserPermissionMatrix>> GetUserPermissions(
        OrganizationScope? scope,
        long organizationId,
        long userId,
        CancellationToken ct = default)
        => assignmentSlices.GetUserPermissionsAsync(scope, organizationId, userId, ct);

    public Task<OrganizationResult<UserPermissionMatrix>> AssignUserRole(
        OrganizationScope? scope,
        AssignUserRole request,
        CancellationToken ct = default)
        => assignmentSlices.AssignUserRoleAsync(scope, request, ct);

    public Task<OrganizationResult<UserPermissionMatrix>> SetUserRoleAssignmentActive(
        OrganizationScope? scope,
        SetUserRoleAssignmentActive request,
        CancellationToken ct = default)
        => assignmentSlices.SetUserRoleAssignmentActiveAsync(scope, request, ct);

    public Task<OrganizationResult<UserPermissionMatrix>> SaveUserOperationGrants(
        OrganizationScope? scope,
        SaveUserOperationGrants request,
        CancellationToken ct = default)
        => assignmentSlices.SaveUserOperationGrantsAsync(scope, request, ct);
}
