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
this design's own hand-rolled `GraphQlFilterPredicateBuilder` resolving
a flat, static `EventFilterInput` list (not HotChocolate's own
filter-argument resolution — see "Pipeline" below for why).

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

1. **`GraphQlFilterPredicateBuilder.Build`** (`src/EventStore.GraphQL/
   GraphQlFilterPredicateBuilder.cs`), not HotChocolate's `[UseFiltering]`
   middleware — the GraphQL schema for Follow is built dynamically, per
   registered event type (`FollowSubscriptionTypeModule`'s `ITypeModule`),
   so there is no bound CLR type for `[UseFiltering]` to reflect over.
   Instead, `where` is declared as a hand-written, static
   `[EventFilterInput!]` list argument; the resolver reads it explicitly
   via `ctx.ArgumentValue<IReadOnlyList<EventFilterInput>?>("where")` and
   hands it to the builder alongside the event type's registered
   `FilterableFields`.
2. **Translate** each clause into a LINQ
   `Expression<Func<StoredEvent, bool>>` by hand —
   `GraphQlFilterPredicateBuilder` AND-combines every entry in the list
   (there is no `and`/`or` combinator; see "GraphQL filter operator → SQL
   mapping" below) and reuses `FilterPredicateBuilder.
   BuildPropertyAccessExpression`/`BuildConstantExpression` (the same
   OData-era builder, `src/EventStore.Follow.Api/
   FilterPredicateBuilder.cs`) for the actual expression-tree
   construction — one hand-rolled front end for GraphQL, one for the
   preserved OData `$filter` string, both driving the identical
   expression-building code beneath. A property reference inside that
   expression becomes a call to one of four provider-neutral marker
   methods, selected by the field's own `FilterableFieldType`:

```csharp
public static class JsonFunctions
{
    // Never executed directly — EF Core intercepts and translates each call.
    public static string JsonValueAsString(string payload, string jsonPath) =>
        throw new InvalidOperationException("For LINQ translation only.");
    public static double JsonValueAsNumber(string payload, string jsonPath) =>
        throw new InvalidOperationException("For LINQ translation only.");
    public static bool JsonValueAsBoolean(string payload, string jsonPath) =>
        throw new InvalidOperationException("For LINQ translation only.");
    public static DateTimeOffset JsonValueAsDateTimeOffset(string payload, string jsonPath) =>
        throw new InvalidOperationException("For LINQ translation only.");

    // One marker method per FilterableFieldType, not one generic method,
    // so each C# return type matches what a LINQ comparison against that
    // type's constants actually needs to type-check.
    public static string MethodNameFor(FilterableFieldType type) => type switch
    {
        FilterableFieldType.String => nameof(JsonValueAsString),
        FilterableFieldType.Number => nameof(JsonValueAsNumber),
        FilterableFieldType.Boolean => nameof(JsonValueAsBoolean),
        FilterableFieldType.DateTimeOffset => nameof(JsonValueAsDateTimeOffset),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
```

3. **Cast** the extracted text to the field's declared `DataType`
   (`FilterableField.DataType`) before comparison — extraction returns
   text on all three providers. Unchanged from the OData era.
4. **Compile & execute** via EF Core; the provider's `HasDbFunction`
   translation emits the native SQL. Unchanged.
5. A separate, complementary concern — narrowing the SQL `SELECT` list
   to only the fields the query document actually asked for, distinct
   from filtering (which rows) — is not currently wired up anywhere in
   this pipeline; see "Projection is separate from filtering" below.

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
`where: [{ field: "isActive", eq: "true" }]` — values always travel as
strings, cast server-side per the field's declared `FilterableFieldType`):

| Provider | Example generated fragment |
|---|---|
| SQLite | `json_extract(Payload, '$.IsActive') = 1` — SQLite's `json_extract` returns a native `0`/`1` integer for a JSON boolean already; no `CAST` needed |
| PostgreSQL | `(Payload::jsonb ->> 'IsActive')::boolean = true` |
| SQL Server | `JSON_VALUE(Payload, '$.IsActive') = 'true'` — `JSON_VALUE` always returns text, and SQL Server's string→`BIT` conversion doesn't accept `'true'`/`'false'`, so this compares the extracted text directly rather than casting |

`DateTimeOffset` fields (`FilterableFieldType.DateTimeOffset`, e.g. a
GraphQL `where: [{ field: "occurredAt", gt: "2026-01-01T00:00:00Z" }]`) —
published values must be ISO-8601 for these to compare correctly, since
JSON has no native date type and extraction always returns text:

| Provider | Example generated fragment |
|---|---|
| SQLite | `json_extract(Payload, '$.OccurredAt') > '2026-01-01T00:00:00Z'` — SQLite has no native datetime type either; this is a lexicographic **text** comparison, correct only if every value is consistently zero-padded ISO-8601 (which publish-time validation should enforce for any field declared `DateTimeOffset`) |
| PostgreSQL | `(Payload::jsonb ->> 'OccurredAt')::timestamptz > '2026-01-01T00:00:00Z'` |
| SQL Server | `TRY_CAST(JSON_VALUE(Payload, '$.OccurredAt') AS DATETIMEOFFSET) > '2026-01-01T00:00:00Z'` |

Registration in `OnModelCreating` — one `HasDbFunction` call per marker
method (four total, one per `FilterableFieldType`), each closing over its
own `fieldType` so the emitted `CAST`/comparison matches that method's
own C# return type:

```csharp
foreach (var fieldType in Enum.GetValues<FilterableFieldType>())
{
    var method = typeof(JsonFunctions).GetMethod(JsonFunctions.MethodNameFor(fieldType))!;
    modelBuilder.HasDbFunction(method)
        .HasTranslation(args => jsonPathTranslator.Translate(args[0], (string)((SqlConstantExpression)args[1]).Value!, fieldType));
}
```

(Exact translation wiring depends on EF Core version — confirm the
current `HasDbFunction`/`IMethodCallTranslatorPlugin` API surface against
the EF Core version pinned in the solution before implementing; this has
changed across EF Core major versions. Unchanged concern from the OData
era — the pinned-version caveat was never about OData at all.)

## GraphQL filter operator → SQL mapping

`EventFilterInput` (`Field` plus one of `Eq`/`Neq`/`Gt`/`Gte`/`Lt`/`Lte`/
`Contains`) is this design's own static, hand-written GraphQL input
type — a deliberate narrowing from HotChocolate's `[UseFiltering]`
convention (see the Pipeline section above for why), so the operator
*names* below are this design's own choice, not adopted from
HotChocolate's filtering docs:

| GraphQL filter operator | LINQ | SQL (conceptually) |
|---|---|---|
| `eq` | `==` | `=` |
| `neq` | `!=` | `<>` |
| `gt` / `gte` / `lt` / `lte` | `>` `>=` `<` `<=` | same |
| `contains` | `.Contains("x")` | `LIKE '%x%'` (extracted text only, `String` fields) |

**There is no `and`/`or` combinator.** `where` is a flat list of
`EventFilterInput` items; `GraphQlFilterPredicateBuilder` AND-combines
every item in the list (and every operator named within one item) —
there is no nested-object syntax and no way to express `OR` at all. A
client wanting an `OR` across values must issue separate
subscriptions/queries, one per branch.

## Projection is separate from filtering, not part of this pushdown mechanism

`[UseProjection]` (HotChocolate's field-selection middleware) *would*
narrow the SQL `SELECT` list to only the fields a query document
actually requested — a genuinely different concern from filtering
(which rows match), even though both would compile down through the
same `IQueryable<T>`. **This mechanism is not what replaces this
document's OData-era content** — `04-odata-filter-pushdown.md`'s job
was always specifically about `$filter`-shaped row selection; OData's
own, much weaker `$select` was never emphasized here, so there's no
"projection pushdown" gap this document needs to backfill.

None of `[UseFiltering]`/`[UseProjection]`/`[UseSorting]`/`[UsePaging]`
are actually attached anywhere in this codebase's resolvers today —
`EventFilterInput.cs`'s own code comment explains why `[UseFiltering]`
specifically wasn't viable for Follow's dynamically-built-per-event-type
schema, and `LineageQueries.cs`'s own comment explains why `[UsePaging]`
wasn't adopted for Lineage either (see `03-api-contracts.md`). The
"recommended middleware order" HotChocolate itself documents
(`[UsePaging] [UseProjection] [UseFiltering] [UseSorting]`) is general
background for readers evaluating HotChocolate's own filtering docs, not
a description of anything currently wired up in this design.

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
> — GraphQL's own parser/validator (schema/type validation of the
> subscription field and payload selection) plus this design's own
> hand-rolled `GraphQlFilterPredicateBuilder` (not HotChocolate's
> `[UseFiltering]` — see "Pipeline" above for why) now does the
> equivalent job for `where`, earlier and schema-enforced for field
> *selection*, runtime-checked (a `GraphQLException` on an undeclared
> `Field`) for the filter argument specifically.

## Suggested References

- [HotChocolate — Filtering](https://chillicream.com/docs/hotchocolate/fetching-data/filtering) — the `[UseFiltering]` convention this document's operator names loosely follow; considered and not adopted for Follow's dynamically-built schema (see "Pipeline" above), reference-only.
- [HotChocolate — Projections](https://chillicream.com/docs/hotchocolate/fetching-data/projections) — `[UseProjection]`, the related-but-distinct mechanism named above; likewise not currently wired up anywhere in this codebase.
- [RFC 9535](https://datatracker.ietf.org/doc/html/rfc9535) — JSONPath, the syntax `FilterableField.JsonPath` (`$.Amount`) follows, unchanged from the OData era.
- [SQLite — JSON Functions](https://sqlite.org/json1.html) — `json_extract`.
- [PostgreSQL — JSON Functions and Operators](https://www.postgresql.org/docs/current/functions-json.html) — `->>`.
- [SQL Server — JSON_VALUE](https://learn.microsoft.com/en-us/sql/t-sql/functions/json-value-transact-sql) and [TRY_CAST](https://learn.microsoft.com/en-us/sql/t-sql/functions/try-cast-transact-sql).
- [RFC 3339](https://datatracker.ietf.org/doc/html/rfc3339) — the date-time text format `DateTimeOffset` filterable fields must be published in for lexicographic/native comparison to be correct.
- [OASIS OData v4.01 — URL Conventions](https://docs.oasis-open.org/odata/odata/v4.01/odata-v4.01-part2-url-conventions.html) — the historical `$filter` grammar `ADR-003` originally borrowed; reference-only now, per `ADR-037`.

See `references.md` for the full bibliography.
