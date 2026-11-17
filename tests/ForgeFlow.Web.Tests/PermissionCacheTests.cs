using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class PermissionCacheTests
{
    private static MemoryOrganizationPermissionCache CreateCache()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        return new MemoryOrganizationPermissionCache(memoryCache);
    }

    [Fact]
    public void PermissionCache_StoresAndRetrieves_GrantedPermissionsPerUserAndScope()
    {
        var cache = CreateCache();
        var platformScope = new OrganizationScope.Platform();
        var org1Scope = new OrganizationScope.Organization(100);
        var site1Scope = new OrganizationScope.Site(100, 200);

        var user10PlatformPerms = new HashSet<string> { "Users.Read", "Users.Create" };
        var user10Org1Perms = new HashSet<string> { "Inventory.Read", "Inventory.Issue" };
        var user20Org1Perms = new HashSet<string> { "Boms.Read" };

        cache.SetGrantedPermissions(10, platformScope, user10PlatformPerms);
        cache.SetGrantedPermissions(10, org1Scope, user10Org1Perms);
        cache.SetGrantedPermissions(20, org1Scope, user20Org1Perms);

        // Verify user 10 platform
        Assert.True(cache.TryGetGrantedPermissions(10, platformScope, out var hit10Plat));
        Assert.NotNull(hit10Plat);
        Assert.Contains("Users.Read", hit10Plat);
        Assert.Contains("Users.Create", hit10Plat);
        Assert.DoesNotContain("Inventory.Read", hit10Plat);

        // Verify user 10 org1
        Assert.True(cache.TryGetGrantedPermissions(10, org1Scope, out var hit10Org));
        Assert.NotNull(hit10Org);
        Assert.Contains("Inventory.Read", hit10Org);
        Assert.DoesNotContain("Users.Read", hit10Org);

        // Verify user 10 site1 (uncached)
        Assert.False(cache.TryGetGrantedPermissions(10, site1Scope, out _));

        // Verify user 20 org1
        Assert.True(cache.TryGetGrantedPermissions(20, org1Scope, out var hit20Org));
        Assert.NotNull(hit20Org);
        Assert.Contains("Boms.Read", hit20Org);
        Assert.DoesNotContain("Inventory.Read", hit20Org);

        // Verify unknown user
        Assert.False(cache.TryGetGrantedPermissions(999, org1Scope, out _));
    }

    [Fact]
    public void PermissionCache_InvalidateUser_InstantlyClearsAllScopesForThatUser()
    {
        var cache = CreateCache();
        var platformScope = new OrganizationScope.Platform();
        var org1Scope = new OrganizationScope.Organization(100);

        cache.SetGrantedPermissions(10, platformScope, new HashSet<string> { "Users.Read" });
        cache.SetGrantedPermissions(10, org1Scope, new HashSet<string> { "Inventory.Read" });
        cache.SetGrantedPermissions(20, org1Scope, new HashSet<string> { "Boms.Read" });

        // Invalidate user 10 only
        cache.InvalidateUser(10);

        // User 10 should be completely evicted across all scopes
        Assert.False(cache.TryGetGrantedPermissions(10, platformScope, out _));
        Assert.False(cache.TryGetGrantedPermissions(10, org1Scope, out _));

        // User 20 must remain intact
        Assert.True(cache.TryGetGrantedPermissions(20, org1Scope, out var hit20));
        Assert.NotNull(hit20);
        Assert.Contains("Boms.Read", hit20);
    }

    [Fact]
    public void PermissionCache_InvalidateAll_InstantlyClearsAllUsersGlobally()
    {
        var cache = CreateCache();
        var org1Scope = new OrganizationScope.Organization(100);

        cache.SetGrantedPermissions(10, org1Scope, new HashSet<string> { "Inventory.Read" });
        cache.SetGrantedPermissions(20, org1Scope, new HashSet<string> { "Boms.Read" });

        // Invalidate globally due to role permission mutations
        cache.InvalidateAll();

        Assert.False(cache.TryGetGrantedPermissions(10, org1Scope, out _));
        Assert.False(cache.TryGetGrantedPermissions(20, org1Scope, out _));
    }

    [Fact]
    public async Task PermissionCache_ConcurrentReadsAndInvalidations_AreThreadSafe()
    {
        var cache = CreateCache();
        var scope = new OrganizationScope.Organization(100);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var writers = Enumerable.Range(1, 5).Select(i => Task.Run(() =>
        {
            var user = i;
            while (!cts.Token.IsCancellationRequested)
            {
                cache.SetGrantedPermissions(user, scope, new HashSet<string> { $"Perm.{user}" });
                cache.InvalidateUser(user);
            }
        }));

        var readers = Enumerable.Range(1, 5).Select(i => Task.Run(() =>
        {
            var user = i;
            while (!cts.Token.IsCancellationRequested)
            {
                cache.TryGetGrantedPermissions(user, scope, out _);
            }
        }));

        await Task.WhenAll(writers.Concat(readers));
    }
}
