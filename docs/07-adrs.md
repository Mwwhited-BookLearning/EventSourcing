# Architecture Decision Records

## ADR template

```
## ADR-NNN: <title>
Status: Proposed | Accepted | Superseded | Deferred
Context: <why this decision was needed>
Decision: <what was decided>
Consequences: <trade-offs accepted>
```

---

## ADR-001: Runtime-switched database provider (vs. per-deployment build)

Status: Proposed (needs confirmation)

Context: The store must run on SQLite, PostgreSQL, or SQL Server. Provider
selection could be a compile-time/per-deployment choice or a runtime
config switch.

Decision: Runtime config switch (`Database:Provider` in configuration),
single deployable artifact, with per-provider migrations assemblies
selected at startup based on the same config value.

Consequences: Simpler CI/CD (one artifact). Requires all three migration
histories to be kept in sync manually when the model changes (add a
migration to all three provider projects together). Startup logic must
correctly route to the matching migrations assembly.

---

## ADR-002: On-demand OpenAPI/AsyncAPI generation (vs. materialized cache)

Status: Proposed (needs confirmation)

Context: Spec documents must always reflect current registry state.
Generating on every request is simplest but has a cost; materializing on
registration requires invalidation logic.

Decision: Generate on demand, with a short (~60s) in-memory cache
invalidated on schema registration events. Revisit if event-type count
grows large enough that generation cost becomes measurable.

Consequences: No staleness bugs, minimal cache-invalidation surface.
Slight repeated generation cost under high spec-endpoint traffic — mitigate
with the short-lived cache rather than a full invalidation pipeline.

---

## ADR-003: Reject filtering on non-indexed/undeclared fields (vs. silent full scan)

Status: Accepted

Context: Filtering must be pushed to the database. Allowing `$filter` on
arbitrary JSON fields would silently degrade to full-scan behavior with a
per-row function call, with no visibility to the caller that this
happened.

Decision: `$filter` may only reference fields declared as
`FilterableField` at schema-registration time. Any other field reference
is rejected with `400 Bad Request` at parse time, before querying the
database.

Consequences: Callers must know which fields are filterable in advance
(discoverable via the registry and via AsyncAPI channel parameter
descriptions). Prevents silent performance cliffs. Requires the schema
registration workflow to include declaring filterable fields up front,
which is an extra step for whoever registers a schema.

---

## ADR-004: JSON payload/schema columns stored as portable text, not native JSON column types

Status: Accepted

Context: SQL Server's `json` type, Postgres's `jsonb`, and SQLite have
different native JSON storage representations; using any of them at the
EF model level would break provider portability of the shared model and
migrations.

Decision: `Payload` and `JsonSchema` are stored as plain text
(`TEXT`/`nvarchar(max)`/`text`). Native JSON *functions* (`json_extract`,
`->>`, `JSON_VALUE`) are still used at query time via the
`IJsonPathTranslator` abstraction — this is a query-translation concern,
not a column-type concern.

Consequences: No native JSON validation/indexing at the column-type level
from EF Core's perspective; indexing is instead achieved via
provider-specific expression indexes / computed columns applied
out-of-band by the Schema Registry Service (see `02-data-model.md`).

---

## ADR-005: Event parenting as an envelope-level DAG, validation mode per event type

Status: Accepted

Context: Consumers need to express that one event is causally parented off
one or more prior events, possibly of a different event type, forming
chains/DAGs across the store. This must not corrupt the JSON-Schema-validated
payload, and different event types have different tolerance for referencing
a parent that hasn't been published yet (e.g. out-of-order ingestion from an
upstream system vs. a strictly ordered internal workflow).

Decision:
- Parent links are envelope metadata (`parentEventIds` on publish), stored in
  a dedicated `EventParents` join table — never embedded in `Payload` or the
  registered JSON Schema.
- `parentEventIds` is optional; omitted/empty means an origin event with no
  parents.
- Parent-existence validation is configurable **per event type** via
  `EventTypeDefinition.ParentValidationMode` (`Strict` default | `Permissive`),
  set at schema registration time.
- Any event type may be listed as a parent of any other event type — chains
  are not restricted to same-type lineage.
- Read access to the DAG is via a dedicated Lineage API
  (`GET /events/{id}/parents|children|ancestors|descendants`), not via
  `$filter` on the follow API.

Consequences:
- `Strict` mode plus the append-only, monotonically increasing
  `SequenceNumber` together guarantee the parent graph is acyclic by
  construction (a parent must already exist, hence have a lower
  `SequenceNumber`, before a child can reference it).
- `Permissive` mode gives up that guarantee: two Permissive-mode events can
  reference each other (A published parented off not-yet-existing X; X later
  published parented off A, which passes because A already exists by then),
  forming a 2-cycle. Ancestors/descendants traversal must therefore be
  cycle-safe **unconditionally**, not just for event types configured
  Permissive — a Strict-mode event can still have a Permissive-mode ancestor
  somewhere in its chain.
- `EventParents.ParentEventId` cannot carry a real database foreign-key
  constraint (it must tolerate dangling references for Permissive event
  types), so Strict-mode existence checks are enforced in the application
  layer (`ParentLinkService`) at publish time, not the database schema.
- Ancestors/descendants require provider-specific raw SQL (recursive CTEs);
  EF Core's LINQ provider has no recursive-query translation, so this is the
  one query path in the store that can't be a pure `IQueryable` like the
  rest.

---

## ADR-006: Dev-mode OAuth2/OIDC bearer-token auth via Keycloak, orchestrated with .NET Aspire

Status: Proposed (needs confirmation)

Context: All four API surfaces (Publish, Follow, Lineage, Registry) are
currently unauthenticated. The three system actors (Publishing System,
Consuming System, Platform Operator) are automated services, not
interactive users, so machine-to-machine token acquisition is the natural
fit rather than an interactive login flow. For local development and this
POC, standing up a real OIDC provider by hand is pure overhead; a
containerized dev IdP plus a multi-service local orchestrator removes that
friction without inventing a bespoke auth mechanism that would need to be
thrown away later.

Decision:
- Authentication: OAuth2 **Client Credentials** grant against an OIDC
  provider; every API request carries `Authorization: Bearer <JWT>`. APIs
  validate the token via standard JWT-bearer middleware against the
  provider's OIDC discovery document (`Authentication:Authority` config
  value) — no custom token-validation code.
- Dev/POC provider: **Keycloak** in `start-dev` mode, running as a
  container, with one realm (`event-store`) and one client per actor
  (`publisher-client`, `follower-client`, `operator-client`), each granted a
  distinct `scope`. This is a dev-only choice — pointing `Authority` at a
  production IdP (Entra ID, Auth0, a production Keycloak, etc.) requires no
  code change, only configuration, since validation is generic OIDC.
- Authorization: one policy per required scope
  (`events:publish`, `events:follow`, `events:lineage:read`,
  `registry:admin`), mapped 1:1 to the endpoints in `03-api-contracts.md`.
  `/openapi.json` and `/asyncapi.json` remain anonymous — they expose
  contract shape only, never event data.
- Local multi-service orchestration: a new `EventStore.AppHost` (.NET
  Aspire) project wires `EventStore.Host` together with a database
  container (Postgres or SQL Server, matching `Database:Provider`) and the
  Keycloak dev container, injecting connection strings and the OIDC
  `Authority` via Aspire service discovery. A `docker-compose.yml` at the
  repo root provides an equivalent path for tooling that doesn't run the
  Aspire CLI (e.g. CI).

Consequences:
- No user-interactive login flow is implemented or needed for v1 — all
  three actors use `client_credentials`, keeping the auth surface small.
- Scope-based authorization needs a custom `IAuthorizationHandler`
  (`ScopeRequirement`) rather than a bare `RequireClaim`, since OAuth2
  `scope` is a single space-delimited string claim, not a repeated claim —
  a naive `RequireClaim` check silently fails to match a token carrying
  multiple scopes.
- The browser `EventSource` API cannot set an `Authorization` header, so the
  Follow API must additionally accept the bearer token via an
  `access_token` query-string parameter for browser-based followers.
  Query-string tokens are more prone to leaking via server/proxy logs than
  header-based ones — mitigated with short-lived tokens, since there is no
  header-based alternative for a real `EventSource` client.
- The Keycloak realm/client/scope setup must be captured as a committed
  realm-export JSON file so `docker-compose up` or `aspire run` produces a
  working dev IdP with zero manual admin-console setup.
- Aspire changes *how the process is launched and wired* (connection
  strings, `Authority`, service discovery) — it does not change the
  per-provider migrations-assembly startup logic from `ADR-001`, which still
  runs exactly as before inside `EventStore.Host`.

---

## ADR-007: Derived/materialized event types via cross-stream join+projection

Status: Deferred — captured for design continuity, not part of v1. Build
after the primary system (publish/follow/registry/lineage/auth) is working.

Context: There's a real want for **derived event types**: an event type
whose instances are produced not by an external publisher, but by a
server-side process that tails one or more existing source event streams,
joins them by key, projects a subset of fields, and publishes the result as
a new event type — e.g. `OrderPlaced` + `PaymentReceived` joined on
`OrderId` produces `OrderPaid`. This is materially more complex than the
rest of v1 (unbounded join-state, emission-trigger semantics, checkpointing,
backfill correctness) and is explicitly sequenced as a secondary feature
set, built once the primary system is in place. Recorded here so the shape
of the idea and the decisions already made about it aren't lost, and so
future v1 work doesn't accidentally foreclose it.

Decision (captured now, not implemented now):
- Registration shape: something like
  `POST /create/{event-type}?$from=A,B&$on=A/OrderId eq B/OrderId&$select=...`
  — this registers a **derivation definition**, analogous to
  `PUT /registry/{event-type}`, except the JSON Schema for the new event
  type is auto-composed from `$select` against the source types' already-
  registered schemas, not hand-authored.
- `$on` is an explicit equality expression across named source fields (not
  a `StreamId`-convention join) — standard OData has no multi-resource join
  operator, so this is necessarily a hand-rolled, OData-*inspired* mini-
  grammar, not literal OData.
- The join/emit trigger — **fire-once inner join** (wait for one event per
  source per key, emit once, key closes) vs. **continuous latest-state
  enrichment** (any new arrival on any source re-emits, joined against the
  current latest state of the others) — is **configurable per derived event
  type** at registration time, not a single global choice.
- Backfill-from-history vs. from-now-only is likewise **configurable per
  derived event type** at registration time.
- Execution model: a background process per derivation, architecturally "an
  internal follower" — it tails each declared source stream the same way
  `EventTailReader` does for the Follow API (`04-odata-filter-pushdown.md`),
  then republishes through the same publish/append path used by external
  publishers.

Consequences / why this doesn't block v1, and what to remember meanwhile:
- **No v1 design change is required to accommodate this later.**
  `EventParents` (`ADR-005`) already provides exactly the right mechanism
  for a derived event to record its sources: a derived `OrderPaid` event
  would simply set `parentEventIds: [orderPlacedId, paymentReceivedId]` when
  published — no schema or data-model change needed. This is a genuine
  synergy between the two features, not a coincidence to re-verify later.
- `EventTypeDefinition` should not be assumed to always come from a
  hand-authored `PUT /registry/{event-type}` body — a future
  `DerivationDefinition` will programmatically register one through the same
  path. Nothing in `05-schema-registry-and-spec-generation.md` currently
  assumes otherwise; keep it that way.
- The derivation background process reuses the tailing/polling primitive
  the Follow API already needs (`EventTailReader`) rather than requiring a
  new persistence mechanism — a reason not to build that primitive in a way
  only the Follow API can reach.
- Derivation registration will most likely reuse the `registry:admin` scope
  (`ADR-006`) rather than inventing a new one — defining an event type is a
  single administrative capability whether the type is hand-authored or
  derived.
- Real open questions deliberately left unresolved here (to be settled when
  this is actually built, not now): what happens to a fire-once join whose
  key never completes on all sides (unbounded pending state, optional TTL?);
  whether `$select` projections can reference more than two sources at once
  or must be expressed as chained pairwise derivations; and how backfill
  interacts with a source stream that is itself a derived event type.
