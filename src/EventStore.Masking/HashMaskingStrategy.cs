using System.Text.Json.Nodes;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;

namespace EventStore.Masking;

// x-masking: { "strategy": "Hash", "keyId": "..." } -- a keyed HMAC (ADR-009),
// not a bare hash: correlatable across events sharing the same real value,
// without being brute-forceable the way an unsalted hash of a small value
// space (e.g. a 9-digit SSN) would be. Delegates to Microsoft.Extensions.
// Compliance.Redaction's HmacRedactor (ADR-050) -- the same primitive, not a
// second hashing mechanism. keyId selects among the HMAC keys registered at
// startup (MaskingServiceCollectionExtensions.AddMasking) via the
// "MaskingHmacKey" taxonomy -- deliberately a distinct taxonomy from any
// x-masking.regulatoryClassification-driven log redaction (PayloadMasker),
// so a keyId string can never collide with an unrelated classification name.
public sealed class HashMaskingStrategy(IRedactorProvider redactorProvider) : IMaskingStrategy
{
    public const string Taxonomy = "MaskingHmacKey";

    public JsonNode Mask(JsonNode realValue, JsonObject maskingConfig)
    {
        var keyId = maskingConfig["keyId"]?.GetValue<string>()
            ?? throw new InvalidOperationException("x-masking.keyId is required when strategy is \"Hash\"");
        var redactor = redactorProvider.GetRedactor(new DataClassification(Taxonomy, keyId));
        return JsonValue.Create(redactor.Redact(PayloadMasker.ExtractRawText(realValue)));
    }
}
