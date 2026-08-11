using System.Text.Json.Nodes;

namespace EventStore.Upcasting;

// ADR-018/037/053 -- the pluggable seam behind the upcast engine. CEL
// (EventStore.Upcasting's CelUpcastExpressionEvaluator) is the only
// registered implementation at this build stage; the keyed, swappable-via-
// configuration registration alongside Jsonata.Net.Native as a second
// option is "Upcast Materialization + Downcast"'s own scope (ADR-053).
public interface IUpcastExpressionEvaluator
{
    // Compile-only check, no evaluation -- used at schema registration time
    // to reject a syntactically-broken expression before any real data is
    // ever run through it (ADR-018).
    bool TryCompile(string expression, out string? error);

    // sourcePayload is the previous hop's fields, exposed to the expression
    // as the "event" variable (e.g. "event.FirstName + ' ' + event.LastName").
    // Null return means the expression evaluated to JSON null, a legitimate
    // result (e.g. a conditional expression), not a failure.
    JsonNode? Evaluate(string expression, JsonNode sourcePayload);
}
