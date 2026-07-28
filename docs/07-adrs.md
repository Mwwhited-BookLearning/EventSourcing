# Architecture Decision Records

## ADR template

```
## ADR-NNN: <title>
Status: Proposed | Accepted | Superseded | Deferred
Context: <why this decision was needed>
Decision: <what was decided>
Consequences: <trade-offs accepted>
```

Every ADR below cites the real-world RFC/standard/library it's grounded
in inline, where relevant — there's no separate "Suggested References"
section for this file, since one per-ADR would just duplicate what's
already cited in that ADR's own text. `references.md` is the consolidated
index across every ADR here plus every other numbered doc.

---

## ADR-001: Per-deployment database provider build (vs. runtime switch)

Status: Accepted

Context: The store must run on SQLite, PostgreSQL, or SQL Server. Provider
selection could be a compile-time/per-deployment choice or a runtime
config switch.

Decision: **Per-deployment build.** Exactly one provider is chosen at
build/publish time, not read from configuration at startup. Three thin
composition-root projects — `EventStore.Host.Sqlite`,
`EventStore.Host.Postgres`, `EventStore.Host.SqlServer` — each hardcode
their one provider's `UseSqlite`/`UseNpgsql`/`UseSqlServer` call, register
that provider's `IJsonPathTranslator`/`IEventLineageQueryProvider`
implementations unconditionally (no `switch`), and reference exactly that
provider's migrations assembly directly. All three share the same
provider-agnostic setup (DI for everything else, endpoint mapping) via
`EventStore.Host.Core` — see `06-solution-structure.md`. There is no
`Database:Provider` configuration value anywhere in this design; it's
superseded by "which of the three projects did you build."

Consequences: CI/CD must build and publish **three** artifacts instead of
one — more pipeline complexity than the runtime-switch alternative. In
exchange, startup has zero provider-branching logic and zero risk of a
misconfigured `Database:Provider` value routing to the wrong migrations
assembly at runtime — the three `switch` statements the runtime-switch
design needed (DbContext options, `IJsonPathTranslator`,
`IEventLineageQueryProvider`, migrations assembly) all collapse to a
single unconditional registration per project, because each project only
ever runs against one provider. Moving a running deployment to a different
provider means redeploying a different artifact, not flipping a config
value — an explicit, accepted trade against the runtime-switch design's
main convenience. Still requires all three migration histories to be kept
in sync manually when the model changes (unchanged from the runtime-switch
alternative — this risk is about EF Core migrations not being portable
across providers, not about how the provider is selected).

---

## ADR-002: On-demand OpenAPI/AsyncAPI generation (vs. materialized cache)

Status: Accepted

Context: Spec documents must always reflect current registry state.
Generating on every request is simplest but has a cost; materializing on
registration requires invalidation logic. Decisively in on-demand's favor:
schemas can be **registered live**, at any time, without a redeploy — a
build-time-generated spec would go stale the instant a new event type or
schema version is registered, defeating the entire point of a live
registry. Materializing-on-registration (rebuild eagerly, serve the cached
result until the next registration) was the only real alternative to
generating fresh per-request, and it still needs the same invalidation
hook this design already has (`05-schema-registry-and-spec-generation.md`,
registration step 10) — it just moves the rebuild earlier for no real
benefit at this scale, so it wasn't worth the extra complexity.

Decision: Generate on demand, with a short (~60s) in-memory cache
invalidated on schema registration events. Revisit if event-type count
grows large enough that generation cost becomes measurable.

**Build mechanism** (the "how," decided alongside the "when" above):

- **One shared schema representation for both specs.** AsyncAPI 3.0
  deliberately reuses the OpenAPI Schema Object dialect, so each
  `EventTypeDefinition.JsonSchema` is parsed exactly once, by
  `EventSchemaConverter`, into a `Microsoft.OpenApi.Models.OpenApiSchema` —
  the official .NET OpenAPI object model, which already understands JSON
  Schema 2020-12 (per OpenAPI 3.1's alignment with it, already noted
  above) and carries unrecognized keywords — including custom vendor
  extensions like `x-masking` — through its `Extensions` dictionary rather
  than dropping them. `OpenApiDocumentBuilder` and `AsyncApiDocumentBuilder`
  both consume this same `OpenApiSchema`, not two independent
  representations.
- **`OpenApiDocumentBuilder`** builds a native `Microsoft.OpenApi`
  `OpenApiDocument` (paths, security schemes, info) using the library's own
  object model end to end, embedding each event type's **unwrapped**
  `OpenApiSchema` (masking's wrapper is never applied on the publish side —
  see `ADR-009`) directly in `Components.Schemas`, and serializes it via
  the library's own `SerializeAsV31` writer. No hand-rolled JSON here —
  OpenAPI is exactly what this library is for.
- **`AsyncApiDocumentBuilder`** has no equivalent library to lean on — .NET
  has no actively-maintained AsyncAPI object model that fits
  runtime-registry-driven generation (the closest, Saunter, is
  attribute/reflection-driven from compile-time C# types, not a schema
  registry). Its channels/messages/operations/components envelope is
  hand-built as a `System.Text.Json.Nodes.JsonObject` tree, embedding each
  event type's schema by serializing the **same** `OpenApiSchema` (now
  passed through `MaskingSchemaTransformer` first — see below) via
  `Microsoft.OpenApi`'s writer and splicing the result into
  `components.schemas`.
- **`MaskingSchemaTransformer`** (schema-level, not data-level — distinct
  from `IPayloadMasker` in `ADR-009`) walks an `OpenApiSchema` recursively
  and, wherever it finds an `x-masking` extension, rewrites that node into
  the `oneOf: [{value: original}, {masked: string}]` wrapper. It is a pure
  function of the schema alone (the wire *shape* is uniform for every
  caller per `ADR-009`, so there is no claims parameter here) and runs once
  per document build, not per caller, not per event. It must exist as soon
  as `AsyncApiDocumentBuilder` does — i.e. from the same phase AsyncAPI
  generation is built, not deferred alongside masking's runtime
  enforcement (`IPayloadMasker`, still deprioritized — see
  `08-build-plan.md`, Phases 4 and 8). The two transforms should share one
  underlying "find every `x-masking` node" tree-walk helper so the
  recursion rule (scalar node / scalar array `items` / property nested
  inside complex-object `items`) is implemented once, not twice with a
  risk of drifting.
- **Validation safety net for the hand-rolled half**: because the AsyncAPI
  envelope has no compiler checking its structure the way
  `Microsoft.OpenApi`'s typed model does for OpenAPI, a test parses each
  generated `asyncapi.json` back against the published AsyncAPI 3.0 JSON
  Schema, catching structural mistakes that a type system can't here.

Consequences: No staleness bugs (within a single instance — see below),
minimal cache-invalidation surface. Slight repeated generation cost under
high spec-endpoint traffic — mitigate with the short-lived cache rather
than a full invalidation pipeline. A single shared `OpenApiSchema`
representation means custom keywords JSON Schema 2020-12 supports but
OpenAPI's dialect doesn't fully model are a residual fidelity risk on
parse — worth a round-trip unit test with an unusual keyword, not assumed
safe. The 60s in-memory cache is per-instance; if a given
`EventStore.Host.<Provider>` deployment is ever scaled to multiple
instances, a registration on one instance does not
invalidate another's cache — bounded by the same 60s TTL either way, so
still "no staleness bugs" *up to* that bound, just not synchronously
consistent across instances. Revisit with a distributed cache if that
staleness window ever matters.

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
  (`/events/{id}/parents|children|ancestors|descendants`, later `QUERY`
  per `ADR-012`), not via `$filter` on the follow API.

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

## ADR-006: Dev-mode OAuth2/OIDC bearer-token auth via an in-process OpenIddict host, orchestrated with .NET Aspire

Status: Accepted — OpenIddict confirmed as the dev/POC provider.

Context: All four API surfaces (Publish, Follow, Lineage, Registry) are
currently unauthenticated. The three system actors (Publishing System,
Consuming System, Platform Operator) are automated services, not
interactive users, so machine-to-machine token acquisition is the natural
fit rather than an interactive login flow. For local development and this
POC, standing up a real OIDC provider by hand is pure overhead — but a full
IdP like Keycloak (JVM, admin console, realm database) is more machinery
than a `client_credentials`-only, no-human-login POC actually needs.

Decision:
- Authentication: OAuth2 **Client Credentials** grant (RFC 6749 §4.4)
  against an OIDC provider; every API request carries `Authorization:
  Bearer <JWT>` per RFC 6750's Bearer Token Usage. APIs
  validate the token via standard JWT-bearer middleware against the
  provider's OIDC discovery document (`Authentication:Authority` config
  value) — no custom token-validation code.
- Dev/POC provider: **OpenIddict**, hosted in-process in a new, small
  ASP.NET Core project, `EventStore.DevIdp` — not a separate off-the-shelf
  container. It uses OpenIddict's EF Core **InMemory** store, seeded at
  startup by a few lines of C# with the three clients
  (`publisher-client`, `follower-client`, `operator-client`) and their
  scopes — no realm-export JSON, no admin console, no persistent identity
  database to provision. Token endpoint is `/connect/token`; the standard
  `/.well-known/openid-configuration` discovery document is exposed so
  every `EventStore.Host.<Provider>`'s shared JWT-bearer validation
  (`EventStore.Host.Core`) needs zero OpenIddict-specific code. This is a
  dev-only choice — pointing `Authority` at a production IdP
  (Entra ID, Auth0, Keycloak, Duende IdentityServer, etc.) requires no code
  change, only configuration, since validation is generic OIDC.
- Authorization: one policy per required scope
  (`events:publish`, `events:follow`, `events:lineage:read`,
  `registry:admin`), mapped 1:1 to the endpoints in `03-api-contracts.md`.
  `/openapi.json` and `/asyncapi.json` remain anonymous — they expose
  contract shape only, never event data.
- Local multi-service orchestration: a new `EventStore.AppHost` (.NET
  Aspire) project wires whichever single `EventStore.Host.<Provider>` it
  targets (per `ADR-001` — the AppHost picks one, there's no runtime
  `Database:Provider` switch) together with that provider's database
  container and `EventStore.DevIdp` — as an Aspire **project** resource
  (`AddProject<Projects.EventStore_DevIdp>`), not a container resource,
  since it's just another .NET project in the same solution — injecting
  connection strings and the OIDC `Authority` via Aspire service discovery.
  A `docker-compose.yml` at the repo root provides an equivalent path for
  tooling that doesn't run the Aspire CLI (e.g. CI); both the chosen
  `EventStore.Host.<Provider>` and `EventStore.DevIdp` are built as
  ordinary app images there, with no third-party image or volume-mounted
  config to manage.

Consequences:
- No user-interactive login flow is implemented or needed for v1 — all
  three actors use `client_credentials`, keeping the auth surface small.
- Scope-based authorization needs a custom `IAuthorizationHandler`
  (`ScopeRequirement`) rather than a bare `RequireClaim`, since OAuth2
  `scope` is a single space-delimited string claim, not a repeated claim —
  a naive `RequireClaim` check silently fails to match a token carrying
  multiple scopes.
- ~~The browser `EventSource` API cannot set an `Authorization` header, so
  the Follow API must additionally accept the bearer token via an
  `access_token` query-string parameter for browser-based followers.~~
  **Superseded by `ADR-012`**: Follow moved from `GET` to the HTTP `QUERY`
  method specifically for its OData query capabilities, which as a side
  effect rules out `EventSource` entirely (it can only issue `GET`) —
  browser clients now use `fetch()`, which sets a real header, so this
  workaround (and the query-string-token leakage risk it carried) no
  longer exists for Follow at all.
- **Plain bearer tokens are usable by anyone who possesses them (RFC
  6750's own stated risk)** — this ADR doesn't address that on its own;
  `ADR-017` hardens every token this identity provider issues into a
  DPoP-bound one (RFC 9449), closing that specific gap without changing
  anything about the grant type or client model decided here.
- Client/scope seeding lives in `EventStore.DevIdp`'s own startup code, not
  a committed realm-export file — simpler than Keycloak's JSON import, but
  means the seed data is C#, not declarative config; keep it in one place
  (a single `DevIdpSeeder` class) so it doesn't drift from
  `03-api-contracts.md`'s scope table.
- Using an EF Core InMemory store means `EventStore.DevIdp` has **no
  persistence** — every restart re-seeds from scratch. That's the right
  trade for a dev/POC token issuer (nothing about it should be treated as
  durable state) but would need revisiting (a real database) if this ever
  became more than throwaway dev infrastructure.
- No admin console exists to eyeball the seeded clients (unlike Keycloak) —
  verify the seed via the discovery document / a token request, not a UI.
- Aspire changes *how the process is launched and wired* (connection
  strings, `Authority`, service discovery) — it does not change the
  per-deployment provider build from `ADR-001`, which still determines the
  DbContext/migrations wiring exactly as described there, independent of
  whether Aspire or plain `docker run` launched the process.

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

---

## ADR-008: Event-type security via per-event-type required claims

Status: Accepted

Context: The four scopes from `ADR-006` (`events:publish`, `events:follow`,
`events:lineage:read`, `registry:admin`) answer "can this caller call this
*operation* at all" — they're static and identical for every event type.
There's a separate need: "may this caller touch *this specific event
type's* data at all," independently for publishing vs. reading, and
configurable per event type at registration time (e.g. a `PatientAdmitted`
event type might require a `clearance:phi` claim that most callers with
plain `events:publish`/`events:follow` scopes don't have).

Decision:
- `EventTypeDefinition` gains two optional fields,
  `RequiredPublishClaim`/`RequiredReadClaim` (`02-data-model.md`), each a
  single `"type:value"` claim string (e.g. `"clearance:secret"`) or `null`
  for no extra restriction. v1 supports exactly one required claim per
  direction — not an AND/OR set of claims.
- These are genuinely separate from each other, per explicit direction: a
  caller can be allowed to publish `PatientAdmitted` events without being
  allowed to read them back (or vice versa) — one claim does not imply the
  other.
- `RequiredPublishClaim` gates `POST /publish/{event-type}`.
  `RequiredReadClaim` gates `QUERY /follow/{event-type}` (`ADR-012`;
  checked once, at connect time) **and** all four Lineage API endpoints.
- **Visibility is per node, not per request: "you can only see what you can
  see."** For the Lineage API and the Follow envelope's `parentEventIds`
  alike, each event a response touches is checked independently against
  the caller's `RequiredReadClaim`. A node the caller can't see is
  **not** shown — not its `eventType`, `sequenceNumber`, `occurredAt`, or
  payload — but that does *not* fail the rest of the response: other
  nodes the caller *can* see are still returned. Lacking access to a
  parent never blocks access to a child the caller otherwise has rights
  to, and vice versa — the two are evaluated completely independently.
  `03-api-contracts.md`, "RequiredReadClaim and the Lineage API", has the
  concrete response shape.
  - The one exception is the **root** `{eventId}` a Lineage call names
    directly: that one must be visible to the caller or the whole request
    is rejected (`403`) — you cannot ask about the lineage of something
    you can't see at all. Everything the traversal *discovers* from there
    (parents, children, ancestors, descendants) is visibility-checked
    per node as above, not gated by the root's check.
  - Traversal does not recurse past a node the caller can't see, for the
    same reason it doesn't recurse past a `resolved: false` (Permissive
    dangling) node: nothing about what's beyond an invisible node is
    revealed either. Both are "leaves" to the caller, for related but
    distinct reasons — one because it doesn't exist yet, one because the
    caller isn't allowed to see it.
  - **This is also why publish never needed to check `RequiredReadClaim`
    on a referenced parent** (an earlier open question): read visibility
    is entirely a per-viewer, read-time decision, never baked in when the
    link is created. `ParentLinkService` (`ADR-005`) still only checks
    *existence*, regardless of who's publishing or who might later be
    unable to read that parent back.
- The check is enforced in application code after the event type is
  resolved from the registry, not as a static ASP.NET Core policy — see
  `06-solution-structure.md`. It requires the caller's claims to already be
  populated by JWT bearer auth (`ADR-006`), so it can't be enforced before
  that exists; see `08-build-plan.md`, Phase 6.
- Registering/changing these claims still only requires `registry:admin` —
  no new scope.

Consequences:
- Two independent knobs (publish vs. read) means an event type can be
  write-only-to-some, read-only-to-others, both, or neither — flexible, but
  it also means there are two places to get the claim wrong when
  registering a sensitive event type, not one.
- A caller who lacks `RequiredReadClaim` for the **root** event a Lineage
  call names directly, when that event **does** exist, gets `403`, not
  `404` — this deliberately leaks that *something* exists at that
  `eventId` (distinguishable from a truly unknown `eventId`, which is still
  `404`), rather than hiding existence the way returning `404` for both
  cases would. That's a conscious trade-off for consistency with how the
  scope-based `403`s already behave, not an oversight; revisit if this ever
  needs to defend against enumeration/existence-probing specifically. A
  node merely *discovered* during traversal, by contrast, is stubbed
  (`restricted: true`, per node, see above) rather than surfaced as a
  distinct status code — there's no equivalent "does this discovered node
  exist" question being asked directly, so there's nothing analogous to
  leak.
- The recursive CTE (`IEventLineageQueryProvider`,
  `06-solution-structure.md`) must enforce the stop-at-invisible-node rule
  *during* recursion, not just redact fields in the final output — a
  provider that fully expanded the graph and only masked the display would
  still silently reveal a restricted node's position and connectivity, the
  exact leak this design exists to prevent.
- Tightening a claim on a new schema version takes effect immediately for
  new requests, but does **not** retroactively affect an already-open
  Follow SSE connection (the check runs once at connect time) — a caller
  connected before the tightening keeps receiving events until they
  reconnect. If this window matters, closing live connections on a claim
  change would need to be a separate mechanism, not assumed to fall out of
  this design for free.
- This is a second, independent enforcement point that must be kept in sync
  with `ADR-006`'s scope checks conceptually (both must pass), but the two
  are implemented differently (static policy vs. per-request data lookup)
  — see `06-solution-structure.md` for why they can't share one mechanism.
- Property-level masking (`ADR-009`) is a finer-grained relative of this
  same idea, reusing the `"type:value"` claim string convention —
  intentionally, so the two features compose rather than inventing a second
  claim format later.

---

## ADR-009: Property-level masking via a value/masked wrapper, applied only to query and stream responses

Status: Design Accepted; **implementation is lower priority — build after
Phases 0–6 are working** (per the user's own sequencing call), not
alongside them. This is a different reason for coming later than `ADR-007`:
there are no unresolved technical questions here (unlike `ADR-007`'s
open questions about join semantics), the design below is complete — it's
purely a priority/sequencing decision, not a "not sure how to build this
yet" one. Depends on `ADR-008` existing first regardless — masking only
matters for callers who already cleared the event-type-level
`RequiredReadClaim` (or the type has none) and are now looking at
individual fields within an event they otherwise have base access to.

Context: `RequiredReadClaim` (`ADR-008`) is all-or-nothing for an event
type: a caller either sees the whole event or none of it. There's a
further want for **field-level** redaction: some properties within an
otherwise-visible event should be hidden from callers who lack a
finer-grained claim — e.g. an `OrderPlaced` event's `Amount` might be
visible to everyone with `RequiredReadClaim` for the type, but a
`CustomerTaxId` property on it might need its own `pii:view` claim, and be
hidden from everyone else.

**Explicitly settled alongside this: there is no erasure/deletion
mechanism, and none is wanted.** A regulated field (`regulatoryClassification`,
`ADR-009` below) that some caller must never see is handled entirely by
masking it at read time — the store persists it exactly as published,
forever, same as everything else (`ADR-004`, `ADR-005`'s append-only
design). If a real deletion requirement ever surfaces (e.g. a legal
erasure order for specific data), that is a deliberately unsolved,
separate problem — not something this design silently precludes, but not
something it builds for either, since it was asked for and confirmed not
needed here.

An earlier version of this ADR tried to solve
this by replacing the value with `null`, but that only works for
properties whose declared type already permits `null` — it doesn't "work
on all fields," which is a real requirement, not a nice-to-have.

Decision:
- Masking rules are declared per property, inside the registered
  `JsonSchema` document itself as a vendor extension:
  `"CustomerTaxId": { "type": "string", "x-masking": { "requiredClaim":
  "pii:view", "strategy": "FixedValue", "maskedValue": "***" } }` — not as
  a new column on `EventTypeDefinition` or `FilterableField`. This reuses
  `ADR-008`'s `"type:value"` required-claim string at a finer grain,
  deliberately, so the two features share one claim-checking primitive.
- **A maskable property's effective type, for any query or stream response
  (never for publish), becomes a wrapper**:
  `oneOf: [{type:"object", properties:{value: <the property's own
  declared type>}, required:["value"], additionalProperties:false},
  {type:"object", properties:{masked:{type:"string"}},
  required:["masked"], additionalProperties:false}]`. A caller who holds
  `requiredClaim` sees `{"value": <the real value>}`; a caller who doesn't
  sees `{"masked": "***"}` (or whatever `maskedValue` was configured,
  defaulting to `"***"`). **This is what resolves "works on all fields":**
  the wrapper is a new type at that JSON position, so it can hold any
  original type inside `value` while `masked` is always a plain string —
  there's no longer a constraint on the property's own declared type, and
  the earlier `null`-compatibility requirement is gone entirely.
- **The wrapper shape is uniform regardless of the caller's claims** — every
  caller sees the same `oneOf(value|masked)` structure; only which branch
  is populated differs. This keeps the wire contract stable and
  independently documentable in AsyncAPI (`03-api-contracts.md`), rather
  than a shape that structurally differs per caller.
- **v1 has exactly one content strategy, `"FixedValue"`** (a configured
  literal string, `maskedValue`, defaulting to `"***"`). Registering any
  other `strategy` value is rejected (`400`) — see "Future: definable
  masking strategies" below for what else this is expected to grow into.
- `x-masking` also carries three **optional, schema-only descriptive
  fields**: `regulatoryClassification` (e.g. `"PHI"`, `"PCI"`),
  `governanceBody` (e.g. `"HHS/OCR"`, `"PCI SSC"`), and
  `regulationReference` (e.g. `"HIPAA 45 CFR §164.514(b)"`). These carry no
  enforcement behavior whatsoever — `IPayloadMasker` never reads them, and
  they never appear inside the runtime `{value:...}`/`{masked:...}`
  wrapper. They exist purely so *why* a field is masked, and under what
  regulation, is captured once at the schema and discoverable via the
  registry and generated specs — not re-derived or documented separately
  from the thing it describes.
- **Recursion through arrays** — `x-masking` is a schema-node-level
  annotation, walkable anywhere in the schema tree, and the same wrapping
  rule applies wherever it's found:
  - On a scalar property (string/number/integer/boolean): wraps that
    property's value directly, as above.
  - On an array's `items` schema, when `items` is itself scalar: wraps
    **each element** of the array — the array itself stays a plain JSON
    array, just of wrapper objects instead of bare scalars.
  - On a property nested inside an array's `items` schema, when `items` is
    a complex object (multiple properties): wraps **only that property**,
    per array element — the rest of each object in the array is untouched,
    at whatever nesting depth the recursive walk reaches it.
  - `x-masking` is **not** valid directly on a property whose own declared
    type is `object` or `array` (masking a whole nested object or an
    entire array as one collapsed unit is out of scope for v1) —
    registration rejects (`400`) that placement. It's only valid on a
    scalar node, or on an array's `items` when that `items` schema is
    itself scalar.
- **Enforcement point**: any query or event-stream response that
  serializes `Payload` back to a caller — today that's exclusively the
  Follow SSE stream (`03-api-contracts.md`); the Lineage API never
  includes `Payload` at all, so it's unaffected. If a future direct
  "read event by id" endpoint is added, masking must apply there too, or
  it's a bypass. **Publish is never affected**: a publisher always sends,
  and the store always validates/persists, the plain unwrapped value —
  `StoredEvent.Payload` is never wrapped, mutated, or touched by this
  feature at all. The wrapper exists purely at the read/response
  boundary, computed fresh from the one authoritative stored `Payload` for
  whichever caller is asking.
- The claims used are fixed for the lifetime of one Follow connection
  (same JWT throughout), so the *set* of properties (and, per the
  recursive rule, array positions) a given connection will mask is
  computed once at connect time, alongside the `RequiredReadClaim` check;
  only "is this property present in *this* event's payload" varies per
  streamed event.
- **The transform itself is a pure function of the extended `JsonSchema`
  (the one with `x-masking` annotations) and the current payload data** —
  nothing else. It does not need a `ClaimsPrincipal`, `HttpContext`, or any
  I/O; claim-checking is a separate, injected `Func<string, bool> hasClaim`
  delegate, not something the transform resolves itself. That's what makes
  it usable as a lifecycle step — a small middleware or command-chain link
  — rather than logic embedded in `FollowEndpoint` specifically: whatever
  future endpoint also serializes `Payload` can drop the same step into its
  own pipeline with zero changes to the transform. See
  `06-solution-structure.md` for the concrete shape.

Consequences:
- This is a genuine improvement over the `null`-out approach it replaces:
  masking now works on **any** scalar-typed field, including required,
  non-nullable ones, with no constraint pushed back onto schema authors.
- It also incidentally fixes a problem the `null`-out approach had: a
  masked value and a legitimately absent/`null` one are no longer
  indistinguishable. A caller can always tell which branch of the wrapper
  is populated (`value` present vs. `masked` present) — no separate
  `maskedFields` signal is needed to know a field was masked.
- The wrapper changes the *shape* every consumer of a maskable field must
  code against — `{value:...}` / `{masked:...}` instead of the bare value
  — for every caller, not just restricted ones. That's a real integration
  cost for consumers of an event type with any maskable field, accepted in
  exchange for a uniform, always-documentable wire contract and universal
  type coverage.
- AsyncAPI must document the wrapped shape as the property's real wire
  type (`03-api-contracts.md`) — the *registered* schema (what
  `SchemaValidationService` validates publish payloads against) keeps the
  plain, unwrapped type; only the generated Follow-side/AsyncAPI view
  wraps it. These are now two different views of the same property's
  type, deliberately, and `AsyncApiDocumentBuilder` is responsible for the
  transform — see `06-solution-structure.md`.
- Ordering with `ADR-008` stays fixed: the event-type-level
  `RequiredReadClaim` check happens first (all-or-nothing); masking only
  ever applies to callers who already passed that check.
- `regulatoryClassification`/`governanceBody`/`regulationReference` are
  free text in v1 — validated only for "non-empty string if present," not
  against a controlled vocabulary. That's a deliberate scope decision, not
  an oversight: a fixed enum of classifications would need someone to
  decide the list, which isn't asked for here. Revisit if compliance
  tooling ever needs to query/aggregate by classification reliably (free
  text invites drift like `"PHI"` vs. `"phi"` vs. `"Protected Health Info"`
  meaning the same thing).
- **Consumer guidance: masked/absent fields must be skipped, never
  overlaid, when building a projection.** A consumer that maintains its
  own materialized state by applying incoming event fields onto existing
  records (a read-model/projection built from the Follow stream) must
  treat a field that arrives as `{"masked": "***"}` — or is legitimately
  absent from the payload — as **no information provided**, not as an
  instruction to write `"***"`, `null`, or the wrapper object itself over
  whatever value it already has for that field. Only a `{"value": ...}`
  branch (or, for a non-maskable field, its plain value) should ever
  update a consumer's own state. This is guidance for consumers, not
  something the store enforces or can verify — the store has no
  visibility into a downstream consumer's state to overlay onto in the
  first place (`Payload` itself is append-only and never mutated). Getting
  this wrong would let a caller who *temporarily* loses the claim (or
  simply reprocesses history from an earlier connection with fewer
  claims) silently clobber good previously-known data in their own
  projection with a placeholder — exactly the kind of corruption masking
  exists to prevent, just one layer further downstream than the store
  itself can reach.

### Future: definable masking strategies (proposal, not decided)

`"FixedValue"` is the only strategy built now. This section stays as a
proposal for later, kept explicitly separate from the Decision above so
it's unambiguous what's built versus sketched:

- Widen `x-masking.strategy` beyond `"FixedValue"` to e.g. `"PartialReveal"`
  (keep the last N characters of the real value inside `masked` — only
  meaningful for an originally-string property) or `"Hash"` (a
  deterministic hash of the real value inside `masked`, letting a caller
  correlate masked values across events without ever seeing the
  underlying value). Both still fit the existing wrapper — only the
  content of `masked` changes, never the shape — so this is a smaller
  extension than it would have been under the old null-out design.
- Whole-object or whole-array masking (collapsing an entire nested object
  or array into one `{value:...}`/`{masked:...}` at that position, instead
  of recursing into it) is explicitly out of scope for v1 (see the
  "not valid directly on `object`/`array`" rule above) — a candidate for
  this same future pass if a real need shows up.
- None of this is scheduled; it's recorded so `"FixedValue"`-only v1
  doesn't get treated as the final word by accident.

---

## ADR-010: Explicit tail-vs-replay mode on Follow, via a `mode` parameter

Status: Accepted

Context: `/follow/{event-type}` previously had no way to ask for
anything other than new events from the moment of connecting — a caller
who wanted the matching history that already exists in the store first had
no path to it short of a separate, unspecified mechanism.
`04-odata-filter-pushdown.md` had gestured at "tailing from connection
time or a resume token" without ever specifying either. (Written when
Follow was still `GET`; `ADR-012` later moves it to the HTTP `QUERY`
method — an unrelated, purely transport-level change made after this one,
which is why "a `mode` parameter" below deliberately avoids the phrase
"query parameter," to not collide with that later method name.)

Decision:
- `/follow/{event-type}` gains a `mode` parameter:
  `mode=tail` (**default** — unchanged from the existing behavior, no
  history) or `mode=replay`.
- `mode=replay` accepts an optional `fromSequenceNumber` (non-negative
  integer, default `0`): replay every matching event with
  `SequenceNumber > fromSequenceNumber`, then — with no gap and no
  duplicate — keep streaming new matching events exactly as `mode=tail`
  already does. This is one continuous poll loop
  (`WHERE SequenceNumber > lastSeen AND predicate`,
  `04-odata-filter-pushdown.md`), not two separate code paths: the only
  difference between the modes is the *initial* value of `lastSeen` —
  "current max `SequenceNumber` at connect time" for `tail`, `fromSequenceNumber`
  (or `0`) for `replay`.
- `fromSequenceNumber` is rejected (`400`) if supplied together with
  `mode=tail` (or the default) — silently ignoring it would let a caller
  believe they got a replay they didn't get.
- Applies uniformly regardless of `$filter`: replay only returns matching
  (filtered) historical events, using the same predicate as live tailing —
  no special-casing needed, per the "one continuous poll loop" point
  above. Applies uniformly regardless of `RequiredReadClaim` (`ADR-008`)
  and masking (`ADR-009`) too — both are checked once at connect time,
  independent of which mode was requested.

Consequences:
- `fromSequenceNumber` is a **raw sequence number the consumer must track
  themselves** (from the `sequenceNumber` field already present on every
  streamed event's envelope headers) — this is deliberately not a
  server-managed consumer-group checkpoint the way Kafka's committed
  offsets are. A consumer that wants to resume after a disconnect persists
  the last `sequenceNumber` it successfully processed and reconnects with
  `mode=replay&fromSequenceNumber=<that value>`; the store keeps no
  per-consumer state at all.
- Connecting with `mode=replay&fromSequenceNumber=0` against an event type
  with a large amount of history bursts that entire matching history at
  the caller as fast as the connection can carry it — there is no
  batching, pacing, or backpressure control on the replay burst. Consumers
  must be able to absorb that burst; this is an accepted v1 limitation, not
  solved here.
- This resolves `04-odata-filter-pushdown.md`'s previously-vague "tailing
  from connection time or a resume token" mention — that line is removed
  from "out of scope" now that it's specified here instead.
- A `mode=replay` burst against a long-lived event type can span every
  schema version that type has ever had — this ADR says nothing about
  reconciling those different shapes into one; `ADR-018` (event upcasting)
  is what actually resolves that, layered on top of the cursor mechanics
  decided here.

---

## ADR-011: Publish idempotency via an optional client-supplied `eventId` + a stored payload hash

Status: Accepted

Context: `EventId` was always server-generated (`Guid.NewGuid()`), so the
unique index on it (`02-data-model.md`) never actually caught a real
duplicate — a publisher whose connection drops after a successful insert
but before the response arrives has no safe way to retry: retrying just
creates a second, distinct `StoredEvent` with a fresh `EventId`, a true
duplicate the store cannot detect at all.

Decision:
- The publish envelope gains an optional `eventId` field (a `Guid`,
  alongside `payload`/`parentEventIds`). Omitted: behavior is unchanged —
  the server generates a fresh `EventId`, no idempotency is possible
  (there's nothing for a retry to be checked against).
- `StoredEvent` gains a `PayloadHash` column: a SHA-256 digest (FIPS
  180-4) over a canonical serialization of `{ eventType, payload,
  parentEventIds: <sorted> }` — computed and stored on every publish,
  whether or not `eventId` was supplied.
- When `eventId` **is** supplied, `PublishEndpoint` looks it up (via the
  existing unique index) immediately after resolving the active
  `EventTypeDefinition` and the `RequiredPublishClaim` check
  (`ADR-008`) — before schema/parent-link validation, as a short-circuit:
  - **Not found**: proceed exactly as the unsupplied case, except the
    caller's `eventId` is used for the new row instead of a generated one.
  - **Found, `PayloadHash` matches** the incoming request's: this is an
    **idempotent replay** — return the identical response as the original
    successful publish (`201`, same body). No new row, no re-validation;
    the store performs no write at all.
  - **Found, `PayloadHash` differs**: `409 Conflict` — the same `eventId`
    was reused for genuinely different content. This is a caller bug
    (idempotency-key reuse), not silently accepted and not treated as a
    fresh publish.

Consequences:
- This is opt-in: a publisher that never supplies `eventId` gets no
  idempotency guarantee, same as before this ADR — an accepted trade
  rather than forcing every publisher to manage an idempotency key.
- The hash **must** include `eventType`, not just `payload` and
  `parentEventIds` — otherwise two genuinely different event types
  publishing byte-identical payload/parent content could collide
  undetected as "the same request retried," which they are not.
- Two concurrent retries with the same never-yet-seen `eventId` can both
  pass the "not found" check before either commits, and race at the
  database's unique-constraint level on insert. The loser's insert fails;
  it must catch that specific constraint violation and re-run the lookup
  (which will now find the winner's row) rather than surfacing a raw DB
  error — functionally the same "found, compare hash" path, just entered
  via a failed insert instead of a preceding `SELECT`.
- An idempotent replay skips schema and parent-link validation entirely
  (it already passed the first time) — this means a schema *version*
  change between the original publish and a much-later retry with the
  same `eventId` has no effect on the replay; it returns the original,
  historically-valid result, consistent with `StoredEvent.SchemaVersion`
  recording whichever version validated a given event at the time
  (`05-schema-registry-and-spec-generation.md`).
- `PayloadHash` has no index of its own — the unique index on `EventId` is
  what makes the lookup fast; the hash is only consulted after that lookup
  finds a match, purely as a content-equality check.
- `PayloadHash` answers content-equality only — it says nothing about
  tamper-evidence across the store's history. `ADR-019` reuses the same
  SHA-256 primitive to build a hash *chain* (`ChainHash`) on top of every
  `StoredEvent`, a genuinely different guarantee layered on the same
  computation this ADR introduces.

---

## ADR-012: HTTP `QUERY` method (RFC 10008) for OData data-queries, replacing `GET`

Status: Accepted

Context: `$filter` (Follow), and now pagination for Lineage and the
registry listing, are genuine data queries expressed via query-string
parameters on a `GET`. `GET` has no well-defined semantics for a request
body, which pushes arbitrarily complex OData expressions into URL length
limits. The HTTP `QUERY` method (RFC 10008) exists specifically for this:
a safe, cacheable method like `GET`, but with a request body.

Decision:
- Every endpoint whose query is genuinely filterable/pageable moves from
  `GET` to `QUERY`, with the OData expression moved from the URL query
  string into the request body (`application/x-www-form-urlencoded`,
  **the same syntax** as before — `$filter=Amount gt 100&mode=replay` —
  parsed by the exact same `ODataFilterParser`/parsing code, just read
  from `Request.Form` instead of `Request.Query`; ASP.NET Core's
  `IFormCollection` mirrors `IQueryCollection`'s API for exactly this
  content type, so the change is mechanical, not a rewrite):
  - `QUERY /follow/{event-type}` — `$filter`, `mode`, `fromSequenceNumber`
    (`ADR-010`) all move into the body. The path segment (`{event-type}`)
    stays in the URL — it identifies *which resource*, the body customizes
    *what you get back*.
  - `QUERY /events/{id}/parents|children|ancestors|descendants` — same
    principle, and picks up **`$top`/`$skip` pagination** as a natural
    consequence of the endpoint shape changing anyway (previously
    undesigned — a deep DAG traversal could return an unbounded result
    set). This is a simple limit/offset slice over the existing response
    array, not full OData collection semantics — no `@odata.count` or
    `@odata.nextLink`, consistent with how `$filter` elsewhere already
    borrows OData syntax without claiming full spec compliance. Both are
    optional; omitting them returns everything, unchanged from before.
  - `QUERY /registry` (the list-all-event-types endpoint) — same `$top`/
    `$skip` pagination, same reasoning.
- **Unchanged, stays `GET`**: single-resource-by-key fetches with nothing
  to query — `GET /registry/{event-type}`, `GET /registry/{event-type}/{version}`,
  `GET /openapi.json`, `GET /asyncapi.json`. There's no filter expression
  to move into a body for any of these; forcing them onto `QUERY` would
  add nothing.
- Routed via `MapMethods(pattern, ["QUERY"], handler)` — ASP.NET Core's
  routing accepts any method string, not a fixed enum, so this needs no
  framework changes.

Consequences:
- **Breaks native browser `EventSource` for Follow entirely** —
  `EventSource` can only issue `GET`, has no method override and no body
  support. A browser client must switch to `fetch()` with a `QUERY`
  request and manually parse the `text/event-stream` response body
  (hand-rolled `ReadableStream` reading, or a small SSE-over-fetch
  library) — `new EventSource(url)` no longer works for this endpoint.
- **The `access_token`-in-URL workaround (`ADR-006`) is removed for
  Follow, not merely unnecessary.** It existed specifically because
  `EventSource` couldn't set an `Authorization` header; `fetch()` can, so
  keeping a leakier, redundant auth path around with no remaining
  justification would be worse than removing it. Follow now authenticates
  exactly like every other endpoint — header only.
- `QUERY` is a "non-simple" method for CORS purposes: every browser call
  triggers a preflight (`OPTIONS`), which is why `ADR-014`'s CORS policy
  explicitly lists it in `WithMethods(...)`.
- AsyncAPI's SSE binding must document `method: QUERY` for the Follow
  channel. `QUERY` is a very new HTTP method — some AsyncAPI-consuming
  tooling may not yet recognize it as a valid binding value. This is a
  documented risk, not something resolved here; if it becomes a real
  blocker, the fallback is a vendor extension (`x-method: QUERY`)
  alongside whatever the binding's schema will actually accept.
- `04-odata-filter-pushdown.md`'s pipeline step 1 ("parse `$filter` string")
  now reads that string from the request body, not the URL — the parser
  itself (`Microsoft.OData.UriParser`) is unaffected, since it only ever
  operated on the string content, never the transport it arrived by.

---

## ADR-013: Canonical error responses via RFC 9457 Problem Details

Status: Accepted

Context: Error responses were described inconsistently across the design
— different feature docs implied different response bodies for `400`/
`401`/`403`/`404`/`409` without ever settling on one shape. Left alone,
every endpoint would plausibly grow its own ad hoc error format.

Decision: Every error response across every endpoint uses **RFC 9457
Problem Details** (`application/problem+json`), via ASP.NET Core's
built-in support (`builder.Services.AddProblemDetails()`,
`Results.Problem(...)`/`Results.ValidationProblem(...)` in minimal APIs) —
no custom error DTO, no library beyond what's already in the framework.
Standard members: `type` (a URI identifying the problem category — see
below), `title` (short, stable summary), `status`, `detail`
(occurrence-specific human-readable text), `instance` (the request path).
Anything beyond that is carried in Problem Details' standard
`Extensions` dictionary, not by inventing new top-level fields:

| Situation | `status` | `type` slug | Extensions |
|---|---|---|---|
| Payload fails schema validation | `400` | `validation-failed` | Uses `ValidationProblemDetails`'s `errors: { "<path>": ["<message>"] }`, not a custom shape — this is the one case with an existing framework type built for exactly this |
| Strict-mode parent event(s) not found | `400` | `parent-not-found` | `missingParentEventIds: [...]` |
| `$filter` references an undeclared field | `400` | `filter-field-not-filterable` | `field: "InternalNotes"` |
| `fromSequenceNumber` supplied with `mode=tail` | `400` | `invalid-replay-parameters` | — |
| Missing/invalid Bearer token | `401` | `unauthenticated` | — |
| Missing/invalid DPoP proof, or proof doesn't match the token's `cnf.jkt` (`ADR-017`) | `401` | `dpop-proof-invalid` | `reason: "..."` |
| Missing scope, or missing `RequiredPublishClaim`/`RequiredReadClaim` | `403` | `forbidden` | `reason: "missing_scope"` \| `"missing_required_claim"` — this is exactly the "response detail, not the status code" distinction `ADR-008` already promised |
| Unknown event-type / unknown `eventId` | `404` | `not-found` | — |
| `eventId` reused with different content | `409` | `event-id-conflict` | `eventId: "..."` |
| `x-masking` malformed at registration (`ADR-009`) | `400` | `masking-invalid` | `path: "<property path>"`, `reason: "..."` |
| `changeKind` missing or not `Full`/`Partial` at registration (`ADR-016`) | `400` | `change-kind-required` | — |

`type` values are placeholder slugs here (`https://eventstore.example/problems/<slug>`
in the examples below) — RFC 9457 wants `type` to ideally resolve to human
documentation, but doesn't require it; picking a real base URL (or
defaulting every `type` to `about:blank`, ASP.NET Core's built-in fallback)
is an implementation-time decision this design doesn't need to make.

```json
{
  "type": "https://eventstore.example/problems/parent-not-found",
  "title": "One or more parent events do not exist",
  "status": 400,
  "detail": "parentEventIds referenced an event that has not been published.",
  "instance": "/publish/OrderShipped",
  "missingParentEventIds": ["00000000-0000-0000-0000-000000000000"]
}
```

Consequences:
- Every response consumers need to parse has one shape, not N ad hoc ones
  — a caller can always check `status` + `type` first and fall back to
  `detail` for a human, without needing per-endpoint response schemas.
- `403`'s `reason` extension is the only place the scope-vs-claim
  distinction from `ADR-008` actually surfaces; the status code alone
  still can't be used to tell them apart, by design.
- OpenAPI/AsyncAPI generation documents every non-`2xx` response as
  `$ref: '#/components/schemas/ProblemDetails'` (a single shared schema)
  plus a `type`-specific `example`, rather than a bespoke schema per
  status code per endpoint.
- This doesn't apply to Lineage's `restricted: true` stubs (`ADR-008`) —
  those are `200` responses with a marked node, not an error at all; there
  is no HTTP error status for "some of what you asked for is hidden."

---

## ADR-014: CORS policy — configurable allowlist, deny by default

Status: Accepted

Context: A browser calling any of these APIs directly from a web page's
JavaScript is subject to CORS (the WHATWG Fetch standard's Cross-Origin
Resource Sharing protocol) — the *browser's* enforcement, not the
server's; it doesn't affect server-to-server calls at all. Nothing in the
design said which origins, if any, are allowed.

Decision:
- ASP.NET Core's built-in CORS middleware (implementing that same Fetch
  standard protocol), one named policy, wired in
  `EventStore.Host.Core` (`app.UseCors(...)`) so it's identical across all
  three `EventStore.Host.<Provider>` deployables (`ADR-001`).
- Allowed origins come from configuration (`Cors:AllowedOrigins`, a plain
  string array), not a hardcoded list — unlike the database provider
  (`ADR-001`), there's no reason this needs to be a build-time choice: it's
  an ordinary environment-varying setting with no NuGet/migrations
  implications, so runtime configuration is the right fit here.
- **Deny by default**: an empty/unset `Cors:AllowedOrigins` means no
  cross-origin browser call succeeds, for any origin. Server-to-server
  calls (the majority of this system's traffic — none of the three system
  actors are literally browsers) are entirely unaffected either way.
- The policy explicitly allows: the `Authorization` header (needed once a
  browser client uses `fetch()` with a real Bearer header instead of the
  old `access_token`-in-URL workaround — see `ADR-012`); and every method
  actually used, including `QUERY` (`ADR-012`) — a "non-simple" method
  that always triggers a browser preflight (`OPTIONS`) request, which
  ASP.NET Core's CORS middleware answers automatically once the method is
  listed, no extra code needed.
- `AllowCredentials()` is **not** set — auth is Bearer-token-in-header
  only, never cookies, so there's nothing that needs it, and leaving it
  off keeps the policy simpler (credentialed CORS has stricter rules
  around wildcard origins that don't need to apply here).

Consequences:
- Exact-string origin matching by default (ASP.NET Core's standard
  `WithOrigins(...)`); wildcard-port localhost matching for local dev (if
  wanted) needs `SetIsOriginAllowed(...)` with a predicate instead of the
  plain list — a small addition, not designed further here since it's a
  dev-convenience detail, not a behavioral decision.
- A fresh deployment with nothing configured is CORS-closed to every
  browser origin — safe-by-default, but means "why can't my browser client
  connect" is the first thing to check `Cors:AllowedOrigins` for.

---

## ADR-015: Read-model projections consume the public Follow API, not a private hook

Status: Accepted

Context: this project's purpose (`README.md`) includes demonstrating CQRS
alongside event sourcing — a read side that materializes query-optimized
read models from the event stream, kept separate from the write side. The
naive way to feed that read side is a private, store-internal notification
mechanism (an in-process event bus, a change-data-capture hook on
`Events`). But this design already has a public, general-purpose consumer
API with exactly the resume/no-gap/no-duplicate semantics a projection
needs: `QUERY /follow/{event-type}` with `mode=tail`/`mode=replay`
(`ADR-010`). Building a second, parallel consumption path would duplicate
that guarantee under a different name for no real benefit, and would mean
a projection sees the store's internals rather than the same contract any
external follower sees.

Decision:
- **Projections are Follow consumers, full stop** — a `ProjectionHost`
  process authenticates like any other `follower-client` (`ADR-006`) and
  issues ordinary `QUERY /follow/{event-type}` calls. Nothing about the
  store's public contract changes to support projections; nothing
  projection-specific is added to `EventStore.Host.*`.
- **Always `mode=replay`, never `mode=tail`.** A `ProjectionHost` tracks its
  own resume position per projection (`ProjectionCheckpoint.LastSequenceNumber`,
  starting at `0` for a projection that has never run) and always connects
  with `mode=replay&fromSequenceNumber=<checkpoint>`. Per `ADR-010`,
  replay-then-tail is one continuous poll loop on the server side — there
  is no behavioral difference from `mode=tail` once a projection is caught
  up, so there is no reason to ever use `mode=tail` and track two code
  paths for "starting fresh" vs. "resuming."
- **A full rebuild is not a separate mechanism — it's the same mechanism
  starting from zero.** Truncate the projection's read-model table(s) and
  its `ProjectionSnapshot` rows (`ADR-016`), reset
  `ProjectionCheckpoint.LastSequenceNumber` to `0`, reconnect. Replay from
  `0` regenerates the read model from the complete history exactly as the
  original incremental build would have, by construction — see `ADR-016`
  for why this determinism holds.
- **The read side is a separate physical store from the write side** —
  its own `DbContext`, its own database, reachable only via HTTP from the
  write side (there is no shared connection string, no cross-database
  query, no read replica of `EventStoreContext`). This is deliberate, not
  incidental: sharing a database would blur exactly the write/read
  separation CQRS exists to make explicit, undermining the point of using
  this as a teaching example for it. Unlike `ADR-001`'s write-side
  three-provider build, the read side does **not** need a per-provider
  split — `09-cqrs-read-models.md` explains why (its schema is ordinary
  typed relational columns, not portable JSON-text-plus-native-JSON-function
  querying, so there's no provider-specific translation layer to isolate
  in the first place). One EF Core provider (SQLite, for the example) is
  enough.
- Runs as its own deployable (`EventStore.Projections.Host`,
  `06-solution-structure.md`) — not in-process inside any
  `EventStore.Host.<Provider>` — so the write/read split is real at the
  deployment level, not just conceptual.

Consequences:
- **Read models are eventually consistent with the write side, inherently
  and by design** — a `ProjectionHost` only sees an event after it's been
  published and after its own poll interval elapses. This design does not
  attempt read-after-write consistency (e.g. a client publishing and then
  immediately querying a projection and expecting to see it) — that would
  need an explicit sync signal this system doesn't provide, same category
  of "not solved here" as Follow's unbounded replay burst (`ADR-010`).
- Projections inherit Follow's existing guarantees for free (no gap, no
  duplicate across a reconnect, `ADR-010`) and its existing limitations for
  free too (an unbounded burst on `fromSequenceNumber=0` against a large
  history, no batching/backpressure) — same accepted trade any other Follow
  consumer already accepts, not a new risk introduced by projections.
- Because rebuild is just "replay from `0` again," there is no separate
  rebuild code path to maintain, test, or let drift from the normal
  incremental path — a real simplification, not just a convenient framing.
- A `ProjectionHost` is subject to `RequiredReadClaim` (`ADR-008`) and
  masking (`ADR-009`) exactly like any other Follow caller — it is not a
  store-internal trust boundary that bypasses either. If a projection needs
  to see a claim-gated event type, its service identity (a fourth OAuth2
  client, alongside the three in `ADR-006`) needs that claim like anyone
  else. This is a genuine constraint on what a projection can be built
  over, not an oversight — see `09-cqrs-read-models.md`.
- Running a dedicated process per projection group, rather than in-process
  inside the write-side host, is more moving parts for this example than
  an in-process background service would be — an accepted cost for making
  the CQRS split legible as two things you actually deploy separately, not
  just two namespaces in one process.

---

## ADR-016: Event-type `ChangeKind` (Full | Partial) and centralized snapshot merge

Status: Accepted

Context: a real business event stream mixes events that establish or
wholesale-replace an entity's known state (e.g. `OrderPlaced`, carrying
everything known about a new order) with events that carry only a delta
(e.g. `OrderAddressUpdated`, carrying only the changed address field). A
projection applying these onto its own materialized state needs a single,
uniform rule for which is which and how each gets applied — otherwise every
`IProjection<TReadModel>` implementation reinvents its own ad hoc merge
logic, and the rule quietly drifts across projections.

Decision:
- `EventTypeDefinition` gains a **required** field, `ChangeKind`
  (`Full` | `Partial`) — set at registration
  (`05-schema-registry-and-spec-generation.md`), alongside
  `ParentValidationMode`/`RequiredPublishClaim`/`RequiredReadClaim`.
  Unlike those three, **`ChangeKind` has no default** — registering an
  event type without it is rejected (`400`), because guessing wrong here
  (assuming `Full` for something that's actually a delta, or vice versa)
  silently corrupts every projection over that type, whereas the other
  three fields default to "no extra restriction," a safe no-op.
- **The merge rule, applied once, centrally, in `ProjectionHost`**
  (`ADR-015`) — never reimplemented per projection:
  - `ProjectionHost` maintains one JSON snapshot per **projection-defined
    key** (`IProjection<TReadModel>.GetKey(StoredEvent)`,
    `09-cqrs-read-models.md`) per projection, in a `ProjectionSnapshot`
    table (`{ProjectionName, Key, SnapshotJson, LastAppliedSequenceNumber}`).
  - Applying a `Full` event **replaces** that key's whole snapshot with the
    event's payload.
  - Applying a `Partial` event **merges** the event's payload onto the
    existing snapshot for that key: a field present in the incoming
    payload overwrites; a field **absent** is left untouched. **This is
    deliberately the same overlay rule masking's consumer guidance already
    states** (`ADR-009`: "masked/absent fields must be skipped, never
    overlaid") — one overlay rule for the whole design, not two
    similar-but-subtly-different ones that could drift apart. A `Partial`
    event whose payload happens to contain a masked field (because
    `ProjectionHost`'s own claims don't cover it) is, from the merge's
    point of view, simply an absent field — no special-casing needed
    beyond the rule already stated.
  - Only **after** the merge does `ProjectionHost` call
    `IProjection<TReadModel>.Project(mergedSnapshotJson)` to map the
    fully-current-state JSON into the strongly-typed read-model row that
    gets upserted. **Individual projections never see raw events, never
    see `ChangeKind`, and never implement merge logic at all** — they only
    ever receive "the current, fully-merged state for this key," already
    resolved.
- A `Partial` event for a key with no existing snapshot (its `Full`/origin
  event hasn't been seen yet, or never will be, under this key) simply
  starts a snapshot from just that event's fields — there is no "wait for
  the `Full` event first" ordering enforcement. Whether a given key's first
  event is actually `Full` is a **producer discipline** concern, same
  category as `StreamId`'s freeform convention elsewhere in this design:
  the store has no way to know what a projection's key even is (key
  extraction is projection-defined, per above), so it has no way to enforce
  anything about ordering relative to it.

Consequences:
- One overlay rule, shared by name and by cross-reference between
  `ADR-009` and here, rather than two independently-maintained "ignore
  missing on merge" rules that could quietly diverge — a direct, deliberate
  payoff of building projections as an ordinary Follow consumer (`ADR-015`)
  subject to the same masking behavior as anyone else, rather than a
  privileged internal path.
- `ChangeKind` being required with no default means every existing/future
  event type registration must decide this explicitly — a small but real
  addition to the registration payload's required fields
  (`03-api-contracts.md`), not purely additive the way the three optional
  fields were.
- Getting a type's `ChangeKind` wrong at registration is a silent data
  problem, not a loud one: a `Partial` type mistakenly registered as `Full`
  causes every projection over it to lose previously-known fields on the
  next event for a key; a `Full` type mistakenly registered as `Partial`
  causes stale fields to survive an event that meant to replace them
  entirely. Neither failure mode produces an error anywhere — this is a
  real risk accepted for v1, not something this design detects.
- `ProjectionSnapshot` grows one row per distinct key per projection,
  unboundedly, same shape of accepted gap as Follow's unbounded replay
  burst (`ADR-010`) — no TTL or eviction is designed here.
- Two different projections over the same event types may use different
  key-extraction logic and therefore maintain entirely separate snapshot
  spaces — `ChangeKind`'s Full/Partial semantics apply per key within one
  projection's snapshot space, not globally across projections.
- The merge itself is exactly **JSON Merge Patch (RFC 7396)** applied to
  the snapshot: "a field present in the incoming payload overwrites; a
  field absent is left untouched" is RFC 7396's semantics verbatim, with
  one deliberate narrowing — RFC 7396 also lets an explicit `null` value
  *delete* a key from the target, which this design does not want (a
  `Partial` event's field is never expected to erase a previously-known
  fact, only add to or overwrite it); `MergePatch` in
  `09-cqrs-read-models.md` implements the overwrite-if-present half only,
  not the delete-on-null half.

---

## ADR-017: DPoP-bound access tokens (RFC 9449)

Status: Accepted — hardens `ADR-006`; built in Phase 10
(`08-build-plan.md`).

Context: `ADR-006` issues plain OAuth2 bearer tokens (Client Credentials
Grant, RFC 6749 §4.4; Bearer Token Usage, RFC 6750). `ADR-012` already
removed the one deliberate token-in-URL leak vector this design had
(Follow's `access_token` query parameter, superseded when Follow moved off
`GET`). What's left is the ordinary risk RFC 6750 itself names in its own
security considerations: a bearer token is usable by *any* party who
possesses it, however it was obtained — a token leaked via logs, a
compromised host, or an SSRF-style relay is fully usable by an attacker,
indistinguishable from the legitimate client, until it expires.

Decision:
- Every access token `EventStore.DevIdp` issues is **DPoP-bound (RFC
  9449)**, not a plain bearer token. Each of the four OAuth2 clients
  (`publisher-client`, `follower-client`, `operator-client`,
  `projections-client` — `ADR-006`/`ADR-015`) generates its own asymmetric
  key pair and proves possession of the private key on every request.
- **Token request**: the client includes a DPoP proof JWT (`typ:
  dpop+jwt`, signed with its private key, carrying `jwk` — its public key
  — plus `htm`/`htu` bound to the token endpoint, `iat`, `jti`) in a
  `DPoP` header on its `POST /connect/token` call. `EventStore.DevIdp`
  embeds a `cnf.jkt` claim (the JWK thumbprint) in the issued access
  token, binding it to that specific key.
- **API request**: the client attaches a fresh DPoP proof (new
  `htm`/`htu` bound to the actual API call, `ath` = hash of the access
  token being presented) alongside `Authorization: Bearer <token>` on
  every request to any `EventStore.Host.<Provider>` endpoint.
- **Resource-server validation** (`EventStore.Host.Core`, alongside the
  existing JWT-bearer validation): verify the proof's signature against
  its own embedded `jwk`; check `htm`/`htu` match the request; check `ath`
  matches the presented token; check the proof's `jwk` thumbprint matches
  the token's `cnf.jkt`; enforce a short proof lifetime via `iat`, tracked
  by `jti` for replay detection.
- **Server-chosen nonce challenge (RFC 9449 §8) is out of scope for v1** —
  this is a dev/POC deployment with a small, fixed set of trusted clients,
  not a public browser-facing token-acquisition surface that needs
  defending against pre-generated-proof attacks.

Consequences:
- Every seeded client now manages a key pair, not just a client secret —
  `DevIdpSeeder` (`ADR-006`) grows a key-generation step; more moving
  parts for a dev/POC identity provider than the client-secret-only model,
  an accepted cost for demonstrating the real mechanism rather than a
  bearer-only story.
- A leaked access token is no longer usable by itself — replaying it with
  a different key produces a proof that fails the `cnf.jkt` check. This is
  the actual value this ADR buys: defense in depth against exactly the
  log/relay-leak scenario RFC 6750 warns about.
- `EventStore.Host.Core`'s JWT-bearer validation now has a second, coupled
  check that must also pass — a request with a technically-valid bearer
  token but a missing/invalid DPoP proof is rejected `401`, a new failure
  mode `03-api-contracts.md`'s Problem Details table (`ADR-013`) must
  cover.
- Client clock skew becomes an operational concern for the first time
  (proof `iat` freshness checking) — nothing else in this design needed
  client/server time agreement.

---

## ADR-018: Event upcasting for schema evolution

Status: Accepted

Context: `EventTypeDefinition` already supports multiple schema versions
(`02-data-model.md`), and `StoredEvent.SchemaVersion` records which
version validated a given event at publish time (`ADR-011`'s
consequences). But nothing in the design so far reshapes an old-version
payload into the current version's shape for a consumer. `ADR-010`'s
`mode=replay` makes this a concrete problem, not a hypothetical one:
replaying an event type's full history from `fromSequenceNumber=0` can
burst events spanning every schema version that type has ever had, in one
stream — and a consumer, especially a CQRS projection
(`09-cqrs-read-models.md`) whose `Project` function expects one consistent
shape, has no designed way to reconcile that today.

Decision:
- A new, **code-registered** component per event type — same pattern as
  `IJsonPathTranslator`, not a `PUT /registry` field —
  `IEventUpcaster`, one per `(EventType, FromVersion)` pair:
```csharp
public interface IEventUpcaster
{
    string EventType { get; }
    int FromVersion { get; }
    JsonNode Upcast(JsonNode payloadAtFromVersion);
}
```
- An `UpcastChain` (the same shape as Axon Framework's upcaster chain —
  see `docs/references.md`) resolves and applies, in order, every
  registered upcaster between a `StoredEvent`'s `SchemaVersion` and the
  event type's current active version, before the payload reaches a
  consumer — Follow and any CQRS projection (`ProjectionHost`,
  `ADR-015`). Lineage never includes `Payload` at all (`ADR-009`), so it's
  unaffected.
- `StoredEvent.Payload` is never rewritten — upcasting is a read-time
  transform, computed fresh per response, the same non-destructive posture
  already taken for masking (`ADR-009`) and for never deleting/mutating
  stored data (`ADR-009`'s closing note).
- Registering a new schema version does **not** require a matching
  upcaster to exist — a purely additive-optional-field change may need no
  transform at all. Whether a version gap that *does* need one is missing
  its upcaster is a runtime data problem, not something registration
  validates; schema-compatibility *checking* at registration time (in the
  style of Confluent Schema Registry's BACKWARD/FORWARD/FULL modes — see
  `docs/references.md`) is a further, undecided extension, not built here.

Consequences:
- Follow/`ProjectionHost` consumers, across a `mode=replay` burst spanning
  many schema versions, now see one consistent (current-version) shape
  throughout, instead of branching on `SchemaVersion` themselves — the
  direct fix for the gap in Context.
- An upcaster runs per event, on every read — for a high-volume replay
  this is a real, uncached cost; no upcast-result caching is designed
  here, the same category of accepted v1 cost as Follow's unbounded
  replay burst (`ADR-010`).
- `IEventUpcaster` is deliberately symmetrical with
  `IJsonPathTranslator`/`IEventLineageQueryProvider`
  (`06-solution-structure.md`) — resolved via DI, one registration per
  `(type, version)` pair, no runtime `switch` — consistent with this
  design's pattern for version-/provider-specific logic.
- **Not decided here**, unlike `ADR-007`'s or `ADR-009`'s deferrals (which
  are fully designed, just scheduled later): whether to add
  compatibility-mode *enforcement* at registration time. v1 only builds
  the transform mechanism, trusting whoever registers a new version to
  also register the matching upcaster.

---

## ADR-019: Hash-chained events for tamper evidence

Status: Accepted

Context: `StoredEvent.PayloadHash` (`ADR-011`) already exists, but purely
as a content-equality check for idempotent-retry detection — it says
nothing about *when* or in what order an event was appended relative to
any other, and nothing detects whether a row in `Events` was ever altered
after the fact (e.g., a direct database edit bypassing the application
entirely). An event-sourced store of record is exactly the shape of system
where that guarantee has real value — Certificate Transparency (RFC 9162)
and Merkle-tree verifiable logs generally (see `docs/references.md`)
exist to solve precisely this: making tampering with *any* past entry
detectable, without needing to trust the store operator.

Decision:
- `StoredEvent` gains a `ChainHash` column:
  `ChainHash[n] = SHA-256(ChainHash[n-1] || PayloadHash[n] || SequenceNumber[n])`,
  computed by `EventAppender` at insert time, chained off the immediately
  preceding `SequenceNumber`'s `ChainHash` (a fixed seed value for
  `SequenceNumber = 1`, the store's first-ever event).
- This is a **linear hash chain, not a full Merkle tree** — deliberately
  simpler than Certificate Transparency's binary tree, since this design
  has no need for CT's specific inclusion/consistency-proof-against-a-
  partial-view use case (one store, not a federation of independently
  operated logs cross-checking each other). A linear chain gives the same
  tamper-evidence property (altering any past `Payload`/`PayloadHash`
  breaks every subsequent `ChainHash`) with a far simpler verification
  procedure: replay the chain from `SequenceNumber = 1` and compare the
  final `ChainHash` to what's stored.
- A read-only verification endpoint,
  `GET /events/verify?throughSequenceNumber=<n>` (or an offline tool —
  left as an implementation detail, not fixed here), recomputes the chain
  from `1` through `n` and reports the first `SequenceNumber` where the
  stored and recomputed `ChainHash` diverge, if any.
- `ChainHash` is computed once, at publish time, in the same transaction
  as the `StoredEvent` insert (`EventAppender`) — never recomputed or
  backfilled. There is no migration path today that alters historical
  `Payload` content (`ADR-009`'s closing note); if one ever existed, it
  would invalidate the chain from that point forward by design, not as an
  oversight to work around.

Consequences:
- Complementary to, not a replacement for, `PayloadHash`/`ADR-011` —
  `PayloadHash` answers "is this retry identical to what I already
  stored," `ChainHash` answers "has anything in this store's history been
  altered since it was written." Different questions, same SHA-256
  primitive, deliberately reused rather than introducing a second hash
  algorithm.
- Verification is `O(n)` from the seed — cheap for a periodic integrity
  audit, not designed for cheaply verifying one arbitrary event's position
  in isolation (that needs real Merkle inclusion proofs — an explicitly
  rejected complexity for v1, per the linear-chain choice above).
- This gives tamper-**evidence**, not tamper-**prevention** — an attacker
  with direct database write access could still rewrite `Events` and
  recompute every downstream `ChainHash` to match. What this closes is the
  *undetected* part: recomputing the entire chain from `1` is a far more
  detectable act (e.g., against an independently-stored periodic
  checkpoint of `ChainHash` at various `SequenceNumber`s) than simply
  editing one row and hoping no one checks.
- No provider-specific translation needed (unlike `IJsonPathTranslator`) —
  `ChainHash` computation is plain application code in `EventAppender`,
  identical on SQLite/Postgres/SQL Server; only the column itself (`TEXT`,
  portable per `ADR-004`) is persisted per provider.
