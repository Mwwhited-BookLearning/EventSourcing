[← ADR index](../07-adrs.md)

# ADR-021: Entity as a first-class concept (`EntityId`, Entity Store, `ExpectedVersion`)

Status: Accepted

Context: Every event in this design has, until now, belonged only to an
**event type** (a registered schema) plus an optional `StreamId` (a loose
grouping convenience, never used for identity, versioning, or a
materialized current-state). Integrating the second design package
(`docs/design-docs/`) surfaces a structural gap this exposes: that
design's entire model — optimistic concurrency (`ExpectedVersion`),
conflict flagging, sharding, GraphQL's entity graph, `Optional<T>`
folding — is built on top of one thing EventSouring never had: an
**entity** with a stable identity, a version number, and a canonical
"current state" that's always kept up to date, not opt-in per projection.
Without this, ADR-015/016's CQRS projections remain a one-off pattern
(`OrderSummaryProjection`) rather than the general mechanism the rest of
this integration needs to build on.

Decision:
- Every `StoredEvent` gains a required `EntityId`:
  `{appId}:{entityType}:{uniqueId}` — resolved either from a
  publisher-supplied `uniqueId` (the common case — the publisher already
  knows which entity it's patching) or server-assigned on first creation
  of a new entity (an *origin* event with no `EntityId` supplied yet).
  This subsumes `StreamId` — `StreamId` is removed as a separate concept;
  it was already trying to be this.
- A new **Entity Store** table — mutable, versioned, hashed, one row per
  `EntityId` — is folded from the event store by the same kind of
  projector `ProjectionHost` already is (`ADR-015`), except this one is
  **not opt-in**: every entity type gets exactly one canonical Entity
  Store row, materialized automatically, the way `OrderSummaryProjection`
  today is a bespoke example a developer chose to write. Custom CQRS
  projections (`ADR-015`/`ADR-016`) remain exactly as designed — this adds
  a *default*, always-present projection beneath them, it doesn't replace
  the general projection framework.
- `ExpectedVersion` (an optional field on publish, alongside `schemaVersion`
  — `ADR-020`) states which `Entity Store` `Version` the sender believed
  they were patching. Omitted: no concurrency check, applied unconditionally
  (matches today's behavior for any event type that doesn't care about
  entity versioning). Supplied: used by `ADR-024`'s conflict detection.
- Entity Store row shape (see `02-data-model.md` for the full column list):
  `EntityId` (PK), `EntityType`, `Version` (monotonic, bumped on every
  fold), `Data` (current materialized snapshot), `Hash` (SHA-256 of
  canonicalized `Data` — reuses `ADR-019`'s hash primitive, a different
  application of it: per-entity integrity/diffing, not a chain),
  `SchemaVersion` (current shape, post-upcast), `LastAppliedSequenceNumber`
  (replay checkpoint, same idea as `ProjectionCheckpoint` in `09-cqrs-
  read-models.md` but for this one default projection specifically).
- **Rebuild is the same "replay from `0`" mechanism `ADR-015` already
  established** for custom projections — the Entity Store is not special
  in this respect, it's just always running.

Consequences:
- `parentEventIds`/`EventParents` (`ADR-005`) and `EntityId` are
  **different axes, deliberately kept separate**: `EntityId` says "this
  event is a patch to entity X"; `parentEventIds` says "this event is
  causally derived from events A, B, C" (possibly of other entities/types
  entirely). An event patching one entity can still declare parents on
  completely different entities — lineage and entity-identity answer
  different questions and neither subsumes the other.
- Every existing event type must now declare how its `uniqueId` is
  derived (usually a field already in its payload, e.g. `OrderId`) —
  a registration-time addition (`EntityIdField` on `EventTypeDefinition`,
  alongside `ChangeKind`) with no safe default, same category of
  required-with-no-default field as `ChangeKind` itself (`ADR-016`) and
  for the same reason: guessing wrong here silently scrambles every
  entity's identity.
- The Entity Store becomes the **read path for "current state of X"**
  queries (`ADR-029`'s GraphQL layer reads from here, not from replaying
  raw events per request) — this is the same split design-docs draws
  between the event store (history, source of truth) and entity store
  (current state, a rebuildable cache of it), now adopted here.
- This is a genuinely large structural addition — every existing feature
  doc/ADR that assumed "no entity concept" (`ADR-008`'s claims, `ADR-009`'s
  masking, `ADR-016`'s merge, `ADR-018`'s upcasting) still works
  unchanged at the *event* level; they simply now also feed one more
  always-on fold in addition to whatever custom projections exist.
