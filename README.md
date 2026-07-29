# Open Event Sourcing Store — Design Package

This folder is a self-contained design handoff for building the system in a
new repository. Read documents in order; each assumes the decisions made in
the previous ones.

**This is a worked example, not just a store.** The point of building this
project is to show, concretely and end-to-end, how event sourcing, complex
business event streaming, and CQRS fit together — not just to describe
each in isolation. `01`–`08` are the event-sourced **write side**: an
append-only store of record, a schema registry, and the publish/follow/
lineage APIs a real business event stream needs (filtering, causal
chains/lineage, security, masking). `09` and `features/cqrs-projections.md`
are the **CQRS read side**: a generic projection framework, fed exclusively through
the write side's own public Follow API, that turns events carrying either
**full state replacements** or **partial deltas** (`ChangeKind`, `ADR-016`)
into query-optimized read models — with a worked example (an `Orders`
domain) carried through both sides so the seam between them, not just each
half separately, is something you can actually read and follow.

**Governing principle: never lose or corrupt data, as close to absolutely
as the design can make it.** Every trade-off in this package defaults
toward that — persist first and flag problems as advisory metadata rather
than rejecting (`ADR-023`), never mutate or delete a stored event
(`ADR-009`'s closing note), detect tampering rather than merely hope for
its absence (`ADR-019`), detect late/out-of-order arrival rather than
silently let it corrupt already-applied data (`ADR-029`). Where a genuine
throughput need forces a lighter durability bar (`ADR-031`'s streaming
channels), that's stated as an explicit, narrow exception with its own
reasoning — never a silent default.

## What this system is

> **The two bullets below describing OData `$filter` pushdown and the
> `QUERY /events/{id}/...` Lineage API are the pre-`ADR-037` surface.**
> GraphQL Query/Mutation/Subscription has since replaced both entirely —
> see "Integration complete" below and `ADR-037`. Left as-is here rather
> than rewritten, consistent with `03-api-contracts.md`/`04-odata-filter-
> pushdown.md`'s own superseded-surface banners — the underlying
> mechanisms this section describes (schema-declared filterable fields,
> per-node lineage visibility) are unchanged, only the query syntax and
> transport are not.

An event-sourcing store with:

- A **JSON Schema registry** for registering named, versioned event types.
- A **publish API**, one logical operation per event type
  (`POST /publish/{event-type}`), which validates the payload against the
  registered schema before appending it to the store. An optional
  client-supplied `eventId` makes retries safe: the same `eventId` with
  identical content replays the original response with no new write; the
  same `eventId` with different content is a `409` (`ADR-011`).
- A **follow API** over Server-Sent Events (SSE)
  (`QUERY /follow/{event-type}` — the HTTP `QUERY` method, `ADR-012`, not
  `GET`) that streams matching events as
  they are appended, filtered using an OData `$filter` expression that is
  **pushed down to the database**, not evaluated in memory. A `mode`
  parameter (`ADR-010`) chooses **tail** (default — new events only) or
  **replay** (matching history first, then tail with no gap or
  duplicate), optionally starting from a given `fromSequenceNumber`.
- Auto-generated **OpenAPI** (publish side) and **AsyncAPI** (follow side)
  documents, both referencing the same JSON Schema definitions stored in the
  registry — no schema is ever duplicated across the two spec formats.
- Persistence via **EF Core**, with **SQLite, PostgreSQL, and SQL Server** as
  first-class providers — chosen at **build/deployment time**, one
  artifact per provider, not a runtime config switch (`ADR-001`).
- **Event lineage**: any published event may declare one or more **parent
  events**, of any event type, that it is causally derived from, forming a
  DAG across the store. Parent existence is validated per event type
  (`Strict` or `Permissive`, see `02-data-model.md` and `ADR-005`). The DAG
  is queryable via a dedicated, paginated Lineage API
  (`QUERY /events/{id}/parents|children|ancestors|descendants` — the HTTP
  `QUERY` method, `ADR-012`, not `GET` — see `03-api-contracts.md`).
- **OAuth2/OIDC bearer-token auth** on every endpoint except the public spec
  documents, scope-checked per operation, and **DPoP-bound (RFC 9449)**
  rather than plain bearer — every token is cryptographically tied to the
  client's own key, so a leaked token alone isn't enough to use it
  (`ADR-017`). For local dev/this POC, an in-process **OpenIddict** token
  issuer (`EventStore.DevIdp`, no separate container) plus a **.NET
  Aspire** AppHost (with a `docker-compose.yml` fallback) orchestrate the
  store, its database, and the dev IdP together — see `03-api-contracts.md`
  and `ADR-006`.
- **Event upcasting**: every event type's schema can evolve across
  versions; an `IEventUpcaster` chain reshapes an old-version payload to
  the current version's shape at read time — for Follow, and for CQRS
  projections — so a `mode=replay` burst spanning years of schema changes
  still presents one consistent shape to a consumer. `Payload` itself is
  never rewritten. See `ADR-018`.
- **Hash-chained tamper evidence**: every stored event chains its content
  hash onto the one before it, so altering any past event — even directly
  in the database, bypassing the application entirely — is detectable by
  replaying the chain, not just trusting the store. See `ADR-019`.
- **Event-type security**: a second, independent authorization dimension on
  top of scopes — each event type can optionally require a claim to publish
  it and a *different* claim to read it (`RequiredPublishClaim`/
  `RequiredReadClaim`), set at registration time. Lineage visibility is
  per node, not per request — "you can only see what you can see": the
  root event a Lineage call names must be visible or the whole call is
  rejected (`403`), but every node it *discovers* is checked
  independently, coming back as a `restricted: true` stub rather than
  failing the rest of the response. See `02-data-model.md`,
  `03-api-contracts.md`, and `ADR-008`.
- **CQRS read-model projections**: a generic `IProjection<TReadModel>` +
  `ProjectionHost` framework that materializes query-optimized read models
  by consuming the store's own public Follow API — never a private,
  store-internal hook — so a projection sees exactly the same contract any
  external consumer does (`ADR-015`). Every event type declares a required
  `ChangeKind` (`Full` | `Partial`, `ADR-016`) at registration: a `Full`
  event replaces everything known about its key; a `Partial` event
  merges only the fields it carries, leaving the rest untouched — the same
  "never overlay a missing/masked field" rule masking's consumers already
  follow, applied here by the framework itself, once, centrally, so no
  projection reimplements it. A full rebuild is just replaying from
  sequence `0` again — not a separate mechanism. See
  `09-cqrs-read-models.md`, `features/cqrs-projections.md`, and
  `ADR-015`/`ADR-016`.

## Integration complete: a second design, fully absorbed

A larger, independently-developed design — a distributed, entity-centric
event-sourced platform (multi-origin replication, sharding, GraphQL,
non-authoritative capture, an MVVM client) — was merged into this
package via `ADR-021` through `ADR-039` (see `CLAUDE.md`'s "Integration
status"). Every decision it raised now has a real, Accepted ADR here:
entities are a first-class concept (`ADR-021`), partial patches are
property-level `Optional<T>` (`ADR-022`), publish is persist-everything
(`ADR-023`), optimistic concurrency + conflict flagging (`ADR-024`),
multi-tenancy (`ADR-030`), streaming channels and binary attachments
(`ADR-031`/`ADR-032`), gossip-topology replication and entity-type
sharding (`ADR-033`/`ADR-034`), non-authoritative capture (`ADR-035`),
DID/UCAN self-attestation (`ADR-036`), GraphQL replacing OData entirely
(`ADR-037`), and the MVVM client (`ADR-039`). The source design package
this merge drew from (`docs/design-docs/`) has been removed now that
every decision it raised is fully captured in its own ADR — it was an
imported reference for this integration, not a permanent part of this
design package. A `docs/design-docs/NN §X.Y`-style citation surviving
elsewhere in these docs is a provenance pointer to that now-absorbed
source, not a link to a file that still exists.

## What this system deliberately is not (v1 scope)

- ~~Not a distributed/clustered event broker (single logical store to
  start).~~ **Superseded in direction, per `ADR-033`/`ADR-034`**: the
  second design's replication/sharding model was adopted, not
  rejected — see above.
- Not attempting arbitrary filtering on unindexed JSON fields — filtering is
  restricted to fields explicitly marked filterable/indexed at schema
  registration time (see `04-odata-filter-pushdown.md`).
- Not supporting OData filtering *inside* JSON arrays (`any`/`all`) in v1.
- Not exposing parent/lineage relationships through `$filter` — lineage is
  queried through the Lineage API, not the follow API's filter pushdown.
- Not guaranteeing the parent DAG is cycle-free for event types registered
  with `Permissive` parent validation — see `ADR-005`.
- Joins/projections across event types (OData `$expand`/`$select`-like
  behavior), specifically **derived/materialized event types** produced by
  server-side joins over other streams, are a deferred, secondary feature
  set — build after the primary system is working. The design is captured
  in `ADR-007` so it isn't lost; nothing in v1 blocks it (see ADR-007's
  consequences for why).
- **Property-level masking** is designed but not built alongside the
  primary system — a caller lacking a field-specific claim would receive
  `{"masked": "***"}` instead of `{"value": ...}` for that field (any
  scalar field, including required ones — see `ADR-009`), but this is a
  deliberate priority call (`08-build-plan.md`, Phase 8), not an unresolved
  design question like derived event types above. Richer masking-content
  strategies than the fixed placeholder (`PartialReveal`/`Hash`) are a
  further, undecided proposal on top of that.
- ~~No deletion/erasure of stored data, ever — not even for regulated
  fields. This was raised and explicitly settled, not overlooked: masking
  is the only redaction mechanism this system has, and it's a read-time
  presentation transform, never a storage-layer change. See `ADR-009`.~~
  **Superseded by `ADR-057`**: erasure is a real requirement, solved via
  crypto-shredding — a regulated field's *value* is encrypted before it's
  first stored, keyed per entity; "erasing" an entity destroys that key
  rather than ever deleting or rewriting a stored event. `StoredEvent`
  itself is still never mutated — see `ADR-057`.

## Document index

| File | Contents |
|---|---|
| `01-c4-architecture.md` | C4 context, container, and component diagrams (PlantUML) |
| `02-data-model.md` | EF Core entities, DbContext, provider-specific notes |
| `03-api-contracts.md` | OpenAPI generation (publish, lineage) and AsyncAPI generation (follow) |
| `04-odata-filter-pushdown.md` | JSON path pushdown design across SQLite/Postgres/SQL Server |
| `05-schema-registry-and-spec-generation.md` | Schema registration lifecycle, validation, spec regeneration |
| `06-solution-structure.md` | .NET solution/project layout, DI wiring, migrations strategy |
| `07-adrs.md` | ADR template + index — the ADRs themselves live one per file under `adrs/` |
| `08-build-plan.md` | Implementation phases, dependencies between them, and exit criteria (tied to `features/*.md` scenarios) |
| `09-cqrs-read-models.md` | CQRS read side: `IProjection<TReadModel>`/`ProjectionHost`, checkpointing, `ChangeKind`-driven snapshot merge, rebuild — the write/read seam this project exists to demonstrate |
| `10-open-questions.md` | Live tracker of genuinely unresolved forks/decisions — distinct from an ADR (already decided) or a comparison (weighed, awaiting a decision already in progress) |
| `references.md` | Bibliography: every real-world RFC/standard/pattern/library this design adopts, plus ones considered and deliberately not adopted, with why |
| `features/*.md` | One standalone doc per feature: context, PlantUML sequence diagrams, an ER diagram for features touching persistent data, a Salt UI mockup where a real UI surface exists, and the embedded Gherkin scenarios for that feature (`features/cqrs-projections.md` is the worked Orders example tying `09` together end-to-end) |
| `docs/data/*.md` | Entity classes, grouped one file per classification group — schema registry, event log, entity store, DbContext/conventions; `02-data-model.md` is the classification overview + index only |
| `docs/adrs/*.md` | Architecture Decision Records, one per file; `07-adrs.md` is the template + index only |
| `docs/patterns/*.md` | General pattern reference — what a pattern is, who named it, then how this design applies it; `patterns/README.md` is the catalog |
| `docs/comparisons/*.md` | Full pros/cons for a genuine multi-option fork, written before the deciding ADR; `comparisons/README.md` is the catalog |
| `docs/libraries/{platform}/*.md` | One file per adopted off-the-shelf library/framework — what it's for, general usage; `libraries/README.md` is the catalog |
| `docs/extensibility-points.md` | Consolidated catalog of every plugin/extension seam a hosting team can customize without forking core code, and the shared registration model (`ADR-059`) they all follow |

## Open decisions flagged for the implementer

**See `docs/10-open-questions.md` for the current, live list** — a
handful of genuinely unresolved forks (scope-to-`AppId` granularity,
the MVVM client's template engine, streaming-channel redaction
mechanics, which CEL library to adopt) surfaced during the `ADR-021`–
`041` integration and are tracked there rather than here, so they don't
get lost as buried sentences in an ADR's Consequences section. Below is
this project's original v1-scope framing, kept for history: every
question surfaced during that first design pass was resolved and
recorded as an ADR (`07-adrs.md`'s index, ADRs live one per file under
`adrs/`) — including unindexed-field filtering (reject
outright, `ADR-003`), dev-mode auth/orchestration (an in-process
OpenIddict host + .NET Aspire, `ADR-006`; the production IdP remains a
separate, later decision, out of scope for this POC), and
schema-compatibility enforcement (`ADR-020`: every real publish against a
lagging `schemaVersion` runs live through `ADR-018`'s
`upcastFromPrevious` `compute()` chain against the caller's real payload,
producing a reserved `EventUpcastFailed` event on failure instead of
silently accepting broken data). A hop nobody has ever actually published
a lagging event against is deliberately left unvalidated ahead of time —
`ADR-020` records this as a considered, settled choice, not a gap: if it's
never exercised it has no observable effect, and if it ever is, the first
real attempt discovers the outcome immediately via the same
`EventUpcastFailed` path. No proactive, synthetic-data check was wanted.

`ADR-007` (derived/materialized event types) is now fully designed —
pending-join TTL, derivation-definition cycle detection (registration-time
graph walk, plus a configured max-hop runtime safety net for the residual
race condition), n-ary `$from`/`$on` sources, and backfill-through-a-
derived-source are all resolved in the ADR itself. It remains
**deferred**, not undecided — a pure scheduling choice, the same as
`ADR-009`'s closing note explains for masking:

- **Derived/materialized event types** (server-side joins/projections
  across streams) — secondary feature set, design captured in `ADR-007`,
  build after the primary system works.
- **Masking-content strategies beyond the fixed placeholder** (e.g.
  `PartialReveal`/`Hash` instead of always `{"masked": "***"}`) — the
  fixed-placeholder mechanism itself is settled (`ADR-009`); only *richer*
  strategies on top of it remain a future proposal.

`09-cqrs-read-models.md`'s `ProjectionHost` checkpoint-advance granularity
(per-event vs. per-batch) is also resolved: it's a configurable
`batchSize`, safe at any size because `SnapshotMerger`'s merge operations
are idempotent (`ADR-016`) — reprocessing after a crash redoes work, it
never corrupts state.
