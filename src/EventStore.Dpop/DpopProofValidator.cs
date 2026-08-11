using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EventStore.Dpop;

// ADR-017 -- the resource-server (and token-endpoint) side of RFC 9449:
// given a submitted DPoP proof and what this specific request expects
// (method/URI, and the presented access token if any), verify it's a
// fresh, correctly-signed proof of possession of the key embedded in its
// own header. Self-contained by design (RFC 9449 §4.3) -- the proof's own
// `jwk` header is both what's checked against and what's used to verify
// the signature; nothing about the signing key is known ahead of time.
public static class DpopProofValidator
{
    private static readonly TimeSpan MaxProofAge = TimeSpan.FromSeconds(60);

    public record Result(bool IsValid, string? Error, string? Jkt)
    {
        public static Result Failure(string error) => new(false, error, null);
        public static Result Success(string jkt) => new(true, null, jkt);
    }

    public static async Task<Result> ValidateAsync(
        string? proofHeader, string expectedHtm, string expectedHtu, string? expectedAth, IDpopReplayCache replayCache)
    {
        if (string.IsNullOrEmpty(proofHeader))
            return Result.Failure("missing DPoP proof");

        JsonWebToken proof;
        try
        {
            proof = new JsonWebToken(proofHeader);
        }
        catch (Exception ex)
        {
            return Result.Failure($"malformed DPoP proof: {ex.Message}");
        }

        if (!proof.TryGetHeaderValue<string>("typ", out var typ) || typ != "dpop+jwt")
            return Result.Failure("DPoP proof header \"typ\" must be \"dpop+jwt\"");

        if (!proof.TryGetHeaderValue<JsonElement>("jwk", out var jwkElement))
            return Result.Failure("DPoP proof header is missing an embedded \"jwk\"");

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
            return Result.Failure("DPoP proof's embedded \"jwk\" is not a valid EC public key");
        }
        if (jwk.Kty != EcJwk.KeyType || jwk.Crv != EcJwk.Curve)
            return Result.Failure($"DPoP proof's embedded jwk must be kty=EC, crv=P-256 (got kty={jwk.Kty}, crv={jwk.Crv})");

        var ecdsa = System.Security.Cryptography.ECDsa.Create(new System.Security.Cryptography.ECParameters
        {
            Curve = System.Security.Cryptography.ECCurve.NamedCurves.nistP256,
            Q = new System.Security.Cryptography.ECPoint
            {
                X = Base64UrlEncoder.DecodeBytes(jwk.X),
                Y = Base64UrlEncoder.DecodeBytes(jwk.Y),
            },
        });

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(proofHeader, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false, // freshness is checked below via "iat", not "exp" -- a DPoP proof carries neither nbf nor exp
            IssuerSigningKey = new ECDsaSecurityKey(ecdsa),
        });
        if (!validation.IsValid)
            return Result.Failure($"DPoP proof signature invalid: {validation.Exception?.Message}");

        if (!proof.TryGetPayloadValue<string>("htm", out var htm) || !string.Equals(htm, expectedHtm, StringComparison.OrdinalIgnoreCase))
            return Result.Failure($"DPoP proof \"htm\" does not match the request (expected {expectedHtm})");

        if (!proof.TryGetPayloadValue<string>("htu", out var htu) || htu != expectedHtu)
            return Result.Failure($"DPoP proof \"htu\" does not match the request (expected {expectedHtu})");

        if (!proof.TryGetPayloadValue<long>("iat", out var iat))
            return Result.Failure("DPoP proof is missing \"iat\"");
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat);
        var now = DateTimeOffset.UtcNow;
        if (issuedAt < now - MaxProofAge || issuedAt > now + MaxProofAge)
            return Result.Failure("DPoP proof \"iat\" is outside the allowed freshness window");

        if (!proof.TryGetPayloadValue<string>("jti", out var jti) || string.IsNullOrEmpty(jti))
            return Result.Failure("DPoP proof is missing \"jti\"");
        if (!replayCache.TryRegister(jti, issuedAt + MaxProofAge))
            return Result.Failure("DPoP proof \"jti\" has already been used (replay)");

        if (expectedAth is not null)
        {
            if (!proof.TryGetPayloadValue<string>("ath", out var ath) || ath != expectedAth)
                return Result.Failure("DPoP proof \"ath\" does not match the presented access token");
        }

        return Result.Success(JwkThumbprint.Compute(jwk));
    }
}
