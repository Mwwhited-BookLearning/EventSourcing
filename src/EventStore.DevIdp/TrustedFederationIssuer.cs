namespace EventStore.DevIdp;

// ADR-047 -- which external, already-authoritative OIDC IdP(s) this
// framework accepts a Token Exchange subject_token from, for a given
// AppId, and where to fetch that issuer's own signing keys. Distinct from
// AppTrustRoot (a different question -- "is this OIDC issuer who it says
// it is" vs. "is this DID authorized to mint UCAN capabilities").
public class TrustedFederationIssuer
{
    public string AppId { get; set; } = default!;
    public string Issuer { get; set; } = default!;
    public string JwksUri { get; set; } = default!;
    public string? Description { get; set; }
}

// ADR-047's own resolved open question (docs/comparisons/federated-
// identity-mapping.md): (Issuer, Sub) together, never bare Sub, is the
// only stable identifier OpenID Connect lets a relying party treat as
// such -- a second TrustedFederationIssuer for the same AppId could
// otherwise collide two different people's Sub values onto one ActorId.
// Populated via lightweight JIT provisioning at exchange time: first-seen
// (Issuer, Sub) mints a new ActorId, a repeat lookup reuses it.
public class FederatedIdentityMapping
{
    public string AppId { get; set; } = default!;
    public string Issuer { get; set; } = default!;
    public string Sub { get; set; } = default!;
    public string ActorId { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
}
