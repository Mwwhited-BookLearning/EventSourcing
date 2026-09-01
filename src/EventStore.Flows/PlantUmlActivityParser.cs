using System.Text.RegularExpressions;

namespace EventStore.Flows;

// Promoted verbatim from spikes/user-flow-dsl/PlantUmlNativeSpike/
// PlantUmlActivityParser.cs (ADR-101) -- the grammar itself does not grow
// for this promotion. "task"/condition-field conventions live entirely in
// FlowInterpreter, one level up; this parser never sees them, it only ever
// produces plain ActionNode/IfNode/StopNode exactly as it always did.
// Deliberately narrow: throws on anything outside start/stop/:action;/
// if-then-else-endif rather than silently ignoring it, so an unsupported
// diagram construct is a loud, immediate failure, not a quietly wrong
// execution.
public static class PlantUmlActivityParser
{
    private static readonly Regex IfPattern = new(@"^if \((?<cond>.+)\) then \(yes\)$", RegexOptions.Compiled);

    public static IReadOnlyList<ActivityNode> Parse(string source)
    {
        var lines = source
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        var index = 0;
        return ParseBlock(lines, ref index, terminators: null);
    }

    private static List<ActivityNode> ParseBlock(List<string> lines, ref int index, HashSet<string>? terminators)
    {
        var nodes = new List<ActivityNode>();
        while (index < lines.Count)
        {
            var line = lines[index];

            if (terminators is not null && terminators.Contains(line))
                break; // caller consumes it

            switch (line)
            {
                case "@startuml" or "start":
                    index++;
                    continue;
                case "@enduml":
                    index++;
                    return nodes;
                case "stop":
                    nodes.Add(new StopNode());
                    index++;
                    continue;
            }

            if (line.StartsWith(':') && line.EndsWith(';'))
            {
                nodes.Add(new ActionNode(line[1..^1]));
                index++;
                continue;
            }

            var ifMatch = IfPattern.Match(line);
            if (ifMatch.Success)
            {
                index++;
                var thenBranch = ParseBlock(lines, ref index, ["else (no)", "endif"]);
                var elseBranch = new List<ActivityNode>();
                if (index < lines.Count && lines[index] == "else (no)")
                {
                    index++;
                    elseBranch = ParseBlock(lines, ref index, ["endif"]);
                }
                if (index < lines.Count && lines[index] == "endif")
                    index++;
                nodes.Add(new IfNode(ifMatch.Groups["cond"].Value, thenBranch, elseBranch));
                continue;
            }

            throw new NotSupportedException(
                $"Unsupported Activity Diagram line (Option G1's own deliberately narrow subset doesn't cover this): \"{line}\"");
        }
        return nodes;
    }
}
