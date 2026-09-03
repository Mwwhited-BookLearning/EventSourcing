[← Document index](../README.md)

# Getting Started

A from-scratch walkthrough: stand up a real local deployment, register
your first event type, publish an event against it, and confirm it
landed — using nothing but the tools this repo already ships with.
Everything below was checked directly against the real code paths it
describes (request/response shapes, actual seeded dev credentials,
actual endpoint routes), not written from a general impression of how
the system probably works.

**Language/platform scope, stated explicitly rather than left
inferable**: this framework's server side is **.NET only** (every
`EventStore.*` project targets `net10.0`; there is no server
implementation in any other language, and none is planned). Its client
side is **TypeScript/Vue only** (`client-web/`, `ADR-039`). `ADR-054`'s
SDK-generation story (Kiota, GraphQL Code Generator, Strawberry Shake)
produces typed clients for **.NET and TypeScript consumers only** — a
team building against this API from a third language talks to it over
plain HTTP/GraphQL like any other external caller, with no generated
client of their own. This is a deliberate scope boundary, not an
oversight: nothing in this design has ever needed a server or generated
client in a third language, and none of the ADRs above assume one.

## Prerequisites

- **.NET SDK** matching the target framework the Host projects build
  against (confirmed in `src/EventStore.Host.Sqlite/
  EventStore.Host.Sqlite.csproj`: `net10.0`).
- **Docker**, for `.NET Aspire`'s own orchestration of PostgreSQL and (if
  you enable that peer) SQL Server — see `ADR-026`.
- Node.js, only if you also want `client-web`'s dev server running
  (Aspire starts it as one more managed resource, `ADR-039`) — not
  required for the API-only walkthrough below.

## 1. Start the local stack

```
dotnet run --project src/EventStore.AppHost
```

(equivalent to `aspire run` from that directory — `06-solution-
structure.md` names both as the supported local-dev entry point). This
brings up, among other resources: the primary `eventstore` Host (Postgres
by default), a one-shot database migrator, `EventStore.DevIdp` (the
dev-only OAuth2 token issuer, `ADR-006`), and `client-web`'s Vite dev
server. The Aspire dashboard (a URL printed to your console on startup)
lists every resource's actual assigned address — every URL below is
relative to `eventstore`'s own address shown there, since Aspire assigns
ports dynamically per run.

## 2. Get a token

`EventStore.DevIdp` seeds a fixed set of dev-only OAuth2 clients at
startup (`src/EventStore.DevIdp/DevIdpSeeder.cs`) — no login flow, no
registration step, `client_credentials` only. Two of them are enough for
this walkthrough:

```
curl -X POST https://<devidp-address>/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=operator-client" \
  -d "client_secret=operator-client-secret"
```

`operator-client` holds `registry:admin`/`registry:trust-admin` — enough
to register an event type. Save the returned `access_token` as
`$ADMIN_TOKEN`. Separately, `publisher-client`/`publisher-client-secret`
holds `events:publish` (get a second token, `$PUBLISH_TOKEN`) — this
project deliberately gives publish and registry-administration
different identities (`DevIdpSeeder.cs`'s own "one identity per real
capability need" convention), not because the demo requires it.

## 3. Register your first event type

Every event type is registered against one `AppId` — pick any string
for a first try, e.g. `"quickstart"`. The request body matches
`RegisterEventTypeRequest` (`src/EventStore.SchemaRegistry/
RegisterEventTypeRequest.cs`) exactly:

```
curl -X PUT https://<eventstore-address>/registry/ItemAdded \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "appId": "quickstart",
    "jsonSchema": "{\"type\":\"object\",\"properties\":{\"Name\":{\"type\":\"string\"},\"Quantity\":{\"type\":\"number\"}},\"required\":[\"Name\"]}",
    "filterableFields": [],
    "changeKind": "Full",
    "entityIdField": "$.Name",
    "parentValidationMode": "Permissive",
    "requiredClaims": null
  }'
```

A `201 Created` response carries the assigned `version` (starts at `1`).
`changeKind` (`Full` vs. `Partial`, `ADR-016`) and `entityIdField` (which
JSON Pointer in the payload names the entity this event patches,
`ADR-021`) are both required — there's no useful default for either.

## 4. Publish an event against it

The request body matches `PublishEventRequest` (`src/EventStore.Inbox/
PublishEventRequest.cs`):

```
curl -X POST https://<eventstore-address>/publish/ItemAdded \
  -H "Authorization: Bearer $PUBLISH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "appId": "quickstart",
    "schemaVersion": 1,
    "payload": "{\"Name\":\"widget-1\",\"Quantity\":5}",
    "parentEventIds": null,
    "eventId": null
  }'
```

`payload` is a JSON string (its shape validated against the schema you
just registered), not a nested object — `PublishEventRequest`'s own
header comment states this explicitly, since which schema applies
depends on `schemaVersion`. A `202` response means it was durably
appended (`ADR-023`'s persist-everything posture) and hash-chained onto
the log (`ADR-019`). Passing a client-generated `eventId` makes a retry
of this exact call safe — the same `eventId` with identical content
replays the original response instead of writing a second time
(`ADR-011`).

## 5. Confirm it's really there

Two ways, both already running, neither needing anything hand-written:

- **Scalar** (`https://<eventstore-address>/scalar/v1`) — an interactive,
  auto-generated OpenAPI explorer covering every registered event type's
  schema, including the one you just registered (`ADR-002`/`025`).
- **GraphQL** — query/subscribe against the event you just published
  (`ADR-037`); `EventStore.GraphQL`'s schema exposes `eventTypes`/
  `eventType` (registry introspection, `src/EventStore.GraphQL/
  RegistryQueries.cs`) and a per-registered-type Subscription field for
  tailing new events live. HotChocolate's own schema explorer (reachable
  from the GraphQL endpoint Aspire's dashboard lists) is the fastest way
  to see the exact field shape without guessing GraphQL syntax by hand.

## Where to go next

- `docs/07-adrs.md` — the full decision history, one ADR per real design
  choice, if you want to understand *why* something works the way it
  does rather than just how to call it.
- `docs/patterns/README.md` — the general patterns this design applies,
  explained portably (not just "how this project uses X").
- `docs/domains/` — two complete worked examples (clinical trials/device
  telemetry, digital identity/KYC) carrying this same publish/register
  flow through real multi-step business workflows, several steps deeper
  than this walkthrough's single event type.
- `docs/10-open-questions.md` / `TODO.md` — what's still genuinely
  undecided or unfinished, if you're picking this project up to
  contribute rather than just to use it.
