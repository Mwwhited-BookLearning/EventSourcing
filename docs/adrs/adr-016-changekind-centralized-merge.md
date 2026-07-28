[← ADR index](../07-adrs.md)

# ADR-016: Event-type `ChangeKind` (Full | Partial) and centralized snapshot merge

Status: Accepted

Context: a real business event stream mixes events that establish or
wholesale-replace an entity's known state (e.g. `OrderPlaced`, carrying
everything known about a new order) with events that carry only a delta
(e.g. `OrderAddressUpdated`, carrying only the changed address field). A
projection applying these onto its own materialized state needs a single,
uniform rule for which is which and how each gets applied — otherwise every
`IProjection<TReadModel>` implementation reinvents its own ad hoc merge
logic, and the rule quietly drifts across projections.

Decision:
- `EventTypeDefinition` gains a **required** field, `ChangeKind`
  (`Full` | `Partial`) — set at registration
  (`05-schema-registry-and-spec-generation.md`), alongside
  `ParentValidationMode`/`RequiredPublishClaim`/`RequiredReadClaim`.
  Unlike those three, **`ChangeKind` has no default** — registering an
  event type without it is rejected (`400`), because guessing wrong here
  (assuming `Full` for something that's actually a delta, or vice versa)
  silently corrupts every projection over that type, whereas the other
  three fields default to "no extra restriction," a safe no-op.
- **The merge rule, applied once, centrally, in `ProjectionHost`**
  (`ADR-015`) — never reimplemented per projection:
  - `ProjectionHost` maintains one JSON snapshot per **projection-defined
    key** (`IProjection<TReadModel>.GetKey(StoredEvent)`,
    `09-cqrs-read-models.md`) per projection, in a `ProjectionSnapshot`
    table (`{ProjectionName, Key, SnapshotJson, LastAppliedSequenceNumber}`).
  - Applying a `Full` event **replaces** that key's whole snapshot with the
    event's payload.
  - Applying a `Partial` event **merges** the event's payload onto the
    existing snapshot for that key: a field present in the incoming
    payload overwrites; a field **absent** is left untouched. **This is
    deliberately the same overlay rule masking's consumer guidance already
    states** (`ADR-009`: "masked/absent fields must be skipped, never
    overlaid") — one overlay rule for the whole design, not two
    similar-but-subtly-different ones that could drift apart. A `Partial`
    event whose payload happens to contain a masked field (because
    `ProjectionHost`'s own claims don't cover it) is, from the merge's
    point of view, simply an absent field — no special-casing needed
    beyond the rule already stated.
  - Only **after** the merge does `ProjectionHost` call
    `IProjection<TReadModel>.Project(mergedSnapshotJson)` to map the
    fully-current-state JSON into the strongly-typed read-model row that
    gets upserted. **Individual projections never see raw events, never
    see `ChangeKind`, and never implement merge logic at all** — they only
    ever receive "the current, fully-merged state for this key," already
    resolved.
- A `Partial` event for a key with no existing snapshot (its `Full`/origin
  event hasn't been seen yet, or never will be, under this key) simply
  starts a snapshot from just that event's fields — there is no "wait for
  the `Full` event first" ordering enforcement. Whether a given key's first
  event is actually `Full` is a **producer discipline** concern, same
  category as `StreamId`'s freeform convention elsewhere in this design:
  the store has no way to know what a projection's key even is (key
  extraction is projection-defined, per above), so it has no way to enforce
  anything about ordering relative to it.

Consequences:
- One overlay rule, shared by name and by cross-reference between
  `ADR-009` and here, rather than two independently-maintained "ignore
  missing on merge" rules that could quietly diverge — a direct, deliberate
  payoff of building projections as an ordinary Follow consumer (`ADR-015`)
  subject to the same masking behavior as anyone else, rather than a
  privileged internal path.
- `ChangeKind` being required with no default means every existing/future
  event type registration must decide this explicitly — a small but real
  addition to the registration payload's required fields
  (`03-api-contracts.md`), not purely additive the way the three optional
  fields were.
- Getting a type's `ChangeKind` wrong at registration is a silent data
  problem, not a loud one: a `Partial` type mistakenly registered as `Full`
  causes every projection over it to lose previously-known fields on the
  next event for a key; a `Full` type mistakenly registered as `Partial`
  causes stale fields to survive an event that meant to replace them
  entirely. Neither failure mode produces an error anywhere — this is a
  real risk accepted for v1, not something this design detects.
- `ProjectionSnapshot` grows one row per distinct key per projection,
  unboundedly, same shape of accepted gap as Follow's unbounded replay
  burst (`ADR-010`) — no TTL or eviction is designed here.
- Two different projections over the same event types may use different
  key-extraction logic and therefore maintain entirely separate snapshot
  spaces — `ChangeKind`'s Full/Partial semantics apply per key within one
  projection's snapshot space, not globally across projections.
- The merge itself is exactly **JSON Merge Patch (RFC 7396)** applied to
  the snapshot: "a field present in the incoming payload overwrites; a
  field absent is left untouched" is RFC 7396's semantics verbatim, with
  one deliberate narrowing — RFC 7396 also lets an explicit `null` value
  *delete* a key from the target, which this design does not want (a
  `Partial` event's field is never expected to erase a previously-known
  fact, only add to or overwrite it); `MergePatch` in
  `09-cqrs-read-models.md` implements the overwrite-if-present half only,
  not the delete-on-null half.
