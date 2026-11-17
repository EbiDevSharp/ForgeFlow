using System.Collections.Concurrent;
using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Units;
using Microsoft.Extensions.Caching.Memory;

namespace ForgeFlow.Infrastructure.Common;

/// <summary>
/// Thread-safe in-memory cache for compiled unit conversion edges and BOM resolution datasets,
/// partitioned by OrganizationId with atomic version tokens for immediate tenant-isolated invalidation.
/// </summary>
public sealed class MemoryOrganizationGraphCache(IMemoryCache cache) : IOrganizationGraphCache
{
    private readonly ConcurrentDictionary<long, int> _unitVersions = new();
    private readonly ConcurrentDictionary<long, int> _bomVersions = new();
    private int _globalVersion = 1;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public bool TryGetUnitEdges(long organizationId, out IReadOnlyList<UnitConversionEdge>? edges)
    {
        var version = _unitVersions.GetOrAdd(organizationId, 1);
        var key = $"unit-graph:{_globalVersion}:{organizationId}:v{version}";
        return cache.TryGetValue(key, out edges);
    }

    public void SetUnitEdges(long organizationId, IReadOnlyList<UnitConversionEdge> edges, TimeSpan? ttl = null)
    {
        var version = _unitVersions.GetOrAdd(organizationId, 1);
        var key = $"unit-graph:{_globalVersion}:{organizationId}:v{version}";
        cache.Set(key, edges, ttl ?? DefaultTtl);
    }

    public void InvalidateUnitConversions(long organizationId)
    {
        _unitVersions.AddOrUpdate(organizationId, 2, (_, v) => v + 1);
    }

    public bool TryGetBomDataset(long organizationId, long itemId, out BomResolutionDataset? dataset)
    {
        var version = _bomVersions.GetOrAdd(organizationId, 1);
        var key = $"bom-ds:{_globalVersion}:{organizationId}:item{itemId}:v{version}";
        return cache.TryGetValue(key, out dataset);
    }

    public void SetBomDataset(long organizationId, long itemId, BomResolutionDataset dataset, TimeSpan? ttl = null)
    {
        var version = _bomVersions.GetOrAdd(organizationId, 1);
        var key = $"bom-ds:{_globalVersion}:{organizationId}:item{itemId}:v{version}";
        cache.Set(key, dataset, ttl ?? DefaultTtl);
    }

    public void InvalidateBoms(long organizationId)
    {
        _bomVersions.AddOrUpdate(organizationId, 2, (_, v) => v + 1);
    }

    public void InvalidateAll()
    {
        Interlocked.Increment(ref _globalVersion);
    }
}
