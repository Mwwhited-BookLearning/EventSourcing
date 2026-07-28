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
- Authentication: OAuth2 **Client Credentials** grant against an OIDC
  provider; every API request carries `Authorization: Bearer <JWT>`. APIs
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
- The browser `EventSource` API cannot set an `Authorization` header, so the
  Follow API must additionally accept the bearer token via an
  `access_token` query-string parameter for browser-based followers.
  Query-string tokens are more prone to leaking via server/proxy logs than
  header-based ones — mitigated with short-lived tokens, since there is no
  header-based alternative for a real `EventSource` client.
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
  `RequiredReadClaim` gates `GET /follow/{event-type}` (checked once, at
  connect time) **and** all four Lineage API endpoints.
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
hidden from everyone else. An earlier version of this ADR tried to solve
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

Context: `GET /follow/{event-type}` previously had no way to ask for
anything other than new events from the moment of connecting — a caller
who wanted the matching history that already exists in the store first had
no path to it short of a separate, unspecified mechanism.
`04-odata-filter-pushdown.md` had gestured at "tailing from connection
time or a resume token" without ever specifying either.

Decision:
- `GET /follow/{event-type}` gains a `mode` query parameter:
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
- `StoredEvent` gains a `PayloadHash` column: a hash (e.g. SHA-256) over a
  canonical serialization of `{ eventType, payload, parentEventIds:
  <sorted> }` — computed and stored on every publish, whether or not
  `eventId` was supplied.
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
