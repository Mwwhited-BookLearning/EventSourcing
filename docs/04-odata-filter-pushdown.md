# GraphQL Filter Pushdown Design

> **Retitled and substantially rewritten, this session — `ADR-037`'s
> queued companion doc, now written.** This file's filename is
> unchanged (stable path, `CLAUDE.md`'s "core design, read `01`–`09` in
> order" numbering) even though its *content* now describes the current
> GraphQL-argument-driven mechanism, not the superseded OData one — the
> same kind of in-place evolution `02-data-model.md` already went
> through when its own scope changed substantially. **The OData-era
> pipeline this file used to describe in full is preserved, not
> deleted**, in "Historical: the OData-era pipeline" below, per this
> project's additive-history convention — read it if you want the prior
> design's own reasoning, not just what changed.

Goal: a GraphQL query's `where` argument on a resolver returning
`IQueryable<StoredEvent>` (Follow, Lineage, registry listing — every
read surface `ADR-037` moved onto GraphQL) is translated into a LINQ
predicate that EF Core compiles into native SQL JSON extraction for
whichever provider is active — never evaluated client-side. **The
mechanism is unchanged from the OData era** (per-provider
`IJsonPathTranslator`, native `json_extract`/`->>`/`JSON_VALUE`
generation) — only *what drives it* changed, from a parsed OData AST to
HotChocolate's own filter-argument resolution.

## Constraint: filterable fields must be pre-declared — now enforced at the schema level, not at parse time

Arbitrary filtering on any JSON field would force a full table scan with
a function call per row on every provider, the same reason `ADR-003`
gave originally. What changed under GraphQL is *how* the constraint is
enforced, and it's a strict improvement:

- Each event type's schema registration still declares a set of
  **filterable fields** (`FilterableField` entities — JSON path + data
  type + whether indexed) — unchanged, `docs/data/schema-registry.md`.
- **The per-`AppId` GraphQL schema (`ADR-037`) is composed at runtime
  directly from the registry**, so a filter-input type for a given
  event type only ever exposes fields actually declared `FilterableField`
  for it. A client literally cannot *construct* a query referencing an
  undeclared field — GraphQL's own introspection and validation reject
  it before the request is even sent, let alone before touching the
  database. `ADR-003`'s OData-era enforcement (parse the request, then
  reject with `400` if a referenced field isn't declared) is now a
  *schema-shape* guarantee instead of a *runtime check* — the same rule,
  enforced earlier and more strongly.

## Pipeline

1. **HotChocolate's `[UseFiltering]` middleware** (attached to a
   resolver returning `IQueryable<T>`) resolves the GraphQL query
   document's `where` argument against the field's generated filter-
   input type — no separate parse step this design owns; HotChocolate
   parses and validates the GraphQL document itself.
2. **Translate** the resolved filter into a LINQ
   `Expression<Func<StoredEvent, bool>>` — HotChocolate does this
   translation natively for scalar/comparison operators; a property
   reference inside that expression becomes a call to the same
   provider-neutral marker method this design has used since the OData
   era:

```csharp
public static class JsonFunctions
{
    // Never executed directly — EF Core intercepts and translates this call.
    public static string JsonValue(string payload, string jsonPath)
        => throw new InvalidOperationException("For LINQ translation only.");
}
```

3. **Cast** the extracted text to the field's declared `DataType`
   (`FilterableField.DataType`) before comparison — extraction returns
   text on all three providers. Unchanged from the OData era.
4. **Compile & execute** via EF Core; the provider's `HasDbFunction`
   translation emits the native SQL. Unchanged.
5. **`[UseProjection]`** (a separate, complementary middleware, not part
   of this filtering pipeline) narrows the SQL `SELECT` list to only the
   fields the query document actually asked for — a distinct concern
   from filtering (which rows) — see "Projection is separate from
   filtering" below.

## Per-provider translation — unchanged from the OData era

Register one `IJsonPathTranslator` implementation per provider, resolved
via DI based on the active provider name — not a single method with a
`switch`, to keep each provider's SQL-generation logic isolated and
independently testable. **Nothing in this section changed when the
surface moved to GraphQL** — it's reused verbatim, the concrete
confirmation of `ADR-037`'s own claim that this mechanism "survives
unchanged."

```csharp
public interface IJsonPathTranslator
{
    SqlExpression Translate(SqlExpression payloadColumn, string jsonPath, FilterableFieldType type);
}
```

| Provider | Extraction | Example generated fragment |
|---|---|---|
| SQLite | `json_extract` | `CAST(json_extract(Payload, '$.Amount') AS REAL) > 100` |
| PostgreSQL | `jsonb ->> path` | `(Payload::jsonb ->> 'Amount')::numeric > 100` |
| SQL Server | `JSON_VALUE` | `TRY_CAST(JSON_VALUE(Payload, '$.Amount') AS DECIMAL(18,2)) > 100` |

`Boolean` fields (`FilterableFieldType.Boolean`, e.g. a GraphQL
`where: { isActive: { eq: true } }`):

| Provider | Example generated fragment |
|---|---|
| SQLite | `json_extract(Payload, '$.IsActive') = 1` — SQLite's `json_extract` returns a native `0`/`1` integer for a JSON boolean already; no `CAST` needed |
| PostgreSQL | `(Payload::jsonb ->> 'IsActive')::boolean = true` |
| SQL Server | `JSON_VALUE(Payload, '$.IsActive') = 'true'` — `JSON_VALUE` always returns text, and SQL Server's string→`BIT` conversion doesn't accept `'true'`/`'false'`, so this compares the extracted text directly rather than casting |

`DateTimeOffset` fields (`FilterableFieldType.DateTimeOffset`, e.g. a
GraphQL `where: { occurredAt: { gt: "2026-01-01T00:00:00Z" } }`) —
published values must be ISO-8601 for these to compare correctly, since
JSON has no native date type and extraction always returns text:

| Provider | Example generated fragment |
|---|---|
| SQLite | `json_extract(Payload, '$.OccurredAt') > '2026-01-01T00:00:00Z'` — SQLite has no native datetime type either; this is a lexicographic **text** comparison, correct only if every value is consistently zero-padded ISO-8601 (which publish-time validation should enforce for any field declared `DateTimeOffset`) |
| PostgreSQL | `(Payload::jsonb ->> 'OccurredAt')::timestamptz > '2026-01-01T00:00:00Z'` |
| SQL Server | `TRY_CAST(JSON_VALUE(Payload, '$.OccurredAt') AS DATETIMEOFFSET) > '2026-01-01T00:00:00Z'` |

Registration in `OnModelCreating` — unchanged:

```csharp
var method = typeof(JsonFunctions).GetMethod(nameof(JsonFunctions.JsonValue))!;
modelBuilder.HasDbFunction(method)
    .HasTranslation(args => jsonPathTranslator.Translate(args[0], (string)((SqlConstantExpression)args[1]).Value!, fieldType));
```

(Exact translation wiring depends on EF Core version — confirm the
current `HasDbFunction`/`IMethodCallTranslatorPlugin` API surface against
the EF Core version pinned in the solution before implementing; this has
changed across EF Core major versions. Unchanged concern from the OData
era — the pinned-version caveat was never about OData at all.)

## GraphQL filter operator → SQL mapping

Verified against [HotChocolate's own filtering
docs](https://chillicream.com/docs/hotchocolate/fetching-data/filtering)
before writing this table — the operator *names* changed from OData's
convention to HotChocolate's, the SQL they compile to did not:

| GraphQL filter operator | LINQ | SQL (conceptually) |
|---|---|---|
| `eq` | `==` | `=` |
| `neq` | `!=` | `<>` |
| `gt` / `gte` / `lt` / `lte` | `>` `>=` `<` `<=` | same |
| `and` / `or` (combinator on the filter-input type) | `&&` / `\|\|` | `AND` / `OR` |
| `contains` | `.Contains("x")` | `LIKE '%x%'` (extracted text only, `String` fields) |

## Projection is separate from filtering, not part of this pushdown mechanism

`[UseProjection]` (HotChocolate's field-selection middleware) narrows
the SQL `SELECT` list to only the fields a query document actually
requested — a genuinely different concern from filtering (which rows
match), even though both compile down through the same `IQueryable<T>`.
**This mechanism is not what replaces this document's OData-era
content** — `04-odata-filter-pushdown.md`'s job was always specifically
about `$filter`-shaped row selection; OData's own, much weaker `$select`
was never emphasized here, so there's no "projection pushdown" gap this
document needs to backfill. Recommended middleware order when a resolver
uses more than one together: `[UsePaging] [UseProjection] [UseFiltering]
[UseSorting]` (HotChocolate's own documented ordering).

## Indexing — unchanged

`FilterableField.IsIndexed = true` triggers the registry to apply a
provider-specific expression index / computed column (see
`docs/data/schema-registry.md`, "Per-provider index strategy"). The
predicate translator does not need to know whether a field is indexed —
that's a storage-layer optimization, transparent to query translation.

## Explicitly out of scope

- Filtering inside JSON arrays (`any`/`all`-shaped lambda operators) —
  HotChocolate's filtering does support list operators; this design has
  not extended `FilterableField`/`IJsonPathTranslator` to cover them yet.
- Cursor pagination shape (`[UsePaging]`) — a real, separate GraphQL
  concern, not detailed in this document, which stays scoped to
  filtering pushdown specifically.
- Querying by parent/lineage relationship via a `where` filter argument.
  Filtering walks `FilterableFields` declared inside `Payload`; the
  `EventParents` graph is a separate concern with its own read surface —
  see the Lineage API in `03-api-contracts.md`. Keeping these
  mechanically separate avoids overloading the filter-input type with a
  relationship it wasn't designed to express (walking an arbitrary-depth
  DAG).

## Historical: the OData-era pipeline (superseded, preserved for reference)

The following describes the pre-`ADR-037` design exactly as originally
written. Every mechanism it names below the parse/translate step (the
per-provider `IJsonPathTranslator`, the operator→SQL tables, the
indexing rule) is identical to what the current design above still
uses — only the parse/validate front end (steps 1–2) changed.

> An incoming `$filter` string on `QUERY /follow/{event-type}` (the HTTP
> `QUERY` method, `ADR-012` — the string itself travels in the request
> body, not a URL) was parsed via `Microsoft.OData.UriParser`, producing
> an OData AST (`FilterClause`), then validated against the event type's
> registered `FilterableFields` (rejecting an unknown field reference
> with `400 Bad Request` at parse time, before touching the database —
> `ADR-003`), then translated into the same LINQ predicate shape the
> current design still produces. `ADR-037` replaced only this front end
> — GraphQL's own parser/validator plus HotChocolate's `[UseFiltering]`
> resolution now does the equivalent job, earlier and schema-enforced
> rather than runtime-checked.

## Suggested References

- [HotChocolate — Filtering](https://chillicream.com/docs/hotchocolate/fetching-data/filtering) — the `[UseFiltering]` middleware this document's current pipeline drives through.
- [HotChocolate — Projections](https://chillicream.com/docs/hotchocolate/fetching-data/projections) — `[UseProjection]`, the related-but-distinct mechanism named above.
- [RFC 9535](https://datatracker.ietf.org/doc/html/rfc9535) — JSONPath, the syntax `FilterableField.JsonPath` (`$.Amount`) follows, unchanged from the OData era.
- [SQLite — JSON Functions](https://sqlite.org/json1.html) — `json_extract`.
- [PostgreSQL — JSON Functions and Operators](https://www.postgresql.org/docs/current/functions-json.html) — `->>`.
- [SQL Server — JSON_VALUE](https://learn.microsoft.com/en-us/sql/t-sql/functions/json-value-transact-sql) and [TRY_CAST](https://learn.microsoft.com/en-us/sql/t-sql/functions/try-cast-transact-sql).
- [RFC 3339](https://datatracker.ietf.org/doc/html/rfc3339) — the date-time text format `DateTimeOffset` filterable fields must be published in for lexicographic/native comparison to be correct.
- [OASIS OData v4.01 — URL Conventions](https://docs.oasis-open.org/odata/odata/v4.01/odata-v4.01-part2-url-conventions.html) — the historical `$filter` grammar `ADR-003` originally borrowed; reference-only now, per `ADR-037`.

See `references.md` for the full bibliography.
