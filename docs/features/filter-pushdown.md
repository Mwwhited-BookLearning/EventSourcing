# Feature: OData filter pushdown to the database

Context: full design in `../04-odata-filter-pushdown.md`; this is the query-
translation mechanics underlying [`follow-subscribe.md`](follow-subscribe.md)
— that doc covers the SSE connection lifecycle, this one covers what happens
inside a single poll/query once a `$filter` has been accepted.

## Sequence diagram

```plantuml
@startuml FilterPushdown_Sequence
autonumber
participant "FollowEndpoint /\nbounded-read caller" as caller
participant "ODataFilterParser" as parser
participant "FilterableFields\n(registry)" as fields
participant "PredicateTranslator" as translator
participant "IJsonPathTranslator\n(impl per provider)" as jsonPath
participant "EF Core" as ef
database "SQLite / PostgreSQL / SQL Server" as db

caller -> parser: parse("$filter=Amount gt 100")
parser -> parser: build OData AST (FilterClause)
parser -> fields: for each referenced property, is it a declared FilterableField?
alt any property not declared filterable
  fields --> caller: 400, before any SQL is executed
else all properties declared
  parser -> translator: translate(AST)
  translator -> jsonPath: JsonFunctions.JsonValue(Payload, "$.Amount") -> provider extraction
  note right of jsonPath
    SQLite:     json_extract(Payload, '$.Amount')
    PostgreSQL: (Payload::jsonb ->> 'Amount')
    SQL Server: JSON_VALUE(Payload, '$.Amount')
  end note
  translator -> translator: CAST extracted text to FilterableField.DataType
  translator -> ef: Expression<Func<StoredEvent,bool>>
  ef -> db: SELECT ... WHERE <native JSON extraction> > 100
  db --> ef: matching rows (index used if IsIndexed = true)
  ef --> caller: matching StoredEvent(s)
end
@enduml
```

## Salt (UI mockup)

Not applicable — filter pushdown is an internal query-translation concern
with no UI surface.

## Gherkin

```gherkin
Feature: OData filter pushdown to the database
  As the follow API
  I want $filter expressions translated into native SQL JSON extraction
  So that filtering is executed by the database, not in application memory

  # Runs under the same events:follow-scoped requests as follow-subscribe.md;
  # see auth.md for authentication/authorization behavior itself.

  Scenario Outline: Filter predicate is pushed down identically on every provider
    Given the active database provider is "<provider>"
    And the event type "OrderPlaced" is registered with filterable field "$.Amount" of type "Number", indexed
    And 3 "OrderPlaced" events exist with Amount values 50, 100, 150
    When I query "/follow/OrderPlaced?$filter=Amount gt 100" with a bounded read (not a live stream)
    Then I should receive only the event with Amount 150
    And the generated SQL should contain a native JSON extraction function for "<provider>"

    Examples:
      | provider   |
      | Sqlite     |
      | Postgres   |
      | SqlServer  |

  Scenario: Unsupported field reference is rejected before query execution
    Given the event type "OrderPlaced" is registered with filterable field "$.Amount" of type "Number", indexed
    When I query "/follow/OrderPlaced?$filter=SecretField eq 'x'"
    Then no SQL query should be executed
    And the response status should be 400

  Scenario: Numeric comparison casts extracted text correctly
    Given the event type "OrderPlaced" is registered with filterable field "$.Amount" of type "Number", indexed
    And an "OrderPlaced" event exists with Amount 99.5
    When I query "/follow/OrderPlaced?$filter=Amount gt 99"
    Then the event with Amount 99.5 should be included in the results

  Scenario: String comparison does not require casting
    Given the event type "OrderPlaced" is registered with filterable field "$.Status" of type "String", not indexed
    And an "OrderPlaced" event exists with Status "Paid"
    When I query "/follow/OrderPlaced?$filter=Status eq 'Paid'"
    Then the event with Status "Paid" should be included in the results

  Scenario: Indexed field query uses the expression index / computed column
    Given the event type "OrderPlaced" is registered with filterable field "$.Amount" of type "Number", indexed
    When I query "/follow/OrderPlaced?$filter=Amount gt 100"
    Then the query execution plan should reference the index created for "$.Amount"
```
