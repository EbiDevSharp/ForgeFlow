using System.Text.Json;

namespace ForgeFlow.Domain.Boms;

/// <summary>Runtime Value Object representing production context parameters (Day 9).</summary>
public sealed class ManufacturingContext
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public ManufacturingContext(IEnumerable<KeyValuePair<string, string>>? entries = null)
    {
        if (entries is null) return;
        foreach (var (key, value) in entries)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                continue;
            values[key.Trim().ToUpperInvariant()] = value.Trim();
        }
    }

    public bool TryGetValue(string dimensionCode, out string value)
    {
        if (string.IsNullOrWhiteSpace(dimensionCode))
        {
            value = string.Empty;
            return false;
        }
        return values.TryGetValue(dimensionCode.Trim().ToUpperInvariant(), out value!);
    }
}

public sealed record BomCandidateCondition(
    string DimensionCode,
    bool DimensionIsActive,
    ApplicabilityOperator Operator,
    string Value);

public sealed record BomCandidateVersion(
    long BomId,
    long BomVersionId,
    int VersionNumber,
    BomVersionStatus Status,
    DateOnly EffectiveFromDate,
    DateOnly? EffectiveToDate,
    bool HasApplicability,
    bool ApplicabilityIsActive,
    IReadOnlyList<BomCandidateCondition> Conditions);

/// <summary>
/// Deterministic BOM Version Resolver enforcing Day 9 applicability, missing-context safety,
/// specificity dominance, A07 candidate-first filtering, and A11 active applicability requirement.
/// </summary>
public static class BomResolutionEngine
{
    public static BomCandidateVersion Resolve(
        DateOnly productionDate,
        ManufacturingContext context,
        IEnumerable<BomCandidateVersion> candidates)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(candidates);

        var matched = new List<(BomCandidateVersion Candidate, HashSet<string> Signatures)>();

        foreach (var candidate in candidates)
        {
            // A07: Only active versions effective on the requested production date are candidates.
            if (candidate.Status != BomVersionStatus.Active)
                continue;
            if (candidate.EffectiveFromDate > productionDate)
                continue;
            if (candidate.EffectiveToDate.HasValue && candidate.EffectiveToDate.Value < productionDate)
                continue;

            // A11: If a version declares an applicability record, it must be active.
            if (candidate.HasApplicability && !candidate.ApplicabilityIsActive)
                continue;

            if (!candidate.HasApplicability || candidate.Conditions.Count == 0)
            {
                matched.Add((candidate, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
                continue;
            }

            if (candidate.Conditions.Any(c => !c.DimensionIsActive))
                continue;

            var allMatch = true;
            var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cond in candidate.Conditions)
            {
                if (!EvaluateCondition(cond, context, out var signature))
                {
                    allMatch = false;
                    break;
                }
                signatures.Add(signature);
            }

            if (allMatch)
                matched.Add((candidate, signatures));
        }

        if (matched.Count == 0)
            throw new BomRuleException(BomErrors.NoActiveBomFound);

        // Day 9 Change 6: Specificity and Dominance (strict superset of condition signatures dominates).
        var nonDominated = new List<BomCandidateVersion>();
        for (var i = 0; i < matched.Count; i++)
        {
            var isDominated = false;
            var sigI = matched[i].Signatures;
            for (var j = 0; j < matched.Count; j++)
            {
                if (i == j) continue;
                var sigJ = matched[j].Signatures;
                if (sigJ.Count > sigI.Count && sigI.IsSubsetOf(sigJ))
                {
                    isDominated = true;
                    break;
                }
            }

            if (!isDominated)
                nonDominated.Add(matched[i].Candidate);
        }

        if (nonDominated.Count == 0)
            throw new BomRuleException(BomErrors.NoActiveBomFound);

        if (nonDominated.Count > 1)
            throw new BomRuleException(BomErrors.AmbiguousResolution);

        return nonDominated[0];
    }

    private static bool EvaluateCondition(
        BomCandidateCondition cond,
        ManufacturingContext context,
        out string signature)
    {
        signature = string.Empty;

        var dim = cond.DimensionCode.Trim().ToUpperInvariant();
        // Day 9 Change 5: Missing context dimension NEVER matches, even for NotEquals / NotIn.
        if (!context.TryGetValue(dim, out var actualValue))
            return false;
        switch (cond.Operator)
        {
            case ApplicabilityOperator.Equals:
            {
                var target = cond.Value.Trim();
                signature = $"{dim}:EQ:{target.ToUpperInvariant()}";
                return string.Equals(actualValue, target, StringComparison.OrdinalIgnoreCase);
            }
            case ApplicabilityOperator.NotEquals:
            {
                var target = cond.Value.Trim();
                signature = $"{dim}:NE:{target.ToUpperInvariant()}";
                return !string.Equals(actualValue, target, StringComparison.OrdinalIgnoreCase);
            }
            case ApplicabilityOperator.In:
            {
                var items = ParseSetValues(cond.Value);
                if (items.Count == 0) return false;
                signature = $"{dim}:IN:{string.Join(",", items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => x.ToUpperInvariant()))}";
                return items.Contains(actualValue);
            }
            case ApplicabilityOperator.NotIn:
            {
                var items = ParseSetValues(cond.Value);
                if (items.Count == 0)
                {
                    signature = $"{dim}:NIN:";
                    return true;
                }
                signature = $"{dim}:NIN:{string.Join(",", items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => x.ToUpperInvariant()))}";
                return !items.Contains(actualValue);
            }
            default:
                return false;
        }
    }

    private static HashSet<string> ParseSetValues(string raw)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(trimmed);
                if (parsed is not null)
                {
                    foreach (var s in parsed)
                    {
                        if (!string.IsNullOrWhiteSpace(s))
                            result.Add(s.Trim());
                    }
                    return result;
                }
            }
            catch (JsonException)
            {
                // Fallback to CSV below.
            }
        }

        foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            result.Add(part);

        return result;
    }
}
