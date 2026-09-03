[← ADR index](../07-adrs.md)

# ADR-005: Event parenting as an envelope-level DAG, validation mode per event type

Status: Accepted

Context: Consumers need to express that one event is causally parented off
one or more prior events, possibly of a different event type, forming
chains/DAGs across the store. This must not corrupt the JSON-Schema-validated
payload, and different event types have different tolerance for referencing
a parent that hasn't been published yet (e.g. out-of-order ingestion from an
upstream system vs. a strictly ordered internal workflow).

Decision:
- Parent links are envelope metadata (`parentEventIds` on publish), stored in
  a dedicated `EventParents` join table — never embedded in `Payload` or the
  registered JSON Schema.
- `parentEventIds` is optional; omitted/empty means an origin event with no
  parents.
- Parent-existence validation is configurable **per event type** via
  `EventTypeDefinition.ParentValidationMode` (`Strict` default | `Permissive`),
  set at schema registration time.
- Any event type may be listed as a parent of any other event type — chains
  are not restricted to same-type lineage.
- Read access to the DAG is via a dedicated Lineage API
  (`/events/{id}/parents|children|ancestors|descendants`, later `QUERY`
  per `ADR-012`), not via `$filter` on the follow API.

Consequences:
- `Strict` mode plus the append-only, monotonically increasing
  `SequenceNumber` together guarantee the parent graph is acyclic by
  construction (a parent must already exist, hence have a lower
  `SequenceNumber`, before a child can reference it).
- `Permissive` mode gives up that guarantee: two Permissive-mode events can
  reference each other (A published parented off not-yet-existing X; X later
  published parented off A, which passes because A already exists by then),
  forming a 2-cycle. Ancestors/descendants traversal must therefore be
  cycle-safe **unconditionally**, not just for event types configured
  Permissive — a Strict-mode event can still have a Permissive-mode ancestor
  somewhere in its chain.
- `EventParents.ParentEventId` cannot carry a real database foreign-key
  constraint (it must tolerate dangling references for Permissive event
  types), so Strict-mode existence checks are enforced in the application
  layer at publish time, not the database schema — ~~`ParentLinkService`~~
  **correction, a design-compliance audit this session found no such
  class exists**: the check lives inline in
  `PublishService.PublishAsyncCore` (`src/EventStore.Inbox/
  PublishService.cs`), never split into its own named service.
- Ancestors/descendants require provider-specific raw SQL (recursive CTEs);
  EF Core's LINQ provider has no recursive-query translation, so this is the
  one query path in the store that can't be a pure `IQueryable` like the
  rest.
