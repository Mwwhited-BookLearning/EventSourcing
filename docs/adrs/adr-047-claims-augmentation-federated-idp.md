[← ADR index](../07-adrs.md)

# ADR-047: Claims augmentation for federated/external identity providers

Status: Accepted

Context: `EventStore.DevIdp` (`ADR-006`) has been this design's only
identity provider so far. Direction received this session: support an
**external, already-authoritative** IdP (a corporate SSO, Azure AD,
Okta — anything OIDC-compliant) as the identity source, while still
layering this framework's *own* application-specific claims/roles
(`ADR-044`'s application-defined permissions, `ADR-046`'s roles) on top
— because a federated user store has no reason to know about a
permission vocabulary a specific application defined for itself. The
external IdP is authoritative for *identity*; it was never going to be
authoritative for *this framework's own claim vocabulary*.

This is a real, well-established identity pattern — **claims
augmentation / claims transformation / token enrichment** — the same
shape Azure AD B2C custom policies, ADFS claim rules, and Auth0
Actions/Rules all solve: take an externally-issued, already-trusted
token and enrich it with locally-known claims before it reaches
application authorization checks.

Decision:
- **Reuse OAuth 2.0 Token Exchange (RFC 8693) a third time — the same
  primitive `ADR-036` (UCAN→JWT) and `ADR-040` (ticket issuance) already
  use.** A client holding an externally-issued token calls:
  ```
  POST /oauth/token
  grant_type=urn:ietf:params:oauth:grant-type:token-exchange
  subject_token=<externally-issued access token>
  subject_token_type=urn:ietf:params:oauth:token-type:access_token
  requested_token_type=urn:ietf:params:oauth:token-type:jwt
  ```
  No new grant type, no new endpoint shape — a third use case for an
  already-adopted mechanism.
- **A new, per-`AppId` `TrustedFederationIssuer` registry entry**
  (`docs/data/schema-registry.md`) — `{ AppId, Issuer, JwksUri,
  Description }` — names which external IdP(s) this framework will
  accept a `subject_token` from for a given application, and where to
  fetch that issuer's own signing keys to verify it. Distinct from
  `ADR-044`'s `AppTrustRoot` (DID roots of trust for UCAN capability
  delegation) — a different question ("is this OIDC issuer who it says
  it is" vs. "is this DID authorized to mint capabilities"), so its own
  entity, per this design's own "a new question gets its own field/
  entity" discipline.
- **The exchange verifies the external token against the registered
  issuer's JWKS, then augments — never replaces — its claims**: identity
  claims (`sub`, `email`, `name`, etc.) pass through unchanged; the
  framework looks up that `sub` against its own locally-managed `Role`/
  `UserPermission` records (`ADR-046`) for the target `AppId` and
  **adds** the resulting application-specific claims to the newly-minted
  JWT. The external IdP's own claims are never removed or overridden —
  consistent with `ADR-046`'s "additive only, never restrictive" rule
  for combining permission sources, extended here to combining claim
  *sources*, not just permission grants within one source.
- **`EventStore.DevIdp` remains the default/fallback** when no
  `TrustedFederationIssuer` is configured for an `AppId` — federation is
  additive capability, not a replacement for the dev-mode IdP `ADR-006`
  already established.

Consequences:
- **A local identity mapping is required** — the framework needs some
  way to associate an external `sub` with its own `Role`/`UserPermission`
  records; not designed further here (a simple `sub == ActorId`
  convention would work for the common case, but isn't mandated) —
  flagged to `docs/10-open-questions.md`.
- `docs/data/schema-registry.md` gains `TrustedFederationIssuer` — done
  this pass.
- No change to any existing claim check (`ADR-008`, `ADR-043`,
  `ADR-044`) — by the time a request reaches one, its JWT already
  carries the fully-augmented claim set, identical in shape whether the
  user authenticated against `EventStore.DevIdp` directly or via a
  federated IdP plus this exchange.
- Verifying an external IdP's JWKS introduces a new external network
  dependency at exchange time (fetching/caching signing keys) — the
  same operational shape `ADR-006`'s own `/.well-known/openid-
  configuration` discovery already has, just pointed at a third party
  instead of `EventStore.DevIdp` itself.
