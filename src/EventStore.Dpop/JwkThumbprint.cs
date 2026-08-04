using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace EventStore.Dpop;

// RFC 7638 JWK thumbprint -- SHA-256 over the canonical JSON of exactly the
// "required members" (for an EC key: crv, kty, x, y), lexicographically
// ordered, no whitespace. This is the value RFC 9449 calls `jkt`: embedded
// as the access token's `cnf.jkt` claim at issuance, and recomputed from
// each API-call proof's own embedded jwk to confirm it's the same key.
public static class JwkThumbprint
{
    public static string Compute(EcJwk jwk)
    {
        var canonical = $"{{\"crv\":\"{jwk.Crv}\",\"kty\":\"{jwk.Kty}\",\"x\":\"{jwk.X}\",\"y\":\"{jwk.Y}\"}}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64UrlEncoder.Encode(hash);
    }
}
