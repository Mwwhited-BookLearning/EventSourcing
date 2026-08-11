using System.Text.RegularExpressions;

namespace EventStore.Persistence;

// FilterableField.JsonPath is documented (04-odata-filter-pushdown.md) as
// following RFC 9535 JSONPath, but every real example anywhere in this design
// is a simple dotted-identifier chain ("$.Amount", "$.Order.Id") -- never
// bracket notation, wildcards, or a filter expression. This design restricts
// registration to that safe subset for two reasons at once: (1) it's the only
// shape any registration-time or query-time mechanism here actually needs to
// support, and (2) a JsonPath flows directly into raw provider DDL at
// registration time (the per-provider index/computed-column migration below)
// and, later, into query pushdown -- an unrestricted grammar would be a real
// injection surface, not just an unsupported-feature gap.
public static class JsonPathValidation
{
    private static readonly Regex SafeIdentifierChain = new(
        @"^\$(\.[A-Za-z_][A-Za-z0-9_]*)+$",
        RegexOptions.Compiled);

    public static bool IsSafe(string jsonPath) => SafeIdentifierChain.IsMatch(jsonPath);

    public static IReadOnlyList<string> Segments(string jsonPath) =>
        jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
}
