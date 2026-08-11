using System.Text.Json.Nodes;

namespace EventStore.Masking;

// x-masking: { "strategy": "PartialReveal", "showFirst": 0, "showLast": 4,
// "maskChar": "X", "preserveSeparators": true } -- modeled on PCI-DSS
// Requirement 3.3's plain-language PAN masking (ADR-009). showFirst/
// showLast count real (alphanumeric) characters to reveal from each end;
// everything else becomes maskChar. preserveSeparators keeps literal
// non-alphanumeric characters (e.g. "-") showing through untouched --
// without it, separators are masked the same as any other position.
// Format-preserving; only meaningful for an originally-string property.
public sealed class PartialRevealMaskingStrategy : IMaskingStrategy
{
    public JsonNode Mask(JsonNode realValue, JsonObject maskingConfig)
    {
        var value = realValue.GetValue<string>();
        var showFirst = maskingConfig["showFirst"]?.GetValue<int>() ?? 0;
        var showLast = maskingConfig["showLast"]?.GetValue<int>() ?? 0;
        var maskChar = maskingConfig["maskChar"]?.GetValue<string>() is { Length: > 0 } configured ? configured[0] : 'X';
        var preserveSeparators = maskingConfig["preserveSeparators"]?.GetValue<bool>() ?? false;

        var isAlphaNumeric = value.Select(char.IsLetterOrDigit).ToArray();
        var alphaNumericIndices = Enumerable.Range(0, value.Length).Where(i => isAlphaNumeric[i]).ToList();
        var revealedIndices = new HashSet<int>(alphaNumericIndices.Take(showFirst).Concat(alphaNumericIndices.TakeLast(showLast)));

        var result = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
            result[i] = !isAlphaNumeric[i] && preserveSeparators ? value[i]
                : revealedIndices.Contains(i) ? value[i]
                : maskChar;

        return JsonValue.Create(new string(result));
    }
}
