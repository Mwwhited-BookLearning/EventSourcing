# C4 Architecture

Diagrams use **plain PlantUML, hand-styled in the C4 notation** (boxes for
Person/System/Container/Component, dashed boundary groupings, a
consistent color tier per level) — not the `C4-PlantUML` macro library.
`C4-PlantUML` requires either a live fetch of its `.puml` files from
GitHub (`!include https://raw.githubusercontent.com/...`) or a bundled
copy of the PlantUML standard library (`!include <C4/C4_Container>`);
both fail silently (a blank or broken diagram, no readable error) in any
renderer without internet access or without that stdlib path configured
— which is most local/offline PlantUML setups. Plain PlantUML has no
such dependency: every diagram below is fully self-contained and renders
anywhere PlantUML itself runs. See `references.md` for why `C4-PlantUML`
moved from adopted to reference-only over this. **This is now a standing
convention for every PlantUML diagram in this repo, not just this
file** — never reach for `C4-PlantUML` (or any other external
`!include`) again; style C4-shaped diagrams by hand instead, the same
way the diagrams below do. These are the static structural views; for the
runtime/dynamic view of a specific feature (plain PlantUML sequence
diagrams, plus the Gherkin scenarios they illustrate), see
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
skinparam shadowing false
skinparam defaultTextAlignment center
skinparam wrapWidth 220
skinparam rectangle<<Person>> {
  BackgroundColor #08427B
  FontColor white
}
skinparam rectangle<<System>> {
  BackgroundColor #1168BD
  FontColor white
}
skinparam rectangle<<System_Ext>> {
  BackgroundColor #999999
  FontColor white
}
skinparam ArrowColor #666666

rectangle "**Publishing System**\n<<Person>>\n--\nEmits domain patches/actions -- may be self-attested (ADR-035/036)" <<Person>> as publisher
rectangle "**Consuming System**\n<<Person>>\n--\nQueries current state, history, and subscribes to live changes" <<Person>> as follower
rectangle "**Platform Operator**\n<<Person>>\n--\nRegisters event types / schemas, per AppId (ADR-030)" <<Person>> as operator

rectangle "**Open Event-Sourced Entity Platform**\n<<System>>\n--\nDedicated, siloed deployment per tenant (ADR-075): persists everything, folds into an Entity Store, exposes GraphQL. May itself span multiple sites for one tenant's own fault tolerance (ADR-033)." <<System>> as eventStore
rectangle "**EventStore.DevIdp**\n<<System_Ext>>\n--\nDev-mode OIDC token issuer + OAuth Token Exchange (OpenIddict, in-process) -- ADR-006/036" <<System_Ext>> as idp
rectangle "**Peer Site**\n<<System_Ext>>\n--\nAnother instance of the same platform, replicating shared shards -- ADR-033" <<System_Ext>> as peerSite

publisher --> idp : Obtains Bearer token (ordinary, or exchanged from a self-attested UCAN)\n//OAuth2 client_credentials / RFC 8693//
follower --> idp : Obtains Bearer token\n//OAuth2 client_credentials//
operator --> idp : Obtains Bearer token\n//OAuth2 client_credentials//

publisher --> eventStore : POST /publish/{event-type}\nBearer <JWT> + DPoP proof -- always 202, never 400 for shape/authority problems (ADR-023)\n//HTTPS/JSON//
follower --> eventStore : GraphQL Query (current state, history) / Subscription (live changes)\nBearer <JWT>, over HTTP QUERY -- never GET, keeps PII/PHI out of URLs (ADR-037)\n//HTTPS//
operator --> eventStore : PUT /registry/{event-type}\nBearer <JWT>\n//HTTPS/JSON//
eventStore --> idp : Validates Bearer token + DPoP proof; exchanges self-attested UCANs (ADR-017/036)\n//OIDC discovery + JWKS//
eventStore --> peerSite : Gossip replication -- durable, fault/abend/restart-tolerant peer-sync outbox/inbox (ADR-033)\n//HTTPS/JSON, bidirectional//
eventStore --> publisher : OpenAPI contract (anonymous)\n//HTTPS//
eventStore --> follower : GraphQL SDL, per AppId (anonymous)\n//HTTPS//

@enduml
```

## Container diagram

```plantuml
@startuml C4_Container
skinparam shadowing false
skinparam defaultTextAlignment center
skinparam wrapWidth 220
skinparam rectangle<<Person>> {
  BackgroundColor #08427B
  FontColor white
}
skinparam rectangle<<System_Ext>> {
  BackgroundColor #999999
  FontColor white
}
skinparam rectangle<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam database<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam rectangle<<Boundary>> {
  BackgroundColor transparent
  BorderColor #666666
  BorderStyle dashed
}
skinparam ArrowColor #666666

rectangle "**Publishing System**\n<<Person>>" <<Person>> as publisher
rectangle "**Consuming System**\n<<Person>>" <<Person>> as follower
rectangle "**Platform Operator**\n<<Person>>" <<Person>> as operator
rectangle "**EventStore.DevIdp**\n<<System_Ext>>\n--\nOIDC + Token Exchange -- ADR-006/036" <<System_Ext>> as idp
rectangle "**Peer Site(s)**\n<<System_Ext>>\n--\nADR-033" <<System_Ext>> as peerSite

rectangle "Open Event-Sourced Entity Platform (one site)" <<Boundary>> as system {
    rectangle "**Inbox / Publish Endpoint**\n<<Container>>\n//.NET (ASP.NET Core)//\n--\nPOST /publish; persists first, always 202 unless the envelope itself is unparseable (ADR-023); Idempotent Receiver (ADR-011)" <<Container>> as inbox
    rectangle "**Router**\n<<Container>>\n//Background service//\n--\nSchema validation, entity resolution (ADR-021), live upcast validation + materialization (ADR-020/027), non-authoritative claim capture (ADR-035) -- all advisory, none block Inbox's 202" <<Container>> as router
    database "**Event Log**\n<<Container>>\n//EF Core over SQLite/Postgres/SqlServer (ADR-001)//\n--\nStoredEvent, EventParent -- insert-only, hash-chained (ADR-019)" <<Container>> as eventLog
    rectangle "**Fold / Projector**\n<<Container>>\n//Background service//\n--\nLogical-order fold (OccurredAt, not arrival order -- ADR-029); optimistic-concurrency conflict flagging (ADR-024); always-on, not opt-in" <<Container>> as fold
    database "**Entity Store**\n<<Container>>\n//Mutable, versioned, hashed, sharded by EntityType (ADR-021/034)//\n--\nCurrent materialized state -- the only thing GraphQL reads read from" <<Container>> as entityStore
    rectangle "**Schema Registry Service**\n<<Container>>\n//.NET//\n--\nCRUD for named/versioned JSON Schemas, AppId-scoped (ADR-030); FilterableFields, ChangeKind, EntityIdField, upcast/downcast maps (ADR-018/028)" <<Container>> as registry
    rectangle "**GraphQL Gateway**\n<<Container>>\n//.NET (Hot Chocolate-class), QUERY method//\n--\nQuery (entity + change history), Subscription (live changes, replaces OData $filter/Follow) -- per-AppId schema (ADR-030/037); depth/cost limiting" <<Container>> as graphql
    rectangle "**Spec Generator**\n<<Container>>\n//.NET//\n--\nBuilds OpenAPI (publish) + GraphQL SDL from registry state; MaskingSchemaTransformer (ADR-002/009)" <<Container>> as specGen
    rectangle "**Streaming Channel Service**\n<<Container>>\n//.NET//\n--\nBatch ingest + tail/replay for telemetry & media channels (ADR-031) -- bypasses schema validation/hash-chain/fold entirely" <<Container>> as streaming
    database "**Streaming Channel Store**\n<<Container>>\n//Plain append-only table, v1 engine choice (ADR-031)//\n--\nTelemetryChannel, TelemetrySample" <<Container>> as streamStore
    rectangle "**Attachment Service**\n<<Container>>\n//.NET//\n--\nContent-addressed binary uploads; browse via GraphQL, fetch via GET with Range support (ADR-032)" <<Container>> as attachments
    database "**Attachment Store**\n<<Container>>\n//Content-addressed//\n--\nAttachment, AttachmentRef" <<Container>> as attachmentStore
    rectangle "**Peer Sync Outbox/Inbox**\n<<Container>>\n//Durable store + background service, gossip topology//\n--\nFault/abend/restart-tolerant (ADR-033); reuses the same durable transport as Inbox above" <<Container>> as peerSync
}

rectangle "CQRS Read Side (example) -- separate deployable and database, ADR-015" <<Boundary>> as readSide {
    rectangle "**Projection Host**\n<<Container>>\n//.NET (background service)//\n--\nOpt-in custom projections, on top of the always-on Entity Store above (ADR-015/016)" <<Container>> as projectionHost
    database "**Read Model Store**\n<<Container>>\n//EF Core, its own database//\n--\nProjectionCheckpoint, ProjectionSnapshot, OrderSummary (example)" <<Container>> as readDb
}

publisher --> inbox : Publishes patches/actions\n//HTTPS/JSON, Bearer + DPoP//
follower --> graphql : Query / Subscription\n//HTTPS (QUERY method), Bearer + DPoP//
operator --> registry : Registers schemas\n//HTTPS/JSON, Bearer//
publisher --> streaming : Batch-ingests channel samples\n//HTTPS, Bearer//
publisher --> attachments : Uploads binary content\n//HTTPS, Bearer//

inbox --> eventLog : Append "received" (Idempotent Receiver + Inbox pattern)
inbox --> router : Notify new item
router --> registry : Schema + claims + upcast/downcast maps lookup (advisory)
router --> eventLog : Append routed event; append UpcastMaterialization (ADR-027) or EventUpcastFailed (ADR-020) as needed
fold --> eventLog : Replay in OccurredAt order
fold --> entityStore : Write materialized version; ConflictFlag/LateArrivalFlag
graphql --> entityStore : Read current state (sharded, ADR-034)
graphql --> eventLog : Read change history (ADR-024 §8.4)
registry --> eventLog : n/a -- registry has its own table, shown for scope only
specGen --> registry : Read schema/event-type metadata
publisher --> specGen : GET /openapi.json (anonymous)
follower --> specGen : GraphQL SDL introspection (anonymous)
streaming --> streamStore : Batch append; tail/replay (ADR-010's shape, reused)
attachments --> attachmentStore : Content-addressed put/get; GraphQL browse, GET with Range
eventLog --> peerSync : Feeds outbound peer sync
peerSync --> eventLog : Delivers events from peers -- same path as Inbox, no special-casing
peerSync --> peerSite : Gossip exchange\n//HTTPS/JSON//

inbox --> idp : Validates Bearer + DPoP; may trigger Token Exchange (ADR-036)
graphql --> idp : Validates Bearer + DPoP
registry --> idp : Validates Bearer + DPoP

projectionHost --> graphql : Subscribes (its own client identity)\n//HTTPS, Bearer//
projectionHost --> readDb : Upsert snapshot + read-model rows\n//EF Core//

@enduml
```

## Component diagram — Inbox & Router (Publish path)

```plantuml
@startuml C4_Component_Publish
skinparam shadowing false
skinparam defaultTextAlignment center
skinparam wrapWidth 220
skinparam rectangle<<Component>> {
  BackgroundColor #85BBF0
  FontColor black
}
skinparam database<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam rectangle<<Boundary>> {
  BackgroundColor transparent
  BorderColor #666666
  BorderStyle dashed
}
skinparam ArrowColor #666666

rectangle "Inbox / Publish Endpoint" <<Boundary>> as inbox {
    rectangle "**PublishEndpoint**\n<<Component>>\n//Minimal API//\n--\nRoutes POST /publish/{event-type}; the ONLY thing that can still return a real error (unparseable envelope) -- ADR-023" <<Component>> as endpoint
    rectangle "**events:publish scope check**\n<<Component>>\n//ScopeRequirement//\n--\nADR-006 -- static, blocking" <<Component>> as scopeCheck
    rectangle "**Idempotent Receiver**\n<<Component>>\n//eventId + PayloadHash lookup//\n--\nADR-011 -- short-circuits before append if eventId supplied" <<Component>> as idempotency
    rectangle "**EventAppender**\n<<Component>>\n//EF Core repository//\n--\nWrites StoredEvent, assigns SequenceNumber, computes ChainHash (ADR-019) -- always succeeds if parseable" <<Component>> as appender
}

rectangle "Router (background, advisory-only)" <<Boundary>> as router {
    rectangle "**Entity Resolver**\n<<Component>>\n//EF Core//\n--\nResolves/creates EntityId via EntityIdField (ADR-021)" <<Component>> as entityResolver
    rectangle "**RequiredClaims (Publish direction) / AuthorityStatus check**\n<<Component>>\n//HasRequiredClaim(...), OR-matched//\n--\nADR-008/050 (blocking, own-scope) + ADR-035 (advisory, never blocks)" <<Component>> as claimCheck
    rectangle "**SchemaValidationService**\n<<Component>>\n//JsonSchema.Net wrapper//\n--\nValidates against declared schemaVersion (ADR-020) -- result is advisory (SchemaStatus), never blocking (ADR-023)" <<Component>> as validator
    rectangle "**ParentLinkService**\n<<Component>>\n//EF Core repository//\n--\nValidates parentEventIds per ParentValidationMode (ADR-005)" <<Component>> as parentLink
    rectangle "**UpcastChain (live validation)**\n<<Component>>\n//OData compute() executor//\n--\nADR-020 -- lagging schemaVersion? validate + materialize (ADR-027) or dead-letter (EventUpcastFailed)" <<Component>> as upcastValidate
}

database "Event Log" <<Container>> as eventLog
database "Schema Registry" <<Container>> as registry

endpoint --> scopeCheck : 1. validate scope (blocking)
endpoint --> idempotency : 2. if eventId supplied
endpoint --> appender : 3. append "received" regardless of shape (ADR-023)
appender --> eventLog : INSERT StoredEvent
endpoint --> router : 4. notify (async, non-blocking)
router --> entityResolver : resolve EntityId
router --> registry : fetch schema + claims + maps
router --> claimCheck : advisory claim/authority checks
router --> validator : advisory schema check -> SchemaStatus
router --> parentLink : validate parentEventIds
router --> upcastValidate : if schemaVersion behind active
router --> eventLog : append routed event, materialization, or EventUpcastFailed

@enduml
```

## Component diagram — GraphQL Gateway

```plantuml
@startuml C4_Component_GraphQL
skinparam shadowing false
skinparam defaultTextAlignment center
skinparam wrapWidth 220
skinparam rectangle<<Component>> {
  BackgroundColor #85BBF0
  FontColor black
}
skinparam database<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam rectangle<<Boundary>> {
  BackgroundColor transparent
  BorderColor #666666
  BorderStyle dashed
}
skinparam ArrowColor #666666

rectangle "GraphQL Gateway" <<Boundary>> as graphql {
    rectangle "**GraphQL Handler**\n<<Component>>\n//QUERY method (queries/subscriptions), POST (mutations, unused here)//\n--\nADR-037 -- one schema per AppId (ADR-030)" <<Component>> as handler
    rectangle "**events:follow / events:lineage:read scope check**\n<<Component>>\n//ScopeRequirement//\n--\nADR-006" <<Component>> as scopeCheck
    rectangle "**RequiredClaims (Read direction) / per-node visibility check**\n<<Component>>\n//HasAnyRequiredClaim(...)//\n--\nADR-008/ADR-050 -- per-node for history/lineage traversal, once at connect time for a live subscription" <<Component>> as claimCheck
    rectangle "**Entity Resolver**\n<<Component>>\n//Reads Entity Store (shard-aware, ADR-034)//\n--\nCurrent-state queries" <<Component>> as entityResolver
    rectangle "**History Resolver**\n<<Component>>\n//Reads Event Log by EntityId//\n--\nADR-024 §8.4 -- entityHistory(entityId, property)" <<Component>> as historyResolver
    rectangle "**Subscription Resolver**\n<<Component>>\n//Bridges the same tail/replay poll loop ADR-010 established//\n--\nLive changes -- GraphQL's transport, not a new polling mechanism" <<Component>> as subResolver
    rectangle "**Depth/Cost Limiter**\n<<Component>>\n//Guards against unbounded hierarchical fan-out//\n--\nMandatory, not optional (ADR-037)" <<Component>> as depthLimiter
    rectangle "**Batching (DataLoader pattern)**\n<<Component>>\n//Per-resolver batching//\n--\nAvoids N+1 across shards/replicas" <<Component>> as dataLoader
    rectangle "**UpcastChain**\n<<Component>>\n//Same executor as the Router uses//\n--\nADR-018/027 -- reshapes a stored/materialized event to current shape on read" <<Component>> as upcaster
    rectangle "**IPayloadMasker**\n<<Component>>\n//schema+data transform//\n--\nADR-009 -- 08-build-plan.md's \"Property-Level Masking\" item, not yet built" <<Component>> as masker
    rectangle "**ExportLineage Resolver**\n<<Component>>\n//Walks the Lineage DAG (same IEventLineageQueryProvider/\nCycleGuard as History Resolver), NDJSON+manifest bundle//\n--\nADR-068 -- a read, full claims/masking/audit pipeline, no bypass" <<Component>> as exportResolver
    rectangle "**PlaybackAsOf Resolver**\n<<Component>>\n//Folds events <= a given SequenceNumber\nin ARRIVAL order, not logical order//\n--\nADR-068 -- bitemporal system-time reconstruction, opposite of Entity Resolver's fold" <<Component>> as playbackResolver
}

database "Entity Store" <<Container>> as entityStore
database "Event Log" <<Container>> as eventLog

handler --> scopeCheck : validate scope
handler --> depthLimiter : validate before execution
handler --> claimCheck : validate RequiredClaims (Read direction)
handler --> entityResolver : Query: current state
handler --> historyResolver : Query: entityHistory
handler --> subResolver : Subscription: live changes
handler --> exportResolver : Query: exportLineage(entityId)
handler --> playbackResolver : Query: playbackAsOf(entityId, asOfSequenceNumber)
entityResolver --> dataLoader : batch reads
dataLoader --> entityStore : SELECT ... (sharded)
historyResolver --> eventLog : SELECT ... WHERE EntityId = ... ORDER BY SequenceNumber
entityResolver --> upcaster : reshape if needed
upcaster --> masker : mask before returning
exportResolver --> eventLog : recursive CTE, cycle-safe (ADR-005), SequenceNumber order
exportResolver --> masker : mask each event's payload before bundling
playbackResolver --> eventLog : SELECT ... WHERE EntityId = ... AND SequenceNumber <= T\nORDER BY SequenceNumber ASC (arrival order)
playbackResolver --> masker : mask each event's payload before returning

@enduml
```

`ExportLineage Resolver` and `PlaybackAsOf Resolver` are both new *read*
shapes over history (`ADR-068`), not a privileged bypass of anything
above — both route through the identical `IPayloadMasker` transform (and,
implicitly, the same `claimCheck` per-node visibility rule `History
Resolver`'s own traversal already applies) rather than a separate
enforcement path. See [`docs/features/lineage-export-and-playback.md`](features/lineage-export-and-playback.md)
for the full mechanism.

## Component diagram — Lineage traversal (now inside the GraphQL Gateway)

```plantuml
@startuml C4_Component_Lineage
skinparam shadowing false
skinparam defaultTextAlignment center
skinparam wrapWidth 220
skinparam rectangle<<Component>> {
  BackgroundColor #85BBF0
  FontColor black
}
skinparam database<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam rectangle<<Boundary>> {
  BackgroundColor transparent
  BorderColor #666666
  BorderStyle dashed
}
skinparam ArrowColor #666666

note as N
  This traversal logic is unchanged from the OData era --
  only its transport moved (QUERY-body $filter -> GraphQL
  query, ADR-037). It lives inside the GraphQL Gateway now,
  not a standalone "Lineage API" container.
end note

rectangle "Lineage Resolver (part of GraphQL Gateway)" <<Boundary>> as lineageResolver {
    rectangle "**EventParentReader**\n<<Component>>\n//EF Core (LINQ)//\n--\nImmediate parents/children via a plain join on EventParents" <<Component>> as directReader
    rectangle "**IEventLineageQueryProvider (impl per provider)**\n<<Component>>\n//SQLite/Postgres/SqlServer raw SQL//\n--\nAncestors/descendants via provider-specific WITH RECURSIVE CTE" <<Component>> as recursiveReader
    rectangle "**CycleGuard**\n<<Component>>\n//In-process//\n--\nBounds traversal depth / rejects a revisited node (ADR-005)" <<Component>> as cycleGuard
    rectangle "**Per-node visibility check**\n<<Component>>\n//restrictedTypes set//\n--\nADR-008 -- stubs a discovered node as restricted:true rather than failing the request" <<Component>> as nodeVisibility
}

database "Event Log" <<Container>> as eventLog

directReader --> nodeVisibility : stubs a restricted direct node
recursiveReader --> cycleGuard : guards recursion
recursiveReader --> nodeVisibility : stops expansion past a restricted node
directReader --> eventLog : SELECT ... FROM EventParents JOIN Events
recursiveReader --> eventLog : WITH RECURSIVE ... (native per provider)

@enduml
```

## Component diagram — Projection Host (CQRS read side)

```plantuml
@startuml C4_Component_ProjectionHost
skinparam shadowing false
skinparam defaultTextAlignment center
skinparam wrapWidth 220
skinparam rectangle<<Component>> {
  BackgroundColor #85BBF0
  FontColor black
}
skinparam rectangle<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam database<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam rectangle<<Boundary>> {
  BackgroundColor transparent
  BorderColor #666666
  BorderStyle dashed
}
skinparam ArrowColor #666666

rectangle "Projection Host" <<Boundary>> as projectionHost {
    rectangle "**ProjectionRunner**\n<<Component>>\n//Background service, one loop per registered IProjection<T>//\n--\nReads checkpoint; Subscription/replay via the GraphQL Gateway (ADR-037), reusing ADR-010's tail/replay shape -- never mode=tail equivalent" <<Component>> as runner
    rectangle "**SnapshotMerger**\n<<Component>>\n//Optional<T>-aware fold//\n--\nFull: replace. Partial: merge-patch, absent -> untouched, explicit null -> clears (ADR-022, refines ADR-016)" <<Component>> as merger
    rectangle "**CheckpointStore**\n<<Component>>\n//EF Core repository//\n--\nProjectionCheckpoint: LastSequenceNumber per projection" <<Component>> as checkpointStore
    rectangle "**SnapshotStore**\n<<Component>>\n//EF Core repository//\n--\nProjectionSnapshot: current merged JSON per (ProjectionName, Key)" <<Component>> as snapshotStore
    rectangle "**OrderSummaryProjection**\n<<Component>>\n//IProjection<OrderSummary> (worked example)//\n--\nGetKey(OrderId); Project(mergedSnapshot) -> OrderSummary row" <<Component>> as orderProjection
}

rectangle "**GraphQL Gateway**\n<<Container>>\n--\nwrite-side read path" <<Container>> as graphql
database "Read Model Store" <<Container>> as readDb

runner --> checkpointStore : read/advance checkpoint
runner --> graphql : Subscribe / replay from checkpoint\n//HTTPS, Bearer//
runner --> snapshotStore : load existing snapshot for key
runner --> merger : apply(ChangeKind, existing, incoming)
merger --> snapshotStore : upsert merged snapshot
runner --> orderProjection : Project(key, mergedSnapshot)
orderProjection --> readDb : upsert OrderSummary row (via runner)
checkpointStore --> readDb : persist checkpoint
snapshotStore --> readDb : persist snapshot

@enduml
```

A **full rebuild** is not a separate component or code path here — it's
`checkpointStore` reset to `0` plus `readDb`'s tables truncated, then the
exact same `runner` loop shown above runs again from scratch (`ADR-015`).

## Component diagram — Streaming Channel Service

```plantuml
@startuml C4_Component_StreamingChannel
skinparam shadowing false
skinparam defaultTextAlignment center
skinparam wrapWidth 220
skinparam rectangle<<Component>> {
  BackgroundColor #85BBF0
  FontColor black
}
skinparam database<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam rectangle<<Boundary>> {
  BackgroundColor transparent
  BorderColor #666666
  BorderStyle dashed
}
skinparam ArrowColor #666666

rectangle "Streaming Channel Service" <<Boundary>> as streamingSvc {
    rectangle "**Batch Ingest Endpoint**\n<<Component>>\n//Minimal API//\n--\nPOST /telemetry/{channelId}/samples -- no JSON Schema, no hash-chain, no fold (ADR-031)" <<Component>> as ingest
    rectangle "**Late-Arrival / Lag Detector**\n<<Component>>\n//High-water-mark comparison//\n--\nADR-029's mechanism reused per-channel; publishes reserved ChannelLagDetected via the normal publish path" <<Component>> as lagDetector
    rectangle "**Tail/Replay Reader**\n<<Component>>\n//mode=tail\\|replay//\n--\nADR-010's shape reused, applied to TelemetrySample instead of StoredEvent" <<Component>> as reader
    rectangle "**ChannelDerivationWorker**\n<<Component>>\n//Background service, one per Derived channel//\n--\nTails source channel(s), applies Resample\\|Filter\\|Aggregate\\|Transcode, appends via the same ingest path (ADR-031)" <<Component>> as derivation
    rectangle "**RedactedRange Enforcer**\n<<Component>>\n//Read-time substitution//\n--\nZero-fill\\|tone\\|blank-frame default (ADR-052); claims-gated, existence-flagged not hidden" <<Component>> as redaction
}

database "TelemetryChannel / TelemetrySample store" <<Container>> as telemetryDb
rectangle "**Router**\n<<Container>>\n--\na detector reading via reader publishes ordinary events here (ADR-031)" <<Container>> as router

ingest --> telemetryDb : append batch (durability: "as good as possible")
ingest --> lagDetector : compare batch receive-gap vs ExpectedInterArrivalInterval
lagDetector --> router : publish ChannelLagDetected (normal path, ADR-023)
reader --> telemetryDb : tail (new) or replay (from fromTimestamp) then tail
reader --> redaction : apply RedactedRange before returning, if caller lacks claim
derivation --> reader : tail source channel(s)
derivation --> ingest : append transformed samples to derived channel

@enduml
```

## Component diagram — Attachment Service

```plantuml
@startuml C4_Component_Attachment
skinparam shadowing false
skinparam defaultTextAlignment center
skinparam wrapWidth 220
skinparam rectangle<<Component>> {
  BackgroundColor #85BBF0
  FontColor black
}
skinparam database<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam rectangle<<Boundary>> {
  BackgroundColor transparent
  BorderColor #666666
  BorderStyle dashed
}
skinparam ArrowColor #666666

rectangle "Attachment Service" <<Boundary>> as attachmentSvc {
    rectangle "**Upload Endpoint**\n<<Component>>\n//Minimal API//\n--\nComputes ContentHash (SHA-256) -- the real PK, content-addressed (ADR-032)" <<Component>> as upload
    rectangle "**Content-Defined Chunker**\n<<Component>>\n//Above a configurable size threshold//\n--\nIndependently-addressable ChunkRefs, each own ContentProviderKey/Ref -- enables partial sync (ADR-032)" <<Component>> as chunker
    rectangle "**IAttachmentContentStore**\n<<Component>>\n//Pluggable, keyed by ContentProviderKey//\n--\nAzure Blob\\|S3\\|local dev store, multiple backends active simultaneously (ADR-032)" <<Component>> as contentStore
    rectangle "**Retrieval Endpoint**\n<<Component>>\n//Plain GET + HTTP Range//\n--\nRFC 7233 byte-range seeking; browsable via GraphQL against the owning entity" <<Component>> as retrieval
    rectangle "**Tiering Mover**\n<<Component>>\n//Background service//\n--\nAccess-pattern-driven hot/cool/cold, keyed on LastAccessedAt (ADR-032)" <<Component>> as tiering
}

database "Attachment / ChunkRef metadata" <<Container>> as attachmentDb

upload --> attachmentDb : INSERT Attachment (or ChunkRef rows if chunked)
upload --> chunker : if size above threshold
chunker --> contentStore : PUT each chunk independently
upload --> contentStore : PUT whole blob, if not chunked
retrieval --> attachmentDb : resolve ContentHash -> ContentProviderKey/Ref (or ChunkIndex)
retrieval --> contentStore : GET bytes (Range-aware)
tiering --> attachmentDb : read LastAccessedAt per Attachment
tiering --> contentStore : move blob to a colder/warmer backend tier

@enduml
```

## Component diagram — Live View (gated authoritative fold)

Details the fold-time split `ADR-042` decided, shown only as a single
"materialization" arrow in the Publish-path diagram above — this
diagram is that arrow's own internals, the composition already
documented in `docs/patterns/interactions/gated-authoritative-
publish.md`.

```plantuml
@startuml C4_Component_LiveView
skinparam shadowing false
skinparam defaultTextAlignment center
skinparam wrapWidth 220
skinparam rectangle<<Component>> {
  BackgroundColor #85BBF0
  FontColor black
}
skinparam database<<Container>> {
  BackgroundColor #438DD5
  FontColor white
}
skinparam rectangle<<Boundary>> {
  BackgroundColor transparent
  BorderColor #666666
  BorderStyle dashed
}
skinparam ArrowColor #666666

rectangle "Router — fold step" <<Boundary>> as foldStep {
    rectangle "**LiveViewFolder**\n<<Component>>\n//Unconditional fold//\n--\nEvery routed event, regardless of AuthorityStatus -- wraps isAuthoritative: false (ADR-042)" <<Component>> as liveFolder
    rectangle "**AuthorityStatus Gate**\n<<Component>>\n//accepted? proceed : hold//\n--\nADR-035/042 -- unattested/pending_review events stop here, never reach the authoritative fold" <<Component>> as gate
    rectangle "**AuthoritativeFolder**\n<<Component>>\n//Gated fold//\n--\nOnly runs once AuthorityStatus reaches accepted, via publish-time default or a later authorityDecision (ADR-035/042)" <<Component>> as authFolder
}

database "Live View (LiveEntityStoreRow)" <<Container>> as liveDb
database "Entity Store (EntityStoreRow, authoritative)" <<Container>> as authDb

liveFolder --> liveDb : upsert, every event, no gate
gate --> authFolder : accepted
authFolder --> authDb : upsert, ExpectedVersion-checked (ADR-024)
note right of liveDb
  Same event, two independent folds --
  a second CQRS materialized view
  (docs/patterns/cqrs-and-materialized-views.md),
  not a cache of authDb.
end note

@enduml
```

## Suggested References

- [C4 model](https://c4model.com/) — Simon Brown; the notation these diagrams follow (Context/Container/Component), hand-styled in plain PlantUML rather than rendered through the macro library below.
- [PlantUML](https://plantuml.com/) — the diagram engine these diagrams are written directly against, with no external `!include`.
- [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) — considered, not used; see `references.md` for why (unreliable rendering without network/stdlib access).

See `references.md` for the full bibliography, including the standards
behind what each container/component actually does (cross-referenced from
the docs where they're decided, e.g. `03-api-contracts.md`, `07-adrs.md`).
