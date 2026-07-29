[← ADR index](../07-adrs.md)

# ADR-002: On-demand OpenAPI/AsyncAPI generation (vs. materialized cache)

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

- **The spec endpoints (`/openapi.json`, `/asyncapi.json`, and their UIs
  — `ADR-025`) can be disabled entirely via configuration.** Resolves
  `ADR-050`'s question of whether `x-required-claims`/`x-masking`
  appearing in a publicly-readable generated document itself leaks
  which fields are sensitive: **by default, no** — the answer settled
  on is that this doesn't meaningfully weaken security (revealing
  *that* a claim is required is not the same as revealing the value,
  and "security through undiscoverability" of the API's own shape is
  weak protection to begin with). For deployments with a stricter
  posture, a single config flag (e.g. `SpecEndpoints:Enabled`) turns
  the routes off completely — not just hiding the UI while leaving the
  raw JSON reachable, the actual `MapGet`/`MapScalarApiReference`
  registrations are conditional on it.

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
