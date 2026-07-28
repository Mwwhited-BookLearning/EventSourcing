# C4 Architecture

Diagrams use PlantUML with the C4-PlantUML macros
(`https://github.com/plantuml-stdlib/C4-PlantUML`). These are the static
structural views; for the runtime/dynamic view of a specific feature (plain
PlantUML sequence diagrams, plus the Gherkin scenarios they illustrate), see
`features/*.md`.

**Deployment note**: per `ADR-001`, the Publish/Follow/Lineage/Registry/Spec
Generator containers below are built as **three separate deployables** —
`EventStore.Host.Sqlite`/`.Postgres`/`.SqlServer` — one per database
provider, not a single artifact with a runtime switch. C4 Container
diagrams describe logical architecture, not deployment topology, so this
is a note rather than three copies of every box; see
`06-solution-structure.md` for the actual project split.

## Context diagram

```plantuml
@startuml C4_Context
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Context.puml

Person(publisher, "Publishing System", "Emits domain events")
Person(follower, "Consuming System", "Subscribes to event streams")
Person(operator, "Platform Operator", "Registers event types / schemas")

System(eventStore, "Open Event Sourcing Store", "Validates, persists, and streams events. Publishes OpenAPI + AsyncAPI contracts.")
System_Ext(idp, "EventStore.DevIdp", "Dev-mode OIDC token issuer (OpenIddict, in-process) -- ADR-006")

Rel(publisher, idp, "Obtains Bearer token", "OAuth2 client_credentials")
Rel(follower, idp, "Obtains Bearer token", "OAuth2 client_credentials")
Rel(operator, idp, "Obtains Bearer token", "OAuth2 client_credentials")

Rel(publisher, eventStore, "POST /publish/{event-type}\nBearer <JWT>", "HTTPS/JSON")
Rel(follower, eventStore, "QUERY /follow/{event-type}\nBearer <JWT>, $filter/mode in body (ADR-012)", "SSE")
Rel(follower, eventStore, "QUERY /events/{id}/parents|children|ancestors|descendants\nBearer <JWT> (ADR-012)", "HTTPS/JSON")
Rel(operator, eventStore, "PUT /registry/{event-type}\nBearer <JWT>", "HTTPS/JSON")
Rel(eventStore, idp, "Validates Bearer token + DPoP proof (ADR-017)", "OIDC discovery + JWKS")
Rel(eventStore, publisher, "OpenAPI contract (anonymous)", "HTTPS")
Rel(eventStore, follower, "AsyncAPI contract (anonymous)", "HTTPS")

@enduml
```

## Container diagram

```plantuml
@startuml C4_Container
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Container.puml

Person(publisher, "Publishing System")
Person(follower, "Consuming System")
Person(operator, "Platform Operator")
System_Ext(idp, "EventStore.DevIdp", "Dev-mode OIDC token issuer (OpenIddict) -- ADR-006")

System_Boundary(system, "Open Event Sourcing Store") {
    Container(publishApi, "Publish API", ".NET (ASP.NET Core)", "POST /publish/{event-type}; validates against registered JSON Schema; RequiredPublishClaim check (ADR-008); eventId/PayloadHash idempotency (ADR-011); records parentEventIds")
    Container(followApi, "Follow API", ".NET (ASP.NET Core, SSE)", "QUERY /follow/{event-type} (ADR-012); parses OData $filter/mode/fromSequenceNumber from the request body; RequiredReadClaim + restricted-parentEventIds filtering (ADR-008)")
    Container(lineageApi, "Lineage API", ".NET (ASP.NET Core)", "QUERY /events/{id}/parents|children|ancestors|descendants (ADR-012, with $top/$skip); walks the event-parent DAG; per-node RequiredReadClaim visibility (ADR-008)")
    Container(registry, "Schema Registry Service", ".NET", "CRUD for named/versioned JSON Schemas; marks indexed/filterable fields; sets ParentValidationMode, RequiredPublishClaim/RequiredReadClaim, x-masking (ADR-005/008/009); QUERY /registry paginated listing")
    Container(specGen, "Spec Generator", ".NET", "Builds OpenAPI (publish, lineage) and AsyncAPI (follow) documents from registry state; MaskingSchemaTransformer wraps maskable properties (ADR-002/009)")
    ContainerDb(db, "Event & Schema Store", "EF Core over SQLite / PostgreSQL / SQL Server (one provider per deployable, ADR-001)", "Events table, EventParents table, EventTypes/Schemas table")
}

System_Boundary(readSide, "CQRS Read Side (example) -- separate deployable and database, ADR-015") {
    Container(projectionHost, "Projection Host", ".NET (background service)", "ProjectionHost + SnapshotMerger: consumes QUERY /follow like any external follower; applies Full-replace/Partial-merge per ChangeKind (ADR-016); OrderSummaryProjection is the worked example")
    ContainerDb(readDb, "Read Model Store", "EF Core, one provider, its own database -- never shared with the write side", "ProjectionCheckpoint, ProjectionSnapshot, OrderSummary (example read model)")
}

Rel(publisher, publishApi, "Publishes events", "HTTPS/JSON, Bearer")
Rel(follower, followApi, "Subscribes with $filter/mode", "SSE, Bearer")
Rel(follower, lineageApi, "Queries event lineage", "HTTPS/JSON, Bearer")
Rel(operator, registry, "Registers schemas", "HTTPS/JSON, Bearer")

Rel(publishApi, idp, "Validates Bearer token")
Rel(followApi, idp, "Validates Bearer token")
Rel(lineageApi, idp, "Validates Bearer token")
Rel(registry, idp, "Validates Bearer token")

Rel(publishApi, registry, "Fetch schema + ParentValidationMode + RequiredPublishClaim for validation")
Rel(publishApi, db, "Append event; validate/insert EventParents rows + ChainHash (ADR-019)", "EF Core")
Rel(followApi, db, "Query events (filter pushed to SQL)", "EF Core")
Rel(lineageApi, db, "Direct joins (parents/children); recursive CTE (ancestors/descendants), stopping at restricted/unresolved nodes", "EF Core / raw SQL")
Rel(registry, db, "Persist schema metadata", "EF Core")
Rel(specGen, registry, "Read schema/event-type metadata")
Rel(publisher, specGen, "GET /openapi.json (anonymous)")
Rel(follower, specGen, "GET /asyncapi.json (anonymous)")

Rel(projectionHost, followApi, "QUERY /follow/{event-type}\nmode=replay&fromSequenceNumber=<checkpoint> (ADR-015)", "HTTPS/JSON, Bearer")
Rel(projectionHost, idp, "Validates Bearer token (its own client, e.g. projections-client)")
Rel(projectionHost, readDb, "Upsert snapshot + read-model rows", "EF Core")

@enduml
```

## Component diagram — Publish API

```plantuml
@startuml C4_Component_Publish
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(publishApi, "Publish API") {
    Component(endpoint, "PublishEndpoint", "Minimal API / Controller", "Routes POST /publish/{event-type}")
    Component(scopeCheck, "events:publish scope check", "ScopeRequirement", "ADR-006 -- static policy, runs first")
    Component(claimCheck, "RequiredPublishClaim check", "HasRequiredClaim(...)", "ADR-008 -- data-driven, after EventTypeDefinition resolves")
    Component(idempotency, "Idempotency check", "eventId + PayloadHash lookup", "ADR-011 -- short-circuits before validation if eventId supplied")
    Component(validator, "SchemaValidationService", "JsonSchema.Net wrapper", "Validates payload against registered schema version")
    Component(parentLink, "ParentLinkService", "EF Core repository", "Validates parentEventIds per ParentValidationMode (ADR-005)")
    Component(appender, "EventAppender", "EF Core repository", "Writes StoredEvent + EventParents rows, assigns SequenceNumber")
    Component(registryClient, "SchemaRegistryClient", "In-process or HTTP client", "Resolves current schema + claims for event-type")
}

ContainerDb(db, "Event & Schema Store")

Rel(endpoint, scopeCheck, "1. validate scope")
Rel(endpoint, registryClient, "2. get schema + claims")
Rel(endpoint, claimCheck, "3. validate RequiredPublishClaim")
Rel(endpoint, idempotency, "4. if eventId supplied")
Rel(endpoint, validator, "5. validate payload")
Rel(endpoint, parentLink, "6. validate parentEventIds")
Rel(endpoint, appender, "7. append on success")
Rel(registryClient, db, "Read EventTypes/Schemas")
Rel(idempotency, db, "Read StoredEvent by EventId")
Rel(appender, db, "Insert StoredEvent + EventParents")

@enduml
```

## Component diagram — Follow API

```plantuml
@startuml C4_Component_Follow
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(followApi, "Follow API") {
    Component(sseEndpoint, "FollowEndpoint", "ASP.NET Core, QUERY method", "QUERY /follow/{event-type} (ADR-012); reads $filter/mode/fromSequenceNumber from the request body")
    Component(scopeCheck, "events:follow scope check", "ScopeRequirement", "ADR-006")
    Component(claimCheck, "RequiredReadClaim check", "HasRequiredClaim(...)", "ADR-008 -- once, at connect time")
    Component(odataParser, "ODataFilterParser", "Microsoft.OData.UriParser", "Parses $filter into an OData AST")
    Component(predicateBuilder, "PredicateTranslator", "Custom", "Walks OData AST -> LINQ Expression using JsonPath functions")
    Component(jsonPathTranslator, "IJsonPathTranslator (impl per provider)", "SQLite/Postgres/SqlServer", "Maps JsonValue() calls to native SQL JSON functions")
    Component(tailReader, "EventTailReader", "EF Core repository", "Polls Events where SequenceNumber > lastSeen (cursor set by mode, ADR-010), applies pushed-down predicate")
    Component(parentFilter, "parentEventIds visibility filter", "restrictedTypes set", "ADR-008 -- omits any parent whose type the caller can't see")
    Component(masker, "IPayloadMasker", "schema+data transform", "ADR-009 -- Phase 8, not yet built; wraps maskable fields per caller's claims")
    Component(upcaster, "UpcastChain", "IEventUpcaster per (EventType, FromVersion)", "ADR-018 -- reshapes an old-version payload to current shape before masking runs")
}

ContainerDb(db, "Event & Schema Store")

Rel(sseEndpoint, scopeCheck, "validate scope")
Rel(sseEndpoint, claimCheck, "validate RequiredReadClaim")
Rel(sseEndpoint, odataParser, "parse $filter")
Rel(odataParser, predicateBuilder, "AST")
Rel(predicateBuilder, jsonPathTranslator, "uses registered translation")
Rel(sseEndpoint, tailReader, "poll for new matching events")
Rel(tailReader, db, "SELECT ... WHERE json_extract/JSON_VALUE/->> (pushed down)")
Rel(sseEndpoint, upcaster, "reshape to current schema version")
Rel(sseEndpoint, parentFilter, "filter each event's parentEventIds")
Rel(upcaster, masker, "mask payload before sending (Phase 8)")

@enduml
```

## Component diagram — Lineage API

```plantuml
@startuml C4_Component_Lineage
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(lineageApi, "Lineage API") {
    Component(lineageEndpoint, "LineageEndpoint", "ASP.NET Core, QUERY method", "Routes QUERY /events/{id}/parents|children|ancestors|descendants (ADR-012); reads optional $top/$skip from the request body")
    Component(scopeCheck, "events:lineage:read scope check", "ScopeRequirement", "ADR-006")
    Component(rootClaimCheck, "Root visibility check", "HasRequiredClaim(...)", "ADR-008 -- pass/fail 403 for {eventId} itself only")
    Component(directReader, "EventParentReader", "EF Core (LINQ)", "Immediate parents/children via a plain join on EventParents")
    Component(recursiveReader, "IEventLineageQueryProvider (impl per provider)", "SQLite/Postgres/SqlServer raw SQL", "Ancestors/descendants via provider-specific WITH RECURSIVE CTE")
    Component(cycleGuard, "CycleGuard", "In-process", "Bounds traversal depth / rejects a revisited node -- required because Permissive-mode event types can form cycles (ADR-005)")
    Component(nodeVisibility, "Per-node visibility check", "restrictedTypes set", "ADR-008 -- 'you can only see what you can see': stubs a discovered node as restricted:true rather than failing the request; recursion stops there, not just output redaction")
}

ContainerDb(db, "Event & Schema Store")

Rel(lineageEndpoint, scopeCheck, "validate scope")
Rel(lineageEndpoint, rootClaimCheck, "validate root's own visibility (403 if restricted)")
Rel(lineageEndpoint, directReader, "parents / children")
Rel(lineageEndpoint, recursiveReader, "ancestors / descendants")
Rel(recursiveReader, cycleGuard, "guards recursion")
Rel(recursiveReader, nodeVisibility, "stops expansion past a restricted node")
Rel(directReader, nodeVisibility, "stubs a restricted direct node")
Rel(directReader, db, "SELECT ... FROM EventParents JOIN Events")
Rel(recursiveReader, db, "WITH RECURSIVE ... (native per provider)")

@enduml
```

## Component diagram — Projection Host (CQRS read side)

```plantuml
@startuml C4_Component_ProjectionHost
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(projectionHost, "Projection Host") {
    Component(runner, "ProjectionRunner", "Background service, one loop per registered IProjection<T>", "Reads checkpoint; QUERY /follow/{event-type} mode=replay&fromSequenceNumber=<checkpoint> (ADR-015); never mode=tail")
    Component(merger, "SnapshotMerger", "Pure function", "Full: replace snapshot. Partial: merge-patch, absent fields untouched (ADR-016; same overlay rule as ADR-009's masking guidance)")
    Component(checkpointStore, "CheckpointStore", "EF Core repository", "ProjectionCheckpoint: LastSequenceNumber per projection")
    Component(snapshotStore, "SnapshotStore", "EF Core repository", "ProjectionSnapshot: current merged JSON per (ProjectionName, Key)")
    Component(orderProjection, "OrderSummaryProjection", "IProjection<OrderSummary> (worked example)", "GetKey(OrderId); Project(mergedSnapshot) -> OrderSummary row -- never sees raw events, ChangeKind, or merge logic")
}

Container(followApi, "Follow API", "write side")
ContainerDb(readDb, "Read Model Store")

Rel(runner, checkpointStore, "read/advance checkpoint")
Rel(runner, followApi, "QUERY /follow/{event-type}", "HTTPS/JSON, Bearer")
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

## Suggested References

- [C4 model](https://c4model.com/) — Simon Brown; the notation these diagrams follow (Context/Container/Component).
- [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) — the macro library used to render them.
- [PlantUML](https://plantuml.com/) — the underlying diagram engine.

See `references.md` for the full bibliography, including the standards
behind what each container/component actually does (cross-referenced from
the docs where they're decided, e.g. `03-api-contracts.md`, `07-adrs.md`).
