using System.Text.Json.Nodes;
using Cel;
using Cel.Checker;
using Cel.Tools;

namespace EventStore.Upcasting;

// docs/libraries/dotnet/cel-dotnet.md -- Cel.NET adopted after spiking it
// against a real compile-and-execute round trip (build-plan item 11).
public class CelUpcastExpressionEvaluator : IUpcastExpressionEvaluator
{
    private readonly ScriptHost _host = ScriptHost.NewBuilder().Build();

    public bool TryCompile(string expression, out string? error)
    {
        try
        {
            _host.BuildScript(expression).WithDeclarations(Decls.NewVar("event", Decls.Dyn)).Build();
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
        var script = _host.BuildScript(expression).WithDeclarations(Decls.NewVar("event", Decls.Dyn)).Build();
        var vars = new Dictionary<string, object> { ["event"] = ToClrValue(sourcePayload)! };
        var result = script.Execute<object>(vars);
        return ToJsonNode(result);
    }

    private static object? ToClrValue(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => obj.ToDictionary(p => p.Key, p => ToClrValue(p.Value)),
        JsonArray arr => arr.Select(ToClrValue).ToList(),
        JsonValue value when value.TryGetValue<string>(out var s) => s,
        JsonValue value when value.TryGetValue<bool>(out var b) => b,
        JsonValue value when value.TryGetValue<long>(out var l) => l,
        JsonValue value when value.TryGetValue<double>(out var d) => d,
        _ => node.ToJsonString(),
    };

    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        float f => JsonValue.Create(f),
        double d => JsonValue.Create(d),
        IDictionary<string, object?> map => new JsonObject(map.Select(p =>
            new KeyValuePair<string, JsonNode?>(p.Key, ToJsonNode(p.Value)))),
        System.Collections.IEnumerable enumerable and not string => new JsonArray(
            enumerable.Cast<object?>().Select(ToJsonNode).ToArray()),
        _ => JsonValue.Create(value.ToString()),
    };
}
