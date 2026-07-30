[← ADR index](../07-adrs.md)

# ADR-066: Digital sign-off for regulated actions — RFC 9470 step-up authentication + an envelope `Signature` object

Status: Accepted — resolves the electronic-signature open question `docs/10-open-questions.md` tracked, in favor of the framework level

Context: `docs/10-open-questions.md` asked whether non-repudiation/
electronic signatures (FDA 21 CFR Part 11-shaped) belong at the
framework level or the domain/application level. Direction received
this session: **framework level** — a real, generalized feature,
triggered when "additional sign-off is required," backed by a secondary
authorization step (password re-entry, one-time code, or another
secondary factor).

Searched real prior art before designing anything bespoke, per this
project's standing convention: **[RFC 9470 — OAuth 2.0 Step Up
Authentication Challenge Protocol](https://www.rfc-editor.org/rfc/rfc9470.html)**
is exactly this need, standardized — a resource server (here, the
Inbox) that decides a request's existing authentication isn't strong or
recent enough responds with a challenge naming the required `acr_values`
(Authentication Context Class Reference) and/or `max_age`; the client
takes the caller back through the authorization server to re-
authenticate at the required strength, then retries with a token that
satisfies it. **The framework never implements password/OTP/WebAuthn
verification itself** — that's the IdP's job, already `ADR-006`'s
concern (`EventStore.DevIdp` in dev, a real IdP in production), exactly
the same division of responsibility this design already keeps between
"verifying identity" (the IdP) and "deciding what a verified identity is
allowed to do" (this framework's claims/RBAC layers).

Decision:
- **`EventTypeDefinition` gains an optional `RequiredSignature`**
  (`schema-registry.md`): `{ AcrValues: ["urn:...:step-up"], MaxAge:
  300 }` — registered per event type the same way `RequiredPublishClaim`
  already is. An event type with no `RequiredSignature` is completely
  unaffected; this is purely additive.
- **Publish-time enforcement via RFC 9470**: if a publish targets a
  `RequiredSignature`-configured event type and the caller's current
  token doesn't carry an `acr` claim meeting the configured
  `AcrValues`/isn't recent enough for `MaxAge`, the Inbox responds with
  RFC 9470's challenge (`WWW-Authenticate` header naming the required
  `acr_values`/`max_age`) instead of accepting the publish. The client
  redirects the caller through the IdP to step up — however that IdP
  implements it (password re-entry, TOTP, WebAuthn, or a combination) —
  and retries with the resulting token.
- **A new envelope field, `Signature`, distinct from `ActorId`/
  `AttestedActorId` per this design's own "a new relationship gets its
  own field" convention** (`parentEventIds`/`MaterializationOfEventId`/
  `TelemetryPointer`/`AttachmentRef`/`erasureScope` are the other five):
  `{ SignerId (denormalized copy of ActorId, kept explicit rather than
  implied), SignedAt, Meaning, Acr }`. **`Meaning` is required, rejected
  if absent** — the signer's stated reason (e.g., `"reviewed"`,
  `"approved"`, `"authorship"`) — satisfying 21 CFR Part 11 §11.50's
  three linked elements directly: printed name (`SignerId`, already
  captured by `ADR-064`), date/time (`SignedAt`), and meaning (this
  field). `Acr` records which authentication context the sign-off was
  actually performed under, for later audit.
- **Non-repudiation reuses the existing hash chain, no new primitive**:
  `Signature` is envelope metadata on the same `StoredEvent` that
  `ADR-019`'s `ChainHash` already covers — a signed record's signature
  is exactly as tamper-evident as everything else in the log, not a
  separately-secured artifact.
- **`SignerId`/`Signature` are categorically exempt from `ADR-057`'s
  erasure, by deliberate legal reasoning, not by accident.** A
  design-review pass this session flagged that `ADR-057`'s crypto-
  shredding structurally can't reach envelope fields (it only encrypts
  `x-masking`-classified `Payload` fields) — which happened to protect
  `Signature` already, but for the wrong reason ("we never built a path
  there") rather than the right one. **The right reason, checked against
  the actual regulation rather than assumed**: [GDPR Article
  17(3)](https://gdpr-info.eu/art-17-gdpr/) lists real exemptions to the
  right to erasure, and two apply directly to a regulated signature —
  **17(3)(b)**, compliance with a legal obligation requiring processing
  (the retention duty `21 CFR Part 11`/`ICH GCP`-shaped records already
  carry), and **17(3)(e)**, establishment, exercise, or defence of legal
  claims (a signature's entire purpose). A signature only has
  evidentiary value *because* it's tied to a specific, verified,
  un-erasable identity — erasing `SignerId` would defeat the reason
  `Signature` exists at all. This is now a **stated, reasoned exemption**
  for any event type with `RequiredSignature` configured, not an
  incidental side effect of where `ADR-057`'s encryption happens to
  reach.

Consequences:
- Resolves `docs/10-open-questions.md`'s electronic-signature row.
- `docs/data/event-log.md`'s `StoredEvent` gains `Signature`;
  `docs/data/schema-registry.md`'s `EventTypeDefinition` gains
  `RequiredSignature` — done this pass.
- **A publish rejected for insufficient `acr`/`max_age` is a real,
  distinguishable outcome** — this is the one new case since `ADR-023`'s
  persist-everything posture where a publish can be legitimately turned
  away before it's stored, alongside the existing "envelope itself is
  unparseable" exception `ADR-023` already carves out. Stated
  explicitly so it isn't read as quietly reintroducing reject-on-
  invalid: the *event's own data* is never rejected for shape/content
  reasons (unchanged); only *insufficient authentication strength* for a
  signature-required type short-circuits before storage, the same way a
  scope check already does (`ADR-006`).
- No new library — `RFC 9470`'s challenge/response shape is plain HTTP
  headers, implementable directly against `ADR-006`'s existing
  OAuth2/OIDC stack with no additional dependency.
- Which specific `acr_values` taxonomy a deployment uses (NIST AAL-style
  levels, an IdP-specific scheme, or something else) is deployment
  configuration, the same way `ADR-058`'s rate-limit values are — not
  standardized by this ADR, since RFC 9470 itself leaves `acr_values`'
  vocabulary to the deployment's own authorization server.

**Compliance note** (a proving-ground compliance review, this session):
beyond `21 CFR Part 11` §11.50 (already this ADR's driving citation),
this mechanism is the natural fit for a **BSA Suspicious Activity Report
(SAR)** filing decision — a compliance officer's sign-off (`Meaning`
capturing the filing rationale, `SignerId`/`SignedAt` satisfying the
same non-repudiation need) on the digital-identity/KYC proving-ground
domain, once that domain's own OFAC/SAR screening logic (flagged as an
open question on `ADR-036`) actually exists to trigger it.
