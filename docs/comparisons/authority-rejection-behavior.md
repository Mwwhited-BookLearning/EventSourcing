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
| **Cons** | The *current*, materialized view of an entity can keep showing data everyone agrees is untrustworthy, unless every single consumer remembers to check `AuthorityStatus` and filter accordingly — a real correctness burden pushed onto every reader, every time, forever. For a domain where "what does the system currently say" needs to be clean by default (not just filterable-to-clean), this is a genuine gap. |

### Option B — Compensating patch

The projector, on seeing a rejection decision, generates a corrective
event reverting the affected properties to their pre-rejected-event state
(or `null`/unspecified).

| | |
|---|---|
| **Pros** | The *current* materialized state is clean by default — a consumer that never checks `AuthorityStatus` still sees a trustworthy answer, not a footgun waiting for whoever forgets to filter. Fits domains where "current state must reflect only authorized data" is a hard requirement, not a nice-to-have (the doc's own example: anything with legal/evidentiary weight). |
| **Cons** | Requires replaying to the point immediately before the rejected event to compute what "pre-rejection state" even was — real replay-order complexity, especially once `ADR-029`'s logical-order fold and `ADR-024`'s conflict flagging are both already in play for the same entity. The compensating patch is itself a new event with its own `ExpectedVersion`/conflict semantics to get right — a second write generated automatically by the system, not a client, which this design hasn't needed anywhere else. If *other*, legitimate events touched the same properties *after* the now-rejected one, "revert to pre-rejection state" is ambiguous — does it revert past those too, or only undo the rejected event's specific contribution? Genuinely hard to define correctly in the general case. |

## Recommendation

**Annotate-only as the system-wide default, with `RejectionBehavior`
staying a genuine per-event-type override** (already the shape
`docs/data/schema-registry.md`'s `EventTypeDefinition` needs regardless —
this comparison argues for *which value is the default*, not for removing
the per-type choice design-docs itself already specified). Annotate-only
is the more consistent default given this design's own "never mutate,
only append" principle stated everywhere else — the compensating-patch
option's correctness problems (ambiguous revert semantics once other
events touched the same properties later) are serious enough that
defaulting to it system-wide would mean shipping a mechanism with a known
unresolved edge case as the common case, not the exception. Domains that
genuinely need Option B's guarantee (legal/evidentiary weight, per the
open question's own framing) opt into it explicitly, per event type,
accepting its sharper edges for the specific data where "clean current
state" matters more than "simple, always-correct replay."
