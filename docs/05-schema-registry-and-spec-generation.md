# Schema Registry — Lifecycle and Validation

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
  "requiredPublishClaim": "clearance:secret",
  "requiredReadClaim": "clearance:secret"
}
```

`parentValidationMode` is optional and defaults to `"Strict"` (the other
value is `"Permissive"` — see `02-data-model.md`, "Event lineage", and
`ADR-005`). It governs how `parentEventIds` on a `POST /publish/{event-type}`
for *this* event type are validated; it has no effect on this event type's
own eligibility to be listed as someone else's parent, which is unrestricted.

`requiredPublishClaim` and `requiredReadClaim` are both optional and default
to unset (no extra restriction). Each is a single `"type:value"` string —
see `02-data-model.md`, "Event-type security", and `ADR-008`. Registering
these still only requires `registry:admin` — defining who may touch an
event type's data is treated as part of the same administrative capability
as defining the type itself, not a separate scope.

**Masking** (`ADR-009`, design accepted, build deprioritized to after
Phases 0–6) is declared differently from the three fields above: it's
*inside* `jsonSchema` itself, as an `x-masking` extension, not a sibling
field in this registration envelope — there's no `"masking": [...]` array
alongside `filterableFields`. `strategy` must be `"FixedValue"` in v1 (any
other value is rejected). Unlike an earlier `null`-out design, there is
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
enforcement, the wrapper shape, or the `FixedValue` strategy reads them.

## Registration steps

1. Validate the submitted document is itself a well-formed JSON Schema
   (structural validation, not business validation).
2. Validate each `filterableFields` entry's `jsonPath` actually resolves
   against the schema's declared properties.
3. Validate `parentValidationMode`, if present, is one of `Strict` /
   `Permissive`.
4. Validate `requiredPublishClaim`/`requiredReadClaim`, if present, are each
   a non-empty `"type:value"` string (reject `400` on a malformed claim
   string, e.g. missing the `:` separator, before persisting anything).
5. Scan `jsonSchema` recursively for any node carrying `x-masking`: reject
   `400` if `strategy` is anything other than `"FixedValue"`, if
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
   `RequiredPublishClaim`, `RequiredReadClaim`) + `FilterableField` rows in a
   single transaction. `x-masking` extensions are not extracted into their
   own columns — they persist as part of the `JsonSchema` text itself.
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

Changing `RequiredPublishClaim`/`RequiredReadClaim` on a new version takes
effect immediately for that event type — unlike `SchemaVersion`, there is no
"claim required as of version N" history to preserve; the check always uses
whichever version is currently active. Tightening either claim doesn't
retroactively change what a caller could already see in a live Follow
connection opened before the change — it only affects new connection
attempts and new lineage queries, since the check runs once at connect time
(Follow) or once per request (Lineage), not continuously against an open
stream.

Adding, removing, or changing `x-masking` on a property likewise takes
effect for the version it's registered on, applied against whichever
version is currently active — same "no retroactive effect on an already-open
Follow connection" caveat as above, since masking is computed once at
connect time alongside `RequiredReadClaim`.

## Validation at publish time

```csharp
public class SchemaValidationService
{
    public async Task<ValidationResult> ValidateAsync(string eventType, string payloadJson)
    {
        var definition = await _registry.GetActiveAsync(eventType)
            ?? throw new UnknownEventTypeException(eventType);

        var schema = JsonSchema.FromText(definition.JsonSchema); // JsonSchema.Net
        var results = schema.Evaluate(JsonNode.Parse(payloadJson));

        return results.IsValid
            ? ValidationResult.Success(definition.Version)
            : ValidationResult.Failure(results.Errors);
    }
}
```

- `404` if `eventType` has no registered schema at all.
- `400` with the JSON Schema validation error list if the payload fails
  validation — no partial writes.
- On success, `StoredEvent.SchemaVersion` is set to the version that
  validated it, so historical events remain interpretable even after the
  schema evolves.

`SchemaValidationService` only validates `payload`. A separate,
independently testable `ParentLinkService` validates `parentEventIds`
against the active version's `ParentValidationMode`:

```csharp
public class ParentLinkService
{
    public async Task<ParentLinkResult> ValidateAsync(
        EventTypeDefinition definition, IReadOnlyList<Guid> parentEventIds)
    {
        if (definition.ParentValidationMode == ParentValidationMode.Permissive)
            return ParentLinkResult.Success(parentEventIds); // dangling refs allowed as-is

        var missing = await _events.FindMissingEventIdsAsync(parentEventIds);
        return missing.Count == 0
            ? ParentLinkResult.Success(parentEventIds)
            : ParentLinkResult.Failure(missing); // 400 — Strict mode
    }
}
```

Both `SchemaValidationService` and `ParentLinkService` must pass before the
`EventAppender` writes the `StoredEvent` + `EventParents` rows — a payload
failure and a Strict-mode missing-parent failure both produce `400` with no
partial write, same as today's payload-only validation.

If the request supplied `eventId` (`ADR-011`), an idempotency check runs
**before** either of the above, right after `RequiredPublishClaim`
(`ADR-008`): look up `StoredEvent` by `EventId`. Found + matching
`PayloadHash` → replay the original response, skip
`SchemaValidationService`/`ParentLinkService`/`EventAppender` entirely, no
write. Found + different `PayloadHash` → `409`, likewise skipping further
validation — the conflict is a definitive answer regardless of whether the
new content would itself have been valid. Not found → proceed through
`SchemaValidationService`/`ParentLinkService`/`EventAppender` exactly as
below, using the caller's `EventId` for the new row instead of a generated
one.

## Versioning policy

- New, backward-compatible fields (optional, with defaults): safe to add as
  a new version without special handling.
- Breaking changes (removing/renaming required fields, changing types):
  register as a new version; do not mutate the old version's stored schema
  text, since existing `StoredEvent` rows reference it by
  `SchemaVersion` for replay/audit purposes.
- Compatibility-mode enforcement (e.g. rejecting breaking changes outright)
  is not in v1 — flag as a v2 candidate if needed.

## Relationship to spec generation

The registry is read-only from the perspective of `OpenApiDocumentBuilder`
and `AsyncApiDocumentBuilder` (see `03-api-contracts.md`) — they only ever
call `ISchemaRegistryReader.GetActiveEventTypesAsync()`. This keeps
registration logic and spec generation logic independently testable.
