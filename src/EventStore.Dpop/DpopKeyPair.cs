using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EventStore.Dpop;

// ADR-017 -- "each of the four OAuth2 clients generates its own asymmetric
// key pair." EC P-256 only (no algorithm agility asked for). This type
// plays the client's own role: hold the private key and mint a fresh,
// signed DPoP proof JWT for every outbound request (RFC 9449 -- a new
// proof per request, never reused).
public sealed class DpopKeyPair
{
    private readonly ECDsa _key;

    public EcJwk PublicJwk { get; }
    public string Thumbprint { get; }

    private DpopKeyPair(ECDsa key, EcJwk publicJwk)
    {
        _key = key;
        PublicJwk = publicJwk;
        Thumbprint = JwkThumbprint.Compute(publicJwk);
    }

    public static DpopKeyPair Generate()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var jwk = new EcJwk(
            EcJwk.KeyType, EcJwk.Curve,
            Base64UrlEncoder.Encode(parameters.Q.X), Base64UrlEncoder.Encode(parameters.Q.Y));
        return new DpopKeyPair(key, jwk);
    }

    // htm/htu per RFC 9449 §4.2: the exact HTTP method and URI (no query/
    // fragment) of the request this proof accompanies. ath is present only
    // on API-request proofs (the hash of the access token being presented);
    // absent on the token-request proof, since no access token exists yet.
    public string CreateProof(string htm, string htu, string? accessToken = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["htm"] = htm,
            ["htu"] = htu,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        if (accessToken is not null)
            claims["ath"] = Base64UrlEncoder.Encode(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(accessToken)));

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(_key), SecurityAlgorithms.EcdsaSha256),
            AdditionalHeaderClaims = new Dictionary<string, object>
            {
                ["typ"] = "dpop+jwt",
                // The header-claim writer (Microsoft.IdentityModel.Tokens.Json.
                // JsonSerializerPrimitives) only knows how to serialize a fixed
                // set of primitive shapes -- an arbitrary POCO like EcJwk isn't
                // one of them (IDX11025), so this is a plain dictionary instead.
                ["jwk"] = new Dictionary<string, object>
                {
                    ["kty"] = PublicJwk.Kty,
                    ["crv"] = PublicJwk.Crv,
                    ["x"] = PublicJwk.X,
                    ["y"] = PublicJwk.Y,
                },
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
