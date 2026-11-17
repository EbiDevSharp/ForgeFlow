using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Application.Security;

/// <summary>
/// Thread-safe caching abstraction for user roles, memberships, and resolved permissions.
/// Strictly partitioned by Tenant and User with deterministic immediate invalidation.
/// </summary>
public interface IOrganizationPermissionCache
{
    /// <summary>
    /// Attempts to retrieve the cached set of granted permission codes for a user in a given scope.
    /// </summary>
    bool TryGetGrantedPermissions(long userId, OrganizationScope scope, out IReadOnlySet<string>? permissions);

    /// <summary>
    /// Caches the set of granted permission codes for a user in a given scope.
    /// </summary>
    void SetGrantedPermissions(long userId, OrganizationScope scope, IReadOnlySet<string> permissions, TimeSpan? ttl = null);

    /// <summary>
    /// Immediately invalidates all cached permissions for a specific user across all scopes.
    /// </summary>
    void InvalidateUser(long userId);

    /// <summary>
    /// Immediately invalidates all cached permissions across all users and organizations (e.g. on role definition changes).
    /// </summary>
    void InvalidateAll();
}
