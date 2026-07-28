# Schema Registry — Lifecycle and Validation

## Registration API

```
PUT  /registry/{event-type}            -- register new version
GET  /registry/{event-type}            -- get active version's schema
GET  /registry/{event-type}/{version}  -- get specific version
GET  /registry                         -- list all registered event types
```

Registration payload:

```json
{
  "jsonSchema": { "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["Amount", "Status"] },
  "filterableFields": [
    { "jsonPath": "$.Amount", "dataType": "Number", "isIndexed": true },
    { "jsonPath": "$.Status", "dataType": "String", "isIndexed": false }
  ]
}
```

## Registration steps

1. Validate the submitted document is itself a well-formed JSON Schema
   (structural validation, not business validation).
2. Validate each `filterableFields` entry's `jsonPath` actually resolves
   against the schema's declared properties.
3. Determine version number: increment from the current active version for
   this event type name (or `1` if new).
4. Persist `EventTypeDefinition` + `FilterableField` rows in a single
   transaction.
5. For each `FilterableField` with `IsIndexed = true`, apply the
   provider-specific index/computed-column migration (see
   `04-odata-filter-pushdown.md`).
6. Mark the new version `IsActive = true`; mark the prior version
   `IsActive = false` (previous versions remain queryable for events
   already stored under them — publish validates against whichever version
   is active *at publish time*, and `StoredEvent.SchemaVersion` records
   which version validated a given event).
7. Invalidate the OpenAPI/AsyncAPI cache (if using the cached-generation
   approach — see `ADR-002`).

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
