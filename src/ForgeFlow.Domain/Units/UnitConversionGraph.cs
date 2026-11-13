using System.Numerics;

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
        var adjacency = new Dictionary<long, List<(long NextUnitId, BigInteger Num, BigInteger Den, bool IsItemSpecific)>>();
        foreach (var edge in directed.Values.OrderByDescending(e => e.IsItemSpecific).ThenBy(e => e.FromUnitId).ThenBy(e => e.ToUnitId))
        {
            var (factorNum, factorDen) = DecimalToRational(edge.Factor);
            AddNeighbor(adjacency, edge.FromUnitId, edge.ToUnitId, factorNum, factorDen, edge.IsItemSpecific);
            AddNeighbor(adjacency, edge.ToUnitId, edge.FromUnitId, factorDen, factorNum, edge.IsItemSpecific);
        }

        if (!adjacency.ContainsKey(fromUnitId) || !adjacency.ContainsKey(toUnitId))
            throw new UnitRuleException(UnitErrors.ConversionPathNotFound);

        var visited = new Dictionary<long, (BigInteger Num, BigInteger Den, int Hops, bool UsedItemEdge)>
        {
            [fromUnitId] = (BigInteger.One, BigInteger.One, 0, false)
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
                    var existingRatio = RationalToDecimal(existing.Num, existing.Den);
                    var candidateRatio = RationalToDecimal(nextNum, nextDen);
                    if (existing.UsedItemEdge == candidateItem && !WithinTolerance(existingRatio, candidateRatio))
                        throw new UnitRuleException(UnitErrors.ConversionConflictingPath);

                    if (!existing.UsedItemEdge && candidateItem && candidateHops <= existing.Hops)
                    {
                        visited[nextUnitId] = (nextNum, nextDen, candidateHops, true);
                        queue.Enqueue(nextUnitId);
                    }
                }
            }
        }

        if (!visited.TryGetValue(toUnitId, out var resolved))
            throw new UnitRuleException(UnitErrors.ConversionPathNotFound);

        var exactFactor = RationalToDecimal(resolved.Num, resolved.Den);
        var roundedFactor = decimal.Round(exactFactor, UnitText.MaxPrecisionScale, MidpointRounding.AwayFromZero);
        var convertedQty = 0m;
        if (includeQuantity)
        {
            var (qtyNum, qtyDen) = DecimalToRational(quantity);
            var (prodNum, prodDen) = MultiplyRational(qtyNum, qtyDen, resolved.Num, resolved.Den);
            var exactQty = RationalToDecimal(prodNum, prodDen);
            convertedQty = decimal.Round(exactQty, UnitText.MaxPrecisionScale, MidpointRounding.AwayFromZero);
        }

        return new(roundedFactor, resolved.Hops, convertedQty);
    }

    private static (BigInteger Num, BigInteger Den) MultiplyRational(BigInteger numA, BigInteger denA, BigInteger numB, BigInteger denB)
        => Reduce(numA * numB, denA * denB);

    private static (BigInteger Num, BigInteger Den) DecimalToRational(decimal value)
    {
        var bits = decimal.GetBits(value);
        var low = (uint)bits[0];
        var mid = (uint)bits[1];
        var high = (uint)bits[2];
        var flags = bits[3];
        var scale = (flags >> 16) & 0x7F;
        var negative = (flags & unchecked((int)0x80000000)) != 0;

        var mantissa = ((BigInteger)high << 64) | ((BigInteger)mid << 32) | low;
        if (negative) mantissa = -mantissa;
        return Reduce(mantissa, BigInteger.Pow(10, scale));
    }

    private static (BigInteger Num, BigInteger Den) Reduce(BigInteger num, BigInteger den)
    {
        if (num.IsZero) return (BigInteger.Zero, BigInteger.One);
        if (den < 0) { num = -num; den = -den; }
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(num), den);
        return gcd > BigInteger.One ? (num / gcd, den / gcd) : (num, den);
    }

    private static decimal RationalToDecimal(BigInteger num, BigInteger den)
    {
        if (num.IsZero) return 0m;
        if (BigInteger.Abs(num) <= (BigInteger)decimal.MaxValue && den <= (BigInteger)decimal.MaxValue)
            return (decimal)num / (decimal)den;

        var sign = num.Sign;
        var absNum = BigInteger.Abs(num);
        for (var scale = 18; scale >= 0; scale -= 6)
        {
            var factor = BigInteger.Pow(10, scale);
            var (q, r) = BigInteger.DivRem(absNum * factor, den);
            if (r * 2 >= den) q += 1;
            if (q <= (BigInteger)decimal.MaxValue)
            {
                var res = (decimal)q / (decimal)factor;
                return sign < 0 ? -res : res;
            }
        }
        return (decimal)(num / den);
    }

    private static void AddNeighbor(
        Dictionary<long, List<(long NextUnitId, BigInteger Num, BigInteger Den, bool IsItemSpecific)>> adjacency,
        long from,
        long to,
        BigInteger num,
        BigInteger den,
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
