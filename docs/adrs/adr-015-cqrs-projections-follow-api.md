[← ADR index](../07-adrs.md)

# ADR-015: Read-model projections consume the public Follow API, not a private hook

Status: Accepted

Context: this project's purpose (`README.md`) includes demonstrating CQRS
alongside event sourcing — a read side that materializes query-optimized
read models from the event stream, kept separate from the write side. The
naive way to feed that read side is a private, store-internal notification
mechanism (an in-process event bus, a change-data-capture hook on
`Events`). But this design already has a public, general-purpose consumer
API with exactly the resume/no-gap/no-duplicate semantics a projection
needs: `QUERY /follow/{event-type}` with `mode=tail`/`mode=replay`
(`ADR-010`). Building a second, parallel consumption path would duplicate
that guarantee under a different name for no real benefit, and would mean
a projection sees the store's internals rather than the same contract any
external follower sees.

Decision:
- **Projections are Follow consumers, full stop** — a `ProjectionHost`
  process authenticates like any other `follower-client` (`ADR-006`) and
  issues ordinary `QUERY /follow/{event-type}` calls. Nothing about the
  store's public contract changes to support projections; nothing
  projection-specific is added to `EventStore.Host.*`.
- **Always `mode=replay`, never `mode=tail`.** A `ProjectionHost` tracks its
  own resume position per projection (`ProjectionCheckpoint.LastSequenceNumber`,
  starting at `0` for a projection that has never run) and always connects
  with `mode=replay&fromSequenceNumber=<checkpoint>`. Per `ADR-010`,
  replay-then-tail is one continuous poll loop on the server side — there
  is no behavioral difference from `mode=tail` once a projection is caught
  up, so there is no reason to ever use `mode=tail` and track two code
  paths for "starting fresh" vs. "resuming."
- **A full rebuild is not a separate mechanism — it's the same mechanism
  starting from zero.** Truncate the projection's read-model table(s) and
  its `ProjectionSnapshot` rows (`ADR-016`), reset
  `ProjectionCheckpoint.LastSequenceNumber` to `0`, reconnect. Replay from
  `0` regenerates the read model from the complete history exactly as the
  original incremental build would have, by construction — see `ADR-016`
  for why this determinism holds.
- **The read side is a separate physical store from the write side** —
  its own `DbContext`, its own database, reachable only via HTTP from the
  write side (there is no shared connection string, no cross-database
  query, no read replica of `EventStoreContext`). This is deliberate, not
  incidental: sharing a database would blur exactly the write/read
  separation CQRS exists to make explicit, undermining the point of using
  this as a teaching example for it. Unlike `ADR-001`'s write-side
  three-provider build, the read side does **not** need a per-provider
  split — `09-cqrs-read-models.md` explains why (its schema is ordinary
  typed relational columns, not portable JSON-text-plus-native-JSON-function
  querying, so there's no provider-specific translation layer to isolate
  in the first place). One EF Core provider (SQLite, for the example) is
  enough.
- Runs as its own deployable (`EventStore.Projections.Host`,
  `06-solution-structure.md`) — not in-process inside any
  `EventStore.Host.<Provider>` — so the write/read split is real at the
  deployment level, not just conceptual.

Consequences:
- **Read models are eventually consistent with the write side, inherently
  and by design** — a `ProjectionHost` only sees an event after it's been
  published and after its own poll interval elapses. This design does not
  attempt read-after-write consistency (e.g. a client publishing and then
  immediately querying a projection and expecting to see it) — that would
  need an explicit sync signal this system doesn't provide, same category
  of "not solved here" as Follow's unbounded replay burst (`ADR-010`).
- Projections inherit Follow's existing guarantees for free (no gap, no
  duplicate across a reconnect, `ADR-010`) and its existing limitations for
  free too (an unbounded burst on `fromSequenceNumber=0` against a large
  history, no batching/backpressure) — same accepted trade any other Follow
  consumer already accepts, not a new risk introduced by projections.
- Because rebuild is just "replay from `0` again," there is no separate
  rebuild code path to maintain, test, or let drift from the normal
  incremental path — a real simplification, not just a convenient framing.
- A `ProjectionHost` is subject to `RequiredReadClaim` (`ADR-008`) and
  masking (`ADR-009`) exactly like any other Follow caller — it is not a
  store-internal trust boundary that bypasses either. If a projection needs
  to see a claim-gated event type, its service identity (a fourth OAuth2
  client, alongside the three in `ADR-006`) needs that claim like anyone
  else. This is a genuine constraint on what a projection can be built
  over, not an oversight — see `09-cqrs-read-models.md`.
- Running a dedicated process per projection group, rather than in-process
  inside the write-side host, is more moving parts for this example than
  an in-process background service would be — an accepted cost for making
  the CQRS split legible as two things you actually deploy separately, not
  just two namespaces in one process.
- **Checkpoint-advance granularity (per-event vs. per-batch) is a pure
  throughput knob, not a correctness one.** Because `SnapshotMerger`'s
  Full-replace and Partial-merge-patch operations are both idempotent
  (`ADR-016`), reprocessing a batch of already-applied events after a
  crash produces the same end state, not corruption — it only redoes
  wasted work. `ProjectionHost` can therefore batch any number of events
  between checkpoint writes with no additional correctness mechanism
  required; see `09-cqrs-read-models.md` for the configurable mechanism.
