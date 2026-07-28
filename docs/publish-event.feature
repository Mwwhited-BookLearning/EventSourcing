Feature: Publish events against a registered schema
  As a publishing system
  I want events validated against a named, registered JSON Schema
  So that only well-formed events enter the store

  # Every request in this file carries a Bearer token with the events:publish
  # scope unless a scenario says otherwise. See auth.feature for
  # authentication/authorization behavior itself.
  # The request body is an envelope: {"payload": {...}, "parentEventIds": [...]}.
  # See event-chains.feature for parentEventIds behavior.

  Background:
    Given the event type "OrderPlaced" version 1 is registered with schema:
      """
      {
        "type": "object",
        "properties": {
          "Amount": { "type": "number" },
          "Status": { "type": "string" }
        },
        "required": ["Amount", "Status"]
      }
      """

  Scenario: Publishing a valid event succeeds
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "Amount": 150.00, "Status": "Paid" } }
      """
    Then the response status should be 201
    And the stored event should have SchemaVersion 1
    And the stored event's SequenceNumber should be assigned

  Scenario: Publishing an event missing a required field is rejected
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "Amount": 150.00 } }
      """
    Then the response status should be 400
    And the response should list "Status" as a missing required property
    And no event should be appended to the store

  Scenario: Publishing an event of the wrong type is rejected
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "Amount": "not-a-number", "Status": "Paid" } }
      """
    Then the response status should be 400
    And no event should be appended to the store

  Scenario: Publishing against an unknown event type is rejected
    When I POST to "/publish/NonExistentType" with body:
      """
      { "payload": { "foo": "bar" } }
      """
    Then the response status should be 404

  Scenario: Publishing after a schema version upgrade validates against the active version
    Given the event type "OrderPlaced" version 2 is registered with schema:
      """
      {
        "type": "object",
        "properties": {
          "Amount": { "type": "number" },
          "Status": { "type": "string" },
          "Currency": { "type": "string", "default": "USD" }
        },
        "required": ["Amount", "Status"]
      }
      """
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "Amount": 150.00, "Status": "Paid" } }
      """
    Then the response status should be 201
    And the stored event should have SchemaVersion 2
