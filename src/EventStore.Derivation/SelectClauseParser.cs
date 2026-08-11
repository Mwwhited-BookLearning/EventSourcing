using System.Text.RegularExpressions;
using EventStore.Domain.SchemaRegistry;

namespace EventStore.Derivation;

// $select's mini-grammar (ADR-007): "output:Source/field" pairs, comma-
// separated. Same safe-identifier-chain restriction as OnClauseParser.
public static class SelectClauseParser
{
    private static readonly Regex FieldPattern = new(
        @"^(?<output>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<source>[A-Za-z_][A-Za-z0-9_]*)/(?<sourceField>[A-Za-z_][A-Za-z0-9_]*)$",
        RegexOptions.Compiled);

    public static bool TryParse(string select, IReadOnlyList<string> normalizedSources, out List<SelectField> fields, out string? error)
    {
        fields = [];
        var entries = select.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
        {
            error = "$select must name at least one output field";
            return false;
        }

        foreach (var entry in entries)
        {
            var match = FieldPattern.Match(entry);
            if (!match.Success)
            {
                error = $"$select entry is not in \"output:Source/field\" form: {entry}";
                return false;
            }

            var source = match.Groups["source"].Value.ToLowerInvariant();
            if (!normalizedSources.Contains(source))
            {
                error = $"$select references a source not listed in $from: {entry}";
                return false;
            }

            fields.Add(new SelectField
            {
                OutputField = match.Groups["output"].Value,
                SourceType = source,
                SourceField = match.Groups["sourceField"].Value,
            });
        }

        error = null;
        return true;
    }
}
