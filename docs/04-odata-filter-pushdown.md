# OData `$filter` Pushdown Design

Goal: an incoming `$filter` string on `QUERY /follow/{event-type}` (the
HTTP `QUERY` method, `ADR-012` — the string itself travels in the request
body, not a URL) is translated into a LINQ predicate that EF Core compiles
into native SQL JSON extraction for whichever provider is active — never
evaluated client-side.

## Constraint: filterable fields must be pre-declared

Arbitrary filtering on any JSON field would force a full table scan with a
function call per row on every provider. Instead:

- Each event type's schema registration also declares a set of
  **filterable fields** (`FilterableField` entities — JSON path + data
  type + whether indexed).
- `$filter` expressions referencing a field **not** in that set are
  **rejected with 400 Bad Request** at parse time, before touching the
  database. (Decision recorded as `ADR-003` — silently falling back to a
  full scan was considered and rejected as a footgun.)

## Pipeline

1. **Parse** `$filter` string using `Microsoft.OData.UriParser`, producing
   an OData AST (`FilterClause`).
2. **Validate** every referenced property path against the event type's
   registered `FilterableFields`. Reject unknown fields immediately.
3. **Translate** the AST into a LINQ `Expression<Func<StoredEvent, bool>>`,
   where each property reference becomes a call to a provider-neutral
   marker method:

```csharp
public static class JsonFunctions
{
    // Never executed directly — EF Core intercepts and translates this call.
    public static string JsonValue(string payload, string jsonPath)
        => throw new InvalidOperationException("For LINQ translation only.");
}
```

4. **Cast** the extracted text to the field's declared `DataType`
   (`FilterableField.DataType`) before comparison — extraction returns text
   on all three providers.
5. **Compile & execute** via EF Core; the provider's `HasDbFunction`
   translation emits the native SQL.

## Per-provider translation

Register one `IJsonPathTranslator` implementation per provider, resolved via
DI based on the active provider name — not a single method with a `switch`,
to keep each provider's SQL-generation logic isolated and independently
testable.

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

`Boolean` fields (`FilterableFieldType.Boolean`, e.g. `$filter=IsActive eq true`):

| Provider | Example generated fragment |
|---|---|
| SQLite | `json_extract(Payload, '$.IsActive') = 1` — SQLite's `json_extract` returns a native `0`/`1` integer for a JSON boolean already; no `CAST` needed |
| PostgreSQL | `(Payload::jsonb ->> 'IsActive')::boolean = true` |
| SQL Server | `JSON_VALUE(Payload, '$.IsActive') = 'true'` — `JSON_VALUE` always returns text, and SQL Server's string→`BIT` conversion doesn't accept `'true'`/`'false'`, so this compares the extracted text directly rather than casting |

`DateTimeOffset` fields (`FilterableFieldType.DateTimeOffset`, e.g.
`$filter=OccurredAt gt 2026-01-01T00:00:00Z`) — published values must be
ISO-8601 for these to compare correctly, since JSON has no native date
type and extraction always returns text:

| Provider | Example generated fragment |
|---|---|
| SQLite | `json_extract(Payload, '$.OccurredAt') > '2026-01-01T00:00:00Z'` — SQLite has no native datetime type either; this is a lexicographic **text** comparison, correct only if every value is consistently zero-padded ISO-8601 (which publish-time validation should enforce for any field declared `DateTimeOffset`) |
| PostgreSQL | `(Payload::jsonb ->> 'OccurredAt')::timestamptz > '2026-01-01T00:00:00Z'` |
| SQL Server | `TRY_CAST(JSON_VALUE(Payload, '$.OccurredAt') AS DATETIMEOFFSET) > '2026-01-01T00:00:00Z'` |

Registration in `OnModelCreating`:

```csharp
var method = typeof(JsonFunctions).GetMethod(nameof(JsonFunctions.JsonValue))!;
modelBuilder.HasDbFunction(method)
    .HasTranslation(args => jsonPathTranslator.Translate(args[0], (string)((SqlConstantExpression)args[1]).Value!, fieldType));
```

(Exact translation wiring depends on EF Core version — confirm the current
`HasDbFunction`/`IMethodCallTranslatorPlugin` API surface against the EF
Core version pinned in the solution before implementing; this has changed
across EF Core major versions.)

## OData operator → SQL mapping

| OData | LINQ | SQL (conceptually) |
|---|---|---|
| `eq` | `==` | `=` |
| `ne` | `!=` | `<>` |
| `gt` / `ge` / `lt` / `le` | `>` `>=` `<` `<=` | same |
| `and` / `or` | `&&` / `\|\|` | `AND` / `OR` |
| `contains(Field,'x')` | `.Contains("x")` | `LIKE '%x%'` (extracted text only, `String` fields) |

## Indexing

`FilterableField.IsIndexed = true` triggers the registry to apply a
provider-specific expression index / computed column (see
`02-data-model.md`, "Per-provider index strategy"). The predicate
translator does not need to know whether a field is indexed — that's a
storage-layer optimization, transparent to query translation.

## Explicitly out of scope for v1

- Filtering inside JSON arrays (`any`/`all` lambda operators).
- `$orderby`, `$top`, `$skip` on the follow stream — ordering is always by
  `SequenceNumber` ascending. Where to start from (tail from now, or
  replay from a given point) is **in scope and specified** via the `mode`/
  `fromSequenceNumber` parameters — see `03-api-contracts.md` and
  `ADR-010` — just not via OData query options.
- Cross-event-type joins/projections (`$expand`/`$select`) — see
  `README.md` scope notes; data model should not preclude this later.
- Querying by parent/lineage relationship via `$filter`. `$filter` walks
  `FilterableFields` declared inside `Payload`; the `EventParents` graph is a
  separate concern with its own read surface — see the Lineage API in
  `03-api-contracts.md`. Keeping these mechanically separate avoids
  overloading OData syntax with a relationship it wasn't designed to
  express (walking an arbitrary-depth DAG).

## Suggested References

- [OASIS OData v4.01 — URL Conventions](https://docs.oasis-open.org/odata/odata/v4.01/odata-v4.01-part2-url-conventions.html) — the `$filter` grammar this design borrows (`ADR-003`).
- [RFC 9535](https://datatracker.ietf.org/doc/html/rfc9535) — JSONPath, the syntax `FilterableField.JsonPath` (`$.Amount`) follows.
- [SQLite — JSON Functions](https://sqlite.org/json1.html) — `json_extract`.
- [PostgreSQL — JSON Functions and Operators](https://www.postgresql.org/docs/current/functions-json.html) — `->>`.
- [SQL Server — JSON_VALUE](https://learn.microsoft.com/en-us/sql/t-sql/functions/json-value-transact-sql) and [TRY_CAST](https://learn.microsoft.com/en-us/sql/t-sql/functions/try-cast-transact-sql).
- [RFC 3339](https://datatracker.ietf.org/doc/html/rfc3339) — the date-time text format `DateTimeOffset` filterable fields must be published in for lexicographic/native comparison to be correct.

See `references.md` for the full bibliography.
