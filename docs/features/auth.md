# Feature: OAuth2/OIDC bearer-token authentication and scope-based authorization

Context: scopes-to-endpoints table and OpenAPI/AsyncAPI security schemes in
`../03-api-contracts.md`; dev-mode OpenIddict (`EventStore.DevIdp`) +
orchestration decision in `ADR-006` (`../07-adrs.md`); DI wiring in
`../06-solution-structure.md`.

## Sequence diagram — token acquisition and an authorized call

```plantuml
@startuml Auth_TokenFlow_Sequence
autonumber
actor "Publishing System\n(publisher-client)" as client
participant "EventStore.DevIdp\n(OpenIddict, in-process)" as idp
participant "API\n(any of Publish/Follow/Lineage/Registry)" as api

client -> idp: POST /connect/token\ngrant_type=client_credentials\nclient_id, client_secret
idp --> client: 200 { access_token (JWT), expires_in }
client -> api: request\nAuthorization: Bearer <access_token>
api -> api: validate JWT signature/expiry against DevIdp's\n/.well-known/openid-configuration (JWKS, cached)
alt token missing or invalid
  api --> client: 401
else token valid, required scope not present in "scope" claim
  api --> client: 403
else token valid and scope present
  api --> client: 200 / 201 (operation proceeds)
end
@enduml
```

## Sequence diagram — browser SSE without a settable header

```plantuml
@startuml Auth_BrowserSSE_Sequence
autonumber
actor "Browser\n(EventSource)" as browser
participant "Follow API" as api

note over browser
  Native EventSource cannot set
  an Authorization header.
end note
browser -> api: GET /follow/OrderPlaced?$filter=...&access_token=<JWT>
api -> api: no Authorization header present -> fall back to access_token query param
api -> api: validate token + events:follow scope (same path as the header case)
alt token missing/invalid (no header, no access_token)
  api --> browser: connection rejected 401
else valid
  api --> browser: SSE connection open (200)
end
@enduml
```

## Data model (ER diagram)

Not applicable — this feature has no persistent entities of its own in
`EventStoreContext`. Identity/token state (clients, scopes) lives entirely
inside `EventStore.DevIdp`'s OpenIddict **InMemory** store — not even
persisted across that process's own restarts, let alone shared with the
event store's database; see `ADR-006`.

## Seeded clients (dev)

Seeded in code by `EventStore.DevIdp`'s `DevIdpSeeder` at startup — no
realm-export file, no admin console:

| Client ID | Grant type | Scope(s) |
|---|---|---|
| `publisher-client` | `client_credentials` | `events:publish` |
| `follower-client` | `client_credentials` | `events:follow events:lineage:read` |
| `operator-client` | `client_credentials` | `registry:admin` |

## Salt (UI mockup)

Not applicable — `EventStore.DevIdp` (OpenIddict) has no admin console or
other UI. Verify the seed by requesting a token from `/connect/token` or
inspecting `/.well-known/openid-configuration`, not by looking at a screen.

## Gherkin

```gherkin
Feature: OAuth2/OIDC bearer-token authentication and scope-based authorization
  As the event store
  I want every request authenticated via a Bearer token and authorized by scope
  So that only permitted services can publish, follow, query lineage, or administer schemas

  Background:
    Given the identity provider is EventStore.DevIdp (OpenIddict, in-process, dev-only)
    And client "publisher-client" has scope "events:publish"
    And client "follower-client" has scopes "events:follow" and "events:lineage:read"
    And client "operator-client" has scope "registry:admin"
    And the event type "OrderPlaced" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }
      """

  Scenario: Request without a Bearer token is rejected
    When I POST to "/publish/OrderPlaced" without an Authorization header
    Then the response status should be 401

  Scenario: Request with an expired or invalid Bearer token is rejected
    Given I have an expired Bearer token for client "publisher-client"
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "Amount": 150.00 } }
      """
    Then the response status should be 401

  Scenario: Request with a token lacking the required scope is rejected
    Given I have a Bearer token for client "follower-client"
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "Amount": 150.00 } }
      """
    Then the response status should be 403

  Scenario: Request with a token carrying the required scope succeeds
    Given I have a Bearer token for client "publisher-client"
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "Amount": 150.00 } }
      """
    Then the response status should be 201

  Scenario: Schema registration requires the registry:admin scope
    Given I have a Bearer token for client "follower-client"
    When I PUT "/registry/OrderPlaced" with body:
      """
      { "jsonSchema": { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }, "filterableFields": [] }
      """
    Then the response status should be 403

  Scenario: OpenAPI and AsyncAPI documents remain publicly readable
    When I GET "/openapi.json" without an Authorization header
    Then the response status should be 200
    When I GET "/asyncapi.json" without an Authorization header
    Then the response status should be 200

  Scenario: Browser-based SSE clients supply the token via query string, not a header
    Given I have a Bearer token for client "follower-client"
    When I open an SSE connection to "/follow/OrderPlaced?access_token=<token>"
    Then the connection should be accepted

  Scenario: An SSE connection with neither a header nor an access_token is rejected
    When I open an SSE connection to "/follow/OrderPlaced"
    Then the connection should be rejected with 401
```
