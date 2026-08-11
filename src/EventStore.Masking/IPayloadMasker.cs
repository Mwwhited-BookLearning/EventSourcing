using System.Text.Json.Nodes;

namespace EventStore.Masking;

// Claim-checking is injected via hasClaim, not resolved internally -- this
// knows nothing about ClaimsPrincipal, HttpContext, or where the data came
// from (docs/06-solution-structure.md). Async, and takes entityId, because
// ADR-057's crypto-shredding reveal path needs to resolve/decrypt against
// EntityErasureKey (a DB lookup, possibly a Vault call) before a claim
// holder's "value" branch can return real plaintext -- the non-claim-holder
// "masked" branch never touches either and stays synchronous in spirit.
public interface IPayloadMasker
{
    Task<JsonNode?> MaskAsync(JsonNode schema, JsonNode? payload, string? entityId, Func<string, bool> hasClaim, CancellationToken ct = default);
}
