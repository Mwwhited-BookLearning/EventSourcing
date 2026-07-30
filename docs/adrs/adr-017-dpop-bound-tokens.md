[← ADR index](../07-adrs.md)

# ADR-017: DPoP-bound access tokens (RFC 9449)

Status: Accepted — hardens `ADR-006`; built in Phase 10
(`08-build-plan.md`).

Context: `ADR-006` issues plain OAuth2 bearer tokens (Client Credentials
Grant, RFC 6749 §4.4; Bearer Token Usage, RFC 6750). `ADR-012` already
removed the one deliberate token-in-URL leak vector this design had
(Follow's `access_token` query parameter, superseded when Follow moved off
`GET`). What's left is the ordinary risk RFC 6750 itself names in its own
security considerations: a bearer token is usable by *any* party who
possesses it, however it was obtained — a token leaked via logs, a
compromised host, or an SSRF-style relay is fully usable by an attacker,
indistinguishable from the legitimate client, until it expires.

Decision:
- Every access token `EventStore.DevIdp` issues is **DPoP-bound (RFC
  9449)**, not a plain bearer token. Each of the four OAuth2 clients
  (`publisher-client`, `follower-client`, `operator-client`,
  `projections-client` — `ADR-006`/`ADR-015`) generates its own asymmetric
  key pair and proves possession of the private key on every request.
- **Token request**: the client includes a DPoP proof JWT (`typ:
  dpop+jwt`, signed with its private key, carrying `jwk` — its public key
  — plus `htm`/`htu` bound to the token endpoint, `iat`, `jti`) in a
  `DPoP` header on its `POST /connect/token` call. `EventStore.DevIdp`
  embeds a `cnf.jkt` claim (the JWK thumbprint) in the issued access
  token, binding it to that specific key.
- **API request**: the client attaches a fresh DPoP proof (new
  `htm`/`htu` bound to the actual API call, `ath` = hash of the access
  token being presented) alongside `Authorization: Bearer <token>` on
  every request to any `EventStore.Host.<Provider>` endpoint.
- **Resource-server validation** (`EventStore.Host.Core`, alongside the
  existing JWT-bearer validation): verify the proof's signature against
  its own embedded `jwk`; check `htm`/`htu` match the request; check `ath`
  matches the presented token; check the proof's `jwk` thumbprint matches
  the token's `cnf.jkt`; enforce a short proof lifetime via `iat`, tracked
  by `jti` for replay detection.
- **Server-chosen nonce challenge (RFC 9449 §8) is out of scope for v1** —
  this is a dev/POC deployment with a small, fixed set of trusted clients,
  not a public browser-facing token-acquisition surface that needs
  defending against pre-generated-proof attacks.

Consequences:
- Every seeded client now manages a key pair, not just a client secret —
  `DevIdpSeeder` (`ADR-006`) grows a key-generation step; more moving
  parts for a dev/POC identity provider than the client-secret-only model,
  an accepted cost for demonstrating the real mechanism rather than a
  bearer-only story.
- A leaked access token is no longer usable by itself — replaying it with
  a different key produces a proof that fails the `cnf.jkt` check. This is
  the actual value this ADR buys: defense in depth against exactly the
  log/relay-leak scenario RFC 6750 warns about.
- `EventStore.Host.Core`'s JWT-bearer validation now has a second, coupled
  check that must also pass — a request with a technically-valid bearer
  token but a missing/invalid DPoP proof is rejected `401`, a new failure
  mode `03-api-contracts.md`'s Problem Details table (`ADR-013`) must
  cover.
- Client clock skew becomes an operational concern for the first time
  (proof `iat` freshness checking) — nothing else in this design needed
  client/server time agreement.

**Compliance note** (a proving-ground compliance review, this session):
DPoP's proof-of-possession and replay resistance (RFC 9449) are exactly
what NIST SP 800-63B requires of an AAL2+ authenticator — §5.2.8's
mandatory replay resistance and §5.2.5's verifier-impersonation-
resistance binding (strongly and irreversibly binding a channel
identifier to the authenticator output via a client-held private key) —
making this a real identity-assurance uplift over plain bearer tokens,
not just defense-in-depth for its own sake.
