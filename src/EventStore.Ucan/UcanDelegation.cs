using System.Text.Json;
using EventStore.Dpop;

namespace EventStore.Ucan;

// ADR-043/044 -- a delegation is a self-verifying JWT, signed by the
// granter's own key (the same DPoP keypair every seeded client already
// holds, ADR-017 -- this implementation's own honest stand-in for a real
// W3C `did:key`, not full DID document resolution). `prf` carries the
// granter's own currently-valid access token verbatim -- UCAN's own
// "chain of proofs" idea, narrowed to exactly one hop (a second-level
// sub-delegation is not built here, nothing in this item's own exit
// criteria needs one). `prf` is omitted entirely when the granter's own
// key IS a registered AppTrustRoot (ADR-044) -- a root of trust needs no
// further proof, it IS the root.
public static class UcanDelegation
{
    public static string Create(
        DpopKeyPair granterKey, string issuerActorId, string granteeActorId, string appId,
        IReadOnlyList<DelegatedCapability> capabilities, TimeSpan validFor, string? proofToken = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString(), // ADR-045's own AccessLogEntry.GrantRef -- this delegation's own stable identity, unrelated to any proof/access token's own jti
            ["iss"] = issuerActorId,
            ["aud"] = granteeActorId,
            ["appId"] = appId,
            // Serialized as a plain JSON string, not a nested object/array --
            // JsonWebTokenHandler's own claim serializer (Microsoft.IdentityModel.
            // Tokens.Json.JsonSerializerPrimitives) only handles a fixed set of
            // primitive shapes, the same limitation DpopKeyPair's own header-claim
            // comment already found for an arbitrary POCO.
            ["cap"] = JsonSerializer.Serialize(capabilities),
            ["exp"] = DateTimeOffset.UtcNow.Add(validFor).ToUnixTimeSeconds(),
        };
        if (proofToken is not null)
            claims["prf"] = proofToken;

        return granterKey.SignJwt(claims, "ucan+jwt");
    }
}
