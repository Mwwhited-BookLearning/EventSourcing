# Open Event Sourcing Store — Design Package

This folder is a self-contained design handoff for building the system in a
new repository. Read documents in order; each assumes the decisions made in
the previous ones.

## What this system is

An event-sourcing store with:

- A **JSON Schema registry** for registering named, versioned event types.
- A **publish API**, one logical operation per event type
  (`POST /publish/{event-type}`), which validates the payload against the
  registered schema before appending it to the store.
- A **follow API** over Server-Sent Events (SSE)
  (`GET /follow/{event-type}?$filter=...`) that streams matching events as
  they are appended, filtered using an OData `$filter` expression that is
  **pushed down to the database**, not evaluated in memory.
- Auto-generated **OpenAPI** (publish side) and **AsyncAPI** (follow side)
  documents, both referencing the same JSON Schema definitions stored in the
  registry — no schema is ever duplicated across the two spec formats.
- Persistence via **EF Core**, with **SQLite, PostgreSQL, and SQL Server** as
  first-class, interchangeable providers selected at runtime via
  configuration.

## What this system deliberately is not (v1 scope)

- Not a distributed/clustered event broker (single logical store to start).
- Not attempting arbitrary filtering on unindexed JSON fields — filtering is
  restricted to fields explicitly marked filterable/indexed at schema
  registration time (see `04-odata-filter-pushdown.md`).
- Not supporting OData filtering *inside* JSON arrays (`any`/`all`) in v1.
- Joins/projections across event types (OData `$expand`/`$select`-like
  behavior) are a v2 concern; the follow API and data model should not block
  it, but it is not implemented in v1.

## Document index

| File | Contents |
|---|---|
| `01-c4-architecture.md` | C4 context, container, and component diagrams (PlantUML) |
| `02-data-model.md` | EF Core entities, DbContext, provider-specific notes |
| `03-api-contracts.md` | OpenAPI generation (publish) and AsyncAPI generation (follow) |
| `04-odata-filter-pushdown.md` | JSON path pushdown design across SQLite/Postgres/SQL Server |
| `05-schema-registry-and-spec-generation.md` | Schema registration lifecycle, validation, spec regeneration |
| `06-solution-structure.md` | .NET solution/project layout, DI wiring, migrations strategy |
| `07-adrs.md` | Architecture Decision Records for the key choices made so far |
| `features/*.feature` | Gherkin BDD scenarios for the core behaviors |

## Open decisions flagged for the implementer

These were surfaced during design and are **not yet finalized** — pick and
record the decision as an ADR when you make it (template in `07-adrs.md`):

1. Provider selection: runtime config switch vs. per-deployment build.
2. OpenAPI/AsyncAPI documents: generated on-demand per request vs.
   materialized and cached on schema registration.
3. Whether unindexed-field filtering should be rejected outright (400) or
   silently degrade to a full scan — current recommendation is **reject**.
