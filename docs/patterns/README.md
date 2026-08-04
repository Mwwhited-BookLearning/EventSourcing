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

**Sorted into four kinds, per direct request** — the same catalog as
before, reorganized so "what altitude is this pattern at" is answered by
which table it's in, not just its name:

- **Design patterns** — code/class-level, reusable regardless of system
  shape (Strategy, Composition Root, Anti-Corruption Layer, and similar).
- **Architecture patterns** — decisions about how *components/stores/
  services* relate (Event Sourcing, CQRS, sharding, replication, and
  similar) — the largest bucket, since most of what this project decides
  operates at this altitude.
- **Standards** — the pattern *is* adopting a real, formally-published
  external spec (an RFC, a W3C spec, GraphQL/OData themselves).
- **Practices** — a technique or methodology, usually algorithmic or
  process-level, not primarily a system-shape or code-structure decision
  (hash chains, testing strategy, and similar).

Each table has a **Status** column: **Written** (has its own standalone
doc) or **Catalog only** (a real, landed ADR exists; the full write-up
doesn't yet — see "Decided, not yet written up" below the tables, same
meaning as before, just no longer split into a second physical table
per kind).

## Design patterns

| Pattern | Summary | Applied in | Status |
|---|---|---|---|
| [Strategy Pattern (Extensible Masking/Redaction Content)](strategy-pattern-extensible-masking.md) | A family of interchangeable algorithms behind one interface, selected at runtime by a data-carried key (here, `x-masking.strategy`) instead of a hardcoded `switch` — new algorithms register in, nothing existing changes | `ADR-009` (`IMaskingStrategy`, keyed DI), reused by `ADR-052` (`IStreamRedactionStrategy`), `ADR-057` (`IErasureKeyStore`, keyed by `AppId`), and `ADR-053` (`IUpcastExpressionEvaluator`, CEL/Jsonata swappable via configuration) | Written |
| [Composition Root & Pure DI](composition-root-and-pure-di.md) | Wire every object graph in exactly one visible place near the entry point, via explicit registration lines, never a reflection-driven scanning convention | `ADR-041`, formalized as the extensibility answer by `ADR-059`, extended to installable packages by `ADR-062` | Written |
| [Anti-Corruption Layer](anti-corruption-layer.md) | An isolating translation layer between two subsystems with different data models, so a foreign system's shape/quirks never leak into your own domain model | `ADR-072` (`IInterchangeFormatAdapter` — HL7v2/FHIR inbound, ICH E2B(R3)/GS1-EPCIS outbound) | Written |
| [Idempotent Receiver & Inbox/Dead Letter](idempotent-receiver-and-inbox.md) | Safe retries; persist-before-understand; failures become inspectable records, not silent drops | `ADR-011`, `ADR-020`, `ADR-023` | Written |
| [Tolerant Reader & Schema Evolution](tolerant-reader-and-schema-evolution.md) | Ignore what you don't recognize; reconcile old-shaped data on read, never by rewriting history | `ADR-018`, `ADR-020`, `ADR-022`; the enum unknown-value fallback contract and N-1/N+1 window are the same tolerance restated as a deployment-time guarantee (`ADR-038`) | Written |
| [Request-Reply & Correlation Identifier](request-reply-correlation.md) | A reply carries a unique identifier naming which request it answers, so the pairing survives arbitrary delay between the two | `ADR-094` (`RespondsToEventId` envelope field, `ExpectedResponse` opt-in tracked deadline) | Written |

## Architecture patterns

| Pattern | Summary | Applied in | Status |
|---|---|---|---|
| [Event Sourcing](event-sourcing.md) | State as a replayable sequence of immutable events, not a mutable row | `ADR-004`, `ADR-009`, `ADR-019`, `ADR-023` | Written |
| [CQRS & Materialized Views](cqrs-and-materialized-views.md) | Separate write model from read model; reads come from a disposable, rebuildable projection | `ADR-015`, `ADR-016`, `ADR-021` | Written |
| [MVVM (Model-View-ViewModel)](mvvm-client-architecture.md) | Structure/style/state/transport kept in separate layers; a ViewModel dispatches commands rather than mutating state directly | `ADR-039` | Written |
| [MVP (Model-View-Presenter)](mvp-pattern.md) | Explicit View/Presenter interface calls instead of a data-binding engine — same strict mediation as MVVM, no reactive substrate required | Compared in `docs/comparisons/ui-architecture-patterns.md` — first fallback below MVVM | Written |
| [MVC (Model-View-Controller)](mvc-pattern.md) | Controller mediates input → Model update → View selection; View may read Model directly to render | Compared in `docs/comparisons/ui-architecture-patterns.md` — second fallback below MVVM | Written |
| [Installable, Offline-Capable Web App with a Persistent Outbox](pwa-offline-outbox.md) | Service Worker + Web App Manifest + Background Sync — a web client that installs, runs with no network, and queues commands durably until connectivity returns | `ADR-039` | Written |
| Multi-tenancy via a namespacing key | One engine, many independent applications, each schema/entity scoped by an `appId` key so unrelated applications can't collide or see each other's registrations | `ADR-030`, reused as the partition key for rate limiting (`ADR-058`) and the scoping key for data residency (`ADR-061`) — see also `ADR-075`, which layers a per-tenant *deployment* boundary underneath this key | Catalog only |
| Claims-based authorization + property-level masking | A second, finer-grained authorization axis on top of coarse operation scopes; redact individual fields via a value/masked wrapper rather than an all-or-nothing response | `ADR-008`, `ADR-009` | Catalog only |
| Sharding (application-level partitioning) | Partition an entity store by key so no single store need hold everything; see [the sharding-strategy comparison](../comparisons/sharding-strategy.md) for why entity-type-based won over hash-based | `ADR-034`, unchanged by `ADR-061`'s region-pinning | Catalog only |
| Multi-origin replication + anti-entropy/gossip | Independent writable replicas, each making local progress with no guarantee of agreement at any instant, converging via background reconciliation; see [the peer-sync-topology comparison](../comparisons/peer-sync-topology.md) | `ADR-033`, extended with per-`AppId` region-tagged destination filtering (`ADR-061`) — now scoped to *one tenant's own* multi-site deployment, per `ADR-075` | Catalog only |
| Non-authoritative capture (Reservation/Provisional + non-repudiation logging) | Accept data whose submitter's authority can't be verified yet; capture now, adjudicate later, via an explicit trust status that never gates ingestion; see [the rejection-behavior comparison](../comparisons/authority-rejection-behavior.md) | `ADR-035`, defaulted for client-captured device readings (`ADR-070`) and EMR-sourced interchange-adapter data (`ADR-072`) | Catalog only |
| Self-attested, offline-verifiable delegation (DID/UCAN) | Prove a chain of delegated capability without needing to reach a central authority at verification time | `ADR-036`; also the mechanism behind true-offline break-glass access (`ADR-043` amendment) | Catalog only |
| Delegated, capped, time-boxed access grants ("secondary opinion" access) | One authorized user grants another temporary, entity-scoped access, capped at the granter's own level, via UCAN delegation's attenuation invariant — not the classical Four Eyes/two-person rule, disambiguated explicitly | `ADR-043` | Catalog only |
| Application-defined permission namespaces via per-tenant trust roots | Resolves the one thing the UCAN spec itself leaves out-of-band (which DID is a root of trust for a capability namespace) using the existing `AppId` scoping key | `ADR-044` | Catalog only |
| Read access audit logging (HIPAA §164.312(b)-shaped) | Every read logged against the reader's identity and trust basis, in its own hash-chained store separate from the business event log | `ADR-045`; `ADR-064`'s `ActorId` is the write-side equivalent | Catalog only |
| Role-Based Access Control (RBAC, base/flat tier) | Permissions granted to roles, roles assigned to users, expanded to a flattened claim set at token issuance | `ADR-046`; `Role`/`UserPermission` fold from core-engine reserved events per `ADR-067` | Catalog only |
| Row-Level Security (application-layer, portable across providers) | Access control finer than type/endpoint-level, checked per specific `EntityId` rather than via provider-native RLS (for SQLite portability) | `ADR-043` | Catalog only |
| Claims augmentation for federated IdPs | Enrich an externally-issued, already-authoritative token with locally-known application claims via Token Exchange, never replacing the original claims | `ADR-047` | Catalog only |
| [Multi-Axis Authority/Assurance](multi-axis-authority-assurance.md) | Keep independent trust questions (identity proofing, authentication strength, federation trust) as separate axes instead of one collapsed score | Raised in conversation — not yet adopted, see `docs/10-open-questions.md` | Written (comparison only, not adopted) |
| SPIFFE/SPIRE workload identity | Attestation-based, short-lived cryptographic identity for services/workloads, with cross-trust-domain federation — no bootstrap secret to rotate manually | `ADR-048` | Catalog only |
| API Gateway | A single external entry point in front of multiple backend services — auth/TLS termination, routing, hiding backend topology from callers | `ADR-049` | Catalog only |
| Data classification + redaction | Tag sensitive data once with a classification; every sink (logs, API responses) that respects it redacts automatically | `ADR-050` | Catalog only |
| Streaming ingestion (telemetry, audio/video) as a separate fast path | Bypass schema validation/hash-chaining/fold entirely for high-frequency chunked data; link back to the event-sourced world only through a detector publishing an ordinary event | `ADR-031` | Catalog only |
| Content-addressable storage | Address a binary object by the hash of its own bytes — naturally deduplicating, naturally cacheable, naturally tamper-evident | `ADR-032`; extended to sub-file granularity via content-defined chunking (`ADR-032` amendment) | Catalog only |
| Crypto-shredding (cryptographic erasure) | Encrypt personal data with a key scoped to the data subject, held separately from the data; "erasure" destroys the key, never the row | `ADR-057`; categorically doesn't reach `ADR-066`'s `Signature`/`ActorId` (GDPR Art. 17(3)(b)/(e)) | Catalog only |
| Envelope encryption | A per-subject data-encryption key (DEK) itself wrapped by a master key-encrypting-key (KEK) held in a KMS — the standard mechanism crypto-shredding is built on | `ADR-057`, keyed/multi-backend (cloud, on-prem/self-hosted, local, composable per `AppId`) | Catalog only |
| Device input integration via Web Hardware APIs | Read directly from USB/HID/serial/BLE hardware from a web page, offline, via a browser-native permission-gated API, with a local native-bridge fallback where unsupported | `ADR-070`, feeding the same client outbox `ADR-039`/`ADR-069` already provide | Catalog only |
| Rate limiting (Token Bucket / Sliding Window / Concurrency Limiter) | Bound a caller's request volume or concurrent resource usage, partitioned per tenant so one caller can't starve another | `ADR-058` | Catalog only |
| Bitemporal modeling (valid time vs. transaction time) | Track two independent time axes — when something was true in reality, vs. when the system learned about it — so "what do we know now" and "what did we show at the time" can both be queried honestly | `ADR-068` | Catalog only |
| Expand/Contract (Parallel Change) database migration | Add new structures without touching existing ones (Expand), cut over writers/readers to the new shape (Migrate), remove the old shape only once nothing depends on it, much later if ever (Contract) — a rolling deployment's binary rollback then just works, since the database never stops understanding the old code | `ADR-038`, layered on this design's existing "never lose data" posture (`ADR-023`) | Catalog only |

## Standards

*The pattern here **is** adopting a real, formally-published external
spec — verified against the actual spec text before citing, per this
project's standing convention (see `.claude/protocols/verify-before-
citing.md`).*

| Pattern | Summary | Applied in | Status |
|---|---|---|---|
| [Ticket Exchange for Header-Incapable Clients](ticket-exchange-headerless-clients.md) | A short-lived, single-use, opaque ticket + client-signed URL, for callers that can't set an `Authorization` header at all | `ADR-040` (composes RFC 8693 issuance, RFC 7662-shaped resolution) | Written |
| [GraphQL Query Language](graphql-query-language.md) | Client-driven hierarchical query/mutation/subscription document, one round trip, partial-success execution | `ADR-037` | Written |
| [OData Query Protocol](odata-query-protocol.md) | Standardized URL query-string conventions for filter/sort/page/expand over a resource collection | `ADR-003`, `ADR-012` (superseded by `ADR-037`) | Written |
| [JSON:API](jsonapi-specification.md) | Standardized REST conventions: sparse fieldsets, compound documents, reserved-but-undefined filter parameter | Compared, not adopted — `docs/comparisons/api-query-layer.md` | Written |
| [gRPC + Protobuf Services](grpc-protobuf-services.md) | Contract-first binary RPC with `FieldMask` partial responses and native streaming | Compared, not adopted — `docs/comparisons/api-query-layer.md` | Written |
| [Declarative REST Filter Operators](declarative-rest-filtering.md) | PostgREST/Hasura-style direct comparison-operator vocabulary over relational columns | Compared, not adopted — `docs/comparisons/api-query-layer.md` | Written |
| [Step-Up Authentication](step-up-authentication.md) | A resource server challenges a caller to re-authenticate at a stronger level/more recently, for a specific action, rather than requiring the strongest factor for everything all the time | `ADR-066` (RFC 9470, `RequiredSignature`) | Written |
| Proof of Possession (vs. bearer tokens) | Cryptographically bind a token to the holder's key, so a leaked token alone isn't usable | `ADR-017` (RFC 9449, DPoP) | Catalog only |
| Safe method with a request body | A `GET`-like, cacheable, side-effect-free method that still carries a body — keeps sensitive query content out of a URL, access log, or proxy cache | `ADR-012`, `ADR-037` (RFC 10008, HTTP `QUERY`) | Catalog only |
| Problem Details (canonical error shape) | One consistent error response shape across every endpoint | `ADR-013` (RFC 9457) | Catalog only |
| Deep-linking via temporal fragment URIs | A stable, shareable reference to a point/interval within a media/signal stream, using a real W3C syntax instead of a bespoke query parameter | `ADR-031` (W3C Media Fragments URI) | Catalog only |
| Seekable playback via byte-range requests | The standard mechanism behind a scrub bar — request a byte range, get `206 Partial Content` back | `ADR-031`, `ADR-032` (RFC 7233) | Catalog only |
| Webhook delivery with HMAC signing and retry | Push-based outbound notification to a registered URL, signed so the receiver can verify authenticity, retried with backoff, dead-lettered rather than dropped silently | `ADR-060` (Standard Webhooks spec); `ADR-072`'s outbound interchange adapters compose ahead of delivery | Catalog only |

## Practices

*A technique or methodology — usually algorithmic or process-level, not
primarily a system-shape or code-structure decision.*

| Pattern | Summary | Applied in | Status |
|---|---|---|---|
| [Optimistic Concurrency](optimistic-concurrency.md) | Check for conflict at commit time instead of locking; flag genuine conflicts rather than silently resolving them | `ADR-024` | Written |
| [Hash Chain (Tamper-Evident Log)](hash-chain-integrity.md) | Each record's hash incorporates the previous one's, so undetected history tampering becomes detectable | `ADR-019`; reused for `ADR-066`'s `Signature` and `ADR-068`'s manifest hash | Written |
| Upcast/downcast schema mapping (Avro-schema-resolution-adjacent) | Forward map (old→current) for replay, persisted once as a materialized event so it's never recomputed; backward map (current→old) computed fresh per request | `ADR-018`, `ADR-027`, `ADR-028` | Catalog only |
| Watermarks / event-time vs. processing-time ordering | Fold by logical occurrence time, not arrival order, so a late-arriving event can't silently revert already-applied newer data; flag it instead | `ADR-029` | Catalog only |
| Merkle tree catch-up | Exchange hash-tree summaries to find and transfer only the differing ranges after a disconnection, instead of a full resync | `ADR-033` | Catalog only |
| Event chain/lineage export | Extract a causally-connected subgraph of history into a portable, provenance-preserving bundle for replay in a different environment | `ADR-068`, its bundle format reused a second time for an air-gapped outbox transfer (`ADR-069`) | Catalog only |
| Bulk/Batch operations | A single request carrying many independent submissions, each processed (and reported) on its own — an efficiency/transport optimization, not a new persistence or atomicity model | `ADR-072` (`POST /publish/batch`) | Catalog only |
| Property-based testing | Test a general property a function must hold ("for all valid inputs, X"), checked against a large number of randomly generated cases | `ADR-063` (`FsCheck`, for `ADR-019`'s hash-chain and `ADR-024`'s conflict-resolution invariants) | Catalog only |
| Fault injection / Chaos Engineering | Deliberately introduce failure into a running system to build confidence it survives real turbulent conditions | `ADR-063` (`Polly`+`Simmy` now; `Testcontainers`+`Toxiproxy` named as the next escalation) | Catalog only |
| Test Pyramid | Many fast, cheap unit tests at the base; fewer integration tests in the middle; the fewest, slowest UI/E2E tests at the top | `ADR-055` (MSTest+Moq unit, Testcontainers integration, Playwright E2E) | Catalog only |

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
(`ADR-037`) — recorded in `references.md` and `docs/changes/2026-07-
30.md`, not silently dropped.
