[← Comparisons index](README.md)

# Authority-Axis Granularity: Collapsed Status vs. Independent Assurance Axes

**Raised by:** conversation, generalizing `ADR-035`/`ADR-042`/`ADR-045`;
tracked as an open question in `docs/10-open-questions.md`, formalized
here per the general pattern in [Multi-Axis Authority/Assurance](../patterns/multi-axis-authority-assurance.md)
(NIST SP 800-63-3's IAL/AAL/FAL split). **Recommendation:** stay
single-axis on both the write side (`AuthorityStatus`) and the read side
(`ReaderTrustBasis`) — no new ADR needed; this document reaffirms
`ADR-035`, `ADR-042`, and `ADR-045` as already correct at this design's
current scale, with concrete, named triggers below for when to revisit.

**Stated requirement driving this comparison:** the pattern doc's own
generalizable rule — "whenever 'trust' in a system is actually answering
more than one independent question, name each question as its own axis
rather than forcing a single field to average them." This comparison's
job is to test that rule against this design's *actual* mechanisms, not
against NIST's identity-proofing domain in the abstract: does splitting
change what gates `ADR-042`'s Entity Store fold, and does any consumer
that exists today (or is concretely planned) actually need the two
questions to diverge in observable behavior?

## The fork

Two independent sub-forks, because the write side and read side of this
design's trust model are structurally different mechanisms with
different consumers:

- **Write side** — does `AuthorityStatus` (`ADR-035`/`ADR-042`, one of
  `unattested | pending_review | accepted | rejected`, the value that
  *gates* the Entity Store fold) split into independent axes?
- **Read side** — does `ReaderTrustBasis` (`ADR-045`, one of
  `Authoritative | Attested`, written to `AccessLogEntry` for audit, and
  *never* gating anything) split similarly?

### The concrete tension motivating this (write side)

`ADR-042`'s own Context names two triggers for a non-`accepted`
`AuthorityStatus`, and they answer visibly different questions:

1. **A field actor capturing data offline**, submitting a self-attested
   DID/UCAN (`ADR-036`) instead of an ordinary bearer JWT (`ADR-006`).
   The *content* of the claim ("this reading happened, at this time, at
   this location") is exactly as trustworthy as any other capture — the
   actor isn't guessing at anything about the world. What's unverified is
   *who they are* — the UCAN chain hasn't been validated against the
   identity provider yet, because connectivity was unavailable at capture
   time. **High content-confidence, low identity-assurance.**
2. **A detector service**, running under an ordinary, fully-authenticated
   `ADR-006`/`ADR-015` service identity — its own identity and
   authentication are as authoritative as this design's trust model ever
   gets, no different from any other internal service call — publishing
   an unconfirmed pattern match, flagged `pending_review` purely because
   the *detection itself* hasn't been validated by a second pass or a
   human reviewer. **High identity-assurance, low content-confidence.**

These are opposite profiles landing on the identical enum value
(`pending_review`). `ADR-042` considered this directly and chose *not*
to split, reasoning that both triggers answer "the same underlying
question — should this claim be trusted as-is yet." This comparison
checks that reasoning against NIST's finer-grained model rather than
taking it as settled by default.

## Option A — Stay single-axis (current: `ADR-035`/`ADR-042`/`ADR-045`)

`AuthorityStatus` (write) and `ReaderTrustBasis` (read) each stay one
field. Domain-specific detail about *why* a write is pending (a
confidence score, which rule flagged it, whether it's the identity or
the content that's unverified) rides inside the existing free-form
`AttestedClaims` JSON — unindexed, unqueryable at the framework level,
but present. On the read side, `GrantRef` (non-null only when
`ReaderTrustBasis: Attested` came from an `ADR-043` delegated grant
specifically) already gives an auditor one bit of disambiguation between
the two ways `Attested` can arise, without a second field.

| | |
|---|---|
| **Pros** | Exactly one gate condition to reason about at the one place that actually branches on it (`ADR-042`'s fold step): `AuthorityStatus == accepted`. No consumer anywhere in this design today needs the write side's two triggers, or the read side's two `Attested` origins, to produce *different* observable behavior — they both currently produce the identical outcome (write: withhold from the authoritative store until reviewed; read: log the access, unauthoritatively-sourced or not). Matches this design's own stated discipline about not multiplying trust fields ahead of a concrete need (`CLAUDE.md`'s note on the four existing envelope-metadata fields: "if a fifth comes up, ask what question it specifically answers before reusing one of these four" — the same discipline applied here in reverse, before adding one). |
| **Cons** | Nothing today *reads* `AttestedClaims` to distinguish "pending because of identity" from "pending because of content" in a structured, filterable way — a review UI or an operational metric ("what fraction of our review backlog is detector output vs. unverified field captures") has to parse free-form JSON per event, or maintain its own out-of-band convention for the claim shape, rather than filtering on an indexed column. This is a real gap *if* such a consumer is ever built — not hypothetical, since `ADR-042` itself is the origin of the detector-confidence trigger. |

## Option B — Split into independent axes

### Write side: what would the axes actually be?

Naming candidates from the open question were identity-assurance,
authorization-validity, and content/detection-confidence. Working
through each against this design's actual mechanisms (not NIST's, which
assumes a human-identity-proofing domain this design only partially
maps onto):

- **`IdentityAssuranceStatus`** (`unverified | pending_review | verified
  | rejected`) — holds up as a genuine, independent axis. This is
  exactly `ADR-035`'s original trigger: a DID/UCAN chain that hasn't yet
  been validated against the identity provider (`ADR-036`), sitting in a
  real pending state for real elapsed time (until connectivity returns
  and the exchange succeeds, or a human confirms the actor out-of-band).
- **`ContentConfidenceStatus`** (`unconfirmed | pending_review |
  confirmed | rejected`) — also holds up independently. This is
  `ADR-042`'s detector trigger: a pattern match sitting in a real pending
  state for real elapsed time (until a second pass, a human, or a
  correlated signal confirms or refutes it), with an identity that was
  never in question.
- **`AuthorizationValidityStatus`** — does **not** hold up as a third
  *stored*, gradually-resolved axis in this design, and this is worth
  stating explicitly since it's one of the three names the open question
  raised. Every path that establishes permission in this design resolves
  it **synchronously, before persistence, as a blocking gate** — an
  ordinary `ADR-006` bearer token's scope check (`ADR-008`'s
  `RequiredPublishClaim`) is a real `401`/`403` at the API boundary, and
  a UCAN's delegation-chain validity (`ADR-036`, capped per `ADR-043`)
  is checked at token-exchange time, also synchronously, also pass/fail.
  `ADR-023` states this directly: persist-everything is "specifically
  about *content* (shape, schema, authority-of-claimed-identity), not
  about whether the caller is allowed to call the endpoint at all." An
  event that fails authorization never becomes a `StoredEvent` at all —
  there is no "authorization pending" state in this design for the same
  reason there's no "schema pending" state broader than `SchemaStatus`
  already covers. Splitting a third axis out for a condition that never
  actually sits at a value other than pass/fail-already-resolved adds a
  field with no real state machine behind it.

So a genuine split, if done, is **two** write-side axes, not three:
identity-assurance and content-confidence. `AuthorityDecisionRef`
(`ADR-035`, a single denormalized back-pointer) would need to become two
— `IdentityDecisionRef` and `ContentDecisionRef` — since a single
`authorityDecision` event can no longer unambiguously mean "the thing
that last changed the status" once there are two statuses to change
independently.

### What would gate the `ADR-042` fold?

**Both axes, ANDed** — the fold requires `IdentityAssuranceStatus ==
verified AND ContentConfidenceStatus == confirmed`. This is worth
stating plainly because it's the answer to the open question's own
framing ("would it need ALL axes to clear a bar, or a specific one, or a
computed combination"): it's a computed combination, specifically the
logical AND of both, and that combination is **behaviorally identical**
to today's single collapsed field. Today, an event author sets
`AuthorityStatus: pending_review` if *either* trigger applies, and the
fold withholds until it's `accepted`; under a split, the fold withholds
until *both* axes independently clear. The gate never actually needed to
distinguish *which* axis was the reason — it only ever needed "is there
any reason to withhold," which a single field already answers exactly as
well as two ANDed fields do. Splitting doesn't change the fold's
decision procedure at all; it only changes what's visible to something
*other* than the fold — namely, a review UI or a metrics query that
wants to know *why*.

### Read side: same treatment for `ReaderTrustBasis`

NIST's IAL/AAL/FAL split maps more naturally to the *read* side, since
`ReaderTrustBasis` is describing an authenticated principal, exactly
NIST's domain — `IdentityAssuranceStatus`-for-readers (was the reader's
own identity ever independently proofed, vs. just DID-self-asserted) and
`AuthenticatorAssuranceStatus`-for-readers (single-factor bearer JWT vs.
something stronger) are both real, nameable axes in principle. But
`ReaderTrustBasis` has exactly one consumer today — `AccessLogEntry`,
written for HIPAA-shaped compliance audit (`ADR-045`) — and that
consumer **never branches on the value**; it only records it. No ADR
gates a read's *result* (redaction, masking, entity-scope) on
`ReaderTrustBasis` — those are handled entirely by `ADR-008`'s claims and
`ADR-043`'s `entityScope`, checked at authorization time, independent of
this axis. `GrantRef`'s presence already tells an auditor whether an
`Attested` reader's credential came from self-attestation (`ADR-036`,
`GrantRef` null) or a peer-granted delegation (`ADR-043`, `GrantRef` set)
— the one distinction anyone has actually asked to disambiguate — without
a second field.

| | |
|---|---|
| **Pros (write-side split)** | A review UI or metrics query gets a real, indexed, queryable answer to "why is this pending" without parsing `AttestedClaims` JSON. Cleanly separates two axes that can, per the scenario above, genuinely disagree in either direction — the exact case the pattern doc says is the tell for "this is really two questions." Matches NIST's precedent for the identity-assurance half specifically. |
| **Cons (write-side split)** | Every place that currently reads one `AuthorityStatus` value (`StoredEvent`, `EntityStoreRow`/`LiveEntityStoreRow`'s rollup, `docs/features/non-authoritative-capture.md`'s scenarios, the `authorityDecision` event and its now-doubled back-pointer, `RejectionBehavior`'s per-event-type override) now has to read and AND two, and every one of those places needs to correctly implement the same AND that the collapsed field already gave for free. No consumer that exists *today* actually needs the two values to diverge in observable behavior — this is added surface area for a distinction only a not-yet-built review UI would exploit. Precisely the cost the pattern doc itself names: "every place that currently checks 'is this trusted' has to decide which axis (or combination) it actually means." |
| **Pros (read-side split)** | Slightly more expressive audit trail, closer to NIST's own domain fit. |
| **Cons (read-side split)** | No consumer branches on `ReaderTrustBasis` today, and `GrantRef` already carries the one disambiguation anyone has asked for. Splitting a field that's purely descriptive, for an audience of one (a compliance report), ahead of any stated requirement to branch on it, is speculative generality with no offsetting benefit named anywhere in `ADR-045`. |

## Scenario walkthrough

| Scenario | Current `AuthorityStatus` | Split `IdentityAssuranceStatus` | Split `ContentConfidenceStatus` | Fold outcome (either model) |
|---|---|---|---|---|
| Field actor, offline, self-attested UCAN not yet exchanged | `pending_review` | `pending_review` | `confirmed` (implicit — the actor isn't claiming a detection, just a fact) | Withheld from authoritative Entity Store until identity clears |
| Detector service, ordinary authenticated identity, low-confidence pattern match | `pending_review` | `verified` | `pending_review` | Withheld from authoritative Entity Store until content clears |
| Ordinary authenticated publish, no self-attestation, no review-pending marker | `accepted` (default, `ADR-042`) | `verified` | `confirmed` | Folded immediately |
| Detector's identity itself later found to be compromised (hypothetical, not currently modeled either way) | `rejected` | `rejected` | *n/a — never independently reached* | Never folded |

The last row is the closest either model gets to a scenario where the
axes would need to disagree *and* the disagreement would need to change
behavior — and even there, a compromised detector identity should
invalidate the content claim too (an untrusted identity's "detection" is
not usefully "confirmed" on its own), so the two axes collapse back to
one outcome anyway. No row in this table produces a case where the split
model's fold decision differs from the collapsed model's.

## Recommendation

**Stay single-axis, on both sides, at this design's current scale.**
The detector-vs-field-actor tension named above is real — `ADR-042`'s
own two triggers genuinely are answering different questions in the
abstract, and NIST's IAL/AAL/FAL precedent is a legitimate model to
check against. But checking it concretely against this design's actual
mechanism shows the split doesn't change the one thing that currently
consumes the value: `ADR-042`'s fold gate needs exactly one yes/no
answer ("trust this enough to treat as current truth"), and a computed
AND of two split axes produces that same one answer, with no case found
where the two models diverge. The gap the split would close — a
structured, queryable "why is this pending" — is real but narrower than
a full schema-level axis split: it's a UI/observability need with no
concrete consumer built yet, not a gating need. `AuthorizationValidityStatus`,
the third axis the open question named, doesn't hold up at all in this
design specifically, because `ADR-006`/`ADR-008`/`ADR-036` all resolve
permission synchronously before persistence — there is no persisted
"authorization pending" state for a third axis to describe.

**Revisit if any of these becomes concretely true, not before:**
- A review UI or reviewer-routing mechanism actually gets built and
  needs to send "verify this submitter's identity" work to a different
  queue/role than "confirm this detection" work — at that point, promote
  a structured `PendingReason` field (a smaller, targeted fix — a
  discriminator inside the existing `AttestedClaims` schema-registry
  entry, per `ADR-035`'s "own lightweight schema-registry entry," not
  necessarily a full second `StoredEvent` column) before reaching for a
  full axis split.
- A metrics/observability requirement is explicitly stated for
  "what fraction of pending review is identity-driven vs.
  content-driven" — the same `PendingReason` promotion would satisfy it.
- A future ADR introduces a permission-granting mechanism that is *not*
  synchronous (e.g., an async approval step between a UCAN's
  cryptographic validity and its actual authorization) — that would be
  the first real "authorization pending" state in this design, and the
  point at which `AuthorizationValidityStatus` would earn a place as a
  genuine third axis, distinct from `IdentityAssuranceStatus`.
- `ReaderTrustBasis` ever needs to *gate* read results (not just be
  logged) at a granularity finer than today's binary — e.g., different
  redaction behavior for self-attested vs. delegated `Attested` readers,
  not just different audit-log values.

None of these is true today. This document, not a new ADR, is the
record of that check — `ADR-035`, `ADR-042`, and `ADR-045` need no
revision; the open question they raised is resolved by this comparison
rather than by a further decision on top of them.
