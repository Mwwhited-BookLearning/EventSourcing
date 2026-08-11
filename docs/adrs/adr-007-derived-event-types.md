[← ADR index](../07-adrs.md)

# ADR-007: Derived/materialized event types via cross-stream join+projection

Status: ~~Deferred — captured for design continuity, not part of v1. Build
after the primary system (publish/follow/registry/lineage/auth) is
working.~~ **Corrected, later pass**: built — `docs/08-build-plan.md`'s
item 8 ("Derived/Materialized Event Types (deferred)") is marked Done.
`src/EventStore.Derivation/` implements the mechanism this ADR describes:
`DerivationRegistrationService`/`DerivationEndpoints` (registration, the
`$from`/`$on`/`$select` grammar via `OnClauseParser`/`SelectClauseParser`,
and the cycle-guard walk) and `DerivationWorker` (the tailing/join/emit
background process).

Context: There's a real want for **derived event types**: an event type
whose instances are produced not by an external publisher, but by a
server-side process that tails one or more existing source event streams,
joins them by key, projects a subset of fields, and publishes the result as
a new event type — e.g. `OrderPlaced` + `PaymentReceived` joined on
`OrderId` produces `OrderPaid`. This is materially more complex than the
rest of v1 (unbounded join-state, emission-trigger semantics, checkpointing,
backfill correctness) and is explicitly sequenced as a secondary feature
set, built once the primary system is in place. Recorded here so the shape
of the idea and the decisions already made about it aren't lost, and so
future v1 work doesn't accidentally foreclose it.

Decision (captured now, not implemented now):
- Registration shape: something like
  `POST /create/{event-type}?$from=A,B&$on=A/OrderId eq B/OrderId&$select=...`
  — this registers a **derivation definition**, analogous to
  `PUT /registry/{event-type}`, except the JSON Schema for the new event
  type is auto-composed from `$select` against the source types' already-
  registered schemas, not hand-authored.
- `$on` is an explicit equality expression across named source fields (not
  a `StreamId`-convention join) — standard OData has no multi-resource join
  operator, so this is necessarily a hand-rolled, OData-*inspired* mini-
  grammar, not literal OData. **`$from` accepts an arbitrary-length,
  comma-separated source list, not just two** — `$on` becomes a
  conjunction of pairwise equalities across however many sources are
  named (e.g. `$from=A,B,C&$on=A/OrderId eq B/OrderId and A/OrderId eq
  C/OrderId`). A single n-ary derivation is preferred over forcing
  three-plus sources into a chain of pairwise derivations — chaining is
  still allowed where it's the more natural shape for the data, but it's
  a choice, not something the design forces on every 3+-source case.
- The join/emit trigger — **fire-once inner join** (wait for one event per
  source per key, emit once, key closes) vs. **continuous latest-state
  enrichment** (any new arrival on any source re-emits, joined against the
  current latest state of the others) — is **configurable per derived event
  type** at registration time, not a single global choice.
- Backfill-from-history vs. from-now-only is likewise **configurable per
  derived event type** at registration time — and when a declared source
  is itself a derived event type, whether backfill recurses through that
  source's own upstream derivation history (vs. treating its existing
  output as the starting point) is a **further, per-derivation
  configuration choice** (e.g. `backfillThroughDerivedSources: true|false`),
  not a single fixed answer for every derivation.
- Execution model: a background process per derivation, architecturally "an
  internal follower" — it tails each declared source stream the same way
  `EventTailReader` does for the Follow API (`04-odata-filter-pushdown.md`),
  then republishes through the same publish/append path used by external
  publishers.

Consequences / why this doesn't block v1, and what to remember meanwhile:
- **No v1 design change is required to accommodate this later.**
  `EventParents` (`ADR-005`) already provides exactly the right mechanism
  for a derived event to record its sources: a derived `OrderPaid` event
  would simply set `parentEventIds: [orderPlacedId, paymentReceivedId]` when
  published — no schema or data-model change needed. This is a genuine
  synergy between the two features, not a coincidence to re-verify later.
- `EventTypeDefinition` should not be assumed to always come from a
  hand-authored `PUT /registry/{event-type}` body — a future
  `DerivationDefinition` will programmatically register one through the same
  path. Nothing in `05-schema-registry-and-spec-generation.md` currently
  assumes otherwise; keep it that way.
- The derivation background process reuses the tailing/polling primitive
  the Follow API already needs (`EventTailReader`) rather than requiring a
  new persistence mechanism — a reason not to build that primitive in a way
  only the Follow API can reach.
- Derivation registration will most likely reuse the `registry:admin` scope
  (`ADR-006`) rather than inventing a new one — defining an event type is a
  single administrative capability whether the type is hand-authored or
  derived.
- **Pending fire-once-join state is durable and TTL-bounded, not a bare
  in-memory cache.** A key that hasn't completed across all its declared
  sources is recorded in a `PendingJoinState` table (derivation name, join
  key, whatever fields have arrived so far, first-seen timestamp) — so it
  survives a worker restart rather than silently vanishing — and expires
  (`ExpiresAt = FirstSeenAt + Ttl`, `Ttl` configurable per derivation
  definition, swept periodically) if the remaining sources never arrive.
  An expired pending join is dropped with a recorded reason, not silently
  discarded — worth a dead-letter-style record so an operator can see
  which keys never completed, though the exact shape of that record isn't
  designed further here. This resolves what was previously this ADR's
  open "unbounded pending state, optional TTL?" question.
- **Derivation-*definition* cycles must be rejected at registration time,
  not just detected at runtime.** This is a different cycle from
  `ADR-005`'s `CycleGuard` — that one guards a single traversal of already-
  published *events'* parent DAG (an inert data structure); this one
  guards the small graph of derivation *definitions themselves* (derived
  type → its declared `$from` sources), where a cycle isn't inert at all:
  if type `C` is derived from `A`+`B`, and someone later registers `A` as
  derived (transitively) from `C`, each derivation worker's republish
  becomes a new triggering event for the other, forever. Registering a
  new derivation must walk the existing derivation-definition graph and
  reject (`400`) if the new `$from` sources transitively include the type
  being defined. **A plain depth-first walk with a hash-set of visited
  types is the standard pattern for this, and it's sufficient here** — no
  specialized cycle-detection algorithm (e.g. Floyd's tortoise-and-hare,
  which targets cycles in a single-successor sequence, not a multi-parent
  graph) is needed, because the graph being walked is the small,
  admin-scale set of *derivation definitions themselves* — tens of
  registrations, changing rarely — not the runtime-scale graph of
  published events `ADR-005`'s `CycleGuard` already has to handle
  differently for exactly that reason.
- **Belt-and-suspenders runtime safety net, for the residual race
  condition the registration-time check can't fully close** (two
  concurrent registrations each passing their own individual check before
  either commits): every derived event's envelope carries a
  `derivationHopCount` — incremented by one each time a derivation
  worker's republish is itself the triggering event for *another*
  derivation. A configured **max depth** per derivation definition
  (a small default, e.g. `5`) causes the worker to stop and dead-letter
  (the same `EventUpcastFailed`-style pattern `ADR-020` establishes,
  generalized — not designed further here) rather than propagate forever
  if that count is ever exceeded. This is a cap on symptoms, not a
  correctness mechanism — the registration-time graph walk above is what
  actually prevents a cycle from being registered in the first place.

With the above, this ADR carries no unresolved technical questions of its
own anymore — like `ADR-009`, it's a pure priority/sequencing decision
(build after Phases 0–6, not because anything here is still undecided).
