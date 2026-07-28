# Open Event Sourcing Store — Design Package

This folder is a self-contained design handoff for building the system in a
new repository. Read documents in order; each assumes the decisions made in
the previous ones.

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
  (`GET /follow/{event-type}?$filter=...`) that streams matching events as
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
  is queryable via a dedicated Lineage API
  (`GET /events/{id}/parents|children|ancestors|descendants`, see
  `03-api-contracts.md`).
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
| `features/*.md` | One standalone doc per feature: context, PlantUML sequence diagrams, an ER diagram for features touching persistent data, a Salt UI mockup where a real UI surface exists, and the embedded Gherkin scenarios for that feature |

## Open decisions flagged for the implementer

These were surfaced during design and are **not yet finalized** — pick and
record the decision as an ADR when you make it (template in `07-adrs.md`):

1. Whether unindexed-field filtering should be rejected outright (400) or
   silently degrade to a full scan — current recommendation is **reject**.

Dev-mode auth provider and orchestration (`ADR-006`, an in-process
OpenIddict host + .NET Aspire) is confirmed, not open — the production IdP
remains a separate, later decision (out of scope for this POC).
