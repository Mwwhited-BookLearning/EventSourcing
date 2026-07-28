# API Contracts — OpenAPI (Publish) and AsyncAPI (Follow)

Both documents are generated from the Schema Registry; neither hand-authors
JSON Schema. The registry is the single source of truth.

## Authentication & Authorization

Every endpoint in this document requires `Authorization: Bearer <JWT>` unless
stated otherwise. Tokens are issued by an OIDC provider via the OAuth2
**Client Credentials** grant — all three actors (Publishing System, Consuming
System, Platform Operator) are services, not interactive users, so there is
no authorization-code/login flow to design for v1. See `ADR-006` for the
dev-mode provider (an in-process OpenIddict host, `EventStore.DevIdp`) and
orchestration story.

Authorization is scope-based, one policy per scope, mapped to endpoints as:

| Endpoint | Required scope |
|---|---|
| `POST /publish/{event-type}` | `events:publish` |
| `GET /follow/{event-type}` | `events:follow` |
| `GET /events/{id}/parents`, `/children`, `/ancestors`, `/descendants` | `events:lineage:read` |
| `PUT /registry/{event-type}` | `registry:admin` |
| `GET /registry/...` | `registry:admin` |
| `GET /openapi.json`, `GET /asyncapi.json` | none (anonymous — contract shape only, no event data) |

OpenAPI documents this with a shared security scheme:

```yaml
components:
  securitySchemes:
    bearerAuth:
      type: http
      scheme: bearer
      bearerFormat: JWT
security:
  - bearerAuth: []   # overridden per-operation with the specific scope, e.g.:
paths:
  /publish/{event-type}:
    post:
      security:
        - bearerAuth: [events:publish]
      responses:
        '401':
          description: Missing or invalid Bearer token
        '403':
          description: Token valid but missing the required scope
```

**Browser SSE caveat**: the native browser `EventSource` API cannot set an
`Authorization` header. The Follow API therefore also accepts the token via
an `access_token` query-string parameter for browser-based followers
(`GET /follow/{event-type}?access_token=<token>`); non-browser followers
using an `HttpClient`-based SSE reader should prefer the header. Query-string
tokens are more prone to leaking via server/proxy logs than header-based
ones — mitigate with short-lived tokens, not by avoiding the mechanism
(there is no alternative for a real `EventSource` client).

### Event-type security (required claims) — a second authorization dimension

The scope table above answers "can this caller call this *operation* at
all." A separate, per-event-type check (`ADR-008`) answers "may this caller
touch *this event type's data*": `EventTypeDefinition.RequiredPublishClaim`
and `RequiredReadClaim` (`02-data-model.md`), each an optional single
`"type:value"` claim string, checked with `ClaimsPrincipal.HasClaim`. Unlike
the four scopes, these aren't static ASP.NET Core policies registered at
startup — they're data loaded from the registry per request, so the check
happens in application code after the event type is resolved, not via
`[Authorize(Policy = "...")]` (see `06-solution-structure.md`).

Both checks apply, in order: scope first (cheap, static), then the
per-event-type claim (needs a registry lookup). A `403` from either layer
looks the same to the caller; if you need to distinguish "missing scope"
from "missing required claim" for debugging, that's in the response detail,
not the status code.

- `POST /publish/{event-type}`: 403 if `RequiredPublishClaim` is set and the
  caller's token lacks it — in addition to the existing `events:publish`
  scope check.
- `GET /follow/{event-type}`: 403 **at connection time** if
  `RequiredReadClaim` is set and the caller's token lacks it — in addition
  to `events:follow`.
- Lineage API: see the dedicated note under "Lineage API (event chains)"
  below — a restricted node anywhere in the result fails the *whole*
  request, it is not stubbed out.
- A caller who lacks `RequiredReadClaim` for an event that does exist gets
  `403`, not `404` — distinguishable from a truly unknown `eventId`, which
  is still `404`. This deliberately leaks existence rather than hiding it
  behind a uniform `404`; see `ADR-008`'s consequences for why that
  trade-off was made explicitly rather than left as an oversight.

Property-level **masking** (wrapping individual field values in a
`{value:...}`/`{masked:"***"}` envelope within an event the caller
otherwise has `RequiredReadClaim` for) is a related, finer-grained feature
— design accepted, build deprioritized to after Phases 0–6 — see `ADR-009`
and the "Masking" note under "AsyncAPI (follow side)" below for where it's
actually applied.

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
- Request body is an **envelope**: `payload` (the schema-validated event
  data) plus optional `parentEventIds` (lineage metadata — see
  `02-data-model.md`, "Event lineage"). `parentEventIds` is never part of the
  registered JSON Schema and is validated separately, against the event
  type's `ParentValidationMode`, not against `payload`'s schema.
- `payload`'s schema per event type is a `$ref` into a `components/schemas`
  section built directly from each `EventTypeDefinition.JsonSchema`.

```yaml
openapi: 3.1.0
info:
  title: Open Event Sourcing Store — Publish API
  version: "1.0"
paths:
  /publish/{event-type}:
    post:
      security:
        - bearerAuth: [events:publish]
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
              type: object
              required: [payload]
              properties:
                payload:
                  oneOf:
                    - $ref: '#/components/schemas/OrderPlaced'
                    - $ref: '#/components/schemas/OrderCancelled'
                parentEventIds:
                  type: array
                  items: { type: string, format: uuid }
                  description: >
                    Events this event is causally parented off. Omit or use
                    an empty array for an origin event. Parents may be of any
                    event type.
      responses:
        '201':
          description: Event accepted and appended
        '400':
          description: >
            payload failed schema validation, OR (Strict ParentValidationMode)
            one or more parentEventIds do not resolve to a stored event
        '401':
          description: Missing or invalid Bearer token
        '403':
          description: Token valid but missing the events:publish scope
        '404':
          description: Unknown event-type
components:
  securitySchemes:
    bearerAuth: { type: http, scheme: bearer, bearerFormat: JWT }
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
  `EventId`, `SequenceNumber`, `OccurredAt`, and `parentEventIds` are
  streamed as message **headers**, mirroring the publish-side split between
  envelope metadata and schema-validated payload.
- `access_token` is documented as a channel parameter alongside `filter`, for
  browser `EventSource` clients that cannot set an `Authorization` header
  (see the "Browser SSE caveat" above).
- Connecting requires `events:follow` plus, if the event type has one set,
  `RequiredReadClaim` (`ADR-008`) — checked once at connect time, same point
  as the `$filter`-field validation, not per streamed event.
- **Masking** (`ADR-009`, design accepted, build deprioritized to after
  Phases 0–6): any property in the message payload's schema carrying an
  `x-masking` extension is checked per connection (using the same claims
  already validated for `RequiredReadClaim`). Its *documented* type in
  this AsyncAPI output — for every caller, regardless of claims — is a
  wrapper, `oneOf: [{value: <the property's real type>}, {masked:
  string}]`, never the bare original type. At serialization time, a caller
  holding the property's `requiredClaim` gets `{"value": <real value>}`;
  one who doesn't gets `{"masked": "***"}` (or whatever `maskedValue` was
  configured). The same rule recurses into arrays: `x-masking` on a scalar
  `items` schema wraps each element; on a property nested inside a
  complex-object `items` schema, wraps just that property per element.
  This happens after `RequiredReadClaim`/`$filter` are satisfied; it never
  changes *whether* an event is streamed, only the shape of the masked
  field(s) within it. The registered/publish-side schema
  (`SchemaValidationService` validates against, and what `/openapi.json`
  documents) is **not** wrapped — publishers always send the plain,
  unwrapped value; only this generated AsyncAPI view and the actual SSE
  wire format wrap it. `x-masking`'s optional
  `regulatoryClassification`/`governanceBody`/`regulationReference` fields
  are schema-only documentation — surfaced in the generated AsyncAPI
  property description for discoverability, never in the runtime
  `value`/`masked` wrapper itself.

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
      access_token:
        description: >
          Bearer token, for clients (e.g. browser EventSource) that cannot
          set an Authorization header. Requires the events:follow scope.
    messages:
      OrderPlaced:
        headers:
          $ref: '#/components/schemas/EventEnvelope'
        payload:
          $ref: '#/components/schemas/OrderPlaced'
operations:
  receiveOrderPlaced:
    action: receive
    channel:
      $ref: '#/channels/follow-order-placed'
    security:
      - bearerAuth: [events:follow]
components:
  schemas:
    OrderPlaced:
      $ref: '#/schemas-from-registry/OrderPlaced/2'
    EventEnvelope:
      type: object
      properties:
        eventId: { type: string, format: uuid }
        sequenceNumber: { type: integer }
        occurredAt: { type: string, format: date-time }
        parentEventIds:
          type: array
          items: { type: string, format: uuid }
```

## Lineage API (event chains)

Unlike `/publish` and `/follow`, these paths are not per-event-type — they
take an `eventId` and are static entries in the generated OpenAPI document
(no `enum` populated from the registry needed):

```
GET /events/{eventId}/parents       -- immediate parents (direct)
GET /events/{eventId}/children      -- immediate children (direct)
GET /events/{eventId}/ancestors     -- full transitive closure "up" the DAG
GET /events/{eventId}/descendants   -- full transitive closure "down" the DAG
```

All four require the `events:lineage:read` scope, plus `RequiredReadClaim`
on every event type touched by the response — see "RequiredReadClaim and
the Lineage API" below. `404` if `eventId` itself is unknown. Each entry in
the response:

```json
{
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "eventType": "PaymentReceived",
  "sequenceNumber": 42,
  "occurredAt": "2026-07-27T10:15:00Z",
  "resolved": true
}
```

`"resolved": false` marks a parent reference that does not currently
correspond to a stored event — only possible for event types registered
with `ParentValidationMode: Permissive` (see `02-data-model.md`,
`05-schema-registry-and-spec-generation.md`). A `resolved: false` entry
carries only `eventId` — no `eventType`/`sequenceNumber`/`occurredAt`, since
none exist yet — and is a leaf: traversal does not recurse past it.

`ancestors`/`descendants` must be cycle-safe regardless of the queried
event's `ParentValidationMode` — see `ADR-005` for why a cycle can exist even
when the event you start from is Strict.

### RequiredReadClaim and the Lineage API

Unlike `$filter` (single event type) or Follow (one event type per
connection), a single lineage response can span multiple event types —
that's the point of cross-type parenting. If **any** event touched by the
response — the root `{eventId}` itself, or any parent/child/ancestor/
descendant reached during traversal — belongs to an event type whose
`RequiredReadClaim` the caller's token lacks, the **entire request fails
with `403`**. No partial results, no stubbing-out of just the restricted
node: per `ADR-008`, this was chosen over hiding just the offending node
because a hidden node's *position* in the graph is itself information (it
would reveal that something exists there, and roughly what it connects to,
even with its details redacted).

This means a single `403` from `/ancestors` or `/descendants` doesn't tell
the caller *which* node was restricted, or how many there were — consistent
with `ADR-008`'s decision not to leak existence information via a different
status code either. `/parents` and `/children` follow the same rule; they
just have a smaller set of nodes to check.

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
