# Schema Registry — Lifecycle and Validation

> **Partially superseded, per `ADR-037`.** `QUERY /registry` (OData
> `$top`/`$skip` listing) is replaced by a GraphQL query — same
> pagination semantics, different transport/syntax (see
> `features/schema-registry.md`'s banner). Registration itself (`PUT
> /registry/{event-type}`) and the validation rules described below are
> unaffected — `ADR-037` only replaces query-side surfaces, and
> registration is a write, not a query. Not yet rewritten for the actual
> GraphQL listing shape — tracked as outstanding propagation work
> (`CLAUDE.md`).

All endpoints in this document require the `registry:admin` scope (see
`03-api-contracts.md`, "Authentication & Authorization", and `ADR-006`) —
schema registration controls validation rules and filterable-field indexes
for the whole store, so it is treated as an administrative operation, not a
read available to every caller.

## Registration API

```
PUT   /registry/{event-type}            -- register new version
GET   /registry/{event-type}            -- get active version's schema
GET   /registry/{event-type}/{version}  -- get specific version
QUERY /registry                         -- list all registered event types, paginated
```

`QUERY /registry` (`ADR-012`, replacing the earlier `GET /registry`) takes
optional `$top`/`$skip` in the request body
(`application/x-www-form-urlencoded`) — a simple limit/offset slice over
the full list, no `@odata.count`/`@odata.nextLink`. Omitting both returns
every registered event type, unchanged from before `ADR-012`. The two
single-event-type lookups above have nothing to paginate or filter and
stay plain `GET`.

Registration payload:

```json
{
  "jsonSchema": {
    "type": "object",
    "properties": {
      "Amount": { "type": "number" },
      "Status": { "type": "string" },
      "CustomerTaxId": {
        "type": "string",
        "x-masking": {
          "requiredClaim": "pii:view",
          "strategy": "FixedValue",
          "maskedValue": "***",
          "regulatoryClassification": "PHI",
          "governanceBody": "HHS/OCR",
          "regulationReference": "HIPAA 45 CFR §164.514(b)"
        }
      }
    },
    "required": ["Amount", "Status", "CustomerTaxId"]
  },
  "filterableFields": [
    { "jsonPath": "$.Amount", "dataType": "Number", "isIndexed": true },
    { "jsonPath": "$.Status", "dataType": "String", "isIndexed": false }
  ],
  "parentValidationMode": "Strict",
  "requiredClaims": [
    { "direction": "Publish", "claim": "clearance:secret" },
    { "direction": "Read", "claim": "clearance:secret" }
  ],
  "changeKind": "Full",
  "upcastFromPrevious": "Amount as Amount, Status as Status"
}
```

(`upcastFromPrevious` is omitted entirely for version `1` of any event
type — there is no "previous" to upcast from.)

`parentValidationMode` is optional and defaults to `"Strict"` (the other
value is `"Permissive"` — see `02-data-model.md`, "Event lineage", and
`ADR-005`). It governs how `parentEventIds` on a `POST /publish/{event-type}`
for *this* event type are validated; it has no effect on this event type's
own eligibility to be listed as someone else's parent, which is unrestricted.

`changeKind` is **required, with no default** — `"Full"` or `"Partial"`,
one or the other must always be supplied (`400` if omitted or any other
value). Unlike the three fields above, there's no safe default to fall
back to: it tells a CQRS read-model projection (`09-cqrs-read-models.md`,
`ADR-016`) whether an event of this type replaces everything known about
its key or merges only the fields it carries — guessing wrong here would
silently corrupt every projection over the type, not merely omit an
optional restriction.

`requiredClaims` is optional and defaults to an empty list (no extra
restriction) — a **list** of `{direction, claim}` pairs, not a pair of
singular fields (`ADR-050` generalized `ADR-008`'s original "exactly one
claim per direction" to `OR` semantics within a direction: holding *any
one* of the `Publish`-direction, or `Read`-direction, entries satisfies
that direction's gate). Each `direction` is `"Publish"` or `"Read"`;
each `claim` is a `"type:value"` string — see `02-data-model.md`,
"Event-type security", `ADR-008`, and `ADR-050`. Registering these still
only requires `registry:admin` — defining who may touch an event type's
data is treated as part of the same administrative capability as
defining the type itself, not a separate scope.

**Masking** (`ADR-009`, built — see `08-build-plan.md`, "Property-Level
Masking") is declared differently from `requiredClaims` above: it's
*inside* `jsonSchema` itself, as an `x-masking` extension, not a sibling
field in this registration envelope — there's no `"masking": [...]` array
alongside `filterableFields`. `strategy` must be one of `"FixedValue"`,
`"PartialReveal"`, or `"Hash"` (any other value is rejected) —
`MaskingSchemaValidator` (`src/EventStore.SchemaRegistry/
MaskingSchemaValidator.cs`) validates all three. Unlike an earlier
`null`-out design, there is
**no constraint on the property's own type or its `required` status** —
`CustomerTaxId` above is masked *and* required, which is exactly the case
the wrapper approach was chosen to support. `x-masking` may be placed on a
scalar property (as above), on an array's `items` when `items` is itself
scalar (wraps each element), or on a property nested inside a
complex-object `items` schema (wraps just that property per element) — but
not directly on a property whose own type is `object` or `array`.

`regulatoryClassification`, `governanceBody`, and `regulationReference` are
all optional, schema-only descriptive strings on `x-masking` — free text in
v1, no controlled vocabulary enforced (see `ADR-009`'s consequences for
why). They carry no runtime behavior at all: nothing about masking
enforcement, the wrapper shape, or any of the three masking strategies
reads them.

## Registration steps

1. Validate the submitted document is itself a well-formed JSON Schema
   (structural validation, not business validation).
2. Validate each `filterableFields` entry's `jsonPath` actually resolves
   against the schema's declared properties.
3. Validate `parentValidationMode`, if present, is one of `Strict` /
   `Permissive`.
4. Validate each `requiredClaims[]` entry's `direction` is `"Publish"` or
   `"Read"` and its `claim` is a non-empty `"type:value"` string (reject
   `400` on a malformed entry, e.g. an unrecognized direction or a claim
   missing the `:` separator, before persisting anything).
   Validate `changeKind` is present and is exactly `"Full"` or `"Partial"`
   — reject `400` if missing or any other value (`ADR-016`; unlike the two
   claim fields, this one has no default to fall back to). If
   `upcastFromPrevious` is present (only meaningful for version `>= 2`),
   parse it as an `"<expression> as <alias>"`, comma-separated clause
   list (`UpcastExpressionListParser`) and validate every alias names a
   real property of *this* version's schema, plus that every individual
   expression itself compiles under `IUpcastExpressionEvaluator`
   (`ADR-018`/`ADR-053` — CEL by default, `CelUpcastExpressionEvaluator`;
   the earlier OData `compute()` expression grammar this field's clause
   syntax was originally modeled on is not what a clause's own
   `<expression>` half is evaluated as) — reject `400` on a parse
   failure, a compile failure, or an unmapped alias.
5. Scan `jsonSchema` recursively for any node carrying `x-masking`: reject
   `400` if `strategy` is anything other than `"FixedValue"`,
   `"PartialReveal"`, or `"Hash"`, if
   `requiredClaim` is malformed, if `regulatoryClassification`/
   `governanceBody`/`regulationReference` are present but not non-empty
   strings, or if the annotation is placed directly on a property whose own
   type is `object` or `array` (only a scalar node, or an array's scalar
   `items`, or a property nested inside a complex-object `items` schema, is
   valid — see `ADR-009`). Nothing further is persisted for masking beyond
   the schema text itself.
6. Determine version number: increment from the current active version for
   this event type name (or `1` if new).
7. Persist `EventTypeDefinition` (including `ParentValidationMode`,
   `RequiredClaims`, `ChangeKind`) + `FilterableField`
   rows in a single transaction. `x-masking` extensions are not extracted
   into their own columns — they persist as part of the `JsonSchema` text
   itself.
8. For each `FilterableField` with `IsIndexed = true`, apply the
   provider-specific index/computed-column migration (see
   `04-odata-filter-pushdown.md`).
9. Mark the new version `IsActive = true`; mark the prior version
   `IsActive = false` (previous versions remain queryable for events
   already stored under them — publish validates against whichever version
   is active *at publish time*, and `StoredEvent.SchemaVersion` records
   which version validated a given event).
10. Invalidate the OpenAPI/AsyncAPI cache: `IMemoryCache.Remove("openapi-document")`
    and `IMemoryCache.Remove("asyncapi-document")` — see `ADR-002` and
    `06-solution-structure.md` for the concrete cache/build mechanism.

Changing `ParentValidationMode` on a new version only affects publishes
validated against that version going forward; existing `EventParents` rows
recorded under the previous version are untouched — consistent with the
append-only, no-mutation treatment of schema versioning generally.

**Registering a new version does not, by itself, do anything about
reading the old version's events back in the new shape** — that needs an
optional `upcastFromPrevious` field on the registration payload: a
comma-separated `"<expression> as <alias>"` clause list (`ADR-018`), e.g.
`"upcastFromPrevious": "Amount as Amount, 'USD' as Currency"`, where each
`<expression>` is evaluated by `IUpcastExpressionEvaluator` — CEL by
default (`CelUpcastExpressionEvaluator`), `ADR-053`'s keyed registration
also allows JSONata as a second, configuration-selected option; **not**
OData `compute()` syntax, which this field's clause grammar was
originally modeled on but no longer evaluates as. Unlike the earlier
code-registered sketch of this same ADR, this *is* an ordinary
registration field, validated the same way `x-masking` already is: each
alias must name a real property of this version's schema, and the
expression itself must compile under the active `IUpcastExpressionEvaluator`
— both rejected `400` if not. Evaluating
the expression against real historical data to confirm the output
actually satisfies the schema is **not** checked at registration — see
`ADR-018`'s consequences for why this only narrows, not closes, the
open compatibility-enforcement question.

Changing `RequiredClaims` on a new version takes
effect immediately for that event type — unlike `SchemaVersion`, there is no
"claim required as of version N" history to preserve; the check always uses
whichever version is currently active. Tightening the Publish- or
Read-direction entries doesn't retroactively change what a caller could
already see in a live Follow connection opened before the change — it only
affects new connection attempts and new lineage queries, since the check
runs once at connect time (Follow) or once per request (Lineage), not
continuously against an open stream.

Adding, removing, or changing `x-masking` on a property likewise takes
effect for the version it's registered on, applied against whichever
version is currently active — same "no retroactive effect on an already-open
Follow connection" caveat as above, since masking is computed once at
connect time alongside the Read-direction `RequiredClaims` check.

## Validation at publish time — rewritten for `ADR-023`'s persist-everything posture

> **Superseded by `ADR-023`, rewritten this session.** The
> `SchemaValidationService`/`ParentLinkService` pair this section used to
> describe — synchronous, both returning a blocking `400`/`404` before any
> write — never actually exists as real code, and describes the
> pre-`ADR-023` design this project moved off of. The real split is
> `EventStore.Inbox.PublishService` (synchronous, still genuinely
> blocking for the few checks below) handing off to
> `EventStore.Router.RouterWorker` (asynchronous, advisory-only for
> schema conformance) — see `03-api-contracts.md`'s own `202`/`400`
> response table for the caller-facing contract this produces.

**`PublishService.PublishAsync`** (`src/EventStore.Inbox/
PublishService.cs`) is the only synchronous gate, checked in this order,
after confirming `eventType` is registered under *some* version at all
(`404` if not — the one case with no schema/`AppId` context to persist
against):

1. **Idempotency** (`ADR-011`) — if the request supplied `eventId`, look
   up `StoredEvent` by `EventId` immediately, before any of the checks
   below. Found + matching `PayloadHash` → replay the original response,
   no new write. Found + different `PayloadHash` → `409`. Not found →
   fall through to the checks below, using the caller's `EventId` for
   the new row instead of a generated one.
2. **`RequiredClaims` (`Publish` direction)** (`ADR-008`/`ADR-050`) —
   checked against the *active* version's claims (never the caller's
   declared `schemaVersion`, which under `ADR-023` might not even name a
   registered version). `403` if configured and the caller's token holds
   none of them.
3. **Step-up authentication** (`ADR-066`, RFC 9470) — if the active
   definition configures `RequiredSignature`, a `401` step-up challenge
   short-circuits an insufficiently-authenticated caller; see
   `03-api-contracts.md`.
4. **Parent-link validation** (`ADR-005`) — inlined here, not a separate
   service: if the active definition's `ParentValidationMode` is
   `Strict` and `parentEventIds` is non-empty, every referenced
   `EventId` must already exist; `400` naming the missing ones if not.
   `Permissive` mode allows dangling references through unchanged. This
   is the one content-shaped check `ADR-023` left blocking — everything
   below it is advisory.

**Nothing above validates `payload` against its JSON Schema, and nothing
here produces a `400` for an unknown `schemaVersion`, a schema-invalid
payload, or a failed upcast.** Once these checks pass, the event is
appended unconditionally with `Status: "received"`.

**`RouterWorker`** (`src/EventStore.Router/RouterWorker.cs`) then picks
up every `"received"` row asynchronously and resolves an advisory
`SchemaStatus`, resolving the *declared* `SchemaVersion` (`AppId` +
`EventType` + `SchemaVersion`) rather than the active one:

- Declared version registered, payload validates against it →
  `SchemaStatus: "conformant"`.
- Declared version registered, payload does not validate →
  `SchemaStatus: "invalid"`.
- Declared version never registered at all (and not newer than the
  active version — see below) → `SchemaStatus: "unknown"`.

**None of the three above ever block `Status` from reaching `"applied"`**
— `SchemaStatus` is purely advisory, exactly `ADR-023`'s point. The one
case `RouterWorker` genuinely defers on (leaves at `Status: "received"`,
retried every subsequent tick) is a declared `SchemaVersion` *ahead* of
anything this deployment's registry has ever seen — `ADR-038`'s
rolled-back-deployment signal, not a validation failure.

## Versioning policy

- New, backward-compatible fields (optional, with defaults): safe to add as
  a new version without special handling.
- Breaking changes (removing/renaming required fields, changing types):
  register as a new version; do not mutate the old version's stored schema
  text, since existing `StoredEvent` rows reference it by
  `SchemaVersion` for replay/audit purposes.
- Compatibility-mode enforcement (e.g. rejecting breaking changes outright,
  in the style of Confluent Schema Registry's BACKWARD/FORWARD/FULL modes
  — see `references.md`) is not in v1 — flag as a v2 candidate if needed.
  `ADR-018`/`ADR-053`'s `IUpcastExpressionEvaluator` (`src/
  EventStore.Abstractions/IUpcastExpressionEvaluator.cs`, CEL/JSONata-
  driven) builds the *transform* half of schema evolution (reshaping an
  old payload forward); this bullet is about the *enforcement* half
  (stopping an incompatible version from being registered at all) — the
  two are independent, and only the first is built. **`IEventUpcaster` is
  a different, separately-catalogued seam** (`docs/extensibility-
  points.md`) — not yet built, and not what `upcastFromPrevious`'s
  transform mechanism actually is; don't conflate the two.

## Relationship to spec generation

The registry is read-only from the perspective of `OpenApiDocumentBuilder`
and `AsyncApiDocumentBuilder` (see `03-api-contracts.md`) — they only ever
call `ISchemaRegistryReader.GetActiveEventTypesAsync()`. This keeps
registration logic and spec generation logic independently testable.

## Suggested References

- [JSON Schema (2020-12)](https://json-schema.org/specification) — the registration payload's `jsonSchema` field.
- [OpenAPI Specification v3.1.1](https://spec.openapis.org/oas/v3.1.1.html) / [AsyncAPI v3.0](https://www.asyncapi.com/docs/reference/specification/v3.0.0) — the two generated contract formats (`ADR-002`).
- [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET) — the .NET object model both document builders share.
- [Confluent Schema Registry — Schema Evolution](https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html) — the compatibility-mode model referenced above as the not-yet-built enforcement half of schema evolution.

See `references.md` for the full bibliography.
