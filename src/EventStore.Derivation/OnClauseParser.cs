using System.Text.RegularExpressions;
using EventStore.Domain.SchemaRegistry;

namespace EventStore.Derivation;

// $on's hand-rolled, OData-inspired mini-grammar (ADR-007): a conjunction of
// pairwise equalities, "Source/Field eq Source/Field and Source/Field eq
// Source/Field and ...". Not literal OData -- standard OData has no
// multi-resource join operator. Restricted to a safe identifier-chain
// subset, same reasoning as JsonPathValidation: this is the only shape any
// real example needs, and an unrestricted grammar would be an injection
// surface once these values reach the worker's own queries.
public static class OnClauseParser
{
    private static readonly Regex ConjunctPattern = new(
        @"^(?<leftSource>[A-Za-z_][A-Za-z0-9_]*)/(?<leftField>[A-Za-z_][A-Za-z0-9_]*)\s+eq\s+(?<rightSource>[A-Za-z_][A-Za-z0-9_]*)/(?<rightField>[A-Za-z_][A-Za-z0-9_]*)$",
        RegexOptions.Compiled);

    public static bool TryParse(string on, IReadOnlyList<string> normalizedSources, out List<JoinCondition> conditions, out string? error)
    {
        conditions = [];
        var conjuncts = on.Split(" and ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (conjuncts.Length == 0)
        {
            error = "$on must contain at least one \"Source/Field eq Source/Field\" conjunct";
            return false;
        }

        foreach (var conjunct in conjuncts)
        {
            var match = ConjunctPattern.Match(conjunct);
            if (!match.Success)
            {
                error = $"$on conjunct is not in \"Source/Field eq Source/Field\" form: {conjunct}";
                return false;
            }

            var leftSource = match.Groups["leftSource"].Value.ToLowerInvariant();
            var rightSource = match.Groups["rightSource"].Value.ToLowerInvariant();
            if (!normalizedSources.Contains(leftSource) || !normalizedSources.Contains(rightSource))
            {
                error = $"$on references a source not listed in $from: {conjunct}";
                return false;
            }

            conditions.Add(new JoinCondition
            {
                LeftSource = leftSource,
                LeftField = match.Groups["leftField"].Value,
                RightSource = rightSource,
                RightField = match.Groups["rightField"].Value,
            });
        }

        error = null;
        return true;
    }
}
