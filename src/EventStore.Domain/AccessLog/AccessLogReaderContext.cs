using System.Security.Claims;

namespace EventStore.Domain.AccessLog;

// ADR-045 -- every read surface resolves the SAME three facts from the
// caller's own ClaimsPrincipal before writing an AccessLogEntry, rather than
// each call site re-deriving them its own way. "trust_basis"/"grant_ref" are
// set only on a token minted via ADR-043's delegated-grant exchange
// (EventStore.DevIdp's own ExchangeUcanDelegationAsync) -- absent for an
// ordinary, directly-authenticated client_credentials token, which is
// Authoritative by definition (ADR-006 already verified identity
// synchronously). A federated-claims-augmented token (ADR-047) is also
// Authoritative here, deliberately: its claims came from a real, already-
// verified external IdP the caller directly authenticated with, not a
// self-attestation or a delegation chain -- ADR-045's own Attested
// definition names only self-attested UCAN (ADR-036) and delegated grants
// (ADR-043).
public static class AccessLogReaderContext
{
    public static (string ReaderActorId, string ReaderTrustBasis, Guid? GrantRef) Resolve(ClaimsPrincipal user)
    {
        // JwtBearer's own default MapInboundClaims=true remaps the token's
        // "sub" claim to ClaimTypes.NameIdentifier before a resolver ever
        // sees it -- TicketAuthenticationHandler's replayed claims (raw
        // JsonWebTokenHandler validation, never through JwtBearerHandler)
        // keep the literal "sub" name instead, so both are checked (found
        // only by running this: "sub" alone silently fell through to
        // "unauthenticated" for every ordinary Bearer-authenticated call).
        var readerActorId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? "unauthenticated";
        var trustBasisClaim = user.FindFirst("trust_basis")?.Value;
        var readerTrustBasis = trustBasisClaim ?? "Authoritative";
        var grantRef = Guid.TryParse(user.FindFirst("grant_ref")?.Value, out var parsed) ? parsed : (Guid?)null;
        return (readerActorId, readerTrustBasis, grantRef);
    }
}
