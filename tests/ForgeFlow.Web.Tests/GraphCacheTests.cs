using ForgeFlow.Application.Boms;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Common;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class GraphCacheTests
{
    private static MemoryOrganizationGraphCache CreateCache()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        return new MemoryOrganizationGraphCache(memoryCache);
    }

    [Fact]
    public void GraphCache_UnitEdges_StoresAndRetrieves_PerOrganization()
    {
        var cache = CreateCache();

        var edgesOrg1 = new List<UnitConversionEdge>
        {
            new(1, 2, 1000m, false),
            new(2, 3, 0.001m, false)
        };

        var edgesOrg2 = new List<UnitConversionEdge>
        {
            new(10, 20, 50m, false)
        };

        cache.SetUnitEdges(101, edgesOrg1);
        cache.SetUnitEdges(102, edgesOrg2);

        // Org 101
        Assert.True(cache.TryGetUnitEdges(101, out var hit101));
        Assert.NotNull(hit101);
        Assert.Equal(2, hit101.Count);
        Assert.Equal(1000m, hit101[0].Factor);

        // Org 102
        Assert.True(cache.TryGetUnitEdges(102, out var hit102));
        Assert.NotNull(hit102);
        Assert.Single(hit102);
        Assert.Equal(50m, hit102[0].Factor);

        // Unknown Org
        Assert.False(cache.TryGetUnitEdges(999, out _));
    }

    [Fact]
    public void GraphCache_InvalidateUnitConversions_OnlyInvalidatesTargetOrganization()
    {
        var cache = CreateCache();

        var edgesOrg1 = new List<UnitConversionEdge> { new(1, 2, 1000m, false) };
        var edgesOrg2 = new List<UnitConversionEdge> { new(10, 20, 50m, false) };

        cache.SetUnitEdges(101, edgesOrg1);
        cache.SetUnitEdges(102, edgesOrg2);

        // Invalidate Org 101 only
        cache.InvalidateUnitConversions(101);

        // Org 101 should be a miss
        Assert.False(cache.TryGetUnitEdges(101, out _));

        // Org 102 must remain cached
        Assert.True(cache.TryGetUnitEdges(102, out var hit102));
        Assert.NotNull(hit102);
        Assert.Single(hit102);
    }

    [Fact]
    public void GraphCache_BomDataset_StoresAndRetrieves_PerOrganizationAndItem()
    {
        var cache = CreateCache();

        var ds1 = new BomResolutionDataset(
            1, "ITEM-1", "Finished Good 1", 10, "EA", "ea", true, [], []);
        var ds2 = new BomResolutionDataset(
            2, "ITEM-2", "Finished Good 2", 10, "EA", "ea", true, [], []);
        var ds3OtherOrg = new BomResolutionDataset(
            1, "ITEM-1", "Other Org Good", 10, "EA", "ea", true, [], []);

        cache.SetBomDataset(101, 1, ds1);
        cache.SetBomDataset(101, 2, ds2);
        cache.SetBomDataset(102, 1, ds3OtherOrg);

        // Verify Org 101, Item 1
        Assert.True(cache.TryGetBomDataset(101, 1, out var hit1));
        Assert.NotNull(hit1);
        Assert.Equal("Finished Good 1", hit1.ItemName);

        // Verify Org 101, Item 2
        Assert.True(cache.TryGetBomDataset(101, 2, out var hit2));
        Assert.NotNull(hit2);
        Assert.Equal("Finished Good 2", hit2.ItemName);

        // Verify Org 102, Item 1
        Assert.True(cache.TryGetBomDataset(102, 1, out var hitOther));
        Assert.NotNull(hitOther);
        Assert.Equal("Other Org Good", hitOther.ItemName);

        // Miss on non-existent item
        Assert.False(cache.TryGetBomDataset(101, 999, out _));
    }

    [Fact]
    public void GraphCache_InvalidateBoms_InvalidatesTargetOrganizationOnly()
    {
        var cache = CreateCache();

        var ds1 = new BomResolutionDataset(
            1, "ITEM-1", "FG 1", 10, "EA", "ea", true, [], []);
        var ds2 = new BomResolutionDataset(
            2, "ITEM-2", "FG 2", 10, "EA", "ea", true, [], []);
        var dsOther = new BomResolutionDataset(
            1, "ITEM-1", "FG Other", 10, "EA", "ea", true, [], []);

        cache.SetBomDataset(101, 1, ds1);
        cache.SetBomDataset(101, 2, ds2);
        cache.SetBomDataset(102, 1, dsOther);

        // Invalidate Org 101 BOMs
        cache.InvalidateBoms(101);

        // Both items in Org 101 must be misses
        Assert.False(cache.TryGetBomDataset(101, 1, out _));
        Assert.False(cache.TryGetBomDataset(101, 2, out _));

        // Org 102 must remain cached
        Assert.True(cache.TryGetBomDataset(102, 1, out var hitOther));
        Assert.NotNull(hitOther);
        Assert.Equal("FG Other", hitOther.ItemName);
    }

    [Fact]
    public void GraphCache_InvalidateAll_ClearsAllOrganizations()
    {
        var cache = CreateCache();

        cache.SetUnitEdges(101, [new(1, 2, 1000m, false)]);
        cache.SetBomDataset(101, 1, new BomResolutionDataset(1, "I1", "N1", 10, "EA", "ea", true, [], []));

        cache.InvalidateAll();

        Assert.False(cache.TryGetUnitEdges(101, out _));
        Assert.False(cache.TryGetBomDataset(101, 1, out _));
    }

    [Fact]
    public async Task GraphCache_ConcurrentReadsAndInvalidations_AreThreadSafe()
    {
        var cache = CreateCache();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var writers = Enumerable.Range(1, 5).Select(i => Task.Run(() =>
        {
            var org = (long)i;
            while (!cts.Token.IsCancellationRequested)
            {
                cache.SetUnitEdges(org, [new(1, 2, 10m, false)]);
                cache.SetBomDataset(org, 1, new BomResolutionDataset(1, "I", "N", 1, "E", "e", true, [], []));
                cache.InvalidateUnitConversions(org);
                cache.InvalidateBoms(org);
            }
        }));

        var readers = Enumerable.Range(1, 5).Select(i => Task.Run(() =>
        {
            var org = (long)i;
            while (!cts.Token.IsCancellationRequested)
            {
                cache.TryGetUnitEdges(org, out _);
                cache.TryGetBomDataset(org, 1, out _);
            }
        }));

        await Task.WhenAll(writers.Concat(readers));
    }
}
