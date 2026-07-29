# 13 — BDD Scenarios (Sample)

These are illustrative starting scenarios, not a complete suite — intended as seeds for
each area of the design. Expand per-feature as implementation proceeds.

```gherkin
Feature: Inbound patch acceptance (see 04)

  Scenario: Malformed payload is still persisted
    Given a client submits a patch with an invalid schema version
    When the server inbox receives the envelope
    Then the event store contains a "received" record
    And the response status is "received"
    And no entity is created until routing completes

  Scenario: Unknown schema version does not block ingestion
    Given a client submits a patch referencing a schema version the server has never registered
    When the server inbox receives the envelope
    Then the event is persisted with status "received"
    And SchemaStatus is "unknown"
    And no other client's or server's processing is delayed as a result


Feature: Partial update semantics (see 06)

  Scenario: Explicit null vs unspecified property
    Given an entity "app1:person:123" with FirstName "Jane" and LastName "Doe"
    When a partial patch specifies LastName as null and does not mention FirstName
    Then the materialized entity has FirstName "Jane" unchanged
    And LastName is cleared to null

  Scenario: Unknown property lands in extensions, known properties still apply
    Given an entity's current schema recognizes FirstName and LastName only
    When a partial patch specifies LastName and an unrecognized property "middleInitial"
    Then LastName is applied to the typed field
    And "middleInitial" is stored in the entity's Extensions bag


Feature: Concurrency and conflict handling (see 08)

  Scenario: Concurrent same-field patch from two origins
    Given entity "app1:person:123" is at version 5
    And Client A submits LastName "Jones" with expectedVersion 5
    And Client B submits LastName "Smith" with expectedVersion 5
    When both patches are appended to the event store
    Then the entity store reflects the later-sequenced patch's value
    And both events are retrievable via entity change history
    And the later-applied event is flagged as a conflict

  Scenario: Concurrent different-field patches do not conflict
    Given entity "app1:person:123" is at version 5
    And Client A submits LastName "Jones" with expectedVersion 5
    And Client B submits Email "b@example.com" with expectedVersion 5
    When both patches are folded
    Then both changes are applied
    And neither event is flagged as a conflict


Feature: Schema evolution (see 07)

  Scenario: Schema upcasting during replay
    Given entity "app1:person:123" has patches authored against schema v1 and v2
    And schema v3 is now current
    When the projector replays the entity's event stream
    Then each patch is upcast to v3 shape before folding
    And the resulting entity conforms to schema v3

  Scenario: Transform function determinism is enforced
    Given a schema map transform function is registered for entityType "widget"
    When the function is evaluated twice with identical input during separate replays
    Then both evaluations produce identical output


Feature: Replication and peer sync (see 09)

  Scenario: Two servers converge after a disconnection
    Given Server A and Server B have been disconnected and each accumulated local writes
    When connectivity is restored and peer sync runs
    Then both servers exchange only the differing event ranges
    And both servers' entity stores eventually converge
    And any genuinely conflicting concurrent writes are flagged, not silently dropped


Feature: Non-authoritative capture (see 12)

  Scenario: Self-attested capture is persisted without proof of authority
    Given a user submits an observation with a self-attested UCAN credential
    And the server cannot reach the identity provider at capture time
    When the event is submitted
    Then the event is persisted with AuthorityStatus "unattested"
    And the event is folded into the entity store like any other event
    And the event is never deleted, regardless of a later review outcome

  Scenario: Authority rejection does not delete history
    Given an event has AuthorityStatus "pending_review"
    When a reviewer submits an authorityDecision event with decision "rejected"
    Then the original event remains in the event store unchanged
    And the entity's AuthorityStatus reflects the rejection per its RejectionBehavior policy


Feature: Deployment and rollback safety (see 11)

  Scenario: Rollback does not lose newer-schema events
    Given a deployment introduces schema v4 and its upcaster
    And an event tagged schema v4 is received and persisted
    When the deployment is rolled back to a version that only knows schema v3
    Then the v4 event remains persisted with status "received"
    And no data is lost or requires database restore
    And the event becomes routable again once v4 support is redeployed
```
