using System.Text.Json.Nodes;

namespace EventStore.Upcasting;

// ADR-018 -- a single generic executor, not N hand-written classes. Pure:
// takes the destination-version definitions it needs as a plain dictionary
// (caller-supplied) rather than resolving them itself, so it stays usable
// both from a bare-EventType-name Follow context (docs/10-open-questions.md
// row 1's own AppId-ambiguity workaround) and an explicit-AppId Publish
// context, without baking either lookup strategy in.
public class UpcastChain(IUpcastExpressionEvaluator evaluator)
{
    public UpcastOutcome Apply(
        IReadOnlyDictionary<int, UpcastableVersion> definitionsByVersion, int fromVersion, int toVersion, JsonNode payload)
    {
        var current = payload;
        for (var version = fromVersion + 1; version <= toVersion; version++)
        {
            if (!definitionsByVersion.TryGetValue(version, out var definition))
                return new UpcastOutcome.Failed(version, "destination schema version not found");

            if (string.IsNullOrEmpty(definition.UpcastFromPrevious))
                continue; // a purely additive hop needs no transform -- payload passes through unchanged

            if (!UpcastExpressionListParser.TryParse(definition.UpcastFromPrevious, out var clauses, out var parseError))
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

// The one field UpcastChain actually needs per destination version --
// deliberately not the full EventTypeDefinition, so this project never needs
// a reference to wherever that type lives.
public record UpcastableVersion(int Version, string? UpcastFromPrevious);

public abstract record UpcastOutcome
{
    public sealed record Success(JsonNode Payload) : UpcastOutcome;

    // FailedAtVersion is the destination version whose hop failed to parse,
    // failed to evaluate, or (a future caller may check) failed to validate.
    public sealed record Failed(int FailedAtVersion, string Reason) : UpcastOutcome;

    private UpcastOutcome() { }
}
