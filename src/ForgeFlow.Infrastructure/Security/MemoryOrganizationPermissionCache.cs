using System.Collections.Concurrent;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Security;
using Microsoft.Extensions.Caching.Memory;

namespace ForgeFlow.Infrastructure.Security;

/// <summary>
/// MemoryCache-backed implementation of <see cref="IOrganizationPermissionCache"/> using atomic version tokens
/// for instantaneous, race-free invalidation per user and globally.
/// </summary>
public sealed class MemoryOrganizationPermissionCache(IMemoryCache cache) : IOrganizationPermissionCache
{
    private readonly ConcurrentDictionary<long, int> _userVersions = new();
    private int _globalVersion = 1;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public bool TryGetGrantedPermissions(long userId, OrganizationScope scope, out IReadOnlySet<string>? permissions)
    {
        var userVersion = _userVersions.GetOrAdd(userId, 1);
        var key = BuildKey(userId, scope, userVersion, _globalVersion);
        return cache.TryGetValue(key, out permissions);
    }

    public void SetGrantedPermissions(long userId, OrganizationScope scope, IReadOnlySet<string> permissions, TimeSpan? ttl = null)
    {
        var userVersion = _userVersions.GetOrAdd(userId, 1);
        var key = BuildKey(userId, scope, userVersion, _globalVersion);
        cache.Set(key, permissions, ttl ?? DefaultTtl);
    }

    public void InvalidateUser(long userId)
    {
        _userVersions.AddOrUpdate(userId, 2, (_, v) => v + 1);
    }

    public void InvalidateAll()
    {
        Interlocked.Increment(ref _globalVersion);
    }

    private static string BuildKey(long userId, OrganizationScope scope, int userVersion, int globalVersion)
        => $"perm:{globalVersion}:{userId}:v{userVersion}:{ScopeString(scope)}";

    private static string ScopeString(OrganizationScope scope) => scope switch
    {
        OrganizationScope.Platform => "plat",
        OrganizationScope.Organization org => $"org:{org.OrganizationId}",
        OrganizationScope.Site site => $"site:{site.OrganizationId}:{site.SiteId}",
        _ => "none"
    };
}
