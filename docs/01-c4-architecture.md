# C4 Architecture

Diagrams use PlantUML with the C4-PlantUML macros
(`https://github.com/plantuml-stdlib/C4-PlantUML`). These are the static
structural views; for the runtime/dynamic view of a specific feature (plain
PlantUML sequence diagrams, plus the Gherkin scenarios they illustrate), see
`features/*.md`.

**This diagram reflects the post-integration shape** (`ADR-021` onward —
see `CLAUDE.md`'s "Integration status"). If you're looking for the
OData-era, single-store, reject-on-invalid picture this superseded, it's
in git history, not here.

**Deployment note**: per `ADR-001`, the Publish/Schema-Registry/Spec-
Generator/GraphQL-Gateway containers below are built as **three separate
deployables per site** — `EventStore.Host.Sqlite`/`.Postgres`/`.SqlServer`
— one per database provider, not a single artifact with a runtime switch.
Per `ADR-034`, a *site* itself holds a subset of shards (by `EntityType`);
per `ADR-033`, at least two sites replicate any given shard. C4 Container
diagrams describe logical architecture, not deployment topology, so this
is a note rather than a full multi-site deployment diagram — see
`06-solution-structure.md` for the project split and
`docs/comparisons/peer-sync-topology.md`/`sharding-strategy.md` for the
topology decisions themselves.

## Context diagram

```plantuml
@startuml C4_Context
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Context.puml

Person(publisher, "Publishing System", "Emits domain patches/actions -- may be self-attested (ADR-035/036)")
Person(follower, "Consuming System", "Queries current state, history, and subscribes to live changes")
Person(operator, "Platform Operator", "Registers event types / schemas, per AppId (ADR-030)")

System(eventStore, "Open Event-Sourced Entity Platform", "Multi-tenant framework: persists everything, folds into an Entity Store, exposes GraphQL. One instance per site.")
System_Ext(idp, "EventStore.DevIdp", "Dev-mode OIDC token issuer + OAuth Token Exchange (OpenIddict, in-process) -- ADR-006/036")
System_Ext(peerSite, "Peer Site", "Another instance of the same platform, replicating shared shards -- ADR-033")

Rel(publisher, idp, "Obtains Bearer token (ordinary, or exchanged from a self-attested UCAN)", "OAuth2 client_credentials / RFC 8693")
Rel(follower, idp, "Obtains Bearer token", "OAuth2 client_credentials")
Rel(operator, idp, "Obtains Bearer token", "OAuth2 client_credentials")

Rel(publisher, eventStore, "POST /publish/{event-type}\nBearer <JWT> + DPoP proof -- always 202, never 400 for shape/authority problems (ADR-023)", "HTTPS/JSON")
Rel(follower, eventStore, "GraphQL Query (current state, history) / Subscription (live changes)\nBearer <JWT>, over HTTP QUERY -- never GET, keeps PII/PHI out of URLs (ADR-037)", "HTTPS")
Rel(operator, eventStore, "PUT /registry/{event-type}\nBearer <JWT>", "HTTPS/JSON")
Rel(eventStore, idp, "Validates Bearer token + DPoP proof; exchanges self-attested UCANs (ADR-017/036)", "OIDC discovery + JWKS")
Rel(eventStore, peerSite, "Gossip replication -- durable, fault/abend/restart-tolerant peer-sync outbox/inbox (ADR-033)", "HTTPS/JSON, bidirectional")
Rel(eventStore, publisher, "OpenAPI contract (anonymous)", "HTTPS")
Rel(eventStore, follower, "GraphQL SDL, per AppId (anonymous)", "HTTPS")

@enduml
```

## Container diagram

```plantuml
@startuml C4_Container
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Container.puml

Person(publisher, "Publishing System")
Person(follower, "Consuming System")
Person(operator, "Platform Operator")
System_Ext(idp, "EventStore.DevIdp", "OIDC + Token Exchange -- ADR-006/036")
System_Ext(peerSite, "Peer Site(s)", "ADR-033")

System_Boundary(system, "Open Event-Sourced Entity Platform (one site)") {
    Container(inbox, "Inbox / Publish Endpoint", ".NET (ASP.NET Core)", "POST /publish; persists first, always 202 unless the envelope itself is unparseable (ADR-023); Idempotent Receiver (ADR-011)")
    Container(router, "Router", "Background service", "Schema validation, entity resolution (ADR-021), live upcast validation + materialization (ADR-020/027), non-authoritative claim capture (ADR-035) -- all advisory, none block Inbox's 202")
    ContainerDb(eventLog, "Event Log", "EF Core over SQLite/Postgres/SqlServer (ADR-001)", "StoredEvent, EventParent -- insert-only, hash-chained (ADR-019)")
    Container(fold, "Fold / Projector", "Background service", "Logical-order fold (OccurredAt, not arrival order -- ADR-029); optimistic-concurrency conflict flagging (ADR-024); always-on, not opt-in")
    ContainerDb(entityStore, "Entity Store", "Mutable, versioned, hashed, sharded by EntityType (ADR-021/034)", "Current materialized state -- the only thing GraphQL reads read from")
    Container(registry, "Schema Registry Service", ".NET", "CRUD for named/versioned JSON Schemas, AppId-scoped (ADR-030); FilterableFields, ChangeKind, EntityIdField, upcast/downcast maps (ADR-018/028)")
    Container(graphql, "GraphQL Gateway", ".NET (Hot Chocolate-class), QUERY method", "Query (entity + change history), Subscription (live changes, replaces OData $filter/Follow) -- per-AppId schema (ADR-030/037); depth/cost limiting")
    Container(specGen, "Spec Generator", ".NET", "Builds OpenAPI (publish) + GraphQL SDL from registry state; MaskingSchemaTransformer (ADR-002/009)")
    Container(streaming, "Streaming Channel Service", ".NET", "Batch ingest + tail/replay for telemetry & media channels (ADR-031) -- bypasses schema validation/hash-chain/fold entirely")
    ContainerDb(streamStore, "Streaming Channel Store", "Plain append-only table, v1 engine choice (ADR-031)", "TelemetryChannel, TelemetrySample")
    Container(attachments, "Attachment Service", ".NET + WebDAV endpoint", "Content-addressed binary uploads, browsable via WebDAV (ADR-032)")
    ContainerDb(attachmentStore, "Attachment Store", "Content-addressed", "Attachment, AttachmentRef")
    Container(peerSync, "Peer Sync Outbox/Inbox", "Durable store + background service, gossip topology", "Fault/abend/restart-tolerant (ADR-033); reuses the same durable transport as Inbox above")
}

System_Boundary(readSide, "CQRS Read Side (example) -- separate deployable and database, ADR-015") {
    Container(projectionHost, "Projection Host", ".NET (background service)", "Opt-in custom projections, on top of the always-on Entity Store above (ADR-015/016)")
    ContainerDb(readDb, "Read Model Store", "EF Core, its own database", "ProjectionCheckpoint, ProjectionSnapshot, OrderSummary (example)")
}

Rel(publisher, inbox, "Publishes patches/actions", "HTTPS/JSON, Bearer + DPoP")
Rel(follower, graphql, "Query / Subscription", "HTTPS (QUERY method), Bearer + DPoP")
Rel(operator, registry, "Registers schemas", "HTTPS/JSON, Bearer")
Rel(publisher, streaming, "Batch-ingests channel samples", "HTTPS, Bearer")
Rel(publisher, attachments, "Uploads binary content", "HTTPS/WebDAV, Bearer")

Rel(inbox, eventLog, "Append \"received\" (Idempotent Receiver + Inbox pattern)")
Rel(inbox, router, "Notify new item")
Rel(router, registry, "Schema + claims + upcast/downcast maps lookup (advisory)")
Rel(router, eventLog, "Append routed event; append UpcastMaterialization (ADR-027) or EventUpcastFailed (ADR-020) as needed")
Rel(fold, eventLog, "Replay in OccurredAt order")
Rel(fold, entityStore, "Write materialized version; ConflictFlag/LateArrivalFlag")
Rel(graphql, entityStore, "Read current state (sharded, ADR-034)")
Rel(graphql, eventLog, "Read change history (ADR-024 §8.4)")
Rel(registry, eventLog, "n/a -- registry has its own table, shown for scope only")
Rel(specGen, registry, "Read schema/event-type metadata")
Rel(publisher, specGen, "GET /openapi.json (anonymous)")
Rel(follower, specGen, "GraphQL SDL introspection (anonymous)")
Rel(streaming, streamStore, "Batch append; tail/replay (ADR-010's shape, reused)")
Rel(attachments, attachmentStore, "Content-addressed put/get; WebDAV PROPFIND/GET/PUT")
Rel(eventLog, peerSync, "Feeds outbound peer sync")
Rel(peerSync, eventLog, "Delivers events from peers -- same path as Inbox, no special-casing")
Rel(peerSync, peerSite, "Gossip exchange", "HTTPS/JSON")

Rel(inbox, idp, "Validates Bearer + DPoP; may trigger Token Exchange (ADR-036)")
Rel(graphql, idp, "Validates Bearer + DPoP")
Rel(registry, idp, "Validates Bearer + DPoP")

Rel(projectionHost, graphql, "Subscribes (its own client identity)", "HTTPS, Bearer")
Rel(projectionHost, readDb, "Upsert snapshot + read-model rows", "EF Core")

@enduml
```

## Component diagram — Inbox & Router (Publish path)

```plantuml
@startuml C4_Component_Publish
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(inbox, "Inbox / Publish Endpoint") {
    Component(endpoint, "PublishEndpoint", "Minimal API", "Routes POST /publish/{event-type}; the ONLY thing that can still return a real error (unparseable envelope) -- ADR-023")
    Component(scopeCheck, "events:publish scope check", "ScopeRequirement", "ADR-006 -- static, blocking")
    Component(idempotency, "Idempotent Receiver", "eventId + PayloadHash lookup", "ADR-011 -- short-circuits before append if eventId supplied")
    Component(appender, "EventAppender", "EF Core repository", "Writes StoredEvent, assigns SequenceNumber, computes ChainHash (ADR-019) -- always succeeds if parseable")
}

Container_Boundary(router, "Router (background, advisory-only)") {
    Component(entityResolver, "Entity Resolver", "EF Core", "Resolves/creates EntityId via EntityIdField (ADR-021)")
    Component(claimCheck, "RequiredPublishClaim / AuthorityStatus check", "HasRequiredClaim(...)", "ADR-008 (blocking, own-scope) + ADR-035 (advisory, never blocks)")
    Component(validator, "SchemaValidationService", "JsonSchema.Net wrapper", "Validates against declared schemaVersion (ADR-020) -- result is advisory (SchemaStatus), never blocking (ADR-023)")
    Component(parentLink, "ParentLinkService", "EF Core repository", "Validates parentEventIds per ParentValidationMode (ADR-005)")
    Component(upcastValidate, "UpcastChain (live validation)", "OData compute() executor", "ADR-020 -- lagging schemaVersion? validate + materialize (ADR-027) or dead-letter (EventUpcastFailed)")
}

ContainerDb(eventLog, "Event Log")
ContainerDb(registry, "Schema Registry")

Rel(endpoint, scopeCheck, "1. validate scope (blocking)")
Rel(endpoint, idempotency, "2. if eventId supplied")
Rel(endpoint, appender, "3. append \"received\" regardless of shape (ADR-023)")
Rel(appender, eventLog, "INSERT StoredEvent")
Rel(endpoint, router, "4. notify (async, non-blocking)")
Rel(router, entityResolver, "resolve EntityId")
Rel(router, registry, "fetch schema + claims + maps")
Rel(router, claimCheck, "advisory claim/authority checks")
Rel(router, validator, "advisory schema check -> SchemaStatus")
Rel(router, parentLink, "validate parentEventIds")
Rel(router, upcastValidate, "if schemaVersion behind active")
Rel(router, eventLog, "append routed event, materialization, or EventUpcastFailed")

@enduml
```

## Component diagram — GraphQL Gateway

```plantuml
@startuml C4_Component_GraphQL
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(graphql, "GraphQL Gateway") {
    Component(handler, "GraphQL Handler", "QUERY method (queries/subscriptions), POST (mutations, unused here)", "ADR-037 -- one schema per AppId (ADR-030)")
    Component(scopeCheck, "events:follow / events:lineage:read scope check", "ScopeRequirement", "ADR-006")
    Component(claimCheck, "RequiredReadClaim / per-node visibility check", "HasRequiredClaim(...)", "ADR-008 -- per-node for history/lineage traversal, once at connect time for a live subscription")
    Component(entityResolver, "Entity Resolver", "Reads Entity Store (shard-aware, ADR-034)", "Current-state queries")
    Component(historyResolver, "History Resolver", "Reads Event Log by EntityId", "ADR-024 §8.4 -- entityHistory(entityId, property)")
    Component(subResolver, "Subscription Resolver", "Bridges the same tail/replay poll loop ADR-010 established", "Live changes -- GraphQL's transport, not a new polling mechanism")
    Component(depthLimiter, "Depth/Cost Limiter", "Guards against unbounded hierarchical fan-out", "Mandatory, not optional (ADR-037)")
    Component(dataLoader, "Batching (DataLoader pattern)", "Per-resolver batching", "Avoids N+1 across shards/replicas")
    Component(upcaster, "UpcastChain", "Same executor as the Router uses", "ADR-018/027 -- reshapes a stored/materialized event to current shape on read")
    Component(masker, "IPayloadMasker", "schema+data transform", "ADR-009 -- Phase 8, not yet built")
}

ContainerDb(entityStore, "Entity Store")
ContainerDb(eventLog, "Event Log")

Rel(handler, scopeCheck, "validate scope")
Rel(handler, depthLimiter, "validate before execution")
Rel(handler, claimCheck, "validate RequiredReadClaim")
Rel(handler, entityResolver, "Query: current state")
Rel(handler, historyResolver, "Query: entityHistory")
Rel(handler, subResolver, "Subscription: live changes")
Rel(entityResolver, dataLoader, "batch reads")
Rel(dataLoader, entityStore, "SELECT ... (sharded)")
Rel(historyResolver, eventLog, "SELECT ... WHERE EntityId = ... ORDER BY SequenceNumber")
Rel(entityResolver, upcaster, "reshape if needed")
Rel(upcaster, masker, "mask before returning")

@enduml
```

## Component diagram — Lineage traversal (now inside the GraphQL Gateway)

```plantuml
@startuml C4_Component_Lineage
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

note as N
  This traversal logic is unchanged from the OData era --
  only its transport moved (QUERY-body $filter -> GraphQL
  query, ADR-037). It lives inside the GraphQL Gateway now,
  not a standalone "Lineage API" container.
end note

Container_Boundary(lineageResolver, "Lineage Resolver (part of GraphQL Gateway)") {
    Component(directReader, "EventParentReader", "EF Core (LINQ)", "Immediate parents/children via a plain join on EventParents")
    Component(recursiveReader, "IEventLineageQueryProvider (impl per provider)", "SQLite/Postgres/SqlServer raw SQL", "Ancestors/descendants via provider-specific WITH RECURSIVE CTE")
    Component(cycleGuard, "CycleGuard", "In-process", "Bounds traversal depth / rejects a revisited node (ADR-005)")
    Component(nodeVisibility, "Per-node visibility check", "restrictedTypes set", "ADR-008 -- stubs a discovered node as restricted:true rather than failing the request")
}

ContainerDb(eventLog, "Event Log")

Rel(directReader, nodeVisibility, "stubs a restricted direct node")
Rel(recursiveReader, cycleGuard, "guards recursion")
Rel(recursiveReader, nodeVisibility, "stops expansion past a restricted node")
Rel(directReader, eventLog, "SELECT ... FROM EventParents JOIN Events")
Rel(recursiveReader, eventLog, "WITH RECURSIVE ... (native per provider)")

@enduml
```

## Component diagram — Projection Host (CQRS read side)

```plantuml
@startuml C4_Component_ProjectionHost
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(projectionHost, "Projection Host") {
    Component(runner, "ProjectionRunner", "Background service, one loop per registered IProjection<T>", "Reads checkpoint; Subscription/replay via the GraphQL Gateway (ADR-037), reusing ADR-010's tail/replay shape -- never mode=tail equivalent")
    Component(merger, "SnapshotMerger", "Optional<T>-aware fold", "Full: replace. Partial: merge-patch, absent -> untouched, explicit null -> clears (ADR-022, refines ADR-016)")
    Component(checkpointStore, "CheckpointStore", "EF Core repository", "ProjectionCheckpoint: LastSequenceNumber per projection")
    Component(snapshotStore, "SnapshotStore", "EF Core repository", "ProjectionSnapshot: current merged JSON per (ProjectionName, Key)")
    Component(orderProjection, "OrderSummaryProjection", "IProjection<OrderSummary> (worked example)", "GetKey(OrderId); Project(mergedSnapshot) -> OrderSummary row")
}

Container(graphql, "GraphQL Gateway", "write-side read path")
ContainerDb(readDb, "Read Model Store")

Rel(runner, checkpointStore, "read/advance checkpoint")
Rel(runner, graphql, "Subscribe / replay from checkpoint", "HTTPS, Bearer")
Rel(runner, snapshotStore, "load existing snapshot for key")
Rel(runner, merger, "apply(ChangeKind, existing, incoming)")
Rel(merger, snapshotStore, "upsert merged snapshot")
Rel(runner, orderProjection, "Project(key, mergedSnapshot)")
Rel(orderProjection, readDb, "upsert OrderSummary row (via runner)")
Rel(checkpointStore, readDb, "persist checkpoint")
Rel(snapshotStore, readDb, "persist snapshot")

@enduml
```

A **full rebuild** is not a separate component or code path here — it's
`checkpointStore` reset to `0` plus `readDb`'s tables truncated, then the
exact same `runner` loop shown above runs again from scratch (`ADR-015`).

## Not yet diagrammed at component level

The Streaming Channel Service (`ADR-031`) and Attachment Service
(`ADR-032`) exist at the Container level above but don't have their own
component diagrams yet — flagged as remaining propagation work, not
omitted on purpose. `docs/data/streaming-and-attachments.md` has the
entity shapes in the meantime.

## Suggested References

- [C4 model](https://c4model.com/) — Simon Brown; the notation these diagrams follow (Context/Container/Component).
- [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) — the macro library used to render them.
- [PlantUML](https://plantuml.com/) — the underlying diagram engine.

See `references.md` for the full bibliography, including the standards
behind what each container/component actually does (cross-referenced from
the docs where they're decided, e.g. `03-api-contracts.md`, `07-adrs.md`).
