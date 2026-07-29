# Feature: MVVM client (entity views, client-local outbox, native/JS bridge)

Context: decision record `ADR-039` in [`../adrs/adr-039-mvvm-client.md`](../adrs/adr-039-mvvm-client.md);
concrete Vue 3 implementation mapping in
[`../patterns/mvvm-client-architecture.md`](../patterns/mvvm-client-architecture.md);
installable/offline PWA mechanics (Service Worker, Web App Manifest,
Background Sync, multi-instance outbox namespacing) in
[`../patterns/pwa-offline-outbox.md`](../patterns/pwa-offline-outbox.md); the
MVVM→MVP→MVC→code-behind fallback order in
[`../comparisons/ui-architecture-patterns.md`](../comparisons/ui-architecture-patterns.md);
Vue-vs-Blazor framework reasoning in
[`../comparisons/ui-framework.md`](../comparisons/ui-framework.md). Build-plan
exit criteria in `../08-build-plan.md`, Phase 20. Data shapes referenced below
(`Optional<T>` patches, `Extensions`, `ConflictFlag`/`LateArrivalFlag`,
`AuthorityStatus`) come from `ADR-022`/`ADR-024`/`ADR-029`/`ADR-035` and
`../data/entity-store.md`; the content-addressed/versioned `ViewDefinition`
shape mirrors `../data/schema-registry.md`'s existing schema-registry entries,
per `ADR-039` itself. This is the one ADR in the design-docs integration's
outstanding-propagation list with a genuine UI surface — the mockup below is
a real one, not a placeholder.

This doc covers only the client-specific mechanics `ADR-039` adds: the
command-dispatch-through-outbox round trip, `ViewDefinition` rendering and its
generic fallback, and PWA installability/offline behavior. It does not
re-derive MVVM itself (see the pattern doc) or the outbox's durability
mechanism (see the PWA pattern doc and `ADR-033`).

## Sequence diagram — command dispatched while offline, delivered on reconnect

```plantuml
@startuml MvvmClient_OfflineCommand_Sequence
autonumber
actor User
participant "View\n(HTML+JS ViewDefinition,\nembedded web engine)" as view
participant "ViewModel" as vm
participant "ClientOutbox\n(IndexedDB / local durable store)" as outbox
participant "Service Worker\n(Background Sync, web target)" as sw
participant "Entity Store API\n(server, ADR-021)" as server
database "Client entity cache\n(last-known-good)" as cache

User -> view: submits a command (e.g. edit Amount)
view -> vm: dispatch command
vm -> outbox: enqueue { CommandId, EntityId, ExpectedVersion, patch }\n(durable write, never mutates local state directly)
outbox --> vm: enqueued
vm --> view: bindable state unchanged\n(optimistic write assumed NOT true, ADR-039)
note right of outbox
  Survives tab close, app crash,
  device restart -- same durability
  bar as ADR-033's peer-sync outbox.
end note
== client is offline ==
outbox -> sw: register sync ("flush-outbox") [web target]
note right of sw
  Fires once connectivity returns,
  even if the app isn't open. Where
  Background Sync isn't supported
  (Safari/WebKit), "flush on next
  open/focus" is the same-outcome
  fallback -- see pwa-offline-outbox.md.
end note
== connectivity returns ==
sw -> outbox: read queued commands, oldest first
outbox -> server: deliver command { CommandId as EventId (ADR-011 dedup) }
alt no conflict
  server --> outbox: 2xx { confirmed entity state, Version }
else conflicting concurrent write (ADR-024)
  server --> outbox: 2xx { confirmed entity state, ConflictFlag: true }
end
outbox -> outbox: dequeue on confirmed delivery only
outbox -> cache: update last-known-good snapshot
cache --> vm: confirmed state flows back (incremental update, ADR-022)
vm --> view: bindable property changes (reactive)
view -> view: re-render bound elements only
@enduml
```

The ViewModel's own write is never treated as truth between the first and
second halves of this diagram — the round trip through the Entity Store is
what makes it real, exactly as `ADR-039` states. `CommandId` doubling as the
`EventId` the server's Idempotent Receiver (`ADR-011`) already dedups on is
why redelivering the same queued command after reconnect never applies
twice — no second dedup mechanism was introduced for the client.

## Sequence diagram — rendering a `ViewDefinition`, with generic fallback

```plantuml
@startuml MvvmClient_ViewDefinition_Fallback_Sequence
autonumber
actor User
participant "ViewModel" as vm
participant "ViewDefinition registry\n(cached client-side, ADR-039)" as registry
participant "Embedded web engine\n(WebView2/WKWebView/CEF/browser)" as webengine
participant "Generic property-list view\n(native fallback)" as fallback
database "Client entity cache" as cache

User -> vm: open entity { EntityType, EntityId }
vm -> cache: read last-known-good snapshot (offline-safe, no network required)
cache --> vm: Data, Extensions, ConflictFlag, LateArrivalFlag, AuthorityStatus
vm -> registry: lookup ViewDefinition(EntityType, CompatibleSchemaVersions)
alt matching ViewDefinition found
  registry --> vm: TemplateContent (HTML+JS, content-addressed, ADR-039)
  vm -> webengine: push entity state via native/JS bridge
  webengine -> webengine: render template, bind fields
  webengine -> webengine: render Extensions/flags via generic\nconvention where the template has no field for them
  webengine --> User: rendered entity view
  User -> webengine: interacts (click, input)
  webengine -> vm: post message through bridge -> dispatch command\n(same downstream event a native control would produce)
else no matching ViewDefinition (unknown entity type/version)
  registry --> vm: not found
  vm -> fallback: render generic property-list view
  fallback -> fallback: list every Data + Extensions property by name,\nplus ConflictFlag/LateArrivalFlag/AuthorityStatus\n(one shared generic "flag" convention)
  fallback --> User: rendered entity view (never a blank/failed render)
end
@enduml
```

## Data model (ER diagram)

```plantuml
@startuml MvvmClient_ER
hide circle
skinparam linetype ortho

entity "ViewDefinition" as viewdef {
  * EntityType : string <<PK>>
  * Version : int <<PK>>
  --
  ViewKind : string
  ' list | detail | edit | custom
  CompatibleSchemaVersions : string
  TemplateContent : text
  ' HTML+JS, content-addressed
  Hash : string
  EffectiveFrom : datetimeoffset
  DeprecatedAt : datetimeoffset <<nullable>>
}

entity "ClientOutboxEntry" as outbox {
  * CommandId : uuid <<PK>>
  --
  InstanceId : string
  ' namespaces entries per client instance/subscription target
  EntityId : string
  ExpectedVersion : long <<nullable>>
  Patch : text
  ' Optional<T>-wrapped, ADR-022
  Status : string
  ' Pending | Delivered | Failed
  EnqueuedAt : datetimeoffset
  Attempts : int
}

entity "ClientEntityCacheEntry" as cache {
  * EntityId : string <<PK>>
  * InstanceId : string <<PK>>
  --
  Data : text
  Extensions : text
  Version : long
  ConflictFlag : bool
  LateArrivalFlag : bool
  AuthorityStatus : string
  CachedAt : datetimeoffset
}

outbox }o..|| cache : "same InstanceId namespace;\nno shared outbox state across instances"
viewdef .. cache : "EntityType/CompatibleSchemaVersions selects\nwhich ViewDefinition renders a cached entity\n(no DB FK -- content-addressed lookup, not a join)"

note right of outbox
  CommandId doubles as the EventId
  the server-side Idempotent Receiver
  (ADR-011) dedups on -- same
  mechanism, not a second one.
end note

note bottom of cache
  InstanceId is the whole reason two
  concurrently-running client instances
  (different EntityType/subscription
  targets) never share outbox or cache
  state, per ADR-039.
end note
@enduml
```

`InstanceId` is the asymmetry worth noticing here, the same way `event-
chains.md`'s ER diagram calls out `ParentEventId`'s missing FK: it's not a
server-side concept at all — it exists purely so two windows of the same
installed app, each configured to a different `EntityType`/subscription
target, can share one Service Worker/cache origin (`pwa-offline-outbox.md`)
while keeping their queued commands and cached snapshots from ever
colliding.

## Salt (UI mockup) — `EntityView` (generic fallback)

Where a registered `ViewDefinition` renders in the embedded web engine, the
rendered shape is exactly the `OrderTable`-style mockup already shown in
[`mvvm-client-architecture.md`](../patterns/mvvm-client-architecture.md)'s
own Salt block — bound data, bound commands, structure-as-config, unchanged
whether a browser or a native WebView2/WKWebView/CEF host renders it. The mockup below
is the other half `ADR-039` requires: the **native, non-web** generic
property-list fallback, for an entity type/version with no matching
`ViewDefinition` at all.

```plantuml
@startsalt
{
  { "Order  o-1  (no registered ViewDefinition -- generic fallback)" }
  ..
  | Property   | Value                        |
  | EntityType | "Order"                      |
  | EntityId   | "o-1"                        |
  | Amount     | 150.00                       |
  | Carrier    | "UPS"                        |
  | PromoCode  | "SPRING24"  ( Extensions )    |
  ..
  { [ ! ConflictFlag ] | [ LateArrivalFlag ] | [ AuthorityStatus: pending_review ] }
  ..
  [ Retry sync ] | [ View change history ]
}
@endsalt
```

Every row comes from the same `Data`/`Extensions` bag a registered
`ViewDefinition` would also receive via the native/JS bridge — the fallback
simply lists properties by name instead of binding them into a
template-defined layout. `PromoCode` renders here because it landed in
`Extensions` (`ADR-022`) rather than a typed slot; a template-backed view
with no field for it would do the same (render it generically, or omit it)
rather than fail to render. The flag row is one shared convention for all
three concerns `ADR-024`/`ADR-029`/`ADR-035` raise (`ConflictFlag` shown
filled/exclaimed here only because it's `true` in this example,
`LateArrivalFlag` shown unset, `AuthorityStatus` shown with its current
value) — not three bespoke indicators.

## Gherkin

```gherkin
Feature: MVVM client (entity views, client-local outbox, native/JS bridge)
  As a user of the MVVM client (native shell or installed web app)
  I want commands to survive being offline and entities to always render
  So that connectivity gaps and unrecognized shapes never lose data or fail the UI

  Background:
    Given the entity type "Order" has a registered ViewDefinition
      | EntityType | Version | ViewKind |
      | Order      | 1       | detail   |
    And the entity type "Shipment" has no registered ViewDefinition
    And client instance "A" is launched configured to follow EntityType "Order"
    And client instance "B" is launched configured to follow EntityType "Shipment"

  Scenario: A command dispatched while offline queues durably and is never lost
    Given client instance "A" is offline
    When I dispatch a command to set Order "o-1"'s Amount to 175.00
    Then the command should be enqueued in client instance "A"'s local outbox
    And the command should still be present in the outbox after the client process restarts

  Scenario: A queued command applies once connectivity resumes, with no duplicate application
    Given a command to set Order "o-1"'s Amount to 175.00 is queued in client instance "A"'s outbox
    When connectivity returns
    Then the outbox should deliver the queued command to the Entity Store
    And Order "o-1"'s confirmed Amount should flow back to the ViewModel as 175.00
    And redelivering the same CommandId should not apply the change a second time

  Scenario: An entity with no registered ViewDefinition still renders via the generic fallback
    When client instance "B" opens Shipment "s-1"
    Then the client should render the generic property-list fallback view
    And the render should not fail

  Scenario: An unaccounted-for property lands in Extensions and never fails the render
    Given Order "o-1"'s ViewDefinition template only has fields for Amount and Carrier
    And Order "o-1"'s entity data carries an additional property "PromoCode"
    When client instance "A" renders Order "o-1"
    Then "PromoCode" should be present in Order "o-1"'s Extensions
    And the rendered view should show it generically or omit it, without a rendering failure

  Scenario: ConflictFlag, LateArrivalFlag, and AuthorityStatus all render via one shared flag convention
    Given Order "o-1" has ConflictFlag true, LateArrivalFlag false, and AuthorityStatus "pending_review"
    When client instance "A" renders Order "o-1"
    Then all three should be shown using the same generic flag-rendering convention
    And no one of the three should use a bespoke indicator of its own

  Scenario: Two client instances scoped to different entity types run concurrently without sharing outbox state
    Given client instance "A" is offline with a queued command for Order "o-1"
    When client instance "B" enqueues a command for Shipment "s-1" while online
    Then client instance "B"'s command should deliver independently of client instance "A"'s connectivity state
    And client instance "A"'s queued command should remain in its own outbox, unaffected by instance "B"

  Scenario: The web client is installable
    When the web client is opened in a browser that supports installation
    Then a Web App Manifest should be served
    And the browser should offer to install the client to the device's home screen/app list

  Scenario: The app shell and cached ViewDefinitions render with no network present at all
    Given the web client has been opened at least once while online
    And Order "o-1"'s ViewDefinition and entity data were cached during that session
    When the device has no network connectivity at all
    And the installed client is opened again
    Then the Service Worker should serve the cached app shell
    And Order "o-1" should render from cached data with no network round trip

  Scenario: Background Sync unavailability falls back to flush-on-focus, never dropping the command
    Given the browser engine does not support the Background Sync API
    And a command for Order "o-1" is queued in the outbox while offline
    When connectivity returns while the app is not open
    Then the queued command should remain in the outbox until the app is next opened or focused
    And the command should flush at that point rather than being lost
```
