# Feature: Compatibility & versioning discipline (enum fallback, capability negotiation, Expand/Contract, N-1/N+1 rollback)

Context: `ADR-038` decides four distinct mechanisms that this doc gives
one coherent home to, since none had a feature-doc scenario before now
(`08-build-plan.md`, "Compatibility & Deployment Discipline" item, own
"Note" call-out): **(1)** an explicit unknown-value fallback contract for
every enum-like field, **(2)** a version-discovery capability-negotiation
handshake at connection start, **(3)** Expand/Contract (Parallel Change)
database migrations, and **(4)** the N-1/N+1 compatibility window that
makes a deployment rollback safe. `EventTypeDefinition.DeprecatedAt` and
`StoredEvent.SchemaVersion` — the two persisted fields these mechanisms
rest on — are defined in [`../data/schema-registry.md`](../data/schema-registry.md)
and [`../data/event-log.md`](../data/event-log.md) respectively; this doc
introduces no new column on either entity. The general pattern these
mechanisms specialize (Tolerant Reader, Postel's Law, upcasting) is
explained once in
[`../patterns/tolerant-reader-and-schema-evolution.md`](../patterns/tolerant-reader-and-schema-evolution.md)
and not re-derived here — this doc is about the *deployment-time*
guarantees `ADR-038` builds on top of that pattern.

This doc deliberately does **not** re-derive:
- The upcast/downcast transform mechanism itself (`ADR-018`/`ADR-028`,
  registration in [`schema-registry.md`](schema-registry.md)) — this doc
  assumes an `UpcastChain` already exists and focuses on the deployment
  discipline around *not deleting* an upcaster too early.
- `ADR-023`'s full status envelope (`received` / `processing` / `applied`
  / `rejected`, `SchemaStatus: unknown | invalid | conformant`) — defined
  in [`../data/event-log.md`](../data/event-log.md) and exercised end to
  end in [`entity-concept.md`](entity-concept.md); this doc only uses the
  `received` state as the safe landing point for an event a rolled-back
  deployment can't yet route.
- The GraphQL Gateway's own connection/transport mechanics (`ADR-037`,
  [`follow-subscribe.md`](follow-subscribe.md)) — this doc's capability-
  negotiation diagram reuses that same `QUERY /graphql` connection-open
  step rather than inventing a second transport.
- Feature flags as a mechanism in their own right (`ADR-077`,
  `FeatureFlagState` in [`../data/schema-registry.md`](../data/schema-registry.md))
  — `ADR-038` names feature flags only as "a faster lever than binary
  rollback," complementary to the mechanisms below, not a fifth
  mechanism this doc needs its own scenario for.
- The rollback drill's own exit criterion, already stated in
  [`../08-build-plan.md`](../08-build-plan.md), "Compatibility &
  Deployment Discipline" — the Gherkin scenario below restates it as a
  test scenario in this doc's own vocabulary, it does not redefine it.

**A wire-shape caveat, stated explicitly rather than glossed over**:
`ADR-038` gives one concrete example of the enum-fallback contract
(`status: "newValue", statusKnown: false`) and names one alternative
strategy (default to a designated `Unknown` enum member) as a **per-field
choice made at registration**, without mandating one over the other or
specifying a field-naming convention for the boolean sibling beyond that
one example. The sequence diagram below shows the example `ADR-038`
itself gives, verbatim. Likewise, `ADR-038` describes capability
negotiation only in the abstract ("a lightweight capability-negotiation
handshake... at connection start... plus self-describing payloads") and
never pins down a concrete endpoint, message, or field shape for it. The
diagram below is **one concrete, consistent realization** built on the
connection-open step `ADR-037`/`follow-subscribe.md` already establish
for Follow — it is this doc's own structural choice, not a shape `ADR-038`
states, and is flagged as such rather than cited as if verified.

## Sequence diagram — enum unknown-value fallback

```plantuml
@startuml Enum_Unknown_Value_Fallback_Sequence
autonumber
participant "Old Client\n(deployed before OrderStatus\ngained a new value)" as client
participant "GraphQL Gateway" as gateway
database "Entity Store" as entityStore

note over gateway
  A newer deployment registered OrderStatus
  value "PartiallyRefunded" -- this client's
  build predates that change (ADR-038's
  N-1/N+1 window: the client is running
  code from a still-supported prior version).
end note

client -> gateway: QUERY /graphql\nquery { order(id: "demo:Order:o-1") { status statusKnown } }
gateway -> entityStore: resolve Order.status for "demo:Order:o-1"
entityStore --> gateway: raw stored value = "PartiallyRefunded"
alt raw value is a recognized OrderStatus member for this client's schema version
  gateway --> client: { status: "Shipped", statusKnown: true }
else raw value is not a recognized member (this scenario)
  gateway --> client: { status: "PartiallyRefunded", statusKnown: false }
  note right: ADR-038's declared contract -- the raw string travels\nalongside a "known" flag, never a thrown error and\nnever a silently substituted wrong value
end
client -> client: exhaustive switch on OrderStatus has no case for\nan unrecognized string -- branches on statusKnown == false\ninstead of throwing (ADR-038's required client-side discipline)
@enduml
```

The alternative strategy `ADR-038` names — defaulting deserialization to
a designated `Unknown` enum member instead of carrying a raw string — is
the same contract expressed as a closed GraphQL enum with an explicit
`UNKNOWN` value baked in at registration time; it isn't diagrammed
separately because the two strategies differ only in *which* schema-
registration-time choice a field's author makes (raw-string-plus-flag
vs. closed-enum-plus-catch-all-member), not in the deployment-safety
property either one delivers.

## Sequence diagram — version-discovery capability negotiation

```plantuml
@startuml Capability_Negotiation_Sequence
autonumber
actor "Consuming System\n(older build)" as client
participant "GraphQL Gateway" as gateway
participant "SchemaRegistryService" as registry

client -> gateway: QUERY /graphql, connection open\ndeclares supportedSchemaVersions: [2, 3] for "OrderPlaced"\n(this doc's own realization of ADR-038's abstract\n"declares supported schema version(s)... at connection start")
gateway -> registry: what versions of "demo:OrderPlaced" does this\ndeployment currently serve (active version, plus the\nN-1/N+1 window's still-supported neighbors)?
registry --> gateway: activeVersion: 3, supportedWindow: [2, 3, 4]\n(version 4 not yet active; 2 kept per the N-1/N+1 window)
alt client's declared versions overlap supportedWindow (this scenario: {2,3} ∩ {2,3,4} = {2,3})
  gateway --> client: connection accepted\nserverCapabilities: { activeVersion: 3, supportedWindow: [2,3,4] }
  note right: client can now choose to request either version's shape --\nself-describing payloads (SchemaVersion on every event/entity)\nmean it never has to guess which one it actually got
else client's declared versions have zero overlap with supportedWindow\n(e.g. client only knows version 1, already outside the window\nand its upcaster since removed)
  gateway --> client: connection rejected -- capability mismatch\n(no schema version this client understands is still served)
  note right: a genuine, no-longer-bridgeable gap -- distinct from an\nordinary N-1/N+1 rollback, where the older version's upcaster\nis still guaranteed present and this branch never triggers
end
@enduml
```

The "rejected" branch is this doc's own extrapolation for the case where
overlap is truly empty (a client stuck on a version old enough that even
its upcaster has since been removed) — `ADR-038` states the N-1/N+1
window as the *minimum* guarantee, not a hard cutoff, and never says what
happens beyond it; this diagram treats "no shared version at all" as the
only case where a hard rejection is warranted, everything inside the
window as an accepted connection.

## Sequence diagram — Expand/Contract migration and the N-1/N+1 rollback window

```plantuml
@startuml Expand_Contract_Rollback_Sequence
autonumber
actor "Release Engineer" as releng
participant "Deployment N\n(old binary)" as depN
participant "Deployment N+1\n(new binary)" as depNplus1
database "Event & Schema Store" as db

== Expand ==
releng -> db: apply migration: ADD COLUMN RefundReason NULLABLE\n(existing columns/tables untouched -- ADR-038 Expand step)
note right: Deployment N (still running the old binary)\nis unaffected -- it never selects the new column

== Migrate: deploy new code, old code kept serving ==
releng -> depNplus1: deploy new binary (writes RefundReason on new events)
depNplus1 -> db: INSERT StoredEvent (..., SchemaVersion: 4, RefundReason: "damaged")
note over depN, depNplus1: rolling deploy -- some requests still land on\nDeployment N while N+1 comes up (ADR-038's "no forced\nrenegotiation" -- no session affinity to a specific version)
depN -> db: INSERT StoredEvent (..., SchemaVersion: 3)\n-- old code, unaware RefundReason even exists
db --> depN: accepted -- old code's writes are still valid rows,\nno NOT NULL constraint on the new column

== Rollback drill (08-build-plan.md, "Compatibility & Deployment\nDiscipline" exit criterion) ==
releng -> depNplus1: publish an event tagged SchemaVersion 4
depNplus1 -> db: INSERT StoredEvent (Status: received, SchemaVersion: 4)
releng -> depNplus1: roll back -- redeploy Deployment N (doesn't know version 4)
depN -> db: poll/route pending events
db --> depN: the SchemaVersion-4 event is present\n(never lost -- ADR-023's status envelope keeps\n"durably persisted" separate from "successfully routed")
depN -> depN: no upcaster/schema known for version 4 --\nevent stays Status: received, unrouted, never dropped
releng -> depNplus1: re-forward-deploy Deployment N+1
depNplus1 -> db: resume routing -- the version-4 event becomes\nroutable and folds normally, no data loss, no database restore
@enduml
```

## Data model (ER diagram)

No new column or table — both fields these mechanisms rest on are
already established elsewhere; this diagram shows only the two that this
doc's own scenarios touch, full column lists remain in
[`../data/schema-registry.md`](../data/schema-registry.md) and
[`../data/event-log.md`](../data/event-log.md).

```plantuml
@startuml CompatibilityVersioning_ER
hide circle
skinparam linetype ortho

entity "EventTypeDefinition" as etd {
  * AppId : string <<PK>>
  * Name : string <<PK>>
  * Version : int <<PK>>
  --
  DeprecatedAt : datetimeoffset?
  UpcastFromPrevious : string?
  DowncastToPrevious : string?
}

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  SchemaVersion : int
  Status : string
}

etd ..> event : "SchemaVersion identifies which\nEventTypeDefinition version\nan event was published against\n-- logical only, not a DB FK"

note right of etd
  DeprecatedAt is set, never removed, when a
  version/field enters its deprecation window
  (ADR-038). UpcastFromPrevious/DowncastToPrevious
  are what makes the N-1/N+1 window actually work
  at read time -- never deleted the moment a new
  version ships.
end note
@enduml
```

## Salt (UI mockup)

Not applicable — this doc covers wire-protocol and deployment-time
discipline with no UI surface of its own, the same reasoning
[`entity-concept.md`](entity-concept.md) applies to the Entity Store fold;
see `ADR-039`/[`mvvm-client.md`](mvvm-client.md) for where a UI eventually
renders an entity carrying a `statusKnown: false` field (the generic
property-list/flag-rendering fallback that doc's own exit criteria
already cover, not re-derived here).

## Gherkin

```gherkin
Feature: Compatibility & versioning discipline
  As a platform operator running a mixed-version deployment
  I want unknown enum values, mismatched client capabilities, database
    migrations, and rolled-back deployments to all fail safe
  So that no event is ever lost or silently misinterpreted across a
    rolling upgrade or rollback

  # ADR-038 throughout. AppId "demo" unless a scenario says otherwise.

  Background:
    Given the event type "OrderPlaced" version 3 is registered with EntityIdField "$.OrderId"
    And "OrderPlaced" version 3's schema declares "status" as an enum-like field
      with the fallback contract "raw string alongside statusKnown"

  Scenario: An old client receives an event carrying an enum value it doesn't recognize
    Given a "demo:Order:o-1" entity was last patched by an event carrying
      status "PartiallyRefunded", a value added after this client's own build
    When the client queries { order(id: "demo:Order:o-1") { status statusKnown } }
    Then the response should equal { "status": "PartiallyRefunded", "statusKnown": false }
    And the client should not throw or crash on the unrecognized value
    # ADR-038's declared contract -- the raw string travels alongside a
    # "known" flag rather than the server substituting a wrong value or
    # the client's exhaustive switch throwing.

  Scenario: A client declaring a schema version inside the N-1/N+1 window negotiates successfully
    Given the deployment's active version for "OrderPlaced" is 3, with versions 2 and 4 also still supported
    When a client opens a connection declaring supportedSchemaVersions [2, 3]
    Then the connection should be accepted
    And the server should report serverCapabilities activeVersion 3, supportedWindow [2, 3, 4]

  Scenario: A client declaring only a version outside the supported window fails negotiation
    Given the deployment's active version for "OrderPlaced" is 3, with versions 2 and 4 also still supported
    And version 1's upcaster was removed after multiple deployment cycles
    When a client opens a connection declaring supportedSchemaVersions [1]
    Then the connection should be rejected with a capability-mismatch error
    # Distinct from an ordinary N-1/N+1 gap -- ADR-038's window guarantees
    # the immediately-prior/next version always work; this scenario is the
    # genuinely-unbridgeable case, once an upcaster far enough back is gone.

  Scenario: An Expand/Contract migration adds a nullable column without breaking the still-running old binary
    Given Deployment N is running against the current database shape
    When the release engineer applies a migration adding nullable column "RefundReason" to the Orders table
    Then Deployment N should continue inserting events successfully with no RefundReason value
    And Deployment N+1, once deployed, should write RefundReason on new events
    And no existing column or table should be altered or dropped by the migration
    # ADR-038's Expand step -- Contract (dropping the old shape) is optional
    # and, given this design's "never lose data" principle, may never happen.

  Scenario: A rolled-back deployment doesn't lose an event tagged with a schema version it doesn't know
    # Restates 08-build-plan.md's "Compatibility & Deployment Discipline"
    # exit criterion in this doc's own Gherkin vocabulary -- not a
    # redefinition of it.
    Given "OrderPlaced" version 4 has been deployed and is active
    When an event tagged SchemaVersion 4 is published and persists with status "received"
    And the deployment is rolled back to a version that does not know SchemaVersion 4
    Then that event should still sit with status "received", never lost
    When the deployment is re-forward-deployed to a version that knows SchemaVersion 4 again
    Then that event should become routable with no data loss and no database restore
```
