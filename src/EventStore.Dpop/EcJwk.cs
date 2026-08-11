using System.Text.Json.Serialization;

namespace EventStore.Dpop;

// RFC 9449 always embeds the proof's own public key as a JWK in the proof's
// JOSE header -- this project only ever needs EC P-256 (ADR-017 doesn't ask
// for algorithm agility), so this is a narrow, hand-shaped JWK rather than a
// dependency on a general-purpose JWK type.
public record EcJwk(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("crv")] string Crv,
    [property: JsonPropertyName("x")] string X,
    [property: JsonPropertyName("y")] string Y)
{
    public const string KeyType = "EC";
    public const string Curve = "P-256";
}
