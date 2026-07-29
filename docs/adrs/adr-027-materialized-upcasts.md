[← ADR index](../07-adrs.md)

# ADR-027: Materialized upcasts persisted to the event log, folded exactly once

Status: Accepted — extends `ADR-020`.

Context: `ADR-018`'s `UpcastChain` and `ADR-020`'s publish-time validation
both compute an upcast result and then **discard it** — `ADR-020`
explicitly: "the upcasted result itself is discarded here — only whether
it succeeded matters." Every consumer that wants an old event in current
shape re-runs the same transform, every time, forever. There's a real
want to persist that result instead — so a full, current-shape history
actually exists in the log, not just a live, ADR-018-dependent view of
one — as long as doing so can't corrupt the Entity Store (`ADR-021`) by
applying the same logical change twice.

Decision:
- **`StoredEvent` gains an `EventKind` field: `Original` (the default —
  every event published today) or `UpcastMaterialization`.** A
  materialization also sets `MaterializationOfEventId` (the original
  event's `EventId`) and is published through the *same* append path as
  any other event — it's an ordinary row in the log, not a separate
  table or mechanism.
- **Trigger 1 — publish time (`ADR-020`, revised):** when a lagging
  publish's live `UpcastChain` validation succeeds, the upcasted result
  is no longer discarded — it's persisted immediately as a new
  `UpcastMaterialization` event, at the target (current) schema version,
  alongside the original (which is still stored exactly as declared,
  unchanged from `ADR-020`).
- **Trigger 2 — background reconciliation, for the existing backlog.**
  Publish-time materialization alone only covers *future* lagging
  publishes; it does nothing for events already sitting in the log at an
  old version before a given `upcastFromPrevious` mapping even existed.
  A background `UpcastMaterializer` — architecturally "an internal
  follower," the same pattern `ADR-007`'s derivation workers and
  `ADR-015`'s `ProjectionHost` already establish (tail via the public
  Follow API, republish through the ordinary publish path) — activates
  whenever a new schema version + mapping is registered, walks existing
  events at the now-superseded version for that type, and materializes
  each one going forward. This is the same "background reconciliation
  pass" idea `docs/design-docs/07 §7.2.1` recommends as the default for
  unresolved-schema history, applied here specifically to materialization
  rather than left as an open question.
- **The critical invariant: materializations are never folded.**
  `ProjectionHost`/the Entity Store fold (`ADR-021`) skips any event whose
  `EventKind` is `UpcastMaterialization` outright — folding continues to
  consume *only* `Original` events, running `UpcastChain` live on them
  exactly as `ADR-018` already specifies, unchanged. A materialization
  is a parallel, optional-to-consume record for *other* readers — never a
  second source of truth competing with the original for the Entity
  Store's attention.

Consequences:
- **This is what makes the design safe.** If a materialization
  *were* folded as an ordinary patch, it would re-apply the original's
  values — now reshaped, but still reflecting whatever the entity looked
  like *at the original's fold time* — on top of whatever newer events
  have since changed those same properties, silently reverting them. By
  never folding materializations at all, this can't happen: fold
  correctness depends only on `ADR-018`'s already-proven live-upcast-at-
  fold-time behavior, not on anything new introduced here.
- Follow (`ADR-010`) and any custom projection (`ADR-015`) **may** choose
  to serve a materialization instead of running `UpcastChain` live when
  one exists for a given original — a real performance win (skip
  re-computing the same transform on every read) — but this is an
  optimization, not a correctness requirement; consuming only originals
  and always upcasting live remains equally correct, just more repeated
  work.
- The log now contains two physical rows for one logical fact once a
  materialization exists — `parentEventIds`/`EventParents` (`ADR-005`) is
  deliberately *not* reused for the original→materialization link
  (`MaterializationOfEventId` is its own field): lineage answers "what is
  this causally derived from," which is a different question from "what
  is this a re-shaped copy of." Conflating the two would make `ADR-005`'s
  lineage traversal have to special-case materializations everywhere it
  currently doesn't need to.
- The `UpcastMaterializer` background worker inherits every limitation
  Follow-based internal followers already accept elsewhere in this
  design (`ADR-007`'s consequences) — an unbounded backlog materializes
  as fast as the worker can go, no batching/pacing guarantee.
