# API Contracts — OpenAPI (Publish) and AsyncAPI (Follow)

Both documents are generated from the Schema Registry; neither hand-authors
JSON Schema. The registry is the single source of truth.

## Generation timing

Recommendation: **generate on demand**, computed fresh from current registry
state on each request to `/openapi.json` and `/asyncapi.json`, with a short
in-memory cache (e.g. 60s) invalidated on schema registration. This avoids
staleness bugs without needing a cache-invalidation pipeline. Revisit only if
the number of registered event types becomes large enough (hundreds+) that
generation cost is measurable — track as `ADR-002`.

## OpenAPI (publish side)

- OpenAPI version: 3.1.x (aligns with JSON Schema 2020-12 — schemas are
  referenced directly, not translated).
- One path template: `POST /publish/{event-type}`, with `event-type` as a
  path parameter constrained by an `enum` populated from active event types
  in the registry.
- Request body schema per event type is a `$ref` into a `components/schemas`
  section built directly from each `EventTypeDefinition.JsonSchema`.

```yaml
openapi: 3.1.0
info:
  title: Open Event Sourcing Store — Publish API
  version: "1.0"
paths:
  /publish/{event-type}:
    post:
      parameters:
        - name: event-type
          in: path
          required: true
          schema:
            type: string
            enum: [OrderPlaced, OrderCancelled]   # generated from registry
      requestBody:
        required: true
        content:
          application/json:
            schema:
              oneOf:
                - $ref: '#/components/schemas/OrderPlaced'
                - $ref: '#/components/schemas/OrderCancelled'
      responses:
        '201':
          description: Event accepted and appended
        '400':
          description: Payload failed schema validation
        '404':
          description: Unknown event-type
components:
  schemas:
    OrderPlaced:
      $ref: '#/schemas-from-registry/OrderPlaced/2'   # inlined at generation time
```

At generation time, `$ref`s under `components/schemas` are populated by
inlining the stored JSON Schema text for the active version of each event
type — no manual authoring, no drift.

## AsyncAPI (follow side)

- AsyncAPI version: 3.0.x.
- One channel per event type (or one parameterized channel — decide based on
  how many event types typically exist; parameterized is simpler to
  maintain if the set is large).
- SSE binding (`bindings.sse`) on the channel.
- `$filter` documented as a channel/operation parameter (string, OData
  syntax), not translated into structured AsyncAPI parameters — the schema
  registry knows which fields are filterable, and that list can be surfaced
  in the parameter description for discoverability.
- Message payload again `$ref`s the same registry-sourced JSON Schema.

```yaml
asyncapi: 3.0.0
info:
  title: Open Event Sourcing Store — Follow API
  version: "1.0"
channels:
  follow-order-placed:
    address: /follow/OrderPlaced
    bindings:
      sse:
        method: GET
    parameters:
      filter:
        description: >
          OData $filter expression. Filterable fields for OrderPlaced:
          Amount (Number), Status (String).
    messages:
      OrderPlaced:
        payload:
          $ref: '#/components/schemas/OrderPlaced'
operations:
  receiveOrderPlaced:
    action: receive
    channel:
      $ref: '#/channels/follow-order-placed'
components:
  schemas:
    OrderPlaced:
      $ref: '#/schemas-from-registry/OrderPlaced/2'
```

## Shared schema source

Both generators call the same internal method:

```csharp
public interface ISchemaRegistryReader
{
    Task<IReadOnlyList<EventTypeDefinition>> GetActiveEventTypesAsync();
}
```

`OpenApiDocumentBuilder` and `AsyncApiDocumentBuilder` both depend on this
interface only — neither talks to EF Core directly, keeping spec generation
decoupled from persistence.
