using System.Text.RegularExpressions;

namespace EventStore.Upcasting;

// ADR-018: upcastFromPrevious is "<expression> as <alias>", comma-separated.
// Splits on top-level commas only (depth-tracked, so a comma inside a
// function call's argument list -- e.g. "max(a, b) as X" -- isn't
// mistaken for a clause separator).
public static class UpcastExpressionListParser
{
    private static readonly Regex AsClausePattern = new(
        @"^(?<expr>.+)\s+as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static bool TryParse(string expressionList, out List<UpcastClause> clauses, out string? error)
    {
        clauses = [];
        var depth = 0;
        var start = 0;
        var rawClauses = new List<string>();

        for (var i = 0; i < expressionList.Length; i++)
        {
            switch (expressionList[i])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    rawClauses.Add(expressionList[start..i]);
                    start = i + 1;
                    break;
            }
        }
        rawClauses.Add(expressionList[start..]);

        foreach (var raw in rawClauses)
        {
            var trimmed = raw.Trim();
            var match = AsClausePattern.Match(trimmed);
            if (!match.Success)
            {
                error = $"upcastFromPrevious clause is not in \"<expression> as <alias>\" form: {trimmed}";
                return false;
            }
            clauses.Add(new UpcastClause(match.Groups["expr"].Value.Trim(), match.Groups["alias"].Value));
        }

        error = null;
        return true;
    }
}

public record UpcastClause(string Expression, string Alias);
