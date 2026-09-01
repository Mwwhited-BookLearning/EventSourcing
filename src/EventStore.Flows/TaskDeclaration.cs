using System.Text.RegularExpressions;

namespace EventStore.Flows;

// ADR-101's "task action" label-text convention -- recognized structurally
// against an ordinary ActionNode.Label by FlowInterpreter, BEFORE the
// explicit actions-delegate registry lookup, so a task declaration can never
// hit "no handler registered": its meaning is data, parsed here, not
// behavior resolved from a dictionary.
//
// Real .puml text: :task "<description>" claim="<claim>" resolvedBy="<EventType>[|<EventType>...]" [correlatedBy="<FieldName>"];
//
// `resolvedBy` accepts a '|'-separated list, reusing this project's existing
// OR-of-list claim idiom (ADR-050) rather than inventing new list syntax --
// needed for flows where more than one event type can resolve the same task
// (e.g. Meridian Workflow C: a sanctions hit is resolved either by
// SarFilingRecorded, or by an authorityDecision rejecting it as a false
// positive).
//
// `correlatedBy` defaults to "targetEventId" (the shared authorityDecision
// shape) and must be the raw JSON payload's own PascalCase property name,
// not a GraphQL-lowered field name -- verify this per flow before landing
// it, the exact class of casing bug spikes/user-flow-dsl/PlantUmlNativeSpike
// already hit once ("\n" vs "\\n").
public sealed record TaskDeclaration(
    string Description,
    string? RequiredClaim,
    IReadOnlyList<string> ResolvedByEventTypes,
    string CorrelatedBy)
{
    private static readonly Regex Pattern = new(
        """^task\s+"(?<description>[^"]*)"\s+claim="(?<claim>[^"]*)"\s+resolvedBy="(?<resolvedBy>[^"]*)"(?:\s+correlatedBy="(?<correlatedBy>[^"]*)")?$""",
        RegexOptions.Compiled);

    public static bool TryParse(string actionLabel, out TaskDeclaration? task)
    {
        var match = Pattern.Match(actionLabel);
        if (!match.Success)
        {
            task = null;
            return false;
        }

        task = new TaskDeclaration(
            match.Groups["description"].Value,
            match.Groups["claim"].Value.Length == 0 ? null : match.Groups["claim"].Value,
            match.Groups["resolvedBy"].Value.Split('|', StringSplitOptions.TrimEntries),
            match.Groups["correlatedBy"].Success ? match.Groups["correlatedBy"].Value : "targetEventId");
        return true;
    }
}
