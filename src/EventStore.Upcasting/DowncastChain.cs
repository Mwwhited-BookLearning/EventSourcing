using System.Text.Json.Nodes;

namespace EventStore.Upcasting;

// ADR-028 -- the reverse of UpcastChain: current-shape data, requested back
// down to an explicitly-named older version. Same one-hop-at-a-time
// design, same pluggable IUpcastExpressionEvaluator seam (ADR-053), walked
// in the opposite direction using each version's downcastToPrevious
// instead of upcastFromPrevious. Never materialized (ADR-028's own
// "unbounded potential targets" reasoning) -- always computed fresh,
// read-time, unlike ADR-027's upcasts.
public class DowncastChain(IUpcastExpressionEvaluator evaluator)
{
    public UpcastOutcome Apply(
        IReadOnlyDictionary<int, DowncastableVersion> definitionsByVersion, int fromVersion, int toVersion, JsonNode payload)
    {
        var current = payload;
        for (var version = fromVersion; version > toVersion; version--)
        {
            if (!definitionsByVersion.TryGetValue(version, out var definition) || string.IsNullOrEmpty(definition.DowncastToPrevious))
                // ADR-028 -- unlike UpcastChain's "no upcaster -- pass through
                // unchanged" fallback, downcast has no safe pass-through: an
                // old consumer fed a field it can't parse is exactly the
                // failure mode this feature exists to prevent. Hard stop.
                return new UpcastOutcome.Failed(version, "no downcastToPrevious registered for this hop");

            if (!UpcastExpressionListParser.TryParse(definition.DowncastToPrevious, out var clauses, out var parseError))
                return new UpcastOutcome.Failed(version, parseError!);

            var next = new JsonObject();
            foreach (var clause in clauses)
            {
                JsonNode? result;
                try
                {
                    result = evaluator.Evaluate(clause.Expression, current);
                }
                catch (Exception ex)
                {
                    return new UpcastOutcome.Failed(version, $"expression '{clause.Expression}' failed to evaluate: {ex.Message}");
                }
                next[clause.Alias] = result;
            }
            current = next;
        }

        return new UpcastOutcome.Success(current);
    }
}

// The one field DowncastChain actually needs per source version -- mirrors
// UpcastableVersion, deliberately not the full EventTypeDefinition.
public record DowncastableVersion(int Version, string? DowncastToPrevious);
