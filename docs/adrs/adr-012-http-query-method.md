[← ADR index](../07-adrs.md)

# ADR-012: HTTP `QUERY` method (RFC 10008) for OData data-queries, replacing `GET`

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
