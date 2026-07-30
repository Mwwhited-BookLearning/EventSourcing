[← Comparisons index](README.md)

# Authority Rejection Behavior: Annotate-Only vs. Compensating-Patch

**Decided in:** `ADR-035` (annotate-only default). **Raised by:** the
second design (`docs/design-docs/`, now absorbed and removed — see
`CLAUDE.md`), which left this explicitly open ("needs input from domains
with legal/evidentiary requirements").

**Stated requirement driving this comparison:** this design's strongest
recurring principle, stated in `README.md`'s opening section — never
mutate or delete a stored event, ever, not even for regulated/rejected
data. Whichever option wins has to be checked against that bar
specifically, not just against which is more convenient to implement.

**Narrowed in scope by `ADR-042`**: since an event now only folds into
the authoritative Entity Store once `AuthorityStatus` reaches `accepted`,
a rejection of an event that was never accepted has nothing to
compensate for — it simply never applied. This fork's Annotate-vs-
Compensate choice now matters specifically for the narrower, real
residual case: an event already `accepted` and folded, later
*re-reviewed* and reversed to `rejected`. The conclusion below (annotate-
only default, per-type override to compensate) is unchanged; only the
set of cases it actually governs got smaller.

## The fork

Once a self-attested event is reviewed and rejected (`AuthorityStatus:
rejected`), does the Entity Store's materialized state change?

### Option A — Annotate-only

The entity store still reflects whatever the rejected event said;
`AuthorityStatus: rejected` is a flag consumers can filter on or a view
can grey out. Nothing is un-applied.

| | |
|---|---|
| **Pros** | Consistent with this design's strongest recurring principle — never mutate or delete (`README.md`'s "never lose or corrupt data," `ADR-009`'s closing note, `ConflictFlag`'s "flagged, not reversed" treatment). No replay-order complexity: rejecting an event doesn't require recomputing every later fold that happened after it. |
| **Cons** | Plain annotate-only, as first written here, left a real gap: the *current*, materialized view of an entity could keep showing data everyone agrees is untrustworthy indefinitely, unless every consumer remembered to check `AuthorityStatus`. **Refined below, not superseded** — a targeted rebuild closes this gap without inheriting Option B's problems. |

**Refinement (this session, prompted by an independent cross-reference finding — Jason McCann's "a compromised-but-still-valid edge signer's events are indelible and replayed on every rebuild" needs a retraction model, `AR5`/`Q27`):** Annotate-only doesn't have to mean "stale until some indeterminate future rebuild." A post-hoc `authorityDecision: rejected` event triggers an immediate **single-entity rebuild** — re-fold just that `EntityId`'s event history from `SequenceNumber 0`, reusing `ADR-021`'s already-cheap "replay is cheap, rebuild is cheap" property, scoped to one entity rather than the whole store. The fold rule is simply "apply this event only if its current `AuthorityStatus` is `accepted`" — checked fresh at fold time, not baked in at original-publish time. This gets Option A's exact guarantee (no compensating patch, no ambiguous "pre-rejection state" computation — the fold algorithm never has to reason about undoing a specific contribution, it just excludes it) **and** Option B's exact benefit (current state is clean by default, not merely filterable-to-clean) — with no known correctness edge case, unlike Option B. The one new, real cost: a rejection now costs one full re-fold of that entity's history (`O(that entity's event count)`, not `O(1)`) — cheap for almost every entity, worth naming explicitly for a pathologically long-lived, high-volume one. The rejected event itself is never deleted — it's excluded from the *fold*, not from the Event Log.

### Option B — Compensating patch

The projector, on seeing a rejection decision, generates a corrective
event reverting the affected properties to their pre-rejected-event state
(or `null`/unspecified).

| | |
|---|---|
| **Pros** | The *current* materialized state is clean by default — a consumer that never checks `AuthorityStatus` still sees a trustworthy answer, not a footgun waiting for whoever forgets to filter. Fits domains where "current state must reflect only authorized data" is a hard requirement, not a nice-to-have (the doc's own example: anything with legal/evidentiary weight). |
| **Cons** | Requires replaying to the point immediately before the rejected event to compute what "pre-rejection state" even was — real replay-order complexity, especially once `ADR-029`'s logical-order fold and `ADR-024`'s conflict flagging are both already in play for the same entity. The compensating patch is itself a new event with its own `ExpectedVersion`/conflict semantics to get right — a second write generated automatically by the system, not a client, which this design hasn't needed anywhere else. If *other*, legitimate events touched the same properties *after* the now-rejected one, "revert to pre-rejection state" is ambiguous — does it revert past those too, or only undo the rejected event's specific contribution? Genuinely hard to define correctly in the general case. |

## Recommendation

**Annotate-only-plus-targeted-rebuild (the refinement above) as the
system-wide default, with `RejectionBehavior` staying a genuine
per-event-type override** (already the shape
`docs/data/schema-registry.md`'s `EventTypeDefinition` needs regardless —
this comparison argues for *which value is the default*, not for removing
the per-type choice design-docs itself already specified). This is a
stronger recommendation than the original Annotate-only framing: it keeps
the "never mutate, only append" principle fully intact (the rejected event
is excluded from a fold, never edited or deleted) while also giving
current state the same cleanliness Option B was for — without inheriting
Option B's real correctness problem (ambiguous revert semantics once
other events touched the same properties later). Domains that still
prefer Option B's literal compensating-patch shape (e.g. a system that
must show an explicit, visible correction event in the timeline, not just
a quietly-excluded one) may still opt into it per event type — but the
default no longer forces a choice between "never mutate" and "clean
current state"; a targeted rebuild gets both.
