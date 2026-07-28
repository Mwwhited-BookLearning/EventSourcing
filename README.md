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

## What this system is

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
  documents, scope-checked per operation. For local dev/this POC, an
  in-process **OpenIddict** token issuer (`EventStore.DevIdp`, no separate
  container) plus a **.NET Aspire** AppHost (with a `docker-compose.yml`
  fallback) orchestrate the store, its database, and the dev IdP together —
  see `03-api-contracts.md` and `ADR-006`.
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

## What this system deliberately is not (v1 scope)

- Not a distributed/clustered event broker (single logical store to start).
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
- **No deletion/erasure of stored data, ever — not even for regulated
  fields.** This was raised and explicitly settled, not overlooked:
  masking is the only redaction mechanism this system has, and it's a
  read-time presentation transform, never a storage-layer change. See
  `ADR-009`.

## Document index

| File | Contents |
|---|---|
| `01-c4-architecture.md` | C4 context, container, and component diagrams (PlantUML) |
| `02-data-model.md` | EF Core entities, DbContext, provider-specific notes |
| `03-api-contracts.md` | OpenAPI generation (publish, lineage) and AsyncAPI generation (follow) |
| `04-odata-filter-pushdown.md` | JSON path pushdown design across SQLite/Postgres/SQL Server |
| `05-schema-registry-and-spec-generation.md` | Schema registration lifecycle, validation, spec regeneration |
| `06-solution-structure.md` | .NET solution/project layout, DI wiring, migrations strategy |
| `07-adrs.md` | Architecture Decision Records for the key choices made so far |
| `08-build-plan.md` | Implementation phases, dependencies between them, and exit criteria (tied to `features/*.md` scenarios) |
| `09-cqrs-read-models.md` | CQRS read side: `IProjection<TReadModel>`/`ProjectionHost`, checkpointing, `ChangeKind`-driven snapshot merge, rebuild — the write/read seam this project exists to demonstrate |
| `features/*.md` | One standalone doc per feature: context, PlantUML sequence diagrams, an ER diagram for features touching persistent data, a Salt UI mockup where a real UI surface exists, and the embedded Gherkin scenarios for that feature (`features/cqrs-projections.md` is the worked Orders example tying `09` together end-to-end) |

## Open decisions flagged for the implementer

None outstanding. Every question surfaced during design has been resolved
and recorded as an ADR in `07-adrs.md` — including unindexed-field
filtering (reject outright, `ADR-003`) and dev-mode auth/orchestration (an
in-process OpenIddict host + .NET Aspire, `ADR-006`; the production IdP
remains a separate, later decision, out of scope for this POC).

Two items are deliberately **deferred**, not undecided — see
`ADR-007` and `ADR-009`'s closing note for why each is safe to build later
without disturbing v1:

- **Derived/materialized event types** (server-side joins/projections
  across streams) — secondary feature set, design captured in `ADR-007`,
  build after the primary system works.
- **Masking-content strategies beyond the fixed placeholder** (e.g.
  `PartialReveal`/`Hash` instead of always `{"masked": "***"}`) — the
  fixed-placeholder mechanism itself is settled (`ADR-009`); only *richer*
  strategies on top of it remain a future proposal.
