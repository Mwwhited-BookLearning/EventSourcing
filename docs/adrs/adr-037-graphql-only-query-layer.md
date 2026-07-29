[← ADR index](../07-adrs.md)

# ADR-037: GraphQL as the sole query layer — supersedes `ADR-003`/`04-odata-filter-pushdown.md`

Status: Accepted — a full swap, not "GraphQL primary, OData secondary"
(`docs/design-docs/10`'s own original recommendation went less far than
this).

Context: `ADR-003`/`ADR-012` committed this design to OData-flavored
`$filter`/`$top`/`$skip` over the HTTP `QUERY` method. Direction received
this session: swap out all OData support entirely for GraphQL — not kept
as a secondary option.

Decision:
- **GraphQL Query/Mutation/Subscription replace every OData-flavored
  read surface**, concretely served by
  [HotChocolate](../libraries/dotnet/hotchocolate.md) rather than a
  hand-rolled GraphQL execution engine — `$filter` on Follow, Lineage's
  traversal, and the
  registry listing all become GraphQL queries against the Entity Store
  (`docs/data/entity-store.md`) and event/change-history
  (`ADR-024` §8.4), not OData query options on the event log directly.
- **GraphQL queries travel over the HTTP `QUERY` method (`ADR-012`,
  retargeted), never `GET`.** This is the specific, stated reason for
  keeping `QUERY` rather than defaulting to GraphQL's usual `POST`-only
  convention: a query document's arguments can carry PII/PHI (filtering
  by a patient name, an SSN, anything a `RequiredReadClaim`-gated field
  might hold), and `QUERY`'s body-carrying, still-safe-and-cacheable
  semantics keep that content out of URLs, access logs, and proxy caches
  the way `GET` never could. Mutations stay `POST` — they have side
  effects regardless of PII concerns, so `QUERY`'s safety guarantee
  wouldn't apply to them anyway.
- **The schema is composed per `AppId` (`ADR-030`), never one fixed
  global SDL** — each application's GraphQL type graph is generated from
  that application's own registered types, the same way `ADR-002`'s
  OpenAPI/AsyncAPI generation already works per the active registry
  (itself now filtered by `AppId`).
- **The per-provider JSON pushdown mechanism survives unchanged —
  only the OData surface syntax on top of it goes away.**
  `IJsonPathTranslator` (`04-odata-filter-pushdown.md`) still exists and
  still does the same job (translate a filter predicate to native
  `json_extract`/`->>`/`JSON_VALUE` per provider); what changes is that a
  GraphQL resolver's field arguments drive it now, not
  `Microsoft.OData.UriParser`'s AST. `ADR-003`'s actual rule — only
  fields declared `FilterableField` at registration can be filtered,
  rejecting anything else before touching the database — is unchanged in
  substance, just expressed as GraphQL argument validation instead of
  OData parse-time rejection.
- **`ADR-018`'s upcast mechanism moves off OData `compute()`** — that
  choice justified itself specifically by reusing the OData parser
  `$filter` already needed; with OData gone, that reuse argument no
  longer holds. Upcast mapping moves onto **JS/CEL transforms** (the
  mechanism `docs/design-docs/07 §7.3.2`–`7.3.3` already designed in
  full — sandboxed [Jint](../libraries/dotnet/jint.md) for the rare
  complex case, [CEL](../libraries/dotnet/cel-dotnet.md) for the common
  declarative one **by default — `ADR-053` makes the declarative engine
  itself pluggable per deployment, CEL/JSONata interchangeable behind
  one interface**) plus **GraphQL SDL directives**
  (`@renamedFrom`/`@derivedFrom`, `docs/design-docs/07 §7.4`) as
  self-describing mapping metadata, so the schema and its migration
  history can't silently drift apart. `ADR-018` itself needs revising to
  reflect this — flagged here, not yet propagated into that file.
- **`extensions: JSON`** is a generic field on every GraphQL type,
  exposing `docs/data/entity-store.md`'s `Extensions` bag — the query-
  layer expression of Tolerant Reader (`docs/patterns/tolerant-reader-
  and-schema-evolution.md`): unknown-but-present data stays queryable,
  never invisible.
- **Depth/cost limiting is mandatory, not optional** — a query-depth
  limiter and complexity/cost scoring middleware guard against
  unbounded hierarchical fan-out, with per-resolver batching (DataLoader
  pattern) to avoid N+1 queries across shards (`ADR-034`)/replicas
  (`ADR-033`).

Consequences:
- `03-api-contracts.md`'s entire OData-era publish/lineage/registry query
  documentation needs rewriting — flagged as a real, substantial
  propagation debt, not yet done.
- `04-odata-filter-pushdown.md` becomes a **superseded** document —
  its per-provider translation content moves to a renamed doc (or gets a
  banner pointing here) describing the same mechanism under GraphQL
  argument resolution instead of OData AST translation.
- Loose schema philosophy (`docs/design-docs/10 §10.2`): a client
  requesting a property absent on a given entity's current version gets
  `null`, never a request failure — GraphQL's partial-success execution
  model (`data` + a separate `errors` array) supports this natively.
  Non-nullable fields (`String!`) are audited and reserved only for
  properties guaranteed across every schema version — a non-null field
  resolving to `null` nulls out its entire parent object per the GraphQL
  spec, the exact failure mode this design's tolerant posture exists to
  avoid.
- `AsyncApiDocumentBuilder` (`ADR-002`) is unaffected — Follow's
  underlying SSE transport and envelope shape don't change; only how a
  *filter* is expressed to select what streams changes.
