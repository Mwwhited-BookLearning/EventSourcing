[← ADR index](../07-adrs.md)

# ADR-040: URL-embeddable ticket exchange for header-incapable clients

Status: Accepted

Context: `ADR-006`/`ADR-017` assume every caller can set an `Authorization`
(and `DPoP`) HTTP header. Some real callers genuinely can't: an HTML
`<video src>`/`<audio src>` element (`ADR-031`'s streaming channel
playback), a WebDAV client mounting an `ADR-032` share without full
custom-header support, or any URL handed to a component the calling
application doesn't control the request internals of. This isn't a new
problem for this design — `ADR-006` originally carried an
`access_token`-in-URL workaround for exactly this reason (`EventSource`
can't set headers either), and `ADR-012` **removed** it once Follow moved
to `fetch()`-based `QUERY`, specifically because a bare bearer token in a
URL leaks via server access logs, browser history, `Referer` headers, and
proxy/CDN caches. That removal was correct, but streaming/attachment
playback now reintroduces the same class of header-incapable caller —
without a mechanism, the only way to make a `<video src>` URL
authenticate at all would be to repeat the exact mistake `ADR-012` was
right to remove.

Decision:
- **This is a three-hop flow, not a single bearer-token substitute.**
  Every hop reuses a real, already-adopted or independently-established
  mechanism rather than inventing a bespoke protocol:

  1. **Ticket issuance — OAuth 2.0 Token Exchange (RFC 8693), the same
     mechanism `ADR-036` already adopted.** The requesting party (which,
     at this point, still has full header capability — it's the SPA or
     backend service constructing a `<video src>` URL, not the `<video>`
     element itself) makes an ordinary, header-based, DPoP-proved request:
     ```
     POST /oauth/token
     grant_type=urn:ietf:params:oauth:grant-type:token-exchange
     subject_token=<bearer JWT, Authorization header as normal>
     subject_token_type=urn:ietf:params:oauth:token-type:access_token
     requested_token_type=urn:eventstore:token-type:ticket
     client_id=<registered client, ADR-006>       # OR:
     one_time_secret=<caller-generated random value>
     ```
     The IdP returns `{ ticket, expiresIn }` — `ticket` is a short,
     opaque, single-use, cryptographically random string, **deliberately
     not a JWT and not self-describing**: unlike a bearer token, a
     ticket reveals nothing about scopes/identity/claims if intercepted
     on its own, matching the same encapsulation goal `ADR-036` stated
     for UCAN/DID ("no downstream service needs to understand what a
     UCAN or DID even is").
  2. **Client-side signing — the same HMAC signed-URL convention CDNs use
     for token-authenticated content** (Google Cloud CDN/AWS CloudFront
     signed URLs, BunnyCDN/nginx `secure_link` token auth): the *same*
     caller that just received the ticket computes
     `sig = base64url(HMAC-SHA256(ticket, sharedSecret))`, where
     `sharedSecret` is either its already-possessed registered
     `client_secret` (`ADR-006`) or the `one_time_secret` it generated in
     step 1 — never a value transmitted over the header-incapable hop.
     It appends both to the target URL: `.../stream/{id}?ticket=...&sig=...`,
     and only *that* URL — never the bearer token, never the shared
     secret — is handed to the header-incapable component (`<video>.src`,
     a WebDAV path, etc.).
  3. **Resolution — an OAuth 2.0 Token Introspection (RFC 7662)-shaped
     call, extended with the `sig` parameter.** The header-incapable
     component's plain `GET .../stream/{id}?ticket=...&sig=...` lands at
     the Streaming Channel Service (`ADR-031`) or Attachment/WebDAV
     Service (`ADR-032`) exactly as any unauthenticated request would.
     That service holds **no shared secret and performs no signature
     verification itself** — it forwards `ticket`+`sig` to the IdP:
     ```
     POST /oauth/introspect
     token=<ticket>
     token_type_hint=urn:eventstore:token-type:ticket
     sig=<sig>                # extension beyond bare RFC 7662
     ```
     The IdP looks up the ticket, confirms it's unexpired and unused,
     recomputes the HMAC against the secret associated with that ticket,
     and — only if `sig` matches — marks the ticket **consumed** and
     responds `{ active: true, ...the original bearer token's claims }`
     (scope, client_id, `AuthorityStatus`/provenance if a `ADR-036` UCAN
     chain produced the original token). The calling service now has
     "the authenticated chain of access" without ever needing to
     understand tickets, HMAC, or the original token's shape — the exact
     same encapsulation property `ADR-036`'s server-side exchange already
     established.

- **This is CAS-adjacent, not CAS.** The overall shape — a short-lived,
  URL-embeddable ticket that a *receiving* service exchanges with a
  *backend* it trusts, rather than validating locally — is the same idea
  the Central Authentication Service (CAS) protocol calls a **service
  ticket**, validated by the target service calling back to CAS's
  `/serviceValidate` endpoint. This design does not adopt CAS's actual
  protocol (no browser-redirect login flow, no XML response format) —
  only the "ticket now, backend-validated later" shape, built entirely
  from primitives already in this design (RFC 8693, RFC 7662-flavored
  introspection).
- **Single-use and short-lived, deliberately shorter than a normal
  bearer token's lifetime** — the introspection call above consumes the
  ticket on first successful use; a second presentation of the same
  ticket+sig fails even if the TTL hasn't elapsed. This bounds the
  window in which a leaked *complete* URL (ticket and signature together
  — the realistic leak shape, since both travel in the same query
  string) is replayable.
- **Explicitly not a general Bearer/DPoP replacement.** Every ordinary
  API call keeps authenticating exactly as `ADR-006`/`ADR-017` already
  specify. This mechanism exists *only* for the specific, real
  capability gap named above — Streaming Channel playback URLs
  (`ADR-031`) and WebDAV/Attachment retrieval URLs (`ADR-032`) — not as
  an alternative auth path for anything that can already send a header.

Consequences:
- **Honest residual risk, stated rather than hidden**: this reopens a
  *narrower* version of exactly the risk `ADR-012` removed
  (secret-bearing material in a URL). Two threats are defended
  differently, and neither is eliminated outright: if a complete URL
  (ticket **and** signature) leaks together — access logs, `Referer`,
  proxy caches, the same channels the old `access_token` workaround was
  vulnerable to — single-use consumption is what limits the damage (the
  first successful replay burns it; anything after fails), not the
  signature. If instead only the ticket leaks or is guessed *without*
  the signature (e.g. a differently-logged path exposes one but not the
  other), the signature is what stops a forged completion. Neither
  property helps against the other threat — this is why both are
  present, not either alone.
- **The receiving service (Streaming Channel/Attachment Service) never
  holds a shared secret and never verifies a signature itself** — it
  only ever forwards `ticket`+`sig` to the IdP, keeping secret material
  confined to the issuing IdP and the original ticket-requesting caller.
  This mirrors `ADR-036`'s consequence that downstream services "never
  need to understand" the credential's internals.
- **DPoP's proof-of-possession isn't lost, it's consumed one hop
  earlier.** The ticket-issuance request (step 1) is itself a normal,
  header-based, DPoP-bound request — `ADR-017` still applies there in
  full. The HMAC signature is what extends an equivalent
  possession-proof across the one hop that genuinely can't carry a
  header; it is not a weaker substitute for DPoP, it's DPoP's job
  handed off for a transport DPoP itself cannot reach.
- No single formal spec governs the HMAC-signed-URL step — unlike RFC
  8693/7662, this is an established **industry convention** (CDN signed
  URLs, cloud storage presigned URLs), not a numbered standard; recorded
  as such in `references.md`, not overclaimed as spec compliance.
- `EventStore.DevIdp` (`ADR-006`) gains a `Ticket` record in its existing
  in-process, non-persistent OpenIddict-adjacent store — `{ ticket,
  clientIdOrOneTimeSecretRef, expiresAt, consumed, originalTokenClaims }`
  — following `auth.md`'s existing statement that identity/token state
  lives entirely there, not in `EventStoreContext`. Not a new persistence
  concern for the event store itself.
- `03-api-contracts.md`'s auth section, `docs/patterns/README.md`'s
  catalog, and `references.md` need this ADR's mechanism reflected —
  tracked as propagation work, done in this pass for the pattern catalog
  and references, deferred for the full API-contract rewrite consistent
  with `ADR-037`'s already-outstanding contract-doc debt.

**Compliance note** (a proving-ground compliance review, this session):
the single-use, signed ticket mechanism is what makes streaming/
attachment retrieval satisfy HIPAA's Transmission Security standard (45
CFR § 164.312(e)) for a header-incapable hop — the addressable integrity-
controls/encryption specifications that standard calls for are exactly
what the HMAC signature and single-use consumption provide where a bare
`Authorization` header can't reach.
