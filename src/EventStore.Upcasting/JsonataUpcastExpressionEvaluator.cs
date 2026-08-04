using System.Text.Json.Nodes;
using Jsonata.Net.Native;
using Jsonata.Net.Native.Json;

namespace EventStore.Upcasting;

// docs/libraries/dotnet/jsonata-dotnet.md -- ADR-053's second, swappable-
// via-configuration IUpcastExpressionEvaluator, alongside CEL
// (CelUpcastExpressionEvaluator). Jsonata.Net.Native has its own JSON DOM
// (Jsonata.Net.Native.Json.JToken), unrelated to System.Text.Json -- rather
// than hand-writing a CLR-object bridge the way CelUpcastExpressionEvaluator
// does for Cel.NET's variable bindings, round-tripping through the JSON
// text form is the simpler, equally-correct seam here, since JsonataQuery
// only ever needs a JToken in and a JToken out.
public class JsonataUpcastExpressionEvaluator : IUpcastExpressionEvaluator
{
    public bool TryCompile(string expression, out string? error)
    {
        try
        {
            _ = new JsonataQuery(expression);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public JsonNode? Evaluate(string expression, JsonNode sourcePayload)
    {
        var query = new JsonataQuery(expression);

        // ADR-053 calls a registered UpcastFromPrevious expression string
        // "engine-agnostic text" -- for that to be literally true, "event"
        // must resolve under JSONata exactly like CEL's own
        // Decls.NewVar("event", ...) binding does, i.e. as a path segment
        // rather than the JSONata root context itself. Wrapping the payload
        // under a synthetic top-level "event" key makes "event.Amount"
        // resolve identically under both engines for the same source text.
        var wrapped = new JsonObject { ["event"] = sourcePayload.DeepClone() };
        var input = JToken.Parse(wrapped.ToJsonString());
        var result = query.Eval(input);

        // A missing-path expression evaluates to "undefined" in JSONata, a
        // concept this interface has no separate slot for -- folded into the
        // same "legitimate null result" case the interface doc already names.
        if (result.Type == JTokenType.Undefined)
            return null;

        return JsonNode.Parse(result.ToFlatString());
    }
}
