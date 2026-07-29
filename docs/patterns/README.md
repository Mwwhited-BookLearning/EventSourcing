# Architecture & Design Pattern Catalog

This folder is a **pattern reference**, distinct from the other two kinds
of document in this design package:

- `docs/adrs/*.md` explains *a specific decision this project made* — the
  context, the trade-off, the consequences, for this system specifically.
- `docs/references.md` is a *bibliography* — every external RFC/standard/
  library this design adopts or considered, one line each.
- **This folder** explains *the general pattern* first (what it is, who
  named it, when you'd reach for it, what it costs), then points to the
  specific ADR(s) that apply it here. Read a pattern doc to learn the
  pattern itself, portably — not just how this one project happens to use
  it. This is itself part of this project's stated purpose as a worked
  teaching example (`README.md`), not an afterthought.

## Written

| Pattern | Summary | Applied in |
|---|---|---|
| [Event Sourcing](event-sourcing.md) | State as a replayable sequence of immutable events, not a mutable row | `ADR-004`, `ADR-009`, `ADR-019`, `ADR-023` |
| [CQRS & Materialized Views](cqrs-and-materialized-views.md) | Separate write model from read model; reads come from a disposable, rebuildable projection | `ADR-015`, `ADR-016`, `ADR-021` |
| [Idempotent Receiver & Inbox/Dead Letter](idempotent-receiver-and-inbox.md) | Safe retries; persist-before-understand; failures become inspectable records, not silent drops | `ADR-011`, `ADR-020`, `ADR-023` |
| [Tolerant Reader & Schema Evolution](tolerant-reader-and-schema-evolution.md) | Ignore what you don't recognize; reconcile old-shaped data on read, never by rewriting history | `ADR-018`, `ADR-020`, `ADR-022` |
| [Optimistic Concurrency](optimistic-concurrency.md) | Check for conflict at commit time instead of locking; flag genuine conflicts rather than silently resolving them | `ADR-024` |
| [Hash Chain (Tamper-Evident Log)](hash-chain-integrity.md) | Each record's hash incorporates the previous one's, so undetected history tampering becomes detectable | `ADR-019` |
| [Ticket Exchange for Header-Incapable Clients](ticket-exchange-headerless-clients.md) | A short-lived, single-use, opaque ticket + client-signed URL, for callers that can't set an `Authorization` header at all | `ADR-040` |
| [MVVM (Model-View-ViewModel)](mvvm-client-architecture.md) | Structure/style/state/transport kept in separate layers; a ViewModel dispatches commands rather than mutating state directly | `ADR-039` |
| [Installable, Offline-Capable Web App with a Persistent Outbox](pwa-offline-outbox.md) | Service Worker + Web App Manifest + Background Sync — a web client that installs, runs with no network, and queues commands durably until connectivity returns | `ADR-039` |
| [GraphQL Query Language](graphql-query-language.md) | Client-driven hierarchical query/mutation/subscription document, one round trip, partial-success execution | `ADR-037` |
| [OData Query Protocol](odata-query-protocol.md) | Standardized URL query-string conventions for filter/sort/page/expand over a resource collection | `ADR-003`, `ADR-012` (superseded by `ADR-037`) |
| [MVP (Model-View-Presenter)](mvp-pattern.md) | Explicit View/Presenter interface calls instead of a data-binding engine — same strict mediation as MVVM, no reactive substrate required | Compared in `docs/comparisons/ui-architecture-patterns.md` — first fallback below MVVM |
| [MVC (Model-View-Controller)](mvc-pattern.md) | Controller mediates input → Model update → View selection; View may read Model directly to render | Compared in `docs/comparisons/ui-architecture-patterns.md` — second fallback below MVVM |
| [JSON:API](jsonapi-specification.md) | Standardized REST conventions: sparse fieldsets, compound documents, reserved-but-undefined filter parameter | Compared, not adopted — `docs/comparisons/api-query-layer.md` |
| [gRPC + Protobuf Services](grpc-protobuf-services.md) | Contract-first binary RPC with `FieldMask` partial responses and native streaming | Compared, not adopted — `docs/comparisons/api-query-layer.md` |
| [Declarative REST Filter Operators](declarative-rest-filtering.md) | PostgREST/Hasura-style direct comparison-operator vocabulary over relational columns | Compared, not adopted — `docs/comparisons/api-query-layer.md` |

## Interactions — where two patterns compose

A pattern rarely runs alone. When two (or more) genuinely combine at one
point in this design — not just "both used somewhere in the same ADR" —
the interaction gets its own page under
[`interactions/`](interactions/), since neither pattern's own doc tells
the whole story on its own:

| Interaction | What it explains |
|---|---|
| [Fold-time ordering + conflict detection](interactions/fold-ordering-and-conflict.md) | Why [Optimistic Concurrency](optimistic-concurrency.md) and [Watermarks/event-time ordering](tolerant-reader-and-schema-evolution.md) are two different checks, run together, catching two different failure modes — and why only one of the two actually blocks a write from applying |
| [The publish pipeline](interactions/publish-pipeline.md) | How [Idempotent Receiver, Inbox](idempotent-receiver-and-inbox.md), [Tolerant Reader](tolerant-reader-and-schema-evolution.md), and Dead Letter Channel compose into one `POST /publish` request, in a specific, load-bearing order |
| [Gated authoritative publish](interactions/gated-authoritative-publish.md) | How Write-Audit-Publish, the Quarantine pattern (deliberately inverted), and a second [CQRS materialized view](cqrs-and-materialized-views.md) compose so unconfirmed data stays visible-but-labeled instead of blocked |

## Decided, not yet written up as standalone docs

Real patterns with a real, landed ADR, listed here so the catalog stays
current even before each gets its own full write-up — **every pattern
this project intends to adopt now has a real ADR; none remain genuinely
queued.**

| Pattern | Summary | Applied in |
|---|---|---|
| Upcast/downcast schema mapping (Avro-schema-resolution-adjacent) | Forward map (old→current) for replay, persisted once as a materialized event so it's never recomputed; backward map (current→old) for serving legacy consumers, computed fresh per request since its target isn't fixed | `ADR-018`, `ADR-027`, `ADR-028` |
| Watermarks / event-time vs. processing-time ordering | Fold by logical occurrence time, not arrival order, so a late-arriving event can't silently revert already-applied newer data; flag it instead of blocking or corrupting | `ADR-029` |
| Multi-tenancy via a namespacing key | One engine, many independent applications, each schema/entity scoped by an `appId` key so unrelated applications can't collide or see each other's registrations | `ADR-030` |
| Claims-based authorization + property-level masking | A second, finer-grained authorization axis on top of coarse operation scopes; redact individual fields via a value/masked wrapper rather than an all-or-nothing response | `ADR-008`, `ADR-009` |
| Proof of Possession (vs. bearer tokens) | Cryptographically bind a token to the holder's key, so a leaked token alone isn't usable | `ADR-017` |
| Safe method with a request body | A `GET`-like, cacheable, side-effect-free method that still carries a body — keeps sensitive query content out of a URL, access log, or proxy cache | `ADR-012`, `ADR-037` (GraphQL queries specifically over `QUERY`, never `GET`) |
| Problem Details (canonical error shape) | One consistent error response shape across every endpoint | `ADR-013` |
| Sharding (application-level partitioning) | Partition an entity store by key so no single store need hold everything; see [the sharding-strategy comparison](../comparisons/sharding-strategy.md) for why entity-type-based won over hash-based | `ADR-034` |
| Multi-origin replication + anti-entropy/gossip | Independent writable replicas, each making local progress with no guarantee of agreement at any instant, converging via background reconciliation; see [the peer-sync-topology comparison](../comparisons/peer-sync-topology.md) for why gossip won for regional fault tolerance specifically | `ADR-033` |
| Merkle tree catch-up | Exchange hash-tree summaries to find and transfer only the differing ranges after a disconnection, instead of a full resync | `ADR-033` |
| Non-authoritative capture (Reservation/Provisional + non-repudiation logging) | Accept data whose submitter's authority can't be verified yet; capture now, adjudicate later, via an explicit trust status that never gates ingestion; see [the rejection-behavior comparison](../comparisons/authority-rejection-behavior.md) for annotate-only vs. compensating-patch | `ADR-035` |
| Self-attested, offline-verifiable delegation (DID/UCAN) | Prove a chain of delegated capability without needing to reach a central authority at verification time | `ADR-036` |
| Delegated, capped, time-boxed access grants ("secondary opinion" access) | One authorized user grants another temporary, entity-scoped access, capped at the granter's own level, via UCAN delegation's attenuation invariant — not the classical Four Eyes/two-person rule, disambiguated explicitly | `ADR-043` |
| Application-defined permission namespaces via per-tenant trust roots | Resolves the one thing the UCAN spec itself leaves out-of-band (which DID is a root of trust for a capability namespace) using the existing `AppId` scoping key | `ADR-044` |
| Read access audit logging (HIPAA §164.312(b)-shaped) | Every read logged against the reader's identity and trust basis, in its own hash-chained store separate from the business event log | `ADR-045` |
| Role-Based Access Control (RBAC, base/flat tier) | Permissions granted to roles, roles assigned to users, expanded to a flattened claim set at token issuance — no change to any existing claim check | `ADR-046` |
| Row-Level Security (application-layer, portable across providers) | Access control finer than type/endpoint-level, checked per specific `EntityId` rather than via provider-native RLS (for SQLite portability) | `ADR-043` |
| Claims augmentation for federated IdPs | Enrich an externally-issued, already-authoritative token with locally-known application claims via Token Exchange, never replacing the original claims | `ADR-047` |
| [Multi-Axis Authority/Assurance](multi-axis-authority-assurance.md) | Keep independent trust questions (identity proofing, authentication strength, federation trust) as separate axes instead of one collapsed score | Raised in conversation — not yet adopted, see `docs/10-open-questions.md` |
| SPIFFE/SPIRE workload identity | Attestation-based, short-lived cryptographic identity for services/workloads, with cross-trust-domain federation — no bootstrap secret to rotate manually | `ADR-048` |
| API Gateway | A single external entry point in front of multiple backend services — auth/TLS termination, routing, hiding backend topology from callers | `ADR-049` |
| Data classification + redaction | Tag sensitive data once with a classification; every sink (logs, API responses) that respects it redacts automatically, without re-deriving the rule per sink | `ADR-050` |
| Streaming ingestion (telemetry, audio/video) as a separate fast path | Bypass schema validation/hash-chaining/fold entirely for high-frequency chunked data; link back to the event-sourced world only through a detector publishing an ordinary event | `ADR-031` |
| Deep-linking via temporal fragment URIs | A stable, shareable reference to a point/interval within a media/signal stream, using a real W3C syntax instead of a bespoke query parameter | `ADR-031` |
| Seekable playback via byte-range requests | The standard mechanism behind a scrub bar — request a byte range, get `206 Partial Content` back, instead of downloading a whole file to seek within it | `ADR-031`, `ADR-032` |
| Content-addressable storage | Address a binary object by the hash of its own bytes — naturally deduplicating, naturally cacheable, naturally tamper-evident | `ADR-032` |
| Browsable access via a real filesystem protocol | Project a virtual folder/file hierarchy over data that isn't actually stored as files, using WebDAV instead of a bespoke browse API | `ADR-032` |

## A note on diagrams

Each written pattern doc includes a PlantUML diagram (sequence, C4
component, or object, whichever actually clarifies that pattern).
[MVVM](mvvm-client-architecture.md) is the first pattern doc with a
Salt wireframe, since it's the first one with a real UI surface.

## A note on superseded patterns

`ADR-018` originally justified its OData `compute()` upcast mechanism by
reusing the OData parser this design already depended on for `$filter`
pushdown — a real instance of "prefer reusing an existing primitive over
adding a new dependency" (see how [Hash Chain](hash-chain-integrity.md)
reuses `ADR-011`'s SHA-256 for the same reason). That specific reuse
argument stopped holding once OData was swapped out entirely for GraphQL
(`ADR-037`) — recorded in `references.md` and `CLAUDE.md`'s integration
status, not silently dropped.
