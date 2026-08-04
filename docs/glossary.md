[← Document index](../README.md)

# Glossary (Duplex engine)

Every cross-cutting term/envelope field this design introduces, defined
in one place. Distinct from a `docs/domains/*.md` file's own `## Glossary`
section: this file covers **Duplex itself** (the base engine — terms
that mean the same thing no matter which proving-ground product is built
on top of it); a domain file's glossary covers that *industry's* jargon
(a Case Report Form, a Beneficial Owner, an EPCIS event) instead. Where a
domain term maps onto a Duplex mechanism, the domain file's own glossary
entry says so — this file doesn't repeat those mappings.

Organized by category, alphabetical within each. Every entry cites the
ADR that decided it — this file explains what a term *means*; the cited
ADR explains *why*. Where a real, well-known synonym exists (a general
pattern name, a term another real system uses for the same shape), it's
noted in italics right after the term — added this session, now the
standing format for any future entry, not just a one-time pass.

## Core write path & event log

- **`ActorId`** — the verified token subject (`sub`, or composite `iss`+`sub` for federated identities) captured on every `StoredEvent`, for every publish, regardless of path. Blocking, not advisory — distinct from `AttestedActorId` below. (`ADR-064`)
- **`AttestedActorId`** — a self-attested submitter identity, advisory only, never gates `Status`. A *claim*, not a verified fact — never conflated with `ActorId`. (`ADR-035`)
- **`AttestedClaims`** — structured capability/delegation claims attached to a self-attested publish (e.g. a UCAN invocation). (`ADR-036`)
- **`AuthorityStatus`** — `unattested` | `pending_review` | `accepted` | `rejected`. An advisory trust axis, independent of `SchemaStatus` — defaults to `accepted` for an ordinary authenticated publish, only starts lower when the publish itself declares a reason not to trust it yet. (`ADR-035`)
- **`AuthorityDecisionRef`** — denormalized back-pointer to the event that last changed an entity's `AuthorityStatus`, set by the fold step. (`ADR-035`)
- **`ChainHash`** *(synonym: hash chain, tamper-evidence chain — not a blockchain: no consensus, no mining, just a linear integrity check)* — `SHA-256(prior ChainHash || PayloadHash || SequenceNumber)`. A linear hash chain (not a Merkle tree) — altering any past row breaks every `ChainHash` after it. (`ADR-019`)
- **`ConflictFlag`** — set by the fold step when a concurrent, conflicting patch is detected against an `ExpectedVersion`. (`ADR-024`)
- **`EventId`** — unique per `StoredEvent`; client-supplied for idempotent retries, or server-generated. Doubles as the correlation ID role. (`ADR-011`)
- **`EventKind`** — `Original` (every ordinary publish) or `UpcastMaterialization` (a persisted upcast result, never folded). (`ADR-027`)
- **`EventParents` / `parentEventIds`** *(synonym: causal DAG, lineage graph)* — a causal-derivation DAG, envelope metadata kept out of `Payload`. Answers "what is this derived from" — a different question from `EntityId` ("what does this patch") or `MaterializationOfEventId` ("what is this a re-shaped copy of"). `ParentValidationMode` (`Strict` default | `Permissive`) controls whether a referenced parent must already exist. (`ADR-005`)
- **Derived/materialized event type / `DerivationDefinition`** — an event type produced by a background `DerivationWorker`, not an external publisher: tails one or more declared source event types, joins them by key (`FireOnce` | `ContinuousEnrichment` trigger mode), and republishes through the ordinary publish path — recording its sources via the existing `parentEventIds` mechanism above, no separate provenance field needed. `PendingJoinState` durably tracks a `FireOnce` join still waiting on its remaining sources, swept and dropped with a recorded reason if its TTL elapses; `derivationHopCount` caps runaway propagation as a belt-and-suspenders runtime safety net alongside a registration-time derivation-*definition* cycle check (a different graph from the `EventParents` cycle-safety `ParentValidationMode: Permissive` above already requires). (`ADR-007`)
- **`LateArrivalFlag`** — set by the fold step when an event's `OccurredAt` is behind the entity/property's already-applied high-water mark. (`ADR-029`)
- **`OccurredAt`** *(synonym: valid time, in the formal bitemporal sense — see Bitemporal playback below)* — the client-declared logical occurrence time (valid time), not server receipt time. Load-bearing for fold order. (`ADR-029`)
- **`PayloadHash`** *(synonym: content hash)* — hash of `{EventType, Payload, sorted parentEventIds}`, used for idempotency matching and as `ChainHash`'s input. (`ADR-011`)
- **`SchemaStatus`** — `unknown` | `invalid` | `conformant`. Advisory only, never gates `Status` — a structurally-invalid event is still persisted, per `ADR-023`'s persist-everything posture. (`ADR-023`)
- **`SequenceNumber`** *(synonym: log offset, arrival order; transaction time, in the formal bitemporal sense)* — global monotonic order, an identity column. **Arrival** order at this store, not logical order — see `OccurredAt`. (`ADR-011`)
- **`Signature`** — `{SignerId, SignedAt, Meaning, Acr}`, set only when `EventTypeDefinition.RequiredSignature` is configured. Satisfies 21 CFR Part 11 §11.50's linked signature-meaning elements. (`ADR-066`)
- **`Status`** — `received` | `processing` | `applied` | `rejected`. Transport-level only — describes whether the publish request itself succeeded, never a business-rule judgment. (`ADR-023`)
- **`TelemetryPointer`** *(synonym: stream pointer, media fragment reference)* — `{ChannelId, FromTimestamp, ToTimestamp?}`, linking an ordinary domain event to a position/window in a streaming channel it was derived from or annotates. Distinct from `parentEventIds` (causal derivation between *events*) and `MaterializationOfEventId` (a re-shaped copy). (`ADR-031`)

## Entity / read path

- **`EntityId`** *(synonym: aggregate ID, aggregate root ID — DDD terminology for the same "one stable identity, one current state" concept)* — `{appId}:{entityType}:{uniqueId}`. Every `StoredEvent` must patch exactly one entity. Resolved from a publisher-supplied `uniqueId` or server-assigned on first creation. Subsumes and replaced the design's older, looser `StreamId` concept. (`ADR-021`)
- **Entity Store / `EntityStoreRow`** *(synonym: read model, materialized view, current-state projection — general CQRS/event-sourcing terms this is one always-on instance of)* — a mutable, versioned, hashed table with one row per `EntityId`, folded automatically from the event log — a cached, always-current materialized projection, not opt-in. `Version` increments only when `Data` actually changes, not on every fold attempt (`ADR-029`) — a no-op re-fold must not bump it, or an idempotent re-fold would never converge; `Hash` is a SHA-256 of canonicalized current state; `LastAppliedSequenceNumber` is the replay checkpoint. (`ADR-021`/`ADR-029`)
- **`ExpectedVersion`** *(synonym: optimistic-concurrency token — the same role an HTTP `ETag`/`If-Match` header plays for a conditional update)* — an optional publish field stating which Entity Store `Version` the sender believed they were patching. Omitted: no concurrency check. Supplied: feeds `ConflictFlag` detection. (`ADR-021`/`ADR-024`)
- **`LiveEntityStoreRow`** — a second fold that applies every event immediately, regardless of `AuthorityStatus`, wrapped `isAuthoritative: false` at the query surface — the non-authoritative counterpart to the gated Entity Store. (`ADR-042`)
- **`ChangeKind`** — declared per event type; drives the JSON Merge Patch overlay/snapshot-merge behavior a CQRS projection applies when folding a change. (`ADR-016`)
- **`IProjection<TReadModel>` / `ProjectionHost`** — the general, opt-in CQRS projection mechanism custom read models are built on; the Entity Store is the one always-on instance of this same shape. (`ADR-015`)
- **Follow API / tail vs. replay** *(synonym: change feed — the term Cosmos DB/CouchDB use for the same "live tail plus historical catch-up" shape)* — `mode=tail` (new events only, live) or `mode=replay&from=<cursor>` (historical, then continuing live with no gap) — one continuous read loop, only the initial cursor differs. (`ADR-010`)

## Schema, versioning & upcasting

- **Schema Registry / `EventTypeDefinition`** *(a broader relative of Confluent Schema Registry's namesake concept — that one is a schema catalog; this one also carries claims/masking/upcast rules, not just the shape)* — the per-`AppId`, per-event-type registration record: JSON Schema, `EntityIdField`, `ChangeKind`, claim requirements, masking metadata, and upcast/downcast expression lists. (`ADR-020`, `docs/data/schema-registry.md`)
- **Upcasting / `upcastFromPrevious`** *(synonym: schema migration, event migration)* — a per-version expression list that reshapes an old-shaped payload forward so every consumer sees the current shape. Materialized to the log once computed (`UpcastMaterialization`), not recomputed on every read. (`ADR-018`/`ADR-027`)
- **Downcasting / `downcastToPrevious`** *(synonym: schema downgrade, backward transformation)* — the reverse direction, applied read-time only, for a consumer explicitly requesting an older shape. Never persisted — unbounded, so not worth materializing. (`ADR-028`)
- **`IUpcastExpressionEvaluator`** — the pluggable seam behind the upcast engine; CEL is the default. (`ADR-053`)
- **`EventUpcastFailed`** — a reserved, platform-owned event type (never registered by an operator) recorded in place of an event that failed publish-time upcast validation. (`ADR-020`)

## Multi-tenancy & distribution

- **`AppId`** *(synonym: tenant ID — though under `ADR-075`'s silo model, "tenant" now means one dedicated deployment; `AppId` scopes applications *within* one tenant's own deployment, not different customers sharing one)* — the top-level multi-tenant scoping key; every store, claim, and configuration record is `AppId`-scoped. (`ADR-030`)
- **`AllowedRegions`** *(synonym: residency policy, geo-fencing rule)* — a per-`AppId` list constraining which peer sites an `AppId`'s events may replicate to, enforced at the peer-sync outbox. (`ADR-061`)
- **`SeedPeers`** *(synonym: bootstrap peers, seed nodes — common P2P/distributed-systems terminology)* — static, explicitly-configured peer addresses; the only peer-discovery mechanism — no automatic discovery of any kind. (`ADR-051`)
- **`ShardKey` (= `EntityType`)** *(synonym: partition key — the term Cosmos DB/Kafka use for the same concept)* — the sharding dimension; a type-based boundary that is also a natural replication-scope boundary. (`ADR-034`)
- **Peer-sync outbox / `PeerSyncCursor`** *(synonym: replication outbox, sync queue)* — the durable, resumable-across-crashes primitive that queues and tracks outbound replication to other sites — the same shape `ADR-039`'s client outbox and `ADR-060`'s webhook dispatcher reuse. (`ADR-033`)
- **Site vs. leaf client — two tiers, deliberately not one uniform "node" concept.** A **site** (an `ADR-033` peer) is a full replica: everything it's entitled to see, fully folded and stored. A **leaf client** (`ADR-039`'s MVVM client, `ADR-065`'s local active-scope caching) is explicitly *not* a fourth peer in the mesh — it's a scoped consumer of `ADR-037`'s filtered GraphQL Subscriptions, holding only an active-use slice, invalidated on erasure. Two different guarantees for two different roles, not one deployment unit reused at every scale. (`ADR-033`/`ADR-065`)

## Security, access & compliance

- **`RequiredPublishClaim` / `RequiredReadClaim` / `RequiredClaims`** — the claim-string gate(s) an event type declares for writing/reading it, in `"type:value"` format. Same-direction multiple claims default to OR-combined. (`ADR-008`/`ADR-050`)
- **`x-masking`** — the OpenAPI/AsyncAPI Specification Extension carrying a field's masking classification, `RequiredClaims`, `regulatoryClassification`, `erasureScope`, and `revealOnDemand` configuration; guaranteed to survive into generated spec docs. (`ADR-050`)
- **Masking wrapper (`value`/`masked`/`erased`)** — the three-branch response shape for a classified field: `masked` means "you lack a claim, someone else can still see this"; `erased` means "permanently destroyed, no one can ever see it again." Never conflated. (`ADR-009`/`ADR-057`)
- **`revealOnDemand`** — an opt-in mode where a classified field's ordinary response is always masked, even for a claim-holder; seeing the real value is a separate, audited `revealField` action. Mitigates shoulder-surfing — a different axis from claims-based access control. (`ADR-009`)
- **`erasureScope`** — a JSON Pointer naming the `EntityId` whose crypto-shredding key protects a field, for PII belonging to a different entity than the event's own. (`ADR-057`)
- **`IErasureKeyStore`** — the keyed, multi-backend Strategy-pattern seam for per-`(AppId, EntityId)` Data-Encryption-Key storage (Azure Key Vault, AWS KMS, Google Cloud KMS, HashiCorp Vault, or a local dev store — several may run simultaneously in one deployment, keyed by `AppId`). (`ADR-057`)
- **Crypto-shredding** *(synonym: cryptographic erasure — both terms used interchangeably in the source literature, e.g. the Verraes write-up `ADR-057` cites)* — GDPR/CCPA erasure via destroying an entity's encryption key rather than touching `Payload` or the hash chain. (`ADR-057`)
- **`Role` / `UserPermission`** — `Role` bundles permission strings (`AppId`-scoped, registry state); `UserPermission` is a direct per-user grant. Both are additive-only — no explicit-deny concept exists anywhere in this model. (`ADR-046`)
- **`AppTrustRoot`** — a per-`AppId` registry resolving which DID is a root of trust for a capability namespace — the one thing the UCAN spec itself leaves out-of-band. (`ADR-044`)
- **Delegated access grant** *(synonym: capability delegation)* — a UCAN-based, capped, time-boxed, entity-scoped access grant one party issues to another ("secondary opinion" access) — distinct from the classical Four Eyes/two-person rule. (`ADR-043`)
- **`AccessLogEntry`** — one row per read, through every surface, hash-chained via a second, independent chain from the write-side event log. (`ADR-045`)
- **`ReaderTrustBasis`** — records whether a reader's credential was `Authoritative` or `Attested` at the moment of a logged read. (`ADR-045`)
- **DPoP** *(synonym: proof-of-possession token)* — Demonstrating Proof of Possession (RFC 9449) — binds a bearer token to a client-held key, so a stolen token alone can't be replayed. (`ADR-017`)
- **Ticket exchange** — a short-lived, single-use, opaque, HMAC-signed ticket letting a header-incapable client (a `<video src>`, an `<img src>` pointing directly at a content-addressed attachment URL) authenticate without an `Authorization` header. **Not a synonym for "Token Exchange"** (RFC 8693, `ADR-036`) despite the similar name — Token Exchange is the underlying OAuth grant type that produces an ordinary bearer JWT from another credential; this ADR's ticket is a separate, narrower, single-use artifact for the one hop that can't carry a header at all. Disambiguated deliberately, not a naming accident. (`ADR-040`)
- **Step-up authentication** *(synonym: re-authentication challenge, MFA step-up)* — an RFC 9470 `acr_values`/`max_age` challenge triggered before a regulated action (a `Signature`); the framework never implements the re-authentication method itself, only the challenge/enforcement. (`ADR-066`)

## Streaming / telemetry

- **`TelemetryChannel` / `ChannelId`** — a named, sequenced stream of raw chunks (declared `ContentKind`: `RawScalar`, `RawBinary`, or `Media`), belonging to one `EntityId` and one `AppId`. A completely separate storage/ingestion path from `StoredEvent` — no JSON Schema, no per-chunk hash-chaining, no Entity Store fold. (`ADR-031`)
- **`TelemetrySample`** — one raw chunk within a channel; batch-ingested, not one-sample-per-request. (`ADR-031`)
- **`Derived` channel** *(synonym: computed stream)* — a channel populated by resampling/filtering/transcoding one or more source channels via a background `ChannelDerivationWorker`, the same "internal follower" shape `ProjectionHost`/`UpcastMaterializer` already use. (`ADR-031`)
- **`RedactedRange`** — `{ChannelId, FromTimestamp, ToTimestamp, RequiredClaim}`; a read-time transform substituting a tone/blank-frame/zero-fill span for a caller lacking the claim. Distinct from `ADR-009`'s masking (that wraps a JSON *value*; this substitutes *byte content*). (`ADR-052`)
- **Media Fragments URI** — the W3C temporal-fragment syntax (`#t=10,20`) this design adopts for deep-linking to a point/interval within a channel, interconvertible with `TelemetryPointer`. (`ADR-031`)

## Litigation, lineage & audit

- **Bitemporal playback** *(synonym: time-travel query, as-of query — e.g. SQL Server's own `FOR SYSTEM_TIME AS OF` clause is the same underlying SQL:2011 concept)* — querying "what did we believe at time T, as of processing time U" — `OccurredAt` is valid time, `SequenceNumber` is transaction time; `ADR-021`'s Entity Store is the valid-time-corrected fold, bitemporal playback is the missing transaction-time query. (`ADR-068`)
- **Lineage export** — walking the `parentEventIds` DAG to produce a portable export bundle for dev/support replay or litigation review, enforcing masking/erasure identically to any other read. (`ADR-068`)
- **Offline player** — a self-contained, single static HTML file (no server, no install) that independently re-verifies a lineage export's hash chain/manifest, for handing to a party with no access to the live system. (`ADR-068`)
- **Control-plane event** — schema registration, RBAC grants, and `AppTrustRoot` registration modeled as reserved event types in the same Event Log as business events, so they can be linked via `parentEventIds` to what they authorize. (`ADR-067`)

## API & client

- **HTTP `QUERY` method** — used instead of `GET` for GraphQL requests specifically so PII/PHI-bearing filter arguments never land in a URL, log, or proxy cache. (`ADR-012`/`ADR-037`)
- **GraphQL Gateway** — the sole query/mutation/subscription surface; superseded the earlier OData-era Follow/Lineage REST containers entirely. (`ADR-037`)
- **Outbox (client-local)** *(synonym: offline queue, write-behind queue)* — the durable, Background-Sync-flushed local queue an offline-capable MVVM client uses to hold not-yet-sent publishes; pluggable flush triggers (opportunistic, scheduled, manual/air-gapped). (`ADR-039`/`ADR-069`)
- **SBOM / SOUP** — Software Bill of Materials (machine-readable dependency manifest, `microsoft/sbom-tool`) and Software of Unknown Provenance (IEC 62304's term for exactly the same catalog, once this design is used in a medical-device context). (`ADR-074`)

## A note on terms still under discussion

Not every term discussed while designing Duplex has been decided yet. As
of this writing, a **recording-level grouping above `ChannelId`**
(a multi-channel recording session, e.g. a 32-electrode EEG montage) —
proposed name `ThreadId`, deliberately not `StreamId`, since `StreamId`
was already used and retired by `ADR-021` ("this subsumes `StreamId`;
it was already trying to be this") — and whether a detected event's
`TelemetryPointer` should generalize to a list (for a detection
triggered by a correlated pattern across multiple channels) are both
real, active design questions — not yet an ADR, not yet in this glossary
as settled terms. Check `docs/10-open-questions.md` and `ADR-031` before
assuming either exists.
