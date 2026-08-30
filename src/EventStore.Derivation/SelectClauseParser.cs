using System.Text.RegularExpressions;
using EventStore.Domain.SchemaRegistry;
using EventStore.Upcasting;

namespace EventStore.Derivation;

// $select's mini-grammar (ADR-007): "output:Source/field" pairs, comma-
// separated. Same safe-identifier-chain restriction as OnClauseParser.
// TODO.md's "Calculated fields" extension adds a second entry form,
// "output:=expression" (ADR-007 addendum), whose expression is arbitrary
// engine-agnostic text (ADR-053) rather than the safe identifier-chain
// subset -- deliberately, the same way UpcastFromPrevious's own expression
// text already is, since it never reaches a query/injection surface, only
// IUpcastExpressionEvaluator.
public static class SelectClauseParser
{
    private static readonly Regex FieldPattern = new(
        @"^(?<output>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<source>[A-Za-z_][A-Za-z0-9_]*)/(?<sourceField>[A-Za-z_][A-Za-z0-9_]*)$",
        RegexOptions.Compiled);

    private static readonly Regex CalculatedFieldPattern = new(
        @"^(?<output>[A-Za-z_][A-Za-z0-9_]*)\s*:=\s*(?<expression>.+)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static bool TryParse(
        string select, IReadOnlyList<string> normalizedSources, IUpcastExpressionEvaluator expressionEvaluator,
        out List<SelectField> fields, out string? error)
    {
        fields = [];
        var entries = SplitTopLevel(select);
        if (entries.Count == 0)
        {
            error = "$select must name at least one output field";
            return false;
        }

        foreach (var entry in entries)
        {
            var mapMatch = FieldPattern.Match(entry);
            if (mapMatch.Success)
            {
                var source = mapMatch.Groups["source"].Value.ToLowerInvariant();
                if (!normalizedSources.Contains(source))
                {
                    error = $"$select references a source not listed in $from: {entry}";
                    return false;
                }

                fields.Add(new SelectField
                {
                    OutputField = mapMatch.Groups["output"].Value,
                    SourceType = source,
                    SourceField = mapMatch.Groups["sourceField"].Value,
                });
                continue;
            }

            var calcMatch = CalculatedFieldPattern.Match(entry);
            if (calcMatch.Success)
            {
                var expression = calcMatch.Groups["expression"].Value.Trim();
                if (!expressionEvaluator.TryCompile(expression, out var compileError))
                {
                    error = $"$select calculated field \"{calcMatch.Groups["output"].Value}\" failed to compile: {compileError}";
                    return false;
                }

                fields.Add(new SelectField
                {
                    OutputField = calcMatch.Groups["output"].Value,
                    Expression = expression,
                });
                continue;
            }

            error = $"$select entry is not in \"output:Source/field\" or \"output:=expression\" form: {entry}";
            return false;
        }

        error = null;
        return true;
    }

    // Comma-separated like the rest of this DSL, but a calculated field's
    // expression can itself legitimately contain a comma (a JSONata/CEL
    // function call's argument list, e.g. "Total:=$sum(event.a, event.b)")
    // -- naive Split(',') would cut that expression in half. Tracks
    // paren/bracket nesting and double-quoted string literals so only a
    // genuinely top-level comma ends an entry.
    private static List<string> SplitTopLevel(string select)
    {
        var parts = new List<string>();
        var depth = 0;
        var inString = false;
        var start = 0;
        for (var i = 0; i < select.Length; i++)
        {
            var c = select[i];
            if (inString)
            {
                if (c == '"' && select[i - 1] != '\\')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(select[start..i]);
                    start = i + 1;
                    break;
            }
        }
        parts.Add(select[start..]);

        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }
}
