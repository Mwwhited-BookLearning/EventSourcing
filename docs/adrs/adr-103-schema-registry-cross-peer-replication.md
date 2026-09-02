[← ADR index](../07-adrs.md)

# ADR-103: Schema-registry state replicates across peers via a targeted Router reactor

Status: Accepted

Context: `ADR-102`'s own live cross-provider verification found a real
gap: `SchemaRegistryService.RegisterAsync` writes `EventTypeDefinition`
directly to the local table, never through the fold pipeline `ADR-033`'s
peer sync replicates — only a reserved `schemaregistered` audit event
(`{EventTypeName, Version}`) travels between peers, and nothing folded it
back into a usable `EventTypeDefinition` row on the receiving peer. A
real GraphQL query against a peer a schema was registered on only
elsewhere correctly errored `"the field ... does not exist on the type
Query"` — confirmed directly, not assumed. `docs/10-open-questions.md`
row 1 named the two ways to close this: build a real fold worker
(mirroring `EventStore.DevIdp`'s `RbacProjectionWorker`), or permanently
correct `ADR-033`'s own claim down to "only the notification replicates."
Direct request: build the fold worker.

`SchemaRegisteredEventType.cs`'s own header comment already states a
real, deliberate reason `EventTypeDefinition` was never rearchitected
onto a generic Router-fold: "every prior build-plan item's own tests
assume synchronous, immediately-consistent registration, which an async
Router-fold would break." This ADR does **not** overturn that — the
*local*, originating registration stays exactly as synchronous as
before; only a *second*, distinct peer never independently registers
that schema.

Decision:
- **The reserved `SchemaRegistered` notification event's payload is
  widened** from `{EventTypeName, Version}` to the full registration —
  `JsonSchema`, `ChangeKind`, `EntityIdField`, `EntityType`,
  `ParentValidationMode`, `RejectionBehavior`, `RequiredClaims`,
  `FilterableFields` (including `ADR-096`/`097`'s own
  `SearchableIndexConfig`), `RequiredSignature`, `ExpectedResponse`,
  `UpcastFromPrevious`/`DowncastToPrevious`. `EventTypeName` (not
  `Name`) is unchanged — `SchemaRegisteredEventType`'s own
  `EntityIdField` (`"$.EventTypeName"`) depends on that exact key.
  Additive only; `required` stays `["EventTypeName", "Version"]`, and a
  pre-this-ADR peer's own narrower notification still parses (a caller
  handling a shorter payload than expected simply sees the newer fields
  absent).
- **A real per-site `OriginId`, not a hardcoded literal, on this
  notification** — a real, separate bug found while designing this:
  `AppendSchemaRegisteredAsync` stamped the literal string `"local"` for
  *every* site, unlike an ordinary published event (`PublishService`'s
  own configurable `OriginIdOptions`). Every site's own notification
  would have been indistinguishable from every other's the moment they
  synced together — the exact signal this mechanism needs to tell "my
  own copy, already applied directly" from "elsewhere, needs folding."
  Fixed via `SchemaRegistryOriginIdOptions`, a **duplicate** of
  `EventStore.Inbox.OriginIdOptions`, not a reference to it —
  `EventStore.Inbox` depends on `EventStore.SchemaRegistry`
  (`ADR-033`/`090`), so a reference the other way would cycle. Both bind
  to the same `"OriginId"` configuration section
  (`AppHost.cs`'s `OriginId__OriginId` env var), so a real deployment's
  one configured value resolves identically through either type — the
  same "duplication over reference" precedent this file's own prior
  hardcoded-`"local"` already established, now made a real, correct
  value instead of a decoy.
- **A new, narrowly-scoped Router reactor
  (`SchemaRegistrationReplicationResolver`), not a generic rearchitecture
  of `EventTypeDefinition`'s own write path** — the exact "special-
  purpose reactor, one event type" shape `AuthorityDecisionResolver`/
  `EntityErasureResolver` already establish (`RouterWorker`'s own
  "ordinary fold above, additional reactor effect here" convention),
  never the deliberately-avoided generic Router-fold
  `SchemaRegisteredEventType.cs`'s own comment names. Fires only when
  `storedEvent.EventType == "schemaregistered" && storedEvent.OriginId
  != schemaRegistry.SiteOriginId` — a notification that arrived via
  peer-sync from **another** site, never this site's own locally-
  originated copy, which already applied directly and synchronously via
  `RegisterAsync` itself, unchanged. Checked **ahead of** `ADR-038`'s own
  rollback gate, not after it — found only by actually running this
  cross-peer, not assumed: that gate treats "this deployment has never
  seen `SchemaRegistered` itself registered for this AppId" as "this
  deployment predates the shape, defer forever," which is exactly true
  and *permanent* for a peer that only ever receives replicated schemas
  and never independently calls `RegisterAsync` for that AppId at all.
  The reactor reads the raw payload directly (the same "no shared schema
  walker" posture its two siblings already use), needing no schema
  resolution to run correctly, and marks the notification `applied`
  immediately — deliberately skipping this copy's own otherwise-generic
  `"schema:{name}"` entity fold (cosmetic lineage bookkeeping nothing in
  this codebase actually queries), not worth re-deriving the bootstrap-
  registration ordering that gate exists to protect just to preserve it
  for a replicated copy.
- **`SchemaRegistryService.ApplyReplicatedRegistrationAsync`** — the
  peer-sync counterpart to `RegisterAsync`, trusting the origin site's
  already-validated decision completely: no re-validation, no auto-
  incrementing version (the replicated `Version` **is** the authoritative
  one), and never appends a second `SchemaRegistered` notification (would
  gossip-amplify forever in a full-mesh topology, `ADR-033`). Idempotent
  by construction — `(AppId, Name, Version)` already present is a no-op,
  the same idempotency `RbacProjectionWorker`'s own fold-target methods
  already establish, covering redelivery from `ADR-033`'s own
  gossip/catch-up mechanics and a mesh with more than two hops alike.
  Only supersedes the currently-`Active` version if the replicated one is
  genuinely newer, guarding against an out-of-order/late redelivery
  regressing `IsActive` backward. Replicates a `FilterableField`'s own
  provider-specific index DDL too (the same generator `RegisterAsync`
  already uses) — a replicated indexed field is genuinely queryable
  identically on the receiving peer, not merely present as metadata.

Consequences:
- **Live-verified cross-provider, the same real-infrastructure bar
  `ADR-102` set**: `ReplicationCrossProviderHttpTests.cs`'s new
  `AnEventTypeRegisteredOnlyAtTheSqliteSiteBecomesQueryableAtTheRealSqlServerSiteWithNoIndependentRegistrationThere`
  registers `WidgetCreated` (with a real indexed `FilterableField`) only
  at a real SQLite site, confirms it 404s at a real SQL Server site
  first, pushes the SQLite site's event log over real HTTP via
  `PeerSyncClient`, and polls until `WidgetCreated` becomes genuinely
  queryable at the SQL Server site — proving both the `EventTypeDefinition`
  row and its filterable-field index actually replicate, not just the
  bare `JsonSchema` text.
- `docs/10-open-questions.md` row 1 is resolved and deleted — this ADR is
  its permanent record.
- **Not solved here, deliberately**: a schema registered before this ADR
  shipped, on a peer that never independently registers it locally,
  needs its own `SchemaRegistered` notification re-delivered somehow to
  benefit (a fresh catch-up sync from a peer that still holds it, or a
  manual re-registration) — this ADR replicates registrations going
  forward, it doesn't backfill history. `ADR-033`'s own named,
  not-yet-built Merkle-tree catch-up optimization is unaffected either
  way.
- `ADR-033`'s own struck-through correction note is updated to point at
  this ADR instead of describing the gap as still-open.
