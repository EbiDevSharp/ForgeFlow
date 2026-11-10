namespace ForgeFlow.Domain.Units;

public sealed record UnitConversionEdge(long FromUnitId, long ToUnitId, decimal Factor, bool IsItemSpecific = false);

public sealed record UnitConversionResolution(decimal Factor, int Hops, decimal ConvertedQuantity = 0m);

/// <summary>
/// Multi-hop unit conversion graph supporting global organization conversions and item-specific overrides (A08).
/// Edge semantics: 1 FromUnit = Factor * ToUnit.
/// </summary>
public static class UnitConversionGraph
{
    private const decimal ConsistencyTolerance = 0.0001m;

    public static UnitConversionResolution ResolveFactor(
        long fromUnitId,
        long toUnitId,
        IEnumerable<UnitConversionEdge> edges)
        => ResolveInternal(1m, fromUnitId, toUnitId, edges, includeQuantity: false);

    public static UnitConversionResolution ConvertQuantity(
        decimal quantity,
        long fromUnitId,
        long toUnitId,
        IEnumerable<UnitConversionEdge> edges)
    {
        if (quantity <= 0m)
            throw new UnitRuleException(UnitErrors.ConversionQuantityInvalid, ("field", "Quantity"));

        return ResolveInternal(quantity, fromUnitId, toUnitId, edges, includeQuantity: true);
    }

    private static UnitConversionResolution ResolveInternal(
        decimal quantity,
        long fromUnitId,
        long toUnitId,
        IEnumerable<UnitConversionEdge> edges,
        bool includeQuantity)
    {
        if (fromUnitId <= 0 || toUnitId <= 0)
            throw new UnitRuleException(UnitErrors.NotFound);
        if (fromUnitId == toUnitId)
        {
            var sameQty = includeQuantity ? decimal.Round(quantity, UnitText.MaxPrecisionScale, MidpointRounding.AwayFromZero) : 0m;
            return new(1m, 0, sameQty);
        }

        ArgumentNullException.ThrowIfNull(edges);

        // Collapse directed pairs deterministically, preferring item-specific edges when both exist.
        var directed = new Dictionary<(long From, long To), UnitConversionEdge>();
        foreach (var edge in edges)
        {
            if (edge.FromUnitId <= 0 || edge.ToUnitId <= 0 || edge.FromUnitId == edge.ToUnitId || edge.Factor <= 0m)
                continue;

            var key = (edge.FromUnitId, edge.ToUnitId);
            if (!directed.TryGetValue(key, out var existing) || (edge.IsItemSpecific && !existing.IsItemSpecific))
                directed[key] = edge;
        }

        // Build bidirectional adjacency list tracking exact rational (Num / Den) multipliers per edge.
        var adjacency = new Dictionary<long, List<(long NextUnitId, decimal Num, decimal Den, bool IsItemSpecific)>>();
        foreach (var edge in directed.Values.OrderByDescending(e => e.IsItemSpecific).ThenBy(e => e.FromUnitId).ThenBy(e => e.ToUnitId))
        {
            AddNeighbor(adjacency, edge.FromUnitId, edge.ToUnitId, edge.Factor, 1m, edge.IsItemSpecific);
            AddNeighbor(adjacency, edge.ToUnitId, edge.FromUnitId, 1m, edge.Factor, edge.IsItemSpecific);
        }

        if (!adjacency.ContainsKey(fromUnitId) || !adjacency.ContainsKey(toUnitId))
            throw new UnitRuleException(UnitErrors.ConversionPathNotFound);

        var visited = new Dictionary<long, (decimal Num, decimal Den, int Hops, bool UsedItemEdge)>
        {
            [fromUnitId] = (1m, 1m, 0, false)
        };
        var queue = new Queue<long>();
        queue.Enqueue(fromUnitId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var state = visited[current];
            if (!adjacency.TryGetValue(current, out var neighbors))
                continue;

            foreach (var (nextUnitId, edgeNum, edgeDen, isItemEdge) in neighbors)
            {
                var (nextNum, nextDen) = MultiplyRational(state.Num, state.Den, edgeNum, edgeDen);
                var candidateHops = state.Hops + 1;
                var candidateItem = state.UsedItemEdge || isItemEdge;

                if (!visited.TryGetValue(nextUnitId, out var existing))
                {
                    visited[nextUnitId] = (nextNum, nextDen, candidateHops, candidateItem);
                    queue.Enqueue(nextUnitId);
                }
                else
                {
                    var existingRatio = existing.Num / existing.Den;
                    var candidateRatio = nextNum / nextDen;
                    if (existing.UsedItemEdge == candidateItem && !WithinTolerance(existingRatio, candidateRatio))
                        throw new UnitRuleException(UnitErrors.ConversionConflictingPath);

                    if (!existing.UsedItemEdge && candidateItem && candidateHops <= existing.Hops)
                    {
                        visited[nextUnitId] = (nextNum, nextDen, candidateHops, true);
                    }
                }
            }
        }

        if (!visited.TryGetValue(toUnitId, out var resolved))
            throw new UnitRuleException(UnitErrors.ConversionPathNotFound);

        var exactFactor = resolved.Num / resolved.Den;
        var roundedFactor = decimal.Round(exactFactor, UnitText.MaxPrecisionScale, MidpointRounding.AwayFromZero);
        var convertedQty = 0m;
        if (includeQuantity)
        {
            var exactQty = (quantity * resolved.Num) / resolved.Den;
            convertedQty = decimal.Round(exactQty, UnitText.MaxPrecisionScale, MidpointRounding.AwayFromZero);
        }

        return new(roundedFactor, resolved.Hops, convertedQty);
    }

    private static (decimal Num, decimal Den) MultiplyRational(decimal numA, decimal denA, decimal numB, decimal denB)
    {
        try
        {
            return (checked(numA * numB), checked(denA * denB));
        }
        catch (OverflowException)
        {
            return ((numA / denA) * (numB / denB), 1m);
        }
    }

    private static void AddNeighbor(
        Dictionary<long, List<(long NextUnitId, decimal Num, decimal Den, bool IsItemSpecific)>> adjacency,
        long from,
        long to,
        decimal num,
        decimal den,
        bool isItemSpecific)
    {
        if (!adjacency.TryGetValue(from, out var list))
        {
            list = [];
            adjacency[from] = list;
        }
        list.Add((to, num, den, isItemSpecific));
    }

    private static bool WithinTolerance(decimal a, decimal b)
    {
        var max = Math.Max(Math.Abs(a), Math.Abs(b));
        if (max == 0m) return true;
        return Math.Abs(a - b) / max <= ConsistencyTolerance;
    }
}
