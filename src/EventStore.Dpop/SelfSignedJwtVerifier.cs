using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EventStore.Dpop;

// The "embed the public JWK in the JOSE header, verify the signature
// against that same embedded key" shape RFC 9449 established for a DPoP
// proof (DpopProofValidator), factored out as its own small, reusable
// primitive -- "Delegated Grants" (EventStore.Ucan) needs the identical
// self-verifying check for a UCAN delegation, which has none of DPoP's own
// htm/htu/iat/jti/ath freshness semantics layered on top. DpopProofValidator
// itself is left untouched (already proven, no reason to risk it) --
// this is a new, narrower primitive both can eventually share.
public static class SelfSignedJwtVerifier
{
    public record Result(bool IsValid, string? Error, EcJwk? Jwk, JsonWebToken? Token)
    {
        public static Result Failure(string error) => new(false, error, null, null);
    }

    public static async Task<Result> VerifyAsync(string jwt, string expectedTyp)
    {
        JsonWebToken token;
        try
        {
            token = new JsonWebToken(jwt);
        }
        catch (Exception ex)
        {
            return Result.Failure($"malformed JWT: {ex.Message}");
        }

        if (!token.TryGetHeaderValue<string>("typ", out var typ) || typ != expectedTyp)
            return Result.Failure($"JWT header \"typ\" must be \"{expectedTyp}\" (got: {typ ?? "<missing>"})");

        if (!token.TryGetHeaderValue<JsonElement>("jwk", out var jwkElement))
            return Result.Failure("JWT header is missing an embedded \"jwk\"");

        EcJwk jwk;
        try
        {
            jwk = new EcJwk(
                jwkElement.GetProperty("kty").GetString()!,
                jwkElement.GetProperty("crv").GetString()!,
                jwkElement.GetProperty("x").GetString()!,
                jwkElement.GetProperty("y").GetString()!);
        }
        catch (Exception)
        {
            return Result.Failure("JWT's embedded \"jwk\" is not a valid EC public key");
        }
        if (jwk.Kty != EcJwk.KeyType || jwk.Crv != EcJwk.Curve)
            return Result.Failure($"embedded jwk must be kty=EC, crv=P-256 (got kty={jwk.Kty}, crv={jwk.Crv})");

        var ecdsa = System.Security.Cryptography.ECDsa.Create(new System.Security.Cryptography.ECParameters
        {
            Curve = System.Security.Cryptography.ECCurve.NamedCurves.nistP256,
            Q = new System.Security.Cryptography.ECPoint
            {
                X = Base64UrlEncoder.DecodeBytes(jwk.X),
                Y = Base64UrlEncoder.DecodeBytes(jwk.Y),
            },
        });

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(jwt, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new ECDsaSecurityKey(ecdsa),
        });
        if (!validation.IsValid)
            return Result.Failure($"signature invalid or expired: {validation.Exception?.Message}");

        return new Result(true, null, jwk, token);
    }
}
