[← ADR index](../07-adrs.md)

# ADR-064: Capture `ActorId` on every `StoredEvent`, not just self-attested ones

Status: Accepted

Context: Found during a traceability/auditability review prompted by
this session's two proving-ground domains (clinical trials, digital
identity/KYC) — both have a basic, well-precedented requirement: know
*who* did something, not just *when* and *what*. Checked against
`docs/data/event-log.md`'s actual `StoredEvent` shape rather than
assumed: `AttestedActorId` exists, but it's explicitly scoped to
`ADR-035`'s self-attestation path ("self-attested submitter identity —
advisory, never gates Status"). For an **ordinary authenticated
publish** — the common case, where `ADR-006`'s OAuth2/DPoP flow already
verifies a real, non-self-attested identity before the request is even
accepted — nothing on `StoredEvent` records who that verified caller
was. The system *can* answer "who published this" at request time; it
simply never writes the answer down.

Decision:
- **`StoredEvent` gains `ActorId`** — the verified token subject
  (`sub`, or the composite `iss`+`sub` mapping `ADR-047` already
  establishes for federated identities) captured at publish time for
  *every* event, regardless of path. Always present for an ordinarily-
  authenticated publish; for a self-attested publish (`ADR-035`), set to
  whatever identity the verifying flow resolves (which may be the
  self-attested `AttestedActorId` itself, if no stronger identity is
  available) — the two fields answer different questions and are kept
  separate, not merged: `ActorId` is "who the platform's own auth layer
  verified," `AttestedActorId` is "who the submitter *claims* to be,
  advisory and unverified until `AuthorityStatus` resolves." Conflating
  them would silently upgrade an unverified claim to a verified fact.
- **Blocking, not advisory** — unlike `AttestedActorId`, `ActorId` is
  populated from a value `ADR-006`'s auth middleware already established
  before the request reached the publish handler at all; there's no
  "advisory" state for it the way there is for self-attestation, since
  it was never in question.

Consequences:
- Resolves the first finding from this session's traceability/
  auditability review.
- `ADR-045`'s read access audit log already records a *reader's*
  identity for every read; `ActorId` is the missing write-side
  equivalent — the two together mean every read *and* every write is
  now attributable to a verified actor, not just reads.
- `docs/data/event-log.md`'s `StoredEvent` class gains the field —
  done this pass.
- No schema/registry change — `ActorId` is envelope metadata, the same
  category as `parentEventIds`/`TelemetryPointer`/`AttachmentRef`, never
  part of the registered `JsonSchema` or subject to `x-masking` (it
  describes the publish request, not domain data — though a future
  masking need for it, if `ActorId` itself is ever considered sensitive
  in some deployment, would compose with `ADR-009` the same way any
  other envelope field could, not blocked by this decision).

**Compliance note** (a proving-ground compliance review, this session):
this is the foundational field several later compliance mechanisms
build on directly — `21 CFR Part 11`'s signer-identity requirement
(`ADR-066`), and every audit-trail-shaped confirming non-gap found
(SEC 17a-4, SOX ITGCs) — none of which would have a verified actor to
attribute a write to without it.
