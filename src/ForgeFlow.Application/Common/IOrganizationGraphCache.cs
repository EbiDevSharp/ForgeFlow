using ForgeFlow.Application.Boms;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Common;

/// <summary>
/// Thread-safe cache for compiled unit conversion edges and BOM resolution datasets,
/// strictly partitioned by OrganizationId with deterministic immediate invalidation.
/// </summary>
public interface IOrganizationGraphCache
{
    // --- Unit Conversions ---
    bool TryGetUnitEdges(long organizationId, out IReadOnlyList<UnitConversionEdge>? edges);
    void SetUnitEdges(long organizationId, IReadOnlyList<UnitConversionEdge> edges, TimeSpan? ttl = null);
    void InvalidateUnitConversions(long organizationId);

    // --- BOM Resolution Datasets ---
    bool TryGetBomDataset(long organizationId, long itemId, out BomResolutionDataset? dataset);
    void SetBomDataset(long organizationId, long itemId, BomResolutionDataset dataset, TimeSpan? ttl = null);
    void InvalidateBoms(long organizationId);

    // Global clear
    void InvalidateAll();
}
