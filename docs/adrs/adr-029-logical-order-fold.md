[← ADR index](../07-adrs.md)

# ADR-029: Logical-order fold for out-of-order/lagged event arrival

Status: Accepted

Context: The Entity Store fold (`ADR-021`) applies events in
`SequenceNumber` order — the order they were durably appended *at this
store*. For a single, non-replicated store that's a true total order, but
it's an **arrival** order, not necessarily the order events logically
*happened* in — a publisher buffering offline, retrying after a network
gap, or simply lagging can append an event well after other events that
logically occurred later than it did. Folding strictly by arrival order
in that case applies the late event's (now-stale) values **on top of**
already-folded newer values, silently reverting them — a real
correctness bug, not just a cosmetic ordering quirk. This is the same
problem stream-processing systems name **event time vs. processing
time**, and solve with **watermarks** — "we'll never see an element with
an earlier timestamp" is treated as a guess, not a guarantee, with
explicit handling for whatever arrives after that guess turns out wrong.
**Source:** [Apache Beam — Streaming 101 / watermarks](https://beam.apache.org/documentation/basics/).

Decision:
- **`StoredEvent.OccurredAt` is the event's client-declared logical
  occurrence time — not server receipt time.** This needed stating
  explicitly and precisely, because it is now load-bearing for
  correctness, not just a display field: `ReceivedAt`-equivalent server
  timestamps (if ever needed separately) are a different field, not
  `OccurredAt`.
- **The Entity Store row (`ADR-021`) gains a `LastAppliedLogicalTime`
  high-water mark.** Fold compares an incoming event's `OccurredAt`
  against it, not against `SequenceNumber` order alone:
  - `OccurredAt` > high-water mark → apply normally; advance the
    high-water mark to this event's `OccurredAt`. This is the common
    case and behaves exactly as today.
  - `OccurredAt` <= high-water mark → a **late arrival**: a logically
    older change showing up after something logically newer already
    won. Its effect on the affected property is **not applied** — the
    logically-newer value already in place is left alone — and the
    event is flagged (`LateArrivalFlag`, a new field, sibling to
    `ADR-024`'s `ConflictFlag` — related but distinct: `ConflictFlag`
    marks two writes based on the *same* prior version; `LateArrivalFlag`
    marks a write that arrived after something logically newer, whether
    or not either one was aware of the other).
- **This is "best as possible," stated as a deliberate choice, not an
  approximation apologized for.** A late arrival is never silently
  applied (which would corrupt current state) and never silently
  dropped (it's still fully present in the immutable log and in entity
  change history, `ADR-024`) — it's visible, flagged, and simply doesn't
  win the property it was late for.
- **Exact correction stays available on demand, at a real cost, only
  when wanted.** Because fold is always "replay from `0`"
  (`ADR-015`), rebuilding an entity by folding *all* its events strictly
  in `OccurredAt` order (rather than `SequenceNumber` order) gives the
  mathematically exact answer once every event — including the late one
  — is known. This is the same two-tier shape already used elsewhere in
  this design (masking's `FixedValue` now vs. richer strategies later,
  `ADR-009`; escalate conflict sophistication only where a field needs it,
  `ADR-024`): cheap, immediate, flagged-if-imperfect by default; exact,
  expensive, on-demand when it matters enough to ask for.
- **`LastAppliedSequenceNumber` (the replay checkpoint) and `Version`
  (the data-change counter) are now explicitly two different things.**
  `LastAppliedSequenceNumber` always advances past every event processed,
  including a late arrival whose change was rejected — otherwise the
  fold would reprocess it forever. `Version` only increments when the
  materialized `Data` actually changes — a rejected late arrival leaves
  `Version` untouched, since nothing about the entity's visible state
  changed.
- Tracking the high-water mark **per property** (not just per entity)
  avoids a late arrival on one field (e.g. `Address`) causing an
  unrelated field (e.g. `Email`) to be treated as contested — consistent
  with `ADR-022`'s property-level patch granularity. Per-entity
  (coarser, cheaper) is an acceptable v1 default; per-property is the
  documented upgrade path, escalated only where it's actually needed,
  the same trade `ADR-024` already makes.

Consequences:
- Directly complements `ADR-033` (multi-origin replication): `OccurredAt`
  alone is a single-origin logical clock; once multiple independent
  origins write concurrently, wall-clock timestamps alone become
  insufficient for correct cross-origin ordering (`docs/design-docs/09
  §9.3`'s own stated reason for `LogicalClock`/HLC) — `ADR-033` extends
  this one's ordering key, it doesn't replace it.
- `LateArrivalFlag` needs the same surfacing `ConflictFlag` already gets
  — entity change history, a query-layer filterable field, a view
  indicator — reusing the existing "flag, don't hide" rendering
  convention rather than inventing a second one.
- This does not change the event log itself in any way — `SequenceNumber`
  ordering, append-only-ness, and `ADR-019`'s hash chain are all
  unaffected. Only how the **fold** interprets order changes; the
  historical record of what arrived when is untouched.
