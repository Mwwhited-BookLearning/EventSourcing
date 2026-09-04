using System.Text.Json;
using EventStore.Dpop;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EventStore.Ucan;

public record UcanValidationResult(
    bool IsValid, string? Error, string? GranterActorId, string? GranteeActorId, string? AppId,
    IReadOnlyList<DelegatedCapability>? Capabilities, Guid? GrantRef)
{
    public static UcanValidationResult Failure(string error) => new(false, error, null, null, null, null, null);
}

// ADR-043/044 -- verifies a UcanDelegation's own signature (self-verifying,
// SelfSignedJwtVerifier), then checks exactly one of two things depending
// on whether the delegation carries a `prf` (proof):
//   - WITH a proof: the proof is the granter's own currently-valid access
//     token (issued by this same IdP) -- verified, then every requested
//     capability must already be present in the proof's own claims (the
//     cap invariant: "never broader than what the delegator holds"), and
//     the delegation must have been signed by the SAME key the proof is
//     bound to (cnf.jkt) -- otherwise anyone could forge a delegation
//     "on behalf of" a token they don't actually hold.
//   - WITHOUT a proof: the delegation's own issuer key must itself be a
//     registered AppTrustRoot (ADR-044) for the target AppId -- a root of
//     trust needs no further "subset of X" check, and the capability
//     strings need no central pre-registration at all, exactly this
//     item's own exit criterion.
public static class UcanValidator
{
    // ADR-104 -- isRevoked is the live revocation check added alongside the
    // pre-existing offline checks below: a delegation that passes every
    // offline check but is found revoked still fails validation. Consulted
    // by GrantRef (this delegation's own "jti"), the same identifier
    // ADR-107's ucanDelegationIssued event and EventStore.Rbac's
    // UcanDelegationRevokedEventType both key on. A delegation with no
    // parseable "jti" at all (already tolerated elsewhere in this method,
    // grantRef stays null) skips this check entirely -- an un-identifiable
    // delegation can never be looked up for revocation either way, the
    // same pre-existing tolerance, not a new gap this decision introduces.
    public static async Task<UcanValidationResult> ValidateAsync(
        string delegationJwt,
        Func<Task<IReadOnlyList<SecurityKey>>> getProofSigningKeys,
        Func<string, string, Task<bool>> isTrustedRootThumbprint,
        Func<Guid, Task<bool>>? isRevoked = null)
    {
        var selfVerify = await SelfSignedJwtVerifier.VerifyAsync(delegationJwt, "ucan+jwt");
        if (!selfVerify.IsValid)
            return UcanValidationResult.Failure(selfVerify.Error!);

        var token = selfVerify.Token!;
        if (!token.TryGetPayloadValue<string>("aud", out var grantee) || string.IsNullOrEmpty(grantee))
            return UcanValidationResult.Failure("delegation is missing \"aud\" (the grantee)");
        if (!token.TryGetPayloadValue<string>("appId", out var appId) || string.IsNullOrEmpty(appId))
            return UcanValidationResult.Failure("delegation is missing \"appId\"");
        if (!token.TryGetPayloadValue<string>("cap", out var capJson) || string.IsNullOrEmpty(capJson))
            return UcanValidationResult.Failure("delegation is missing \"cap\"");

        List<DelegatedCapability> requestedCapabilities;
        try
        {
            requestedCapabilities = JsonSerializer.Deserialize<List<DelegatedCapability>>(capJson) ?? [];
        }
        catch (JsonException)
        {
            return UcanValidationResult.Failure("delegation's \"cap\" is not valid JSON");
        }
        if (requestedCapabilities.Count == 0)
            return UcanValidationResult.Failure("delegation names no capabilities");

        var thumbprint = JwkThumbprint.Compute(selfVerify.Jwk!);
        token.TryGetPayloadValue<string>("iss", out var granterActorId);
        var grantRef = token.TryGetPayloadValue<string>("jti", out var jti) && Guid.TryParse(jti, out var parsedJti) ? parsedJti : (Guid?)null;

        if (token.TryGetPayloadValue<string>("prf", out var proofToken) && !string.IsNullOrEmpty(proofToken))
        {
            var signingKeys = await getProofSigningKeys();
            var proofValidation = await new JsonWebTokenHandler().ValidateTokenAsync(proofToken, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                IssuerSigningKeys = signingKeys,
            });
            if (!proofValidation.IsValid)
                return UcanValidationResult.Failure("delegation's own proof token is invalid or expired");

            var proofClaims = proofValidation.ClaimsIdentity!;
            var proofJkt = proofClaims.FindFirst("cnf.jkt")?.Value;
            if (proofJkt != thumbprint)
                return UcanValidationResult.Failure("delegation was not signed by the same key the proof token is bound to");

            foreach (var requested in requestedCapabilities)
            {
                var separatorIndex = requested.Claim.IndexOf(':');
                var claimHeld = separatorIndex > 0 &&
                    proofClaims.Claims.Any(c => c.Type == requested.Claim[..separatorIndex] && c.Value == requested.Claim[(separatorIndex + 1)..]);
                if (!claimHeld)
                    return UcanValidationResult.Failure($"delegation attempts to grant \"{requested.Claim}\", which the granter's own proof does not hold -- over-broad delegation");
            }
        }
        else
        {
            if (!await isTrustedRootThumbprint(appId!, thumbprint))
                return UcanValidationResult.Failure("delegation has no proof and its issuer key is not a registered AppTrustRoot for this AppId");
        }

        if (isRevoked is not null && grantRef is { } grantRefValue && await isRevoked(grantRefValue))
            return UcanValidationResult.Failure("delegation has been revoked");

        return new UcanValidationResult(true, null, granterActorId, grantee, appId, requestedCapabilities, grantRef);
    }
}
