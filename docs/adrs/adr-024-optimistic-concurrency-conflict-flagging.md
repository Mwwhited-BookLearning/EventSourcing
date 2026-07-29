[← ADR index](../07-adrs.md)

# ADR-024: Optimistic concurrency + conflict flagging

Status: Accepted

Context: With entities now real (`ADR-021`) and patches property-level
(`ADR-022`), a genuine question follows: what happens when two patches,
both based on the same prior entity version, from different callers,
touch the *same* property? There is no true causal order to discover
between two causally-concurrent writes — any order imposed (arrival time,
priority) is a policy choice, not a fact being uncovered
(`docs/design-docs/08 §8.1`). This needs a stated default, not silence.

Decision:
- **Default policy: event-store append order (`SequenceNumber`) is
  authoritative — stream-order last-write-wins.** Simple, deterministic,
  explainable without a resolution engine.
- **Layered on top, not instead of: the fold step (`ProjectionHost`
  applying to the Entity Store, `ADR-021`) detects and flags conflicts,
  never blocks or rejects either event.** Detection is narrow and cheap:
  compare a patch's `ExpectedVersion` (`ADR-021`) to the Entity Store's
  `Version` *at fold time*. If another patch touching the *same property*
  was already applied since `ExpectedVersion`, set `ConflictFlag: true`
  on the later-applied event — the earlier one is never retroactively
  touched.
- **Most concurrent edits are not real conflicts, and detection must stay
  narrow enough to reflect that.** Because patches are property-level
  (`ADR-022`), two patches based on the same version touching *different*
  properties both fold cleanly regardless of arrival order — that is not
  a conflict. A real conflict is specifically: same property, same prior
  version, different value.
- **Entity change-history becomes a first-class query**, not new storage
  — "every event for entity X" is a stream read from `SequenceNumber` 0
  filtered by `EntityId`, already fully answerable by Follow's existing
  mechanics (`ADR-010`) once `EntityId` exists (`ADR-021`); `ADR-029`'s
  GraphQL layer additionally exposes it as a direct
  `entityHistory(entityId, property)` query, matching
  `docs/design-docs/08 §8.4`'s shape.
- **Escalate sophistication only where a specific field genuinely needs
  it** — three levels, chosen per field/entity type, not system-wide:
  (1) arrival-order LWW with no detection at all (fine for fields where
  staleness truly doesn't matter); (2) this ADR's default — optimistic
  concurrency + flagging; (3) a field-level conflict policy (e.g. summing
  deltas instead of overwriting a balance) — this is where CRDT-style
  merge logic would live if ever needed, reserved for specifically
  contentious fields, not a general mechanism (consistent with
  `references.md`'s existing "CRDTs: not adopted, no general merge
  problem exists" entry — that entry now needs updating: a general merge
  problem *has* emerged with entities; CRDTs remain not-adopted as a
  system-wide default, but are no longer categorically inapplicable).

Consequences:
- Conflicts are surfaced, never hidden or auto-resolved beyond stream-
  order LWW — a user or support engineer can always see both concurrent
  values via change history and understand a genuine concurrent edit
  happened, not a bug.
- A correction after the fact is a new patch, not a rewrite — consistent
  with every other "never mutate, only append" decision already made in
  this design (`ADR-009`'s closing note, `ADR-019`'s hash chain).
- `ConflictFlag` is the **same mechanism** `ADR-025`'s cross-server
  divergence resolution reuses without modification — a sync-delivered
  event that conflicts with a local one is detected identically to a
  same-server concurrent write; the only thing that differs is which
  event triggered the fold that discovered it.
- `ExpectedVersion` being optional (`ADR-021`) means conflict detection is
  opt-in per publish, not universally enforced — a caller that never
  supplies it gets no detection, the same trade `ADR-011`'s `eventId`
  already makes for idempotency (opt-in, not automatic).
