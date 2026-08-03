# Feature: Controlled Technical Data Access Request

Context: exercises `ADR-061` (data residency/region-pinning — this
domain's *defining* mechanism per [`../README.md`](../README.md)'s
Applicable ADRs section), `ADR-046` (RBAC — role-to-permission
expansion at token issuance), `ADR-043` (delegated, capped, entity-
scoped UCAN grants — the Technical Assistance Agreement exception),
`ADR-045` (read access audit log), `ADR-066` (digital sign-off / RFC
9470 step-up, for the export-control release approval that issues a
delegated grant), `ADR-032` (binary attachments — the technical data
itself is largely attachment-shaped content, per this domain's
Overview), and `ADR-005` (event lineage — a derived/redacted
technical-data artifact traces causally to its controlled source).
Entity/event shapes referenced below are defined in
[`../../../data/event-log.md`](../../../data/event-log.md)
(`StoredEvent`, `Signature`),
[`../../../data/entity-store.md`](../../../data/entity-store.md)
(`EntityStoreRow`),
[`../../../data/schema-registry.md`](../../../data/schema-registry.md)
(`AppDataResidencyPolicy`, `Role`), and
[`../../../data/access-log.md`](../../../data/access-log.md)
(`AccessLogEntry`) — this doc shows only the columns its own scenarios
touch, not full column lists.

This doc deliberately does **not** re-derive:
- `ADR-061`'s region-pinning mechanics in general (peer `Region`
  tagging, `ADR-051`'s `SeedPeers`, the outbox filtering rule itself as
  a general mechanism) — see `ADR-061` directly. This doc only shows
  that already-decided mechanism applying to one ITAR-scoped `AppId`.
- UCAN/DID token-exchange mechanics in general (`ADR-036`) — see that
  ADR and `did-ucan-attestation.md`. This doc only shows the
  TAA-authorized foreign-partner exchange as one more use of an
  already-adopted mechanism, exactly the framing `ADR-043` itself uses.
- **Non-authoritative capture (`ADR-035`) — explicitly out of scope,
  not merely unused.** This domain's own `README.md` lists `ADR-035`
  as a **weak/no fit**: controlled technical data entering this system
  is typically already vetted through an export-control review process
  before ingestion, not organically pending the way a self-attested
  identity claim is. Every event in this doc's scenarios defaults to
  `AuthorityStatus: accepted` (`ADR-042`); no `pending_review`/Live-View
  mechanics appear anywhere below.
- `ADR-045`'s `AccessLogEntry` mechanics in general (its own hash
  chain, retention, which surfaces write to it) — see
  [`access-log.md`](../../../data/access-log.md). This doc only shows
  that every read here, ordinary or delegated, writes one.
- `ADR-009`/`ADR-050`'s masking-strategy machinery in general —
  `RequiredReadClaim` is used below exactly as those ADRs already
  define it, not redesigned.

## Sequence diagram — publishing a controlled technical-data asset and its region-pinned replication

Publish itself is unchanged in shape from
[`../../../features/entity-concept.md`](../../../features/entity-concept.md)'s
`202` response (`ADR-023`); what's new here is what happens once
`ADR-033`'s peer-sync outbox picks the event up for outbound gossip
replication. `AppId "defco"` (a fictional cleared defense contractor
tenant) is ITAR-scoped via an `AppDataResidencyPolicy` row restricting
it to `["us-east", "us-west"]`; `AppId "acme"` has no such row and
stays unconstrained, shown as the contrasting `else` branch.

```plantuml
@startuml Controlled_Technical_Data_Publish_And_Region_Pinned_Replication
autonumber
actor "Cleared Engineer\n(defco)" as engineer
participant "PublishEndpoint\n(Inbox)" as inbox
participant "Router" as router
participant "EventStore.Fold" as fold
database "Event Log" as eventLog
database "Entity Store" as entityStore
participant "PeerSync Outbox\n(ADR-033)" as outbox
participant "us-east-1 Peer\n(Region: us-east)" as peerUsEast
participant "us-west-1 Peer\n(Region: us-west)" as peerUsWest
participant "eu-west-1 Peer\n(Region: eu-west)" as peerEuWest

engineer -> inbox: POST /publish/TechnicalDataAssetPublished\n{ payload: { AssetId: "td-4471", UsmlCategory: "USML Category XII",\n  Classification: "ITAR-Controlled", AttachmentRef: "sha256:9f2c..." } }\nAuthorization: Bearer <JWT, ActorId "engineer-17">
inbox -> eventLog: INSERT StoredEvent\n(EntityId: null yet, ActorId: "engineer-17" (ADR-064), AppId: "defco")
inbox --> engineer: 202 { correlationId, status: "received" }
...picked up by the Router, asynchronously (ADR-023)...
router -> fold: resolve EntityId via "$.AssetId", fold
fold -> entityStore: UPSERT TechnicalDataAssetEntityStoreRow\n(EntityId: "defco:TechnicalDataAsset:td-4471",\n AssetId: "td-4471", UsmlCategory: "USML Category XII",\n Classification: "ITAR-Controlled")
fold -> eventLog: UPDATE StoredEvent SET EntityId, Status = "applied"
...picked up by the PeerSync Outbox, asynchronously (ADR-033)...
outbox -> outbox: SELECT AppDataResidencyPolicy WHERE AppId = "defco"\n-> AllowedRegions: ["us-east", "us-west"] (ADR-061)
alt AppId is region-constrained ("defco", ITAR-scoped)
  outbox -> peerUsEast: sync batch { events: [...] }\n(Region "us-east" is in AllowedRegions)
  outbox -> peerUsWest: sync batch { events: [...] }\n(Region "us-west" is in AllowedRegions)
  note right of outbox
    eu-west-1 (Region "eu-west") is filtered out of the
    candidate destination list before a sync batch is even
    built -- it never receives, and never holds, a copy of
    this event (ADR-061). Not a read-time/query-time check:
    a foreign site structurally has nothing to serve.
  end note
else AppId is unconstrained ("acme", no AppDataResidencyPolicy row)
  outbox -> peerUsEast: sync batch { events: [...] }
  outbox -> peerUsWest: sync batch { events: [...] }
  outbox -> peerEuWest: sync batch { events: [...] }
end
@enduml
```

## Sequence diagram — access request (RBAC read, TAA-delegated read, and a denied out-of-scope read)

Three genuinely different branches of the same GraphQL query
(`ADR-037`), reading the same `TechnicalDataAssetEntityStoreRow`: an
ordinary claim check for a cleared US engineer (`ADR-046`), a
delegated-grant claim-plus-entity-scope check for a TAA-authorized
foreign partner (`ADR-043`), and the same delegated grant failing when
the requested asset falls outside its `entityScope`.

```plantuml
@startuml Controlled_Technical_Data_Access_Request
autonumber
actor "Cleared US Engineer\n(engineer-17)" as usEngineer
actor "TAA-Authorized Foreign Partner\n(foreign-partner-42)" as foreignPartner
participant "IdP / Token Exchange\n(ADR-036)" as idp
participant "GraphQL Gateway" as gateway
participant "Entity Resolver" as resolver
database "Entity Store" as entityStore
database "Access Log" as accessLog

alt Cleared US engineer, ordinary RBAC-gated read (ADR-046)
  usEngineer -> idp: (token already issued at login -- role "ClearedEngineer"\n  expanded into claim "itar:read" at issuance time, not per-request)
  usEngineer -> gateway: QUERY { technicalDataAsset(entityId: "defco:TechnicalDataAsset:td-4471") {\n  usmlCategory classification } }\nAuthorization: Bearer <JWT, claims: ["itar:read"]>
  gateway -> resolver: resolve(entityId, callerClaims)
  resolver -> resolver: HasClaim("itar:read")? -- yes, RequiredReadClaim satisfied (ADR-008/ADR-050)
  resolver -> entityStore: SELECT TechnicalDataAssetEntityStoreRow\nWHERE EntityId = "defco:TechnicalDataAsset:td-4471"
  entityStore --> resolver: { usmlCategory: "USML Category XII", classification: "ITAR-Controlled" }
  resolver -> accessLog: INSERT AccessLogEntry\n(ReaderActorId: "engineer-17", ReaderTrustBasis: "Authoritative",\n ViewAccessed: "Authoritative", ResourceRef: entityId) (ADR-045)
  resolver --> gateway: 200 { usmlCategory, classification }
  gateway --> usEngineer: response
else TAA-scoped foreign partner, delegated UCAN-exchanged read, within granted entityScope (ADR-043)
  foreignPartner -> idp: POST /oauth/token\n{ grant_type: "urn:ietf:params:oauth:grant-type:token-exchange",\n  subject_token: "<UCAN: claim itar:read,\n  entityScope defco:TechnicalDataAsset:td-4471, exp 2026-12-31>" }\n(ADR-036)
  idp --> foreignPartner: 200 { access_token: <JWT, claims: [itar:read],\n  entityScope: defco:TechnicalDataAsset:td-4471> }
  foreignPartner -> gateway: QUERY { technicalDataAsset(entityId: "defco:TechnicalDataAsset:td-4471") {\n  usmlCategory classification } }\nAuthorization: Bearer <JWT>
  gateway -> resolver: resolve(entityId, callerClaims, entityScope)
  resolver -> resolver: HasClaim("itar:read") AND entityScope == entityId? -- yes (ADR-043)
  resolver -> entityStore: SELECT TechnicalDataAssetEntityStoreRow\nWHERE EntityId = "defco:TechnicalDataAsset:td-4471"
  entityStore --> resolver: { usmlCategory: "USML Category XII", classification: "ITAR-Controlled" }
  resolver -> accessLog: INSERT AccessLogEntry\n(ReaderActorId: "foreign-partner-42", ReaderTrustBasis: "Attested",\n GrantRef: <grant id>, ViewAccessed: "Authoritative") (ADR-045)
  resolver --> gateway: 200 { usmlCategory, classification }
  gateway --> foreignPartner: response
else same TAA-scoped foreign partner, asset OUTSIDE the grant's entityScope
  foreignPartner -> gateway: QUERY { technicalDataAsset(entityId: "defco:TechnicalDataAsset:td-9982") {\n  usmlCategory classification } }\nAuthorization: Bearer <JWT, entityScope: "defco:TechnicalDataAsset:td-4471">
  gateway -> resolver: resolve(entityId, callerClaims, entityScope)
  resolver -> resolver: HasClaim("itar:read") AND entityScope == entityId?\n-- entityScope "td-4471" != requested "td-9982" -- no
  resolver --> gateway: 403 { error: "entity outside granted scope" }
  gateway --> foreignPartner: 403 { error: "entity outside granted scope" }
  note right of resolver
    Whether a denied attempt itself writes an AccessLogEntry, and how
    (AccessLogEntry, access-log.md, has no outcome/denied field today),
    is left unspecified here -- not re-deriving ADR-045's own mechanics,
    per this doc's out-of-scope list above.
  end note
end
@enduml
```

## Data model (ER diagram)

```plantuml
@startuml ControlledTechnicalData_ER
hide circle
skinparam linetype ortho

entity "ControlledTechnicalDataEvent\n(StoredEvent subset, event-log.md)" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EntityId : string <<FK>>
  EventType : string
  ActorId : string
  Payload : text
  Signature : Signature?
}

entity "TechnicalDataAssetEntityStoreRow\n(EntityStoreRow subset, entity-store.md)" as assetRow {
  * EntityId : string <<PK>>
  --
  AssetId : string
  UsmlCategory : string
  Classification : string
  AttachmentRef : string?
  RequiredReadClaim : string?
}

entity "AppDataResidencyPolicy\n(schema-registry.md, ADR-061 -- already exists,\nnot new)" as residency {
  * AppId : string <<PK>>
  --
  AllowedRegions : string[]
}

entity "AccessGrant\n(EntityStoreRow.Data shape,\nEntityType AccessGrant, ADR-043)" as grant {
  * EntityId : string <<PK>>
  --
  GranterActorId : string
  GranteeDid : string
  DelegatedClaim : string
  EntityScope : string
  ExpiresAt : datetime
  ApprovalSignature : Signature?
}

event "*" --> "1" assetRow : "folds into\n(ADR-021)"
residency "1" ..> "*" event : "constrains outbound\nreplication of\n(ADR-061, enforced at\nADR-033's peer-sync outbox --\nnot shown on this diagram)"
grant "*" --> "1" assetRow : "EntityScope references\n(ADR-043)"

note right of assetRow
  RequiredReadClaim here is a denormalized copy of
  EventTypeDefinition.RequiredClaims (ADR-050) for
  query-time convenience -- the schema registry
  remains the source of truth, the same
  denormalization shape EntityStoreRow already uses
  for EntityType (entity-store.md).
end note

note right of grant
  ApprovalSignature denormalizes the accessGrant
  event's own StoredEvent.Signature (ADR-066,
  Meaning "export_control_release_approval") --
  not a second sign-off record.
end note
@enduml
```

Full column lists live in
[`../../../data/event-log.md`](../../../data/event-log.md),
[`../../../data/entity-store.md`](../../../data/entity-store.md), and
[`../../../data/schema-registry.md`](../../../data/schema-registry.md)
— this diagram shows only what this doc's scenarios touch.

## State machine — access request lifecycle

This models the *request* underlying an `AccessGrant` (`ADR-043`) —
the workflow a TAA-authorized foreign partner (or, equally, an internal
requester asking for a new RBAC role) goes through before an
`AccessGrant`/role assignment exists — not the technical-data asset's
own entity lifecycle, which is the ordinary Entity Store fold already
covered by
[`../../../features/entity-concept.md`](../../../features/entity-concept.md).

```plantuml
@startuml AccessRequest_Lifecycle
state Requested
state UnderReview
state Denied
state Approved
state "Active -- RBAC role granted\n(no entityScope, no expiry, ADR-046)" as ActiveRbac
state "Active -- TAA-delegated grant\n(entityScope + ExpiresAt, ADR-043)" as ActiveTaa
state Expired
state Revoked

[*] --> Requested
Requested --> UnderReview
UnderReview --> Denied : reviewer declines
UnderReview --> Approved : reviewer approves\n(digital sign-off, ADR-066,\nMeaning "export_control_release_approval",\nvia RFC 9470 step-up challenge)
Approved --> ActiveRbac : grant type = RBAC role (ADR-046)\n-- PermissionGranted/RoleGranted event (ADR-067)
Approved --> ActiveTaa : grant type = TAA-scoped delegated (ADR-043)\n-- accessGrant event, UCAN issued (ADR-036)
ActiveTaa --> Expired : ExpiresAt reached --\nan exchange attempted after this fails (ADR-043)
ActiveTaa --> Revoked : accessGrantRevoked event published\nbefore natural expiration (ADR-043)
ActiveRbac --> Revoked : role/permission removed --\nadditive-only model, no deny entry (ADR-046)
Denied --> [*]
Expired --> [*]
Revoked --> [*]
@enduml
```

## Salt (UI mockup) — request-to-read user flow, across the approver's queue, decision, and delegated-read screens

### Screen 1: Access Request Queue — AppId defco

```plantuml
@startsalt
{
  { "Access Request Queue -- AppId: defco" }
  ..
  | Request ID | Asset ID | Requester             | Grant Type    | Status      |
  | ar-2201    | td-4471  | "foreign-partner-42"  | TAA-delegated | UnderReview |
  | ar-2198    | td-4471  | "engineer-22"          | RBAC role     | Approved    |
  | ar-2190    | td-9982  | "foreign-partner-42"  | TAA-delegated | Denied      |
}
@endsalt
```

Each row is one position in the *Access Request Lifecycle* state machine
above (`Requested`/`UnderReview`/`Approved`/`Denied`) — this queue is what
a cleared engineer holding `itar:read` (`ADR-046`) works from, not a
read of `TechnicalDataAssetEntityStoreRow` itself. Clicking `ar-2201`
opens Screen 2, the pending TAA-delegated request's own approval detail.

### Screen 2: Approver's review and sign-off screen

```plantuml
@startsalt
{
  { "Access Request  ar-2201  --  defco:TechnicalDataAsset:td-4471" }
  ..
  | Field            | Value                                    |
  | Asset ID         | "td-4471"                                 |
  | USML Category    | "USML Category XII"                       |
  | Classification   | "ITAR-Controlled"                          |
  | Requester        | "M. Dubois  (foreign-partner-42)"          |
  | Asserted region  | "France"   ( not a US Person )             |
  | Grant type       | () RBAC role   (X) TAA-delegated           |
  | TAA reference    | "TAA-2026-0417"                            |
  | Delegated claim  | "itar:read"                                |
  | Entity scope     | "defco:TechnicalDataAsset:td-4471"         |
  | Expires          | "2026-12-31"                               |
  ..
  { "Digital sign-off required (ADR-066, RFC 9470 step-up)" | [Re-authenticate & Approve] }
  ..
  [Approve] | [Deny]
}
@endsalt
```

Every field above is a read of `TechnicalDataAssetEntityStoreRow` plus
the pending request/grant fields (`AssetId`/`UsmlCategory`/
`Classification` from the asset row, `GranteeDid`/`DelegatedClaim`/
`EntityScope`/`ExpiresAt` from the request-in-progress) — no new storage
for the screen itself. `Approve` triggers the `ADR-066` step-up challenge
inline, per the state machine above, then publishes the `accessGrant`
event that moves this request to `ActiveTaa`; UCAN issuance and exchange
happen next, out of frame, before Screen 3. `Deny` transitions straight
to `Denied` with no sign-off required, since a denial grants no new
access, and never reaches Screen 3.

### Screen 3: Foreign partner's delegated read, within granted scope

```plantuml
@startsalt
{
  { "defco:TechnicalDataAsset:td-4471 -- read via TAA-delegated grant" }
  ..
  { "Caller" | "foreign-partner-42 (DID token-exchanged, ADR-036)" }
  { "Entity scope" | "defco:TechnicalDataAsset:td-4471" }
  ..
  { "USML Category" | "USML Category XII" }
  { "Classification" | "ITAR-Controlled" }
  ..
  "200 OK -- AccessLogEntry written: ReaderTrustBasis 'Attested', GrantRef ar-2201 (ADR-045)"
}
@endsalt
```

This is the second sequence diagram's TAA-scoped branch, dramatized: once
`foreign-partner-42` exchanges the UCAN issued by Screen 2's approval for
a bearer JWT, the same `GraphQL Gateway`/`Entity Resolver` path a cleared
US engineer uses returns this asset's data because `HasClaim("itar:read")
AND entityScope == entityId` holds (`ADR-043`). The same query against
`td-9982` — outside `ar-2201`'s `entityScope` — never reaches this screen
at all; it returns `403` instead, exactly as this doc's second sequence
diagram's third branch shows.

## Gherkin

```gherkin
Feature: Controlled Technical Data Access Request
  As the platform hosting export-controlled defense technical data
  I want technical-data-asset events to stay region-pinned to US-tagged peers, and every read gated by RBAC or a TAA-scoped delegated grant
  So that ITAR/EAR's US-persons/US-soil restriction (22 CFR 120-130 / 15 CFR 730-774) is enforced structurally, not just by policy

  Background:
    Given the event type "TechnicalDataAssetPublished" version 1 is registered with ChangeKind "Full", EntityIdField "$.AssetId", and RequiredClaims [{ "Direction": "Read", "Claim": "itar:read" }] (denormalized as RequiredReadClaim on the folded entity, see the ER diagram note below):
      """
      {
        "type": "object",
        "properties": {
          "AssetId": { "type": "string" },
          "UsmlCategory": { "type": "string" },
          "Classification": { "type": "string" },
          "AttachmentRef": { "type": "string" }
        },
        "required": ["AssetId", "UsmlCategory", "Classification"]
      }
      """
    And AppId "defco" has an AppDataResidencyPolicy with AllowedRegions ["us-east", "us-west"] (ADR-061)
    And AppId "acme" has no AppDataResidencyPolicy row (unconstrained, today's default behavior)
    And peer "us-east-1" is tagged Region "us-east"
    And peer "us-west-1" is tagged Region "us-west"
    And peer "eu-west-1" is tagged Region "eu-west"
    And "engineer-17" holds role "ClearedEngineer", which bundles claim "itar:read" (ADR-046)
    And "engineer-22" holds no ITAR-related claim
    And "foreign-partner-42" holds DID "did:key:z6Mk...partner42" (ADR-036)

  Scenario: Publishing an ITAR-scoped asset queues sync only to US-tagged peers
    When "engineer-17" POSTs to "/publish/TechnicalDataAssetPublished" for AppId "defco" with body:
      """
      { "payload": { "AssetId": "td-4471", "UsmlCategory": "USML Category XII", "Classification": "ITAR-Controlled" } }
      """
    Then the response status should be 202
    And eventually the PeerSync outbox should queue the resulting event to "us-east-1" and "us-west-1"
    And the PeerSync outbox should never queue it to "eu-west-1"
    # Filtered before a sync batch is even built (ADR-061) -- eu-west-1 never
    # holds a copy to serve, not merely blocked from serving one on request.

  Scenario: Publishing an equivalent event under an unconstrained AppId replicates to every peer
    When "engineer-17" POSTs to "/publish/TechnicalDataAssetPublished" for AppId "acme" with body:
      """
      { "payload": { "AssetId": "td-9001", "UsmlCategory": "USML Category XII", "Classification": "Unclassified" } }
      """
    Then the response status should be 202
    And eventually the PeerSync outbox should queue the resulting event to "us-east-1", "us-west-1", and "eu-west-1"
    # No AppDataResidencyPolicy row for "acme" -- unconstrained is still the
    # default (ADR-061), contrasted directly against the scenario above.

  Scenario: A cleared US engineer with itar:read reads the asset and an AccessLogEntry is written
    Given "engineer-17"'s token carries claim "itar:read" (via role "ClearedEngineer", ADR-046)
    When "engineer-17" QUERYs technicalDataAsset(entityId: "defco:TechnicalDataAsset:td-4471")
    Then the response status should be 200 with UsmlCategory "USML Category XII" and Classification "ITAR-Controlled"
    And an AccessLogEntry should be written with ReaderActorId "engineer-17" and ReaderTrustBasis "Authoritative" (ADR-045)

  Scenario: An engineer lacking itar:read is denied
    Given "engineer-22"'s token carries no claim "itar:read"
    When "engineer-22" QUERYs technicalDataAsset(entityId: "defco:TechnicalDataAsset:td-4471")
    Then the response status should be 403
    # RequiredReadClaim gates this event type (ADR-008/ADR-050); role
    # membership is additive-only (ADR-046) -- there is no deny-entry path
    # that could grant "engineer-22" access some other way.

  Scenario: A TAA-scoped delegated grant is issued with a digital sign-off and used within its entityScope and expiration
    Given "engineer-17" (holding itar:read) approves an access request for "foreign-partner-42" scoped to entityScope "defco:TechnicalDataAsset:td-4471", claim "itar:read", expiring "2026-12-31"
    And the approval captures a Signature with Meaning "export_control_release_approval" via an RFC 9470 step-up challenge (ADR-066)
    And "foreign-partner-42" exchanges the resulting UCAN for a bearer JWT via POST /oauth/token (ADR-036/ADR-043)
    When "foreign-partner-42" QUERYs technicalDataAsset(entityId: "defco:TechnicalDataAsset:td-4471") before "2026-12-31"
    Then the response status should be 200 with UsmlCategory "USML Category XII" and Classification "ITAR-Controlled"
    And an AccessLogEntry should be written with ReaderTrustBasis "Attested" and a GrantRef

  Scenario: The same TAA-scoped grant is denied for an asset outside its entityScope
    Given "foreign-partner-42" holds a bearer JWT with claim "itar:read" and entityScope "defco:TechnicalDataAsset:td-4471"
    When "foreign-partner-42" QUERYs technicalDataAsset(entityId: "defco:TechnicalDataAsset:td-9982")
    Then the response status should be 403
    # The claim exists but its entityScope doesn't cover td-9982 -- ADR-043's
    # check is "HasClaim AND entityScope == this EntityId," not a bare HasClaim.

  Scenario: An expired TAA-scoped grant is denied at token exchange
    Given "foreign-partner-42"'s TAA-scoped grant expired "2026-12-31"
    When "foreign-partner-42" attempts to exchange the UCAN for a bearer JWT via POST /oauth/token after "2026-12-31"
    Then the token exchange should be rejected
    # Expiration is enforced at exchange/introspection time, not by scanning
    # events at query time (ADR-043) -- the same operational shape ADR-040's
    # ticket consumption already relies on.
```
