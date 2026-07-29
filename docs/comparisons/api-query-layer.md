[← Comparisons index](README.md)

# API Query Layer: OData vs. GraphQL vs. JSON:API vs. gRPC vs. REST-ad-hoc/PostgREST-style

**Decided in:** `ADR-037` (GraphQL, full swap). **Written here:** the fuller
side-by-side that ADR's own Context section didn't have room for,
expanded per direct request beyond just the two options this project
actually chose between, to cover the other common named ways APIs expose
a query surface — so the GraphQL choice is checked against the field, not
just its one predecessor.

**Stated requirements driving this comparison** (from `ADR-037`,
`ADR-005`, `ADR-021`, `ADR-038`, and `README.md`'s framework-not-
application framing):

1. **Hierarchical/graph traversal** — Lineage's ancestor/descendant walk
   (`ADR-005`) and an entity's related data are naturally graph-shaped,
   not flat resource lists.
2. **A real-time subscription story** — Follow (`ADR-010`) needs a query
   layer that can express "and keep sending me matches," not just
   point-in-time reads.
3. **PII/PHI never lands in a URL, access log, or proxy cache** — the
   reason `ADR-012` keeps the HTTP `QUERY` method at all.
4. **Tolerant, partial-success reads** (`ADR-038`) — a client requesting
   a property absent on a given schema version gets `null` for that
   field, never a whole-request failure.
5. **Schema composed per `AppId` at runtime** (`ADR-030`) — this is a
   framework with no fixed global shape, not one API with one schema.
6. **Usable from any ordinary HTTP client** — no requirement that a
   caller run a specific vendor's SDK or binary codec.

## The options

### Option A — GraphQL (chosen, `ADR-037`)

| | |
|---|---|
| **Pros** | Native hierarchical query shape (nested fields resolve exactly Lineage's graph); **Subscriptions** are a first-class operation type, not a bolted-on extension — directly satisfies requirement 2; partial-success execution (`data` + separate `errors`, non-null fields aside) is *exactly* requirement 4's tolerant-reader posture, not an approximation of it; a schema is just an SDL document, trivially regenerated per `AppId` (requirement 5); mature tooling (introspection, GraphiQL/Apollo) despite being newer than REST/OData. |
| **Cons** | Convention is `POST`-only — satisfying requirement 3 required a deliberate, documented deviation (`ADR-012`'s `QUERY` retargeting) rather than falling out for free; N+1 resolver calls across nested fields need explicit DataLoader-style batching (`ADR-037` already mandates this); unbounded query depth/complexity needs its own guard (also already mandated); `QUERY` is new enough that some AsyncAPI-consuming tooling may not yet recognize it as a valid SSE binding (documented risk in `ADR-012`, unresolved). |

### Option B — OData (this project's prior choice, superseded)

| | |
|---|---|
| **Pros** | Mature, heavily used in enterprise/.NET ecosystems (Microsoft Graph, SAP, Dynamics); `$filter`/`$top`/`$skip`/`$expand` cover most of requirements 1 and 6 out of the box; this project already had working `Microsoft.OData.UriParser` integration, so it wasn't a hypothetical fit. |
| **Cons** | No native subscription/streaming concept — requirement 2 was always something *this project* bolted on (Follow's SSE wrapper), never something OData itself expressed; `$expand`-based traversal is workable but visibly a retrofit for genuinely graph-shaped data (Lineage's ancestor/descendant walk) compared to GraphQL's native nesting; conventionally a `GET`-with-query-string protocol — requirement 3 needed the same `QUERY`-method deviation GraphQL also needed, so OData earns no advantage there; a fixed-shape query grammar is a slightly awkward fit for requirement 5's per-`AppId` dynamic schema (workable, since `$filter` targets declared `FilterableFields`, but the whole *type graph* isn't self-describing to a client the way a GraphQL SDL naturally is). |

### Option C — JSON:API

| | |
|---|---|
| **Pros** | A real, versioned spec ([jsonapi.org](https://jsonapi.org/format/), v1.1) with more standardized conventions than plain ad-hoc REST: sparse fieldsets (`fields`), pagination, and compound documents (`include`) for fetching related resources in one round trip — genuinely solves REST's usual over-/under-fetching complaints without a new query language. Real adopters (Ember Data historically, many public REST APIs). |
| **Cons** | The spec **deliberately leaves `filter` semantics implementation-defined** — "the filter query parameter is reserved for filtering data... its strategy is not defined by this specification." That's a hard miss on requirement 1/6 together: this project needs one well-defined, safe filter grammar usable by any client, not an invitation for every deployment to invent its own. No native subscription concept (requirement 2) — real-time is out of scope for the spec entirely. `include`-based compound documents help with shallow relationships but don't generalize to Lineage's arbitrary-depth traversal (requirement 1) the way GraphQL's recursive field selection does. |

### Option D — gRPC (Protobuf services + `FieldMask`)

| | |
|---|---|
| **Pros** | Contract-first (a `.proto` file *is* the schema — arguably even more rigorous self-description than GraphQL SDL for requirement 5); native bidirectional streaming RPCs could express requirement 2 directly, no SSE-style bolt-on needed; `google.protobuf.FieldMask` (Google AIP-161) gives a standard partial-response mechanism close in spirit to requirement 4, and AIP-160's filter-string convention is a real, if less universally tooled, answer to requirement 1/6. |
| **Cons** | Binary wire format — fails requirement 6 outright for "usable from any ordinary HTTP client" without a gRPC-Web proxy translation layer, which reintroduces most of the complexity this option was supposed to avoid; filtering/partial-response conventions here are Google-house-style AIPs, not an independently multi-vendor-adopted spec the way OData/GraphQL/JSON:API are; heavier client tooling burden (codegen from `.proto`) than any option above, a real cost against this project's stated teaching/framework-for-anyone purpose. |

### Option E — REST-ad-hoc / PostgREST/Hasura-style declarative filter operators

| | |
|---|---|
| **Pros** | Simple, REST-native, easy to reason about for flat CRUD resources — `?amount=gt.100` (PostgREST) or `where: {amount: {_gt: 100}}` (Hasura) reads naturally as "just filter the table." This project's own pre-`ADR-003` state (bare, undeclared-field filtering) was an even simpler version of this option, and `ADR-003` already recorded exactly why unconstrained versions of it are a footgun (silent full scans on undeclared fields). |
| **Cons** | Tightly coupled to a relational operator model — no native answer to requirement 1's graph traversal or requirement 2's subscriptions at all. Telling evidence against this option specifically: **Hasura itself layers GraphQL on top of Postgres for exactly the hierarchical-query-plus-subscription need** — the tool most associated with this style of filtering didn't consider its own REST/declarative-filter surface sufficient once real-time and nested relations mattered, which is this project's exact situation. |

## Recommendation

**GraphQL**, unchanged from `ADR-037` — this comparison's purpose was to
check that decision against the full field, not to reopen it. GraphQL is
the only option that satisfies requirements 1 (native hierarchical
queries), 2 (Subscriptions as a first-class operation), and 4 (partial-
success execution model) simultaneously, without retrofitting. Every
other option is genuinely strong on its own home turf — JSON:API for
flat CRUD REST, gRPC for a same-vendor internal service mesh, OData for
query-heavy enterprise .NET shops, PostgREST/Hasura-style filters for a
thin layer directly over a relational table — but each drops at least one
must-have this project actually has. Requirement 3 (PII out of URLs) is
the one place GraphQL needed a deliberate assist (`ADR-012`'s `QUERY`
retargeting) rather than getting it for free — worth stating plainly
rather than implying GraphQL simply wins on every axis by default.

See [the GraphQL query pattern](../patterns/graphql-query-language.md)
and [the OData query pattern](../patterns/odata-query-protocol.md) for
each option written up independently of this specific project's choice
between them.
