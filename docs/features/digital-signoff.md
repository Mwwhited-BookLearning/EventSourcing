# Feature: Digital Sign-off (RFC 9470 step-up authentication + envelope `Signature`)

Context: decision record `ADR-066` in `../07-adrs.md` (`EventTypeDefinition.
RequiredSignature`, the RFC 9470 step-up challenge, the envelope `Signature`
object, the `ADR-057` erasure exemption) — folded in below as its own
section, `ADR-086` (RFC 3161 trusted timestamping of a `Signature`-bearing
event's `ChainHash`, via a pluggable `ITimestampAuthorityClient`). Data
model in [`../data/schema-registry.md`](../data/schema-registry.md)
(`EventTypeDefinition.RequiredSignature`) and
[`../data/event-log.md`](../data/event-log.md) (`StoredEvent.Signature`,
the `Signature` class including `RFC3161Timestamp`) — this doc shows only
the columns its own scenarios touch; full column lists live there.

This mechanism's *shared claim-checking primitive* — `HasClaim`,
`RequiredClaims`'s `{Direction, Claim}` list, `ADR-008`/`ADR-050` — is
covered in full in [`event-security.md`](event-security.md); ordinary
Bearer-token acquisition and scope enforcement in [`auth.md`](auth.md).
Neither file currently frames `RequiredSignature`/step-up itself beyond a
one-line pointer to the full column list (verified by re-reading both
before writing this doc) — this doc is the first place that mechanism gets
its own full treatment at the core-engine level; a fully worked domain
example already exists in
[`../domains/clinical-trials-device-telemetry/features/adverse-event-capture-and-review.md`](../domains/clinical-trials-device-telemetry/features/adverse-event-capture-and-review.md)
("PI's review decision, gated on step-up"), which this doc is the
mechanism-level counterpart to, not a repeat of.

This doc deliberately does **not** re-derive:
- **`RequiredClaims`/`HasClaim` and ordinary Bearer-token auth** (`ADR-006`,
  `ADR-008`/`ADR-050`) — see [`event-security.md`](event-security.md) and
  [`auth.md`](auth.md), cross-referenced above. `RequiredSignature` is a
  *different* dimension from both: an event type can require a claim
  (who may publish at all), a signature (a step-up-authenticated,
  attributable sign-off), or both, independently.
- **How an IdP actually implements step-up authentication itself**
  (password re-entry, TOTP, WebAuthn) — `ADR-066` is explicit that this is
  the IdP's job (`ADR-006`), never this framework's own code. This doc
  shows the RFC 9470 challenge/response *contract*, never a specific
  factor's UX.
- **The persist-everything publish pipeline and `ChainHash` tamper-evidence
  mechanics** (`ADR-023`, `ADR-019`) — see
  [`publish-event.md`](publish-event.md) and `../data/event-log.md`'s
  "Tamper evidence" section. `Signature` is envelope metadata on the same
  `StoredEvent` `ChainHash` already covers — this doc doesn't re-explain
  the chaining itself.
- **`ADR-057`'s crypto-shredding erasure mechanism in general** — see
  `event-security.md`'s masking section. This doc only shows the specific,
  reasoned *exemption* `SignerId`/`Signature` get from it, never the
  general per-entity-key/destroy-on-request mechanism.

## Sequence diagram — publish gated by `RequiredSignature` (RFC 9470 step-up)

![Sequence diagram — publish gated by `RequiredSignature` (RFC 9470 step-up)](../diagrams/features/digital-signoff/01-sequence-diagram-publish-gated-by-requiredsignatur.svg)

```plantuml
@startuml DigitalSignoff_StepUp_Sequence
autonumber
actor "Signing Actor" as signer
participant "Inbox Endpoint" as inbox
participant "SchemaRegistryClient" as registry
participant "EventStore.DevIdp\n(or a real IdP, ADR-006)" as idp
database "Event & Schema Store" as db

signer -> inbox: POST /publish/PolicyApproval\nBearer <JWT, acr insufficient/stale>\n{ payload: {...}, meaning: "approved" }
inbox -> registry: get EventTypeDefinition("PolicyApproval").RequiredSignature
alt RequiredSignature is null (not configured for this type)
  inbox -> inbox: proceed exactly as an ordinary publish\n(ADR-023) -- completely unaffected, purely additive
else RequiredSignature = { AcrValues, MaxAge } and caller's token\ndoesn't carry a satisfying "acr" claim / isn't recent enough
  inbox --> signer: 401\nWWW-Authenticate: step-up required\n(acr_values="urn:...:step-up", max_age=300)
  note right of inbox
    Turned away BEFORE storage -- the one new
    pre-storage rejection case ADR-066 names,
    alongside ADR-023's existing envelope-parse
    exception. The event's own DATA is never
    rejected for shape/content reasons.
  end note
  signer -> idp: re-authenticate (password re-entry / OTP / WebAuthn --\nthe IdP's own mechanism, never this framework's, ADR-066)
  idp --> signer: new token, acr = "urn:...:step-up", auth_time recent
  signer -> inbox: retry POST /publish/PolicyApproval\n(same payload, stepped-up token)
else RequiredSignature satisfied, but "meaning" is absent from the request
  inbox --> signer: 400 -- Signature.Meaning is required\n(envelope metadata, not Payload -- treated like any\nother missing required transport field under\nADR-023's envelope-shape exception, never persisted)
else RequiredSignature satisfied and meaning present
  inbox -> db: INSERT StoredEvent\n{ ..., ActorId: signer's verified sub,\n  Signature: { SignerId: ActorId, SignedAt: now,\n    Meaning: "approved", Acr: "urn:...:step-up" } }
  inbox --> signer: 202 { status: "received" }
  note right of db
    Signature is envelope metadata on the SAME
    StoredEvent ChainHash already covers (ADR-019)
    -- exactly as tamper-evident as everything else
    in the log, no separately-secured artifact.
  end note
end
@enduml
```

## Sequence diagram — RFC 3161 timestamp obtained for a `Signature`-bearing event (`ADR-086`)

![Sequence diagram — RFC 3161 timestamp obtained for a `Signature`-bearing event (`ADR-086`)](../diagrams/features/digital-signoff/02-sequence-diagram-rfc-3161-timestamp-obtained-for-a.svg)

```plantuml
@startuml DigitalSignoff_Rfc3161_Sequence
autonumber
participant "Inbox Endpoint" as inbox
participant "ITimestampAuthorityClient\n(pluggable, ADR-086)" as tsaClient
participant "Time Stamping Authority\n(any RFC-3161-compliant TSA)" as tsa
database "Event & Schema Store" as db

inbox -> db: INSERT StoredEvent (Signature set, per the diagram above)
alt this event type's RequiredSignature does NOT also opt into RFC3161Timestamp
  inbox -> inbox: Signature.RFC3161Timestamp stays null -- optional,\nnot every Signature-requiring type needs\nthird-party-verifiable timing (ADR-086)
else opted in (same EventTypeDefinition configuration surface as RequiredSignature)
  inbox -> tsaClient: request timestamp over hash(ChainHash)
  tsaClient -> tsa: RFC 3161 TimeStampReq { hashedMessage: hash(ChainHash) }
  tsa --> tsaClient: TimeStampToken (signed, over that same hash)
  tsaClient --> inbox: TimeStampToken bytes
  inbox -> db: UPDATE StoredEvent SET Signature.RFC3161Timestamp = TimeStampToken
  note right of tsa
    Timestamped over ChainHash, not Payload directly --
    ChainHash already transitively commits to the full
    event content (ADR-019), so one hash covers both.
  end note
end
@enduml
```

Verification needs no new mechanism this framework builds: an
`RFC3161Timestamp` is checked against the issuing TSA's own published
X.509 certificate chain by **any** off-the-shelf RFC 3161 verifier,
independent of this codebase — the same trust-chain verification
`ADR-006`'s auth stack already performs elsewhere, per `ADR-086`.

## Sequence diagram — an erasure request against a signed event's `SignerId` is refused (`ADR-057`/`ADR-066` exemption)

![Sequence diagram — an erasure request against a signed event's `SignerId` is refused (`ADR-057`/`ADR-066` exemption)](../diagrams/features/digital-signoff/03-sequence-diagram-an-erasure-request-against-a-sign.svg)

```plantuml
@startuml DigitalSignoff_ErasureExemption_Sequence
autonumber
actor "Data Subject / Erasure Requester" as requester
participant "Erasure Endpoint\n(ADR-057)" as erasureEndpoint
database "Event & Schema Store" as db

requester -> erasureEndpoint: erasure request for EntityId "demo:PolicyApproval:pa-1"
erasureEndpoint -> db: identify classified (x-masking) fields on\nmatching StoredEvent rows for this EntityId
loop for each classified Payload field
  erasureEndpoint -> db: destroy that field's crypto-shredding key (ADR-057)
end
erasureEndpoint -> erasureEndpoint: SignerId / Signature are envelope\nmetadata, categorically exempt --\nnever enumerated as erasable in the first place
erasureEndpoint --> requester: 200 -- erasure applied to classified\nPayload fields; SignerId/Signature retained,\nper GDPR Art. 17(3)(b)/(e)
note right of erasureEndpoint
  Not an accident of scope -- ADR-057's crypto-
  shredding structurally can't reach envelope fields
  regardless, but ADR-066 states the RIGHT reason:
  17(3)(b) legal-obligation retention (21 CFR Part 11
  record-retention duties) and 17(3)(e) legal-claims
  defence -- a signature has evidentiary value ONLY
  because it's tied to a specific, un-erasable identity.
end note
@enduml
```

## Data model (ER diagram)

![Data model (ER diagram)](../diagrams/features/digital-signoff/04-data-model-er-diagram.svg)

```plantuml
@startuml DigitalSignoff_ER
hide circle
skinparam linetype ortho

entity "EventTypeDefinition" as etd {
  * AppId : string <<PK>>
  * Name : string <<PK>>
  * Version : int <<PK>>
  --
  RequiredSignature : RequiredSignature?
  ' null = no sign-off required (ADR-066)
}

entity "RequiredSignature" as reqSig {
  AcrValues : list<string>
  ' RFC 9470 acr_values
  MaxAge : int?
  ' RFC 9470 max_age, seconds
}

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EventId : uuid <<unique>>
  ActorId : string
  ' verified caller identity, ADR-064 -- always populated
  ChainHash : string
  Signature : Signature?
}

entity "Signature" as sig {
  SignerId : string
  ' denormalized copy of ActorId, kept explicit (ADR-066)
  SignedAt : datetimeoffset
  Meaning : string
  ' required -- rejected if absent (ADR-066)
  Acr : string
  ' the acr claim the signing token actually carried
  RFC3161Timestamp : byte[]?
  ' optional -- a TSA TimeStampToken over ChainHash (ADR-086)
}

etd ||--o| reqSig : "RequiredSignature, when configured"
event ||--o| sig : "Signature, when RequiredSignature\nwas satisfied at publish time"

note right of sig
  RFC3161Timestamp is timestamped over hash(ChainHash),
  not Payload directly -- ChainHash already transitively
  commits to the full event content (ADR-019). SignerId/
  Signature are categorically exempt from ADR-057's
  crypto-shredding erasure (GDPR Art. 17(3)(b)/(e)).
end note
@enduml
```

Full column lists are in `../data/schema-registry.md` and
`../data/event-log.md` — this diagram shows only what a sign-off publish
and its optional RFC 3161 timestamping actually read/write.

## Salt (UI mockup) — sign-off action across a pending queue, step-up, and the signed record

### Screen 1: Records pending sign-off

![Screen 1: Records pending sign-off diagram](../diagrams/features/digital-signoff/05-screen-1-records-pending-sign-off.svg)

```plantuml
@startsalt
{
  { "Pending Sign-off -- App 'demo'" }
  ..
  | Entity                  | Type            | Required Acr           | Status         |
  | demo:PolicyApproval:pa-1 | PolicyApproval  | urn:demo:step-up        | awaiting sign-off |
  | demo:PolicyApproval:pa-2 | PolicyApproval  | urn:demo:step-up        | awaiting sign-off |
}
@endsalt
```

Clicking a row opens Screen 2 for that one record.

### Screen 2: Sign-off action

![Screen 2: Sign-off action diagram](../diagrams/features/digital-signoff/06-screen-2-sign-off-action.svg)

```plantuml
@startsalt
{
  { "pa-1 -- Sign-off" }
  ..
  { "Meaning" | ^approved^ }
  "Meaning is required -- rejected if absent (ADR-066)"
  ..
  { [ Sign off: Approve ] | [ Sign off: Reject ] }
  "Requires step-up authentication (RFC 9470) if your\ncurrent session doesn't already satisfy urn:demo:step-up"
}
@endsalt
```

Clicking **Sign off: Approve** (or **Reject**) submits the publish; if the
caller's current token doesn't satisfy `RequiredSignature`, the client is
redirected through the IdP to step up before the request actually
completes (the sequence diagram above) — only then does the flow move to
Screen 3.

### Screen 3: Signed record

![Screen 3: Signed record diagram](../diagrams/features/digital-signoff/07-screen-3-signed-record.svg)

```plantuml
@startsalt
{
  { "pa-1 -- Signed Record" }
  ..
  { "Signed by" | "actor-7" }
  { "Signed at" | "2026-08-03 10:12 UTC" }
  { "Meaning" | "approved" }
  { "Acr" | "urn:demo:step-up" }
  { "RFC 3161 timestamp" | "present -- independently verifiable (ADR-086)" }
}
@endsalt
```

## Gherkin

```gherkin
Feature: Digital sign-off (RFC 9470 step-up authentication + envelope Signature)
  As a regulated deployment
  I want a designated event type to require a signed, step-up-authenticated sign-off before it counts as final
  And that signature to be independently timestamped and permanently un-erasable
  So that a signed record satisfies non-repudiation requirements like 21 CFR Part 11 §11.50

  # Every publish below carries an ordinary Bearer token with the
  # events:publish scope (auth.md) unless a scenario says otherwise --
  # RequiredSignature is an independent dimension from that scope check
  # and from RequiredClaims (event-security.md).

  Background:
    Given the event type "PolicyApproval" version 1 is registered with schema:
      """
      { "type": "object", "properties": { "PolicyId": { "type": "string" } }, "required": ["PolicyId"] }
      """
      with EntityIdField "$.PolicyId" and RequiredSignature { "AcrValues": ["urn:demo:step-up"], "MaxAge": 300 }
    And the event type "OrderPlaced" version 1 is registered with no RequiredSignature configured

  Scenario: An event type with no RequiredSignature is completely unaffected
    Given I have a Bearer token with acr "urn:demo:ordinary"
    When I POST to "/publish/OrderPlaced" with body:
      """
      { "payload": { "Amount": 150.00 } }
      """
    Then the response status should be 202
    And the stored event's Signature should be null
    # Purely additive -- ADR-066's own stated guarantee.

  Scenario: Publishing a signature-required event type without sufficient step-up is challenged, not stored
    Given I have a Bearer token with no "urn:demo:step-up" acr claim
    When I POST to "/publish/PolicyApproval" with body:
      """
      { "payload": { "PolicyId": "pa-1" }, "meaning": "approved" }
      """
    Then the response should be an RFC 9470 step-up challenge naming acr_values "urn:demo:step-up" and max_age 300
    And no "PolicyApproval" event should be persisted for "pa-1"
    # The one new pre-storage rejection case since ADR-023's persist-
    # everything posture, alongside the existing envelope-parse exception.

  Scenario: A missing Meaning is rejected as an incomplete envelope, never persisted with an advisory flag
    Given I have a Bearer token with acr "urn:demo:step-up", authenticated within the last 300 seconds
    When I POST to "/publish/PolicyApproval" with body:
      """
      { "payload": { "PolicyId": "pa-2" } }
      """
    Then the response status should be 400
    And no "PolicyApproval" event should be persisted for "pa-2"
    # Signature.Meaning is envelope metadata, not Payload -- its absence
    # is treated like any other missing required transport field under
    # ADR-023's envelope-shape exception, not a SchemaStatus: invalid
    # advisory flag on a stored event.

  Scenario: Signing off with a satisfying step-up token and a Meaning stores the Signature
    Given I have a Bearer token with acr "urn:demo:step-up", authenticated within the last 300 seconds, sub "actor-7"
    When I POST to "/publish/PolicyApproval" with body:
      """
      { "payload": { "PolicyId": "pa-1" }, "meaning": "approved" }
      """
    Then the response status should be 202
    And the stored event should carry Signature { SignerId: "actor-7", Meaning: "approved", Acr: "urn:demo:step-up" }
    And the stored event's SignedAt should be populated

  Scenario: A Signature-bearing event's tamper evidence uses the same hash chain as any other event, no new primitive
    Given a "PolicyApproval" event "pa-1" was signed and stored, per above
    When any single byte of that stored event's Signature.Meaning is altered directly in storage
    And the chain is replayed from SequenceNumber 1
    Then chain verification should detect a broken ChainHash starting at "pa-1"'s row
    # ChainHash already covers Signature as ordinary envelope metadata
    # on the same StoredEvent (ADR-019) -- no separately-secured artifact.

  Scenario: SignerId and Signature survive an erasure request that a Payload field does not
    Given a "PolicyApproval" event "pa-1" was signed by "actor-7", per above
    And "PolicyApproval" has a classified Payload field masked behind requiredClaim "clearance:pii"
    When an erasure request is submitted for entity "demo:PolicyApproval:pa-1"
    Then that classified Payload field's value should become unreadable ({"erased": true})
    And the stored event's SignerId should still equal "actor-7"
    And the stored event's Signature.Meaning should still equal "approved"
    # GDPR Art. 17(3)(b)/(e) exemption -- a deliberate, reasoned carve-out
    # (ADR-066), not an accident of where ADR-057's encryption reaches.

  Scenario: RFC3161Timestamp is optional per event type, independent of RequiredSignature itself
    Given "PolicyApproval" has RequiredSignature configured but does NOT opt into RFC3161Timestamp
    When a "PolicyApproval" event is signed and stored, per above
    Then the stored event's Signature.RFC3161Timestamp should be null
    # Not every Signature-requiring type needs third-party-verifiable
    # timing -- a deployment enables it per event type (ADR-086).

  Scenario: A RequiredSignature type that also opts into RFC3161Timestamp obtains one over the event's ChainHash
    Given "PolicyApproval" is configured to also opt into RFC3161Timestamp
    When a "PolicyApproval" event "pa-3" is signed and stored
    Then ITimestampAuthorityClient should have been called with a hash of "pa-3"'s ChainHash, not its Payload
    And the stored event's Signature.RFC3161Timestamp should be populated with the returned TimeStampToken

  Scenario: The RFC3161Timestamp is verifiable independently of this framework's own code
    Given a "PolicyApproval" event "pa-3" carries a populated Signature.RFC3161Timestamp, per above
    When that TimeStampToken is checked with any standard, off-the-shelf RFC 3161 verifier against the issuing TSA's published certificate chain
    Then verification should succeed without this framework's own code being involved at all
    # No new cryptographic primitive introduced -- the same X.509 trust-
    # chain verification ADR-006's auth stack already performs elsewhere.
```
