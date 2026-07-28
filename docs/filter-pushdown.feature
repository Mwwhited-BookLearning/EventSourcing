Feature: OData filter pushdown to the database
  As the follow API
  I want $filter expressions translated into native SQL JSON extraction
  So that filtering is executed by the database, not in application memory

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
