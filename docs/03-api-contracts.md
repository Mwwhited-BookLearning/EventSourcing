# API Contracts — OpenAPI (Publish) and GraphQL (everything else)

Both the OpenAPI Publish contract and the GraphQL schema are generated
from the Schema Registry; neither hand-authors JSON Schema or SDL. The
registry is the single source of truth.

> **Rewritten this session, per `ADR-037` — the queued companion
> rewrite is now done.** Follow, Lineage, and Registry-listing (the
> sections that used to describe the OData-era AsyncAPI/`$filter`
> surface in full) are rewritten below for the actual GraphQL contract.
> `ADR-023`/`ADR-020`'s Publish contract needed no such rewrite — `ADR-
> 037` replaced only the *read* surfaces, never Publish — but is updated
> for `ADR-050`'s `RequiredClaims` generalization and `ADR-066`'s RFC
> 9470 step-up challenge. New sections cover what this file never had at
> all: `ADR-040`'s ticket exchange, `ADR-072`'s bulk-ingestion/
> interchange endpoints, `ADR-009`/`ADR-050`'s `revealField` mutation,
> `ADR-068`'s export/playback fields, and `ADR-060`'s webhook-
> registration mutation.

## Error responses

Every non-`2xx` response, from every endpoint in this document, is **RFC
9457 Problem Details** (`application/problem+json`) — see `ADR-013` for
the full decision, the per-situation `type`/status/extension table, and
why. In the endpoint-specific sections below, a response line like
`'403': Token valid but missing the events:publish scope` means "a
Problem Details response with that `detail`," not a bespoke body — assume
the shape from `ADR-013` throughout rather than a per-endpoint schema.
**GraphQL errors follow the same underlying reasoning, expressed in
GraphQL's own partial-success shape instead** — see "GraphQL error
shape" under Follow/Lineage below, not a second Problem Details profile.

## Authentication & Authorization

Every endpoint in this document requires `Authorization: Bearer <JWT>` unless
stated otherwise. Tokens are issued by an OIDC provider via the OAuth2
**Client Credentials** grant — all four actors (Publishing System, Consuming
System, Platform Operator, and `ProjectionHost` — `ADR-015`) are services,
not interactive users, so there is no authorization-code/login flow to
design for v1. See `ADR-006` for the dev-mode provider (an in-process
OpenIddict host, `EventStore.DevIdp`) and orchestration story.

Every request also carries a `DPoP` header — a signed proof-of-possession
JWT (`ADR-017`) — alongside the bearer token; a technically-valid bearer
token with a missing or invalid DPoP proof is rejected `401` the same as a
missing token, per the error table below.

Authorization is scope-based, one policy per scope, mapped to endpoints as:

| Endpoint | Required scope |
|---|---|
| `POST /publish/{event-type}` | `events:publish` |
| `POST /publish/batch` (`ADR-072`) | `events:publish` |
| GraphQL `Subscription` (Follow, `ADR-037`) | `events:follow` |
| GraphQL `Query` — lineage fields (`ancestors`/`descendants`/`parents`/`children`) | `events:lineage:read` |
| GraphQL `Query` — registry listing | `registry:admin` |
| GraphQL `Query` — `exportLineage`, `playbackAsOf` (`ADR-068` — reads, never mutations, since neither changes stored state) | `events:lineage:read` |
| GraphQL `Mutation` — `revealField`, `registerWebhookSubscription` | scope per mutation, named in each section below |
| `PUT /registry/{event-type}` | `registry:admin` |
| `POST /oauth/token` (ticket issuance, `ADR-040`) | requires an existing valid bearer token as the Token Exchange subject |
| `GET /openapi.json`, GraphQL schema introspection | none (anonymous — contract shape only, no event data) |

OpenAPI documents the Publish contract with a shared security scheme
(unchanged from before this rewrite):

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

GraphQL authorization is enforced per-field/per-operation via
HotChocolate's own authorization directives, checked against the same
bearer token — not a second auth stack, the identical scopes/claims
table above applied at GraphQL's own enforcement points instead of
OpenAPI's `security` blocks.

**Browser transport, post-`ADR-012`/`ADR-037`**: every GraphQL operation
(Query, Mutation, Subscription) travels over the HTTP `QUERY` method
(`ADR-012`), never `GET` — the query document itself, which can carry
PII/PHI-bearing filter arguments, stays out of URLs/access-logs/proxy
caches. A browser client uses `fetch()` with a `QUERY` request for
Query/Mutation, and the same `fetch()`-initiated connection for
Subscription (below) — never the native `EventSource` API, which can
only issue `GET` with no custom headers and so cannot carry a real
`Authorization` header at all. There is no `access_token` query-string
workaround anywhere in this contract — removed along with
`EventSource` support (`ADR-012`), not merely superseded.

### Event-type security (required claims) — a second authorization dimension

The scope table above answers "can this caller call this *operation* at
all." A separate, per-event-type check (`ADR-008`, generalized to a list
by `ADR-050`) answers "may this caller touch *this event type's data*":
`EventTypeDefinition.RequiredClaims` (`docs/data/schema-registry.md`) —
a list of `{Direction, Claim}` pairs, each `Claim` an opaque
`"type:value"` string, checked with `ClaimsPrincipal.HasClaim`. **`OR`
semantics within one `Direction` by default** (`ADR-050`) — holding
*any one* of the `Publish`-direction (or `Read`-direction) claims
satisfies the gate; `ADR-008`'s original "exactly one claim per
direction" limitation no longer applies. Unlike the four scopes, these
aren't static ASP.NET Core policies registered at startup — they're
data loaded from the registry per request, so the check happens in
application code after the event type is resolved, not via
`[Authorize(Policy = "...")]` (see `06-solution-structure.md`).

Both checks apply, in order: scope first (cheap, static), then the
per-event-type claim (needs a registry lookup). A `403` from either layer
looks the same to the caller; if you need to distinguish "missing scope"
from "missing required claim" for debugging, that's in the response detail,
not the status code.

- `POST /publish/{event-type}`: `403` if a `Publish`-direction
  `RequiredClaims` entry is configured and the caller's token holds
  none of them — in addition to the existing `events:publish` scope
  check.
- GraphQL `Subscription` (Follow): `403` **at connection time** if a
  `Read`-direction `RequiredClaims` entry is configured and the
  caller's token holds none of them — in addition to `events:follow`.
- Lineage query fields: see "RequiredClaims and the Lineage API" below —
  only the root `eventId` a query names is pass/fail (`403` if
  restricted); every node the traversal *discovers* is checked
  independently and stubbed (`restricted: true`), never fails the rest
  of the response.
- A caller who lacks the required `Read`-direction claim for an event
  that does exist gets `403`, not `404` — distinguishable from a truly
  unknown `eventId`, which is still `404`. This deliberately leaks
  existence rather than hiding it behind a uniform `404`; see `ADR-008`'s
  consequences for why that trade-off was made explicitly rather than
  left as an oversight.

Property-level **masking** (wrapping individual field values in a
`{value:...}`/`{masked:"***"}`/`{erased:true}` envelope within an event
the caller otherwise holds the required claim for) is a related,
finer-grained feature — see `ADR-009`/`ADR-050`/`ADR-057` and "GraphQL
schema shape (Follow)" below for where it's actually applied, and
"`revealField` — explicit reveal-on-demand" for the display-mask
refinement.

### Step-up authentication for signature-required event types (`ADR-066`, RFC 9470)

An `EventTypeDefinition` may configure `RequiredSignature: { AcrValues,
MaxAge }`. If a publish targets such a type and the caller's current
token's `acr` claim doesn't meet the configured `AcrValues`, or the
token is older than `MaxAge`, the Inbox responds with **RFC 9470**'s
step-up challenge instead of accepting the publish:

```
POST /publish/{event-type}
Authorization: Bearer <token, acr insufficient>

HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer error="insufficient_user_authentication",
  error_description="A higher authentication context is required",
  max_age=300, acr_values="urn:eventstore:step-up"
```

The client redirects the caller through the IdP to step up (password
re-entry, TOTP, WebAuthn — however that IdP implements it, not this
framework's concern), then retries the identical publish with the
resulting, stronger token. On acceptance, the stored event's `Signature`
envelope field (`SignerId`, `SignedAt`, `Meaning` — required, rejected
if absent — `Acr`) records the sign-off; see `docs/data/event-log.md`.
This is the one case since `ADR-023`'s persist-everything posture where
a publish can be legitimately turned away before it's stored, alongside
the pre-existing "envelope itself is unparseable" exception — the
event's own *data* is never rejected for shape/content reasons; only
*insufficient authentication strength* for a signature-required type
short-circuits before storage.

## Ticket exchange for header-incapable clients (`ADR-040`)

An `<video src>`/`<img src>`/`<a href>` pointed at streaming playback
(`ADR-031`) or attachment retrieval (`ADR-032`) can't carry an
`Authorization` header. A three-hop flow, each hop a real, already-
adopted mechanism:

```
1. POST /oauth/token                              (header-based, DPoP-proved, normal caller)
   grant_type=urn:ietf:params:oauth:grant-type:token-exchange
   subject_token=<bearer JWT>
   requested_token_type=urn:eventstore:token-type:ticket
   → { "ticket": "<opaque, single-use, random>", "expiresIn": 60 }

2. (client-side, no network call)
   sig = base64url(HMAC-SHA256(ticket, sharedSecret))
   target URL becomes: /stream/{channelId}?ticket=...&sig=...
                    or: /attachments/{contentHash}?ticket=...&sig=...

3. GET /stream/{channelId}?ticket=...&sig=...      (the header-incapable request itself)
   → Streaming/Attachment Service forwards ticket+sig to:
     POST /oauth/introspect
     token=<ticket>
     sig=<sig>
     → { "active": true, ...original token's claims } (ticket now consumed, single-use)
```

Every ordinary API call in this document keeps authenticating via
`Authorization`/`DPoP` exactly as specified above — this flow exists
*only* for the two named header-incapable retrieval paths, not as a
general auth alternative. See `ADR-040` for the full three-hop reasoning
and the honest residual-risk accounting.

## Bulk ingestion and external interchange adapters (`ADR-072`)

**Bulk/batch ingestion**: `POST /publish/batch` accepts an NDJSON or
JSON-array body of multiple event submissions in one request — each
inside the batch is validated and persisted exactly as an individual
`POST /publish/{event-type}` call would be (same `202` + status-
envelope-per-item semantics, same `ADR-023` persist-everything posture);
this is a transport-batching convenience, not a new persistence model or
a new validation path.

**External interchange-format adapters** (`IInterchangeFormatAdapter` —
`Hl7V2Adapter`, `FhirAdapter`, `IchE2bR3Adapter`, `Gs1EpcisAdapter`) are
**not a new public endpoint in this contract at all** — each adapter
transforms an externally-standardized inbound format into this
framework's own registered `JsonSchema` shape, then publishes the
result through the *ordinary* publish path above (individual or
`batch`), inheriting `ADR-023`'s persist-everything posture and
`ADR-035`'s non-authoritative capture. HL7v2 specifically arrives over
its own real transport (MLLP/TCP, not HTTP) ahead of that adapter step —
outside this HTTP API contract's scope entirely, documented in `ADR-072`
itself, not duplicated here.

## `revealField` — explicit reveal-on-demand (`ADR-009`, `ADR-050`)

A GraphQL mutation, distinct from an ordinary masked-field read:

```graphql
mutation {
  revealField(entityId: "trial1:Patient:S-0091", eventId: "3fa8...afa6", fieldPath: "$.SubjectNationalId") {
    value
  }
}
```

Checks the field's `requiredClaim` **at the moment of the request**,
writes an `ADR-045` `AccessLogEntry`
with `Action: "reveal"` naming the specific field path (sharper audit
granularity than an ordinary bulk query already has), and returns the
real value only if authorized — otherwise the same `403` Problem
Details shape any other claim-gated read uses. Never affects the
underlying event's stored shape; a caller without the claim, or who
never calls `revealField` at all, keeps seeing the ordinary
`{masked: "..."}`/`{erased: true}` wrapper.

**`ADR-066`'s step-up-authentication refinement for `revealField` — a
masked field requiring a *fresh* re-authentication specifically to
reveal it, not just an ordinary claim — is still not built.** Build-plan
item 29 ("Digital Sign-Off for Regulated Actions") wired RFC 9470
step-up enforcement into `POST /publish/{event-type}` only
(`PublishService.PublishAsync`'s `StepUpSatisfied` check); `x-masking`
itself has no step-up configuration surface yet (`MaskingSchemaValidator`
validates only `strategy`/`requiredClaim`/`regulatoryClassification`/
`governanceBody`/`regulationReference`/`erasureScope`), and
`RevealFieldMutation.RevealFieldAsync` (`src/EventStore.GraphQL/
RevealFieldMutation.cs`) checks only `requiredClaim`. This gap is
honestly flagged in the mutation's own code comment and remains open
post-item-29 — tracked in `08-build-plan.md`, not silently implied
closed by item 29 landing.

## Lineage export and bitemporal playback (`ADR-068`)

Two new GraphQL query fields on the same Gateway every other query goes
through — an export or a playback position is a read, enforced through
the identical `RequiredClaims`/masking/access-audit pipeline as any
other query, never a privileged bypass:

```graphql
query {
  exportLineage(entityId: "trial1:Patient:S-0091") {
    bundleUrl   # NDJSON bundle + manifest hash + RFC 3161 timestamp (ADR-086) over that hash
  }
}

query {
  playbackAsOf(entityId: "trial1:Patient:S-0091", asOfSequenceNumber: 48810) {
    data        # reconstructed state as this design's own record stood at that SequenceNumber, in arrival order
    extensions
  }
}
```

**`entityId`, not a root event, is the correct starting point for
`exportLineage`** — corrected against `ADR-068`'s own text ("given a
starting `EntityId`... gathers every causally-connected event"), matching
`docs/features/lineage-export-and-playback.md` and the domain docs that
already used it this way; an earlier version of this section used
`rootEventId`/`direction`, conflating this with the unrelated per-event
Lineage API (`ancestors`/`descendants` above) rather than `ADR-068`'s own
entity-scoped export. `exportLineage`'s NDJSON bundle is the same portable
format `04-odata-filter-pushdown.md`'s (now `04-*.md`'s) archival
mechanism (`ADR-089`) also uses for a detached Event Log segment — one
serialization convention, reused, not two. `playbackAsOf` reconstructs
*system-time* state (what this design knew as of a point in time, folded
in arrival order rather than logical order) — a different axis from
`mode=replay`'s event-arrival-order replay of *new* events, and a
different parameter shape from an ordinary timestamp: `asOfSequenceNumber`
directly matches `ADR-068`'s own "fold only events with `SequenceNumber
<= T`" mechanism, and is what the VCR-style play/rewind/fast-forward
controls step through one position at a time — see `ADR-068` for the full
distinction and the offline-player export target these same fields also
feed.

## Webhook subscription registration (`ADR-060`)

```graphql
mutation {
  registerWebhookSubscription(
    targetUrl: "https://sponsor.example.com/hooks/eventstore",
    eventTypes: ["AdverseEventReported"]
  ) {
    subscriptionId
    signingSecret   # returned once, at registration — a Standard Webhooks-shaped whsec_ value
  }
}
```

Requires `registry:admin` (subscription registration is control-plane
configuration, the same scope tier as schema registration). The
`FixedClaimsSnapshot` `ADR-060` computes at registration time — the
claim set every future delivery to this subscription is masked against
— is computed once, server-side, from the registering caller's own
token; not a client-supplied input to this mutation.

## Generation timing

Recommendation: **generate on demand**, computed fresh from current
registry state on each request, with a short in-memory cache (e.g. 60s)
invalidated on schema registration — applies identically to the OpenAPI
Publish document and the per-`AppId` GraphQL SDL (`ADR-037`). This
avoids staleness bugs without needing a cache-invalidation pipeline.
Revisit only if the number of registered event types becomes large
enough (hundreds+) that generation cost is measurable — track as
`ADR-002`.

**Both `/openapi.json` and GraphQL schema introspection are anonymous**
(per the scope table above), served by `EventStore.Host.Core` and
shared identically by all three `EventStore.Host.<Provider>` deployables
(`ADR-001`).

**How the GraphQL schema is actually built**: composed per-`AppId`
(`ADR-030`/`ADR-037`) directly from that application's own registered
event types — a filter-input type for a given event type only ever
exposes fields actually declared `FilterableField` for it (see `04-*.md`,
formerly `04-odata-filter-pushdown.md`, for the full pushdown mechanism
this composition drives). A maskable property's GraphQL type is the
same `oneOf`-shaped wrapper `MaskingSchemaTransformer` already produces
for the OpenAPI/AsyncAPI side (`value`/`masked`/`erased`, `ADR-057`) —
one shared schema-transform concept, expressed in whichever document
format is being generated.

## OpenAPI (publish side) — unchanged by `ADR-037`

- OpenAPI version: 3.1.x (aligns with JSON Schema 2020-12 — schemas are
  referenced directly, not translated).
- One path template: `POST /publish/{event-type}`, with `event-type` as a
  path parameter constrained by an `enum` populated from active event types
  in the registry.
- Request body is an **envelope**: `schemaVersion` (**optional**, `int`,
  defaults to `0` when omitted — which registered version of
  `{event-type}`'s schema `payload` is shaped for, `ADR-020`; `payload`
  is validated against *that* version specifically, not automatically
  "whichever is active" — but see below, this is advisory, never
  blocking), `payload` (the one field that actually is required), plus
  optional `parentEventIds` (lineage metadata — see
  `docs/data/event-log.md`, "Event lineage"), optional `eventId`
  (idempotency key — see "Publish idempotency", `ADR-011`), and optional
  `expectedVersion` (the Entity Store `Version` this patch was based on
  — enables conflict detection, `ADR-024`). If `schemaVersion` is behind
  the active version, the payload is also run through `UpcastChain`
  (`ADR-018`, now CEL/JSONata-driven per `ADR-053`, not OData `compute()`)
  as a live compatibility check. **Per `ADR-023`, none of
  this blocks persistence** — an unknown or unresolvable `schemaVersion`
  (`PublishService.EncryptClassifiedFieldsAsync` and the Router's schema
  lookup both simply no-op when the declared version isn't registered,
  never rejecting), a schema-invalid `payload`, or a failed upcast no
  longer produce a `400`; they persist with an advisory `SchemaStatus`
  instead (see the response shape below). None of these fields is ever
  part of the registered JSON Schema itself; each is validated against
  its own rule, advisory or blocking as stated — never against
  `payload`'s schema.
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
                schemaVersion:
                  type: integer
                  default: 0
                  description: >
                    Which registered version of this event type's schema
                    payload is shaped for (ADR-020). Optional -- defaults
                    to 0 when omitted. Never rejected, including when the
                    named version doesn't exist (ADR-023): if it's behind
                    the active version, the payload is upcast-validated
                    live as an advisory check only, reflected in the
                    response's SchemaStatus, never a 400.
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
                eventId:
                  type: string
                  format: uuid
                  description: >
                    Optional idempotency key (ADR-011). Omit for normal
                    publish (server-generated EventId, no idempotency
                    guarantee). Supplying it and retrying with identical
                    payload/parentEventIds replays the original response
                    with no new write; retrying with the same eventId but
                    different content is a 409.
                uniqueId:
                  type: string
                  description: >
                    Resolves this event's EntityId (ADR-021), together with
                    {event-type} and the deployment's appId. Omit only if
                    EntityIdField (registered per event type) is itself
                    derivable from a field already inside payload.
                expectedVersion:
                  type: integer
                  format: int64
                  nullable: true
                  description: >
                    The Entity Store Version (ADR-021) this patch was based
                    on. Omit for no conflict detection; supply to enable
                    ConflictFlag detection (ADR-024) if another patch to the
                    same property was applied first.
      responses:
        '202':
          description: >
            Persisted (ADR-023) — status envelope below. Covers what used
            to be a 201, an idempotent eventId replay, AND a
            schema-invalid/unknown-version/failed-upcast submission: all
            of those now persist and return 202 with an advisory
            SchemaStatus, never a 400. status="applied" once folded into
            the Entity Store (ADR-021); status="received" if still
            in flight.
          content:
            application/json:
              schema:
                type: object
                properties:
                  correlationId: { type: string, format: uuid, description: "== eventId (ADR-011)" }
                  status: { type: string, enum: [received, processing, applied, rejected] }
                  entityId: { type: string, nullable: true }
                  schemaStatus: { type: string, nullable: true, enum: [unknown, invalid, conformant] }
                  authorityStatus: { type: string, enum: [unattested, pending_review, accepted, rejected] }
                  conflictFlag: { type: boolean }
                  reason: { type: string, nullable: true }
                  sequenceNumber: { type: integer, format: int64, description: "ADR-090 -- lets a caller filter a later read for read-your-writes" }
                  originId: { type: string, nullable: true, description: "ADR-033/090 -- null for a single-site deployment" }
        '400':
          description: >
            The envelope itself couldn't be parsed as a valid publish
            request (not valid JSON, or missing a structurally required
            transport field) — the one case ADR-023 still rejects
            outright, because there is no event to persist at all. Never
            used for a schema-invalid payload, an unknown schemaVersion,
            or a failed upcast (ADR-023) — those are 202 + SchemaStatus.
        '401':
          description: Missing or invalid Bearer token, or an RFC 9470 step-up challenge (ADR-066) — see WWW-Authenticate
        '403':
          description: Token valid but missing the events:publish scope or a required claim
        '404':
          description: Unknown event-type
        '409':
          description: >
            eventId was already used for a stored event whose content
            (payload/parentEventIds) differs from this request
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

### Publish idempotency (`ADR-011`)

`eventId` is opt-in: omit it and every publish behaves exactly as before
this ADR (server-generated `EventId`, no idempotency guarantee). Supply it
to make retries safe:

1. First attempt: `{ "payload": {...}, "eventId": "3fa8...afa6" }` →
   `202`, stored with that `EventId`.
2. Connection drops before the response arrives; caller retries the exact
   same request → the store finds the existing row by `EventId`, confirms
   the content hash matches, and replays the **original** response.
   No second `StoredEvent` is created.
3. Caller instead retries with the same `eventId` but a *different*
   `payload` → `409 Conflict` — this `eventId` is already bound to
   different content, which is treated as a caller error (idempotency-key
   reuse), not a fresh publish and not silently accepted.

Checked immediately after the `RequiredClaims` (`Publish` direction)
check (`ADR-008`/`ADR-050`), before schema/parent-link validation — an
idempotent replay skips both (they already passed the first time) and
performs no write at all.

## Follow — GraphQL Subscription over SSE (`ADR-037`)

**Transport is unchanged: Server-Sent Events.** Only the query syntax
changed, from an OData `$filter` string to a GraphQL Subscription
document — `ADR-037`'s own consequences state this directly ("Follow's
underlying SSE transport and envelope shape don't change; only how a
filter is expressed"). Concretely, this design adopts the [GraphQL over
Server-Sent Events
Protocol](https://github.com/enisdenjo/graphql-sse/blob/master/PROTOCOL.md)
("distinct connections mode" — one SSE connection per subscription
operation), which HotChocolate (`ADR-037`'s adopted server) implements
natively since v13 with no additional middleware — verified before
adopting, not assumed.

```graphql
subscription {
  onOrderPlaced(where: [{ field: "amount", gt: "100" }], mode: TAIL) {
    orderId
    amount
    customerTaxId { value masked erased }   # x-masking wrapper, ADR-009/050/057
  }
}
```

- **Connection**: `QUERY /graphql` (`ADR-012`) with the subscription
  document as the request body — never `GET`, for the same PII/PHI-in-
  URL reason every other GraphQL operation avoids it (a `where` argument
  can carry PII).
- **Filtering**: `where` is a flat list of `EventFilterInput` items
  (`Field`, plus one of `Eq`/`Neq`/`Gt`/`Gte`/`Lt`/`Lte`/`Contains`, all
  carried as strings and cast server-side to the field's own declared
  `FilterableFieldType`) — a static, hand-written GraphQL input type,
  not HotChocolate's `[UseFiltering]` middleware (which infers a
  per-CLR-type filter input by reflection; there is no bound CLR type to
  reflect over here, since this schema is generated dynamically per
  registered event type). Every item in the list is AND-ed together;
  there is no `and`/`or` combinator and no nested-object syntax.
  `GraphQlFilterPredicateBuilder` (`src/EventStore.GraphQL/
  GraphQlFilterPredicateBuilder.cs`) rejects an undeclared `Field` with a GraphQL
  error before it ever reaches the database — a deliberate, honestly-
  flagged narrowing of `ADR-037`'s schema-level guarantee to a runtime
  check for filtering specifically (still schema-enforced for the
  subscription field name and payload fields). See `04-*.md` (formerly
  `04-odata-filter-pushdown.md`) for the full per-provider pushdown
  mechanism this drives.
- **`mode`** (`ADR-010`, unchanged semantics): `TAIL` (default — only
  events from connection time forward) or `REPLAY` (replay matching
  history first, then tail with no gap or duplicate), with an optional
  `fromSequenceNumber` argument valid only alongside `REPLAY`.
- **Required claims**: connecting requires `events:follow` plus, if the
  event type has one configured, a `Read`-direction `RequiredClaims`
  entry (`ADR-008`/`ADR-050`) — checked once at connect time, same point
  as `where`-field validation, not per streamed event.
- **`parentEventIds`** travels as GraphQL envelope-metadata fields
  alongside the payload selection, not a separate transport header —
  any parent whose type is restricted for the connected caller is
  omitted from that list, the same per-node visibility rule the Lineage
  API uses below.

### GraphQL schema shape (masking)

A maskable property's GraphQL type is the three-way wrapper
`MaskingSchemaTransformer` produces — `{ value: T, masked: String,
erased: Boolean }`, exactly one populated per response, selectable by
name in the query document (as `customerTaxId { value masked erased }`
above) rather than a fixed JSON shape every caller receives identically.
This is schema-level and claims-independent (every caller's *schema*
looks the same); which field actually resolves to non-null for a given
caller is `IPayloadMasker`'s data-level enforcement, unchanged from the
OpenAPI/AsyncAPI-era mechanism, just resolved per GraphQL field instead
of serialized into one fixed JSON wrapper.

### GraphQL error shape (Follow, and every other operation)

GraphQL's own partial-success execution model (`data` + a separate
`errors` array) is this contract's error shape for every GraphQL
operation — a restricted/masked field resolves to `null` (or the
appropriate wrapper branch) with a corresponding `errors` entry, rather
than failing the whole response the way a REST `4xx` would. Non-nullable
fields (`String!`) are audited and reserved only for properties
guaranteed across every schema version, since a non-null field
resolving to `null` nulls out its entire parent object per the GraphQL
spec — the exact failure mode this design's tolerant posture exists to
avoid (`ADR-037`).

## Lineage API — GraphQL query fields (`ADR-037`)

```graphql
query {
  event(eventId: "3fa8...afa6") {
    ancestors(first: 50) { eventId eventType sequenceNumber occurredAt resolved restricted }
    descendants(first: 50) { eventId eventType sequenceNumber occurredAt resolved restricted }
    parents { eventId eventType sequenceNumber occurredAt resolved restricted }
    children { eventId eventType sequenceNumber occurredAt resolved restricted }
  }
}
```

Same semantics as the pre-`ADR-037` REST paths, expressed as GraphQL
fields on a resolved `event` root instead of four separate `QUERY
/events/{id}/...` paths: `ancestors`/`descendants`/`parents`/`children`
each take plain `first`/`skip` integer arguments (`LineageQueries.cs`),
the exact replacement for the pre-`ADR-037` `$top`/`$skip` — **not**
HotChocolate's `[UsePaging]` Relay-style `Connection`/`edges`/`node`
cursor wrapping, and no `after` argument exists. This is a deliberate,
honestly-flagged narrowing from a full Relay cursor implementation
(`08-build-plan.md`) — `first`/`skip` are applied inside
`LineageService` exactly as its pre-existing `top`/`skip` parameters
were. Everything else — the `resolved`/`restricted` flag semantics,
cycle-safety regardless of `ParentValidationMode`, `DataLoader`-batched
traversal across shards/replicas (`ADR-034`/`ADR-033`) — is unchanged.

Two independent reasons a node can be a leaf, each with its own flag —
either flag makes traversal stop at that node, but they mean different
things and both omit `eventType`/`sequenceNumber`/`occurredAt`:

- `resolved: false` — the reference does not currently correspond to a
  stored event at all, only possible for event types registered with
  `ParentValidationMode: Permissive` (`docs/data/schema-registry.md`).
- `restricted: true` — the event **exists** (`resolved: true`) but the
  caller lacks the required `Read`-direction claim for its type, so
  nothing further about it is revealed. See "`RequiredClaims` and the
  Lineage API" below.

`ancestors`/`descendants` must be cycle-safe regardless of the queried
event's `ParentValidationMode` — see `ADR-005` for why a cycle can exist even
when the event you start from is Strict.

### `RequiredClaims` and the Lineage API

Unlike Follow (one event type per subscription), a single lineage query
can span multiple event types — that's the point of cross-type
parenting. Per `ADR-008`, visibility is evaluated **per node, not per
request** — "you can only see what you can see":

- The **root** `event(eventId: ...)` a query names directly is a special
  case: if it exists but the caller lacks the required `Read`-direction
  claim for its type, **the whole request is rejected with `403`** (not
  `404` — this deliberately leaks that *something* exists at that
  `eventId`, distinguishable from a truly unknown one). You cannot ask
  about the lineage of something you can't see at all.
- Every node the traversal *discovers* from there — parents, children,
  ancestors, descendants — is checked **independently**. A node the
  caller can't see comes back as a `restricted: true` stub (above) and
  traversal doesn't recurse past it, but every *other* node in the result
  — including ones on the far side of a restricted node from a different
  path, or ones that are simply unrelated to it — is returned normally.
  Lacking access to a parent's type never hides a child the caller
  otherwise has rights to, and vice versa: the two directions are
  evaluated completely independently, not linked.
- This means `ancestors`/`descendants` return a normal GraphQL response
  with a mix of full nodes and `restricted: true` stubs whenever the
  caller has partial access across the discovered graph — there's no
  single error signaling "some nodes were hidden," the stubs themselves
  are the signal. `parents`/`children` follow the identical rule; they
  just have a smaller set of nodes to check (no recursion).
- The same per-node check applies to Follow's `parentEventIds` field
  (above): any parent whose type is restricted for the connected caller
  is omitted from that list, not surfaced as a bare ID they can't
  otherwise learn anything about.

## Registry listing — GraphQL query field (`ADR-037`)

```graphql
query {
  eventTypes(first: 50) {
    name
    version
    isActive
    filterableFields { jsonPath dataType isIndexed }
  }
  eventType(name: "OrderPlaced", version: 2) {
    jsonSchema
    requiredClaims { direction claim }
  }
}
```

Requires `registry:admin`, matching the pre-`ADR-037` `QUERY /registry`
scope. Same underlying `ISchemaRegistryReader` data source the OpenAPI
Publish document's generation already uses — one registry-reading
interface, two document formats built from it.

## Shared schema source

Both the OpenAPI Publish document and the GraphQL SDL call the same
internal method:

```csharp
public interface ISchemaRegistryReader
{
    Task<IReadOnlyList<EventTypeDefinition>> GetActiveEventTypesAsync();
}
```

Neither talks to EF Core directly, keeping spec/schema generation
decoupled from persistence.

## Suggested References

- [OpenAPI Specification v3.1.1](https://spec.openapis.org/oas/v3.1.1.html) — the publish-side contract format (`ADR-002`).
- [GraphQL Specification](https://spec.graphql.org/) — the Query/Mutation/Subscription contract format for everything else (`ADR-037`).
- [GraphQL over Server-Sent Events Protocol](https://github.com/enisdenjo/graphql-sse/blob/master/PROTOCOL.md) — Follow's Subscription transport, HotChocolate-native since v13.
- [RFC 9457](https://datatracker.ietf.org/doc/html/rfc9457) — Problem Details, every non-GraphQL error response's shape (`ADR-013`).
- [RFC 10008](https://datatracker.ietf.org/doc/html/rfc10008) — the HTTP QUERY method, carrying every GraphQL operation (`ADR-012`).
- [RFC 6749 §4.4](https://datatracker.ietf.org/doc/html/rfc6749#section-4.4) / [RFC 6750](https://datatracker.ietf.org/doc/html/rfc6750) — Client Credentials grant and bearer token usage (`ADR-006`).
- [RFC 9449](https://datatracker.ietf.org/doc/html/rfc9449) — DPoP, the proof header every request also carries (`ADR-017`).
- [RFC 9470](https://www.rfc-editor.org/rfc/rfc9470.html) — OAuth 2.0 Step Up Authentication Challenge Protocol (`ADR-066`).
- [RFC 8693](https://datatracker.ietf.org/doc/html/rfc8693) — OAuth 2.0 Token Exchange, the ticket-issuance hop (`ADR-040`).
- [RFC 7662](https://datatracker.ietf.org/doc/html/rfc7662) — OAuth 2.0 Token Introspection, the ticket-resolution hop (`ADR-040`).

See `references.md` for the full bibliography, including the historical
OASIS OData reference (superseded, `ADR-037`) still cited from `04-*.md`.
