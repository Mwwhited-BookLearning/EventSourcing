# Feature: Specimen Collection, Derivation, and Lineage

Context: this doc exercises the mechanisms `../README.md`'s "Applicable
ADRs" section already names as biobanking's primary fits: `ADR-005`
(event lineage/DAG — named there as "the cleanest lineage fit found
across every candidate," a derived sample tracing back to its source
specimen as a literal DAG, not an analogy for one), `ADR-043`/`ADR-044`
(delegated, capped, time-boxed access grants — the domain README quotes
`ADR-043`'s own Compliance note naming "biobanking's IRB/biobank-
committee-reviewed researcher access requests" as one of its two
real-world standout fits), `ADR-036` (UCAN delegation/OAuth Token
Exchange, the mechanism `ADR-043` reuses rather than reinventing), and
`ADR-045` (the read access audit log every one of those reads also
writes to). Persist-everything ingestion and `EntityId` resolution are
`ADR-023`/`ADR-021`; the specimen and derived-sample rows this doc
folds into are ordinary `EntityStoreRow`s per
[`../../../data/entity-store.md`](../../../data/entity-store.md); the
event envelope fields used below (`EntityId`, `parentEventIds`,
`ActorId`, `AttestedClaims`) are defined in
[`../../../data/event-log.md`](../../../data/event-log.md).

This doc deliberately does **not** re-derive:
- The general `parentEventIds`/lineage DAG mechanics (`ParentValidationMode`,
  cycle-safety, the Lineage API's traversal shape) — those are
  `ADR-005`, covered end-to-end in
  [`../../../features/event-chains.md`](../../../features/event-chains.md).
  This doc shows the same mechanism landing on a *literal* physical-specimen
  DAG rather than re-explaining how traversal works.
- The general UCAN delegation / DID / OAuth Token Exchange mechanics
  (issuance, self-verifying signature chains, the `POST /oauth/token`
  exchange shape) — those are `ADR-036`, covered end-to-end in
  [`../../../features/did-ucan-attestation.md`](../../../features/did-ucan-attestation.md).
  This doc shows that same exchange used for `ADR-043`'s entity-scoped
  grant case, not a second attestation mechanism.
- `ADR-045`'s `AccessLogEntry` mechanics themselves (hash-chain shape,
  independent sequence) — this doc only notes *that* a delegated read
  writes one, per [`../../../data/access-log.md`](../../../data/access-log.md).
- The GDPR erasure-vs-broad-consent tension the domain README's "Special
  concerns" section already names as "the sharpest erasure-vs-retention
  tension of any candidate considered" — that tension exists and is
  real, but resolving it is out of scope here; this doc's scenarios
  never attempt an erasure request against an active specimen.

## Sequence diagram — collecting a specimen, deriving a sample, and querying lineage

Collection is an origin event (no `parentEventIds`); deriving a secondary
sample is a *child* event that declares the source specimen as its
parent, exactly the literal DAG shape the domain README calls out. The
Lineage query at the end is the same GraphQL-transported traversal
`event-chains.md` already documents (`ADR-037` moved it off the old
`QUERY /events/...` REST shape onto GraphQL) — shown here against real
specimen data rather than re-derived.

```plantuml
@startuml Specimen_Collection_Derivation_Lineage_Sequence
autonumber
actor "Biobank Staff\n(collection tech)" as staff
participant "PublishEndpoint\n(Inbox)" as inbox
participant "Router / EventStore.Fold" as fold
database "Event Log" as eventLog
database "Entity Store" as entityStore
actor "Lab Technician\n(derives DNA extract)" as labTech
actor "Consuming System\n(lineage viewer)" as viewer
participant "GraphQL Gateway" as gql

staff -> inbox: POST /publish/SpecimenCollected\n{ payload: { SpecimenId: "spec-001", SpecimenType: "Blood",\n  DonorReference: "donor-77", CollectionDate: "2026-07-01" } }
inbox -> eventLog: INSERT StoredEvent\n(EntityId: "biobank:Specimen:spec-001", ActorId: "staff-12",\n no parentEventIds -- origin event, ADR-005)
inbox --> staff: 202 { correlationId, status: "received" }
fold -> entityStore: INSERT EntityStoreRow\n(EntityId: "biobank:Specimen:spec-001", Data: { SpecimenType: "Blood", ... })

...later, in the lab...

labTech -> inbox: POST /publish/SpecimenDerived\n{ payload: { SpecimenId: "spec-001-dna", DerivedFrom: "spec-001",\n  SpecimenType: "DNA Extract", CollectionDate: "2026-07-03" },\n  parentEventIds: ["<eventId of the SpecimenCollected event above>"] }
inbox -> eventLog: validate parentEventIds under ParentValidationMode\n(Strict for SpecimenDerived -- parent must already exist, ADR-005)
alt parent SpecimenCollected event exists (it does here)
  inbox -> eventLog: INSERT StoredEvent\n(EntityId: "biobank:Specimen:spec-001-dna",\n EventParents row: ChildEventId -> ParentEventId)
  inbox --> labTech: 202 { correlationId, status: "received" }
  fold -> entityStore: INSERT EntityStoreRow\n(EntityId: "biobank:Specimen:spec-001-dna",\n Data: { SpecimenType: "DNA Extract", DerivedFrom: "spec-001" })
end

...a researcher later reviews provenance for the derived sample...

viewer -> gql: QUERY { specimenLineage(entityId: "biobank:Specimen:spec-001-dna") {\n  ancestors { entityId eventType occurredAt }\n  descendants { entityId eventType occurredAt } } }
gql -> eventLog: walk EventParents transitively from the derived sample's\noriginating event (ADR-005 traversal, event-chains.md)
eventLog --> gql: [ { entityId: "biobank:Specimen:spec-001",\n    eventType: "SpecimenCollected", occurredAt: "2026-07-01" } ]
gql --> viewer: 200 { ancestors: [ { specimen "spec-001" } ], descendants: [] }
note over viewer, gql
  Every read through this Gateway also writes an ADR-045 AccessLogEntry
  (ReaderActorId, ReaderTrustBasis) -- not repeated per-call in this
  diagram, see the second sequence diagram below for the delegated-access
  case where trust basis actually varies.
end note
@enduml
```

## Sequence diagram — IRB-authorized delegated access for a collaborating researcher

The granter here is biobank staff holding a real clearance claim over
specimen data (`ADR-046`); the grant itself is scoped to exactly one
`EntityId` (one specimen), not blanket access — the actual shape of an
IRB-approved external-researcher request the domain README and
`ADR-043`'s Compliance note both name directly. Grant issuance/
revocation are ordinary registered event types (`accessGrant`/
`accessGrantRevoked`, per `ADR-043`), folded and audited like any other
event.

```plantuml
@startuml Specimen_Delegated_Access_Sequence
autonumber
actor "Biobank Staff\n(IRB-cleared granter,\nclearance:specimen-data)" as granter
actor "External Researcher\n(holds own DID)" as researcher
participant "PublishEndpoint\n(Inbox)" as inbox
participant "EventStore.DevIdp\n(OAuth Token Exchange, ADR-036)" as idp
participant "GraphQL Gateway" as gql
database "Event Log" as eventLog
database "Access Audit Log" as accessLog

== IRB-approved grant issuance ==
granter -> granter: issue UCAN delegation naming\nresearcher's DID, claim "clearance:specimen-data",\nentityScope "biobank:Specimen:spec-001-dna", exp: 2026-08-06T00:00Z
granter -> inbox: POST /publish/accessGrant\n{ payload: { GranteeDid: "did:key:z6Mk...researcher",\n  DelegatedClaim: "clearance:specimen-data",\n  EntityScope: "biobank:Specimen:spec-001-dna",\n  ExpiresAt: "2026-08-06T00:00:00Z" } }
inbox -> eventLog: INSERT StoredEvent (accessGrant, ActorId: "staff-12")
inbox --> granter: 202 { correlationId, status: "received" }

== Researcher exchanges the UCAN for a bearer JWT ==
researcher -> idp: POST /oauth/token\ngrant_type=urn:ietf:params:oauth:grant-type:token-exchange\nsubject_token=<UCAN invocation>\nrequested_token_type=urn:ietf:params:oauth:token-type:jwt
idp -> idp: validate UCAN delegation chain\n(self-verifying, capped to granter's own clearance -- ADR-036/ADR-043)
idp --> researcher: 200 { access_token }\nJWT claims include clearance:specimen-data,\nentityScope: "biobank:Specimen:spec-001-dna", exp

== Successful read, within scope and before expiration ==
researcher -> gql: QUERY { specimen(entityId: "biobank:Specimen:spec-001-dna") { specimenType donorReference collectionDate } }\nAuthorization: Bearer <exchanged JWT>
gql -> gql: check claim "clearance:specimen-data" AND entityScope\nmatches requested EntityId (ADR-043/ADR-008 extension)
gql --> researcher: 200 { specimenType: "DNA Extract",\n  donorReference: { "masked": "***" }, collectionDate: "2026-07-03" }
gql -> accessLog: INSERT AccessLogEntry\n(ReaderActorId: researcher DID, ReaderTrustBasis: "Attested",\n GrantRef: <accessGrant eventId>, ResourceRef: "biobank:Specimen:spec-001-dna")

alt researcher attempts to read a specimen OUTSIDE the granted entityScope
  researcher -> gql: QUERY { specimen(entityId: "biobank:Specimen:spec-002") { specimenType } }\nAuthorization: Bearer <same exchanged JWT>
  gql -> gql: claim "clearance:specimen-data" present,\nbut entityScope "biobank:Specimen:spec-001-dna" != "biobank:Specimen:spec-002"
  gql --> researcher: 403 (claim does not apply to this EntityId)
  gql -> accessLog: INSERT AccessLogEntry\n(Action: "query", denied -- ADR-045 logs attempted reads too)
else grant has since expired or been revoked
  researcher -> idp: POST /oauth/token (same exchange, after ExpiresAt\nor after an accessGrantRevoked event for this grant)
  idp -> idp: check UCAN exp / revocation status at exchange time\n(not just relying on a previously issued JWT's own exp)
  idp --> researcher: 400 invalid_grant
  note right: revocation depends on the IdP actually checking revocation\nstatus at each exchange, same operational requirement ADR-040's\nticket consumption already has (ADR-043 Consequences)
end
@enduml
```

## Data model (ER diagram)

```plantuml
@startuml Specimen_Lineage_ER
hide circle
skinparam linetype ortho

entity "StoredEvent (SpecimenCollected)" as collected {
  * SequenceNumber : bigint <<PK>>
  --
  EventId : uuid <<unique>>
  EntityId : string  ' biobank:Specimen:spec-001
  EventType : string  ' "SpecimenCollected"
  ActorId : string  ' verified collection staff (ADR-064)
  Payload : text  ' SpecimenType, DonorReference, CollectionDate
}

entity "StoredEvent (SpecimenDerived)" as derived {
  * SequenceNumber : bigint <<PK>>
  --
  EventId : uuid <<unique>>
  EntityId : string  ' biobank:Specimen:spec-001-dna
  EventType : string  ' "SpecimenDerived"
  ActorId : string
  Payload : text  ' SpecimenType, DerivedFrom, CollectionDate
}

entity "EventParent" as parent {
  * ChildEventId : uuid <<PK, FK>>
  * ParentEventId : uuid <<PK>>
}

entity "SpecimenEntityStoreRow" as row {
  * EntityId : string <<PK>>
  --
  EntityType : string  ' "Specimen"
  SpecimenType : string  ' "Blood" | "Tissue" | "DNA Extract" | ...
  DonorReference : masked<string>  ' value/masked wrapper, ADR-009
  CollectionDate : datetimeoffset
  DerivedFrom : string?  ' denormalized parent EntityId, convenience only --
                          ' the EventParent DAG above is the source of truth
  Version : bigint
}

entity "AccessGrant (accessGrant event, folded)" as grant {
  * EntityId : string <<PK>>  ' biobank:AccessGrant:grant-1
  --
  GranterActorId : string
  GranteeDid : string  ' did:key:...
  DelegatedClaim : string  ' e.g. "clearance:specimen-data"
  EntityScope : string  ' the one Specimen EntityId this grant applies to
  ExpiresAt : datetimeoffset
  RevokedAt : datetimeoffset?
}

derived --> parent : "ChildEventId"
collected --> parent : "ParentEventId\n(SpecimenDerived's parent, ADR-005)"
collected ..> row : "folds into (ADR-021)"
derived ..> row : "folds into a second row,\nDerivedFrom points back at the source"
grant ..> row : "EntityScope restricts a delegated\nclaim to exactly this EntityId (ADR-043)"

note right of row
  DonorReference is x-masking classified
  (ADR-009) -- an ordinary claim-holder
  (biobank staff) sees the real value,
  a scoped external researcher sees
  {"masked": "***"}, as shown in the
  delegated-access sequence diagram above.
end note
@enduml
```

Full column lists live in
[`../../../data/event-log.md`](../../../data/event-log.md),
[`../../../data/entity-store.md`](../../../data/entity-store.md), and
[`../../../data/access-log.md`](../../../data/access-log.md) — this
diagram shows only the columns this doc's scenarios actually touch.

```csharp
// Illustrative only -- SpecimenCollected/SpecimenDerived are ordinary
// registered event types, not new StoredEvent subclasses; shown here as
// the resolved shape of Payload plus the envelope fields these scenarios
// exercise (../../../data/event-log.md defines StoredEvent itself).

public class SpecimenCollectedPayload
{
    public string SpecimenId { get; set; } = default!;   // resolves EntityId via EntityIdField "$.SpecimenId"
    public string SpecimenType { get; set; } = default!; // "Blood" | "Tissue" | "DNA Extract" | ...
    public string DonorReference { get; set; } = default!; // x-masking classified (ADR-009) -- masked by default
    public DateTimeOffset CollectionDate { get; set; }
}

public class SpecimenDerivedPayload
{
    public string SpecimenId { get; set; } = default!;   // the NEW derived specimen's own id
    public string DerivedFrom { get; set; } = default!;  // denormalized convenience copy of the source SpecimenId --
                                                            // parentEventIds (ADR-005) is the actual causal DAG edge, this
                                                            // field never substitutes for it
    public string SpecimenType { get; set; } = default!;
    public DateTimeOffset CollectionDate { get; set; }
    // parentEventIds is envelope metadata on the publish request, not a
    // Payload field -- see event-log.md's "Event lineage" section.
}

public class SpecimenEntityStoreRow
{
    public string EntityId { get; set; } = default!;     // biobank:Specimen:{specimenId}, PK
    public string EntityType { get; set; } = default!;   // "Specimen"
    public string SpecimenType { get; set; } = default!;
    public string DonorReference { get; set; } = default!; // wrapped value/masked at query time (ADR-009), stored plaintext
    public DateTimeOffset CollectionDate { get; set; }
    public string? DerivedFrom { get; set; }               // denormalized parent EntityId, null for an origin specimen
    public long Version { get; set; }
}

public class AccessGrant
{
    public string EntityId { get; set; } = default!;      // biobank:AccessGrant:{grantId}, PK -- an ordinary folded entity
    public string GranterActorId { get; set; } = default!; // must hold DelegatedClaim already (ADR-043's UCAN cap invariant)
    public string GranteeDid { get; set; } = default!;      // did:key:... (ADR-036)
    public string DelegatedClaim { get; set; } = default!;  // e.g. "clearance:specimen-data"
    public string EntityScope { get; set; } = default!;      // the ONE Specimen EntityId this grant applies to (ADR-043/ADR-008)
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }           // set by a later accessGrantRevoked event, never deletes this row
}
```

## State machine — one specimen's lifecycle

```plantuml
@startuml Specimen_Lifecycle_State
skinparam StateFontSize 12

[*] --> Collected : SpecimenCollected
Collected --> Stored : SpecimenStored
Stored --> Derived : SpecimenDerived\n(creates a NEW child specimen,\nparentEventIds -> this one, ADR-005)
Derived --> Stored : (the derived child specimen\nitself becomes Stored)
Stored --> Distributed : SpecimenDistributed
Stored --> Destroyed : SpecimenDestroyed
Distributed --> [*]
Destroyed --> [*]

note right of Stored
  Consent is an orthogonal concern, not a
  state of the specimen itself -- a
  ConsentWithdrawn event can arrive while a
  specimen sits in any of these states and
  does not by itself move this state machine
  (the erasure-vs-broad-consent tension this
  raises is named in ../README.md's Special
  concerns, deliberately not resolved here).
end note
@enduml
```

## Salt (UI mockup)

```plantuml
@startsalt
{
  { "Specimen Lineage Viewer -- spec-001-dna" }
  ..
  { "Ancestors" | "This specimen" | "Derived children" }
  ..
  {
    { "spec-001\n(Blood, collected 2026-07-01)" } | > | { "spec-001-dna\n(DNA Extract, 2026-07-03)" [selected] } | > | { "(none yet)" }
  }
  ..
  | Property        | Value                          |
  | SpecimenType     | "DNA Extract"                  |
  | DonorReference   | "***"  ( masked -- no clearance:specimen-data ) |
  | CollectionDate   | "2026-07-03"                    |
  ..
  { [ Request Access ] | "Pending IRB-approved grant: staff-12 -> did:key:z6Mk...researcher  [ Awaiting IRB ]" }
  ..
  [ View full ancestor chain ] | [ Close ]
}
@endsalt
```

The lineage tree, masked `DonorReference`, and "Request Access"/pending-
grant indicator all read directly off the mechanisms shown in this doc's
sequence diagrams — the tree from the Lineage query, the mask from
`ADR-009` applied to a caller without `clearance:specimen-data`, and the
pending-grant row from an issued-but-not-yet-IRB-approved `accessGrant`.
Which UI architecture actually renders this screen (`ADR-039`'s MVVM, or
a named fallback) is out of scope here — see
[`../../../features/mvvm-client.md`](../../../features/mvvm-client.md).

## Gherkin

```gherkin
Feature: Specimen Collection, Derivation, and Lineage
  As a biobank operator
  I want a collected specimen's derived samples to carry a real, queryable lineage,
  and IRB-approved external access to be scoped to exactly one specimen and time-boxed
  So that provenance is never lost and researcher access never exceeds what was approved

  # Every request in this file carries a Bearer token with sufficient scope
  # (events:publish for collection/derivation staff, a delegated
  # clearance:specimen-data claim for the researcher scenarios below) unless
  # a scenario says otherwise. See ../../../features/auth.md for
  # authentication/authorization itself, ../../../features/did-ucan-attestation.md
  # for the UCAN/Token Exchange mechanics, and ../../../features/event-chains.md
  # for the general parentEventIds/lineage traversal mechanics -- none of the
  # three are re-derived here.

  Background:
    Given the event type "SpecimenCollected" version 1 is registered with EntityIdField "$.SpecimenId" and schema:
      """
      {
        "type": "object",
        "properties": {
          "SpecimenId": { "type": "string" },
          "SpecimenType": { "type": "string" },
          "DonorReference": { "type": "string" },
          "CollectionDate": { "type": "string", "format": "date-time" }
        },
        "required": ["SpecimenId", "SpecimenType", "DonorReference", "CollectionDate"]
      }
      """
    And "DonorReference" on "SpecimenCollected" is x-masking classified with requiredClaim "clearance:specimen-data" (ADR-009)
    And the event type "SpecimenDerived" version 1 is registered with EntityIdField "$.SpecimenId", ParentValidationMode "Strict", and schema:
      """
      {
        "type": "object",
        "properties": {
          "SpecimenId": { "type": "string" },
          "DerivedFrom": { "type": "string" },
          "SpecimenType": { "type": "string" },
          "CollectionDate": { "type": "string", "format": "date-time" }
        },
        "required": ["SpecimenId", "DerivedFrom", "SpecimenType", "CollectionDate"]
      }
      """
    And the event type "accessGrant" version 1 is registered per ADR-043

  Scenario: Collecting a specimen creates an origin EntityStoreRow with no parents
    When "staff-12" POSTs to "/publish/SpecimenCollected" with body:
      """
      { "payload": { "SpecimenId": "spec-001", "SpecimenType": "Blood", "DonorReference": "donor-77", "CollectionDate": "2026-07-01T00:00:00Z" } }
      """
    Then the response status should be 202 with status "received"
    And eventually an EntityStoreRow for "biobank:Specimen:spec-001" should exist with SpecimenType "Blood"
    And the stored event should have no parent events

  Scenario: Deriving a sample records parentEventIds, and the lineage query returns both specimens
    Given a "SpecimenCollected" event "evt-collect-1" was published and folded for "spec-001"
    When "lab-tech-3" POSTs to "/publish/SpecimenDerived" with body:
      """
      { "payload": { "SpecimenId": "spec-001-dna", "DerivedFrom": "spec-001", "SpecimenType": "DNA Extract", "CollectionDate": "2026-07-03T00:00:00Z" }, "parentEventIds": ["evt-collect-1"] }
      """
    Then the response status should be 202 with status "received"
    And the stored event's parents should be exactly ["evt-collect-1"]
    And eventually an EntityStoreRow for "biobank:Specimen:spec-001-dna" should exist with DerivedFrom "spec-001"
    When I query specimenLineage(entityId: "biobank:Specimen:spec-001-dna")
    Then the ancestors list should include "biobank:Specimen:spec-001"
    # ADR-005's Strict mode requires the parent to already exist with a lower
    # SequenceNumber -- evt-collect-1 is published and folded before this scenario's
    # SpecimenDerived publish, satisfying that ordering.

  Scenario: An IRB-authorized grant is issued and successfully used within scope and before expiration
    Given a "SpecimenCollected" event was published and folded for "spec-001"
    And a "SpecimenDerived" event was published and folded for "spec-001-dna", parented off "spec-001"
    And "staff-12" holds the claim "clearance:specimen-data"
    When "staff-12" POSTs to "/publish/accessGrant" with body:
      """
      { "payload": { "GranteeDid": "did:key:z6Mk...researcher", "DelegatedClaim": "clearance:specimen-data", "EntityScope": "biobank:Specimen:spec-001-dna", "ExpiresAt": "2026-08-06T00:00:00Z" } }
      """
    Then the response status should be 202
    When "did:key:z6Mk...researcher" exchanges the delegated UCAN for a bearer JWT via "/oauth/token"
    Then the exchange should succeed with a JWT carrying claim "clearance:specimen-data" and entityScope "biobank:Specimen:spec-001-dna"
    When the researcher queries specimen(entityId: "biobank:Specimen:spec-001-dna") using that JWT, before 2026-08-06T00:00:00Z
    Then the response status should be 200 with SpecimenType "DNA Extract"
    And an AccessLogEntry should be recorded with ReaderTrustBasis "Attested" and GrantRef pointing at the accessGrant event
    # This is the exact "collaborating lab's temporary, scoped access to a specific
    # specimen" fit ../README.md's Applicable ADRs section and ADR-043's own
    # Compliance note both name directly.

  Scenario: Access is denied for a specimen outside the granted entityScope
    Given "did:key:z6Mk...researcher" holds a bearer JWT with claim "clearance:specimen-data" and entityScope "biobank:Specimen:spec-001-dna", per above
    And a separate, unrelated specimen "biobank:Specimen:spec-002" exists
    When the researcher queries specimen(entityId: "biobank:Specimen:spec-002") using that JWT
    Then the response status should be 403
    And an AccessLogEntry should still be recorded for the attempted read
    # The claim is present but entityScope doesn't match -- ADR-043's extension to
    # ADR-008's claim model checks both, not a bare HasClaim boolean.

  Scenario: Access is denied once the grant has expired
    Given "did:key:z6Mk...researcher" was issued a grant for "biobank:Specimen:spec-001-dna" with ExpiresAt "2026-08-06T00:00:00Z", per above
    And the current time is now 2026-08-07T00:00:00Z
    When the researcher attempts to exchange the same UCAN for a fresh bearer JWT via "/oauth/token"
    Then the exchange should fail with "invalid_grant"
    # Revocation/expiration is checked at exchange time by the IdP, not solely by a
    # previously issued JWT's own exp claim -- ADR-043's Consequences, same
    # operational requirement as ADR-040's ticket consumption.

  Scenario: Access is denied once the grant has been explicitly revoked, even before its natural expiration
    Given "did:key:z6Mk...researcher" was issued a grant for "biobank:Specimen:spec-001-dna" with ExpiresAt "2026-08-06T00:00:00Z", per above
    And "staff-12" published an "accessGrantRevoked" event targeting that grant on "2026-07-15T00:00:00Z"
    When the researcher attempts to exchange the UCAN for a fresh bearer JWT via "/oauth/token" on "2026-07-16T00:00:00Z"
    Then the exchange should fail with "invalid_grant"
    # Revocation is an ordinary registered event (accessGrantRevoked), folded and
    # auditable like any other -- ADR-043 -- and checked at exchange time.
```
