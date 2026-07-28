# Feature: Follow an event type via SSE

Context: full contract in `../03-api-contracts.md`; the `$filter` pushdown
mechanics (per-provider SQL translation) are covered in depth in
[`filter-pushdown.md`](filter-pushdown.md), not repeated here; auth
requirements, including the browser `EventSource` `access_token`
query-string caveat, in [`auth.md`](auth.md).

## Sequence diagram

```plantuml
@startuml Follow_Sequence
autonumber
actor "Consuming System" as follower
participant "Follow API\n(FollowEndpoint)" as endpoint
participant "Auth\n(JWT Bearer + scope policy)" as auth
participant "ODataFilterParser" as parser
participant "PredicateTranslator" as translator
participant "EventTailReader" as tailReader
database "Event & Schema Store" as db

follower -> endpoint: GET /follow/{event-type}?$filter=...[&access_token=...]
endpoint -> auth: validate token (header, or access_token query param) + events:follow scope
alt missing/invalid token
  auth --> follower: connection rejected 401
else valid token, missing scope
  auth --> follower: connection rejected 403
else authorized
  endpoint -> parser: parse $filter (if present)
  alt event-type unknown
    endpoint --> follower: connection rejected 404
  else $filter references a field not in FilterableFields
    parser --> follower: connection rejected 400
  else filter valid or absent
    endpoint -> translator: build predicate against declared FilterableFields
    endpoint -> follower: SSE connection open (200)
    loop every poll interval, while connection open
      endpoint -> tailReader: poll WHERE SequenceNumber > lastSeen AND predicate
      tailReader -> db: SELECT ... (predicate pushed down, see filter-pushdown.md)
      db --> tailReader: matching StoredEvent rows (if any)
      tailReader --> endpoint: matching events
      endpoint -> follower: SSE event(s): headers{eventId, sequenceNumber,\nparentEventIds}, data{payload}
    end
  end
end
@enduml
```

## Data model (ER diagram)

```plantuml
@startuml FollowSubscribe_ER
hide circle
skinparam linetype ortho

entity "EventTypeDefinition" as etd {
  * Name : string <<PK>>
  * Version : int <<PK>>
  --
  IsActive : bool
}

entity "FilterableField" as ff {
  * Id : int <<PK>>
  --
  EventTypeName : string <<FK>>
  EventTypeVersion : int <<FK>>
  JsonPath : string
  DataType : enum {String, Number, Boolean, DateTimeOffset}
  IsIndexed : bool
}

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EventId : uuid <<unique>>
  EventType : string
  SchemaVersion : int
  Payload : text
  OccurredAt : datetimeoffset
}

etd ||--o{ ff : "(Name, Version) = (EventTypeName, EventTypeVersion)"
etd ..> event : "logical only -- EventType/SchemaVersion,\nNOT a DB foreign key"

note right of ff
  $filter may only reference a JsonPath
  declared here (400 otherwise -- see
  filter-pushdown.md).
end note
@enduml
```

Full entity set is in `../02-data-model.md` — this diagram shows only what
the follow/tail path reads.

## Salt (UI mockup)

Not applicable — following is a machine-to-machine (or browser-`EventSource`)
API with no UI surface in scope.

## Gherkin

```gherkin
Feature: Follow an event type via SSE
  As a consuming system
  I want to subscribe to a stream of events of a given type
  So that I receive matching events as they are published

  # Every connection in this file carries a Bearer token with the
  # events:follow scope unless a scenario says otherwise. See auth.md for
  # authentication/authorization behavior itself.

  Background:
    Given the event type "OrderPlaced" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["Amount", "Status"] }
      """
    And "OrderPlaced" has filterable fields:
      | jsonPath   | dataType | isIndexed |
      | $.Amount   | Number   | true      |
      | $.Status   | String   | false     |

  Scenario: Connecting without a filter streams all events of the type
    Given I open an SSE connection to "/follow/OrderPlaced"
    When an "OrderPlaced" event with body {"Amount": 50, "Status": "Paid"} is published
    Then I should receive that event on the SSE stream

  Scenario: Connecting with a filter only streams matching events
    Given I open an SSE connection to "/follow/OrderPlaced?$filter=Amount gt 100"
    When an "OrderPlaced" event with body {"Amount": 50, "Status": "Paid"} is published
    And an "OrderPlaced" event with body {"Amount": 150, "Status": "Paid"} is published
    Then I should receive only the event with Amount 150 on the SSE stream

  Scenario: Filtering on a field not marked filterable is rejected at connection time
    When I open an SSE connection to "/follow/OrderPlaced?$filter=InternalNotes eq 'x'"
    Then the connection should be rejected with 400
    And the response should state "InternalNotes" is not a filterable field

  Scenario: Filtering combines multiple conditions
    Given I open an SSE connection to "/follow/OrderPlaced?$filter=Amount gt 100 and Status eq 'Paid'"
    When an "OrderPlaced" event with body {"Amount": 150, "Status": "Pending"} is published
    And an "OrderPlaced" event with body {"Amount": 150, "Status": "Paid"} is published
    Then I should receive only the second event on the SSE stream

  Scenario: Connecting to an unknown event type is rejected
    When I open an SSE connection to "/follow/NonExistentType"
    Then the connection should be rejected with 404
```
