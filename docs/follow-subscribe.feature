Feature: Follow an event type via SSE
  As a consuming system
  I want to subscribe to a stream of events of a given type
  So that I receive matching events as they are published

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
