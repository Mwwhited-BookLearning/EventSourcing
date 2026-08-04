using System.Text.Json.Nodes;

namespace EventStore.Masking;

// Pure: only needs the schema (the registered JsonSchema text, parsed, carrying
// x-masking annotations) and the data. Claim-checking is injected via hasClaim,
// not resolved internally -- this knows nothing about ClaimsPrincipal,
// HttpContext, or where the data came from (docs/06-solution-structure.md).
public interface IPayloadMasker
{
    JsonNode? Mask(JsonNode schema, JsonNode? payload, Func<string, bool> hasClaim);
}
