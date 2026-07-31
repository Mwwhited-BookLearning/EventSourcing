# Feature: Trial Data Export and Subject Rights

Context: this is Workflow C's own doc, covering two related but distinct
needs this domain's `../README.md` ("Special concerns") already names as
real, not hypothetical: a sponsor/regulator inspecting a patient's trial
record exactly as it stood at a point in time (`ADR-068`'s lineage export
+ bitemporal system-time playback — a routine trial-monitoring and
litigation-hold need), and a withdrawn subject's GDPR erasure request
(`ADR-057`'s crypto-shredding), directly stress-testing the
retention-vs-erasure tension ICH-GCP and GDPR create together. Both are
shown here, not split into two docs, because they're the same underlying
theme: data leaving the system properly, under the same read-path
enforcement and audit discipline as any other access.

**Continuity note, deliberate**: the export/playback scenario reuses
`S-0091` — the same patient enrolled in
[`patient-enrollment-and-informed-consent.md`](patient-enrollment-and-informed-consent.md)
and whose severe adverse event `ae-1042` is reviewed in
[`adverse-event-capture-and-review.md`](adverse-event-capture-and-review.md)
— since a regulator inspecting *that* record is exactly the scenario
`ADR-068` was built for. The **erasure** scenario deliberately introduces
a **different** subject, `S-0077`, specifically so this domain's main
continuity thread (`S-0091`) is never itself erased mid-narrative — a
reader following all four docs in order should be able to keep treating
`S-0091` as a live, intact record throughout.

This doc deliberately does **not** re-derive:
- **The `{value}`/`{masked}`/`{erased}` masking wrapper mechanics
  themselves** — see
  [`../../../features/masking.md`](../../../features/masking.md). This
  doc only shows the `erased` branch appearing on a read, not the wrapper
  or `IPayloadMasker`'s general mechanics.
- **`ADR-019`'s `ChainHash`/hash-chain derivation formula** — see
  [`../../../data/event-log.md`](../../../data/event-log.md)'s "Tamper
  evidence" section. This doc only shows the manifest hash's *inputs*
  (the ordered `ChainHash` values), not how a `ChainHash` itself is
  computed.
- **UCAN/DID delegation and Token Exchange mechanics** (`ADR-036`) — see
  [`../../../features/did-ucan-attestation.md`](../../../features/did-ucan-attestation.md).
- **`ADR-045`'s read access audit log mechanics in general** — this doc
  only notes that an export or playback query *is* an ordinary logged
  read, not how `AccessLogEntry`'s hash chain works.
- **How the device-linked adverse event or the enrollment record this
  doc exports were originally captured** — fully owned by
  `adverse-event-capture-and-review.md` and
  `patient-enrollment-and-informed-consent.md` respectively.

Every event type below is registered under `AppId` `"trial1"`
(`ADR-030`); `EntityId` format is `{appId}:{entityType}:{uniqueId}`
(`ADR-021`).

## Sequence diagram — sponsor/regulator lineage export with a chain-of-custody manifest

```plantuml
@startuml LineageExport_Sequence
autonumber
actor "Sponsor Auditor\n(auditor-3)" as auditor
participant "GraphQL Gateway" as gateway
participant "LineageExportResolver" as resolver
database "Event Log" as eventLog
participant "IPayloadMasker\n(ADR-009/057)" as masker
database "Access Audit Log\n(ADR-045)" as accessLog

auditor -> gateway: QUERY exportLineage(entityId: "trial1:AdverseEvent:ae-1042")\nBearer <JWT>
gateway -> gateway: HasClaim("export:lineage") (ADR-008)
alt missing claim
  gateway --> auditor: 403 (claim "export:lineage" missing)
else authorized
  gateway -> resolver: exportLineage("trial1:AdverseEvent:ae-1042")
  resolver -> eventLog: walk Lineage DAG (ADR-005) --\ngather ae-1042's AdverseEventReported event,\nits accessGrant "grant-1", and its authorityDecision
  eventLog --> resolver: StoredEvent rows, SequenceNumber order
  resolver -> masker: mask/decrypt each Payload per auditor-3's OWN claims\n(ADR-009 claims check, ADR-057 erased-branch check)
  masker --> resolver: rendered Payloads (value / masked / erased per field)
  note right of masker
    No bypass -- a field auditor-3 couldn't see live,
    they can't see exported either (ADR-068's own
    stated rule). If ae-1042's subject had ever been
    erased, those fields would render "erased": true
    in the bundle too.
  end note
  resolver -> accessLog: INSERT AccessLogEntry\n(ReaderActorId: "auditor-3", CredentialKind: "Authoritative")
  note right of accessLog
    An export IS an ordinary logged read (ADR-045) --
    not a privileged escape hatch around it.
  end note
  resolver -> resolver: build NDJSON bundle (SequenceNumber order)\n+ manifest { EventTypeDefinitions referenced,\n  ManifestHash = SHA256(ordered ChainHash values\n  + ExportedByActorId + ExportedAt), FrameworkVersion }\n(ADR-068, reusing ADR-019's hash primitive)
  resolver --> gateway: bundle.ndjson + manifest.json
  gateway --> auditor: 200 { bundle.ndjson, manifest.json }
end
@enduml
```

## Sequence diagram — bitemporal system-time playback, a correction recovered in place

```plantuml
@startuml SystemTimePlayback_Sequence
autonumber
actor "Sponsor Auditor\n(auditor-3)" as auditor
participant "GraphQL Gateway" as gateway
participant "SystemTimePlaybackResolver" as playback
database "Event Log" as eventLog

auditor -> gateway: QUERY systemTimePlayback(entityId: "trial1:Patient:S-0091",\n  asOfSequenceNumber: 48812)\nBearer <JWT, claim "export:playback">
gateway -> playback: reconstruct("trial1:Patient:S-0091", 48812)
playback -> eventLog: SELECT StoredEvent\nWHERE (EntityId = "trial1:Patient:S-0091"\n  OR TelemetryPointer.ChannelId's linked patient EntityId matches)\n  AND SequenceNumber <= 48812\nORDER BY SequenceNumber ASC
eventLog --> playback: events, in ARRIVAL order (not OccurredAt order --\nthe literal opposite of the authoritative fold's rule, ADR-068)
loop fold each event in arrival order
  playback -> playback: apply event to the in-memory reconstruction
  alt this event carries LateArrivalFlag = true
    playback -> playback: the reconstruction visibly changes\nRIGHT HERE -- "recovered in place, in real time,"\nnever smoothed away as EntityStoreRow already does
  end
end
playback --> gateway: reconstructed PatientRecord as of SequenceNumber 48812,\nwith the late correction shown landing exactly when it arrived
gateway --> auditor: 200 { reconstruction, asOfSequenceNumber: 48812 }
@enduml
```

`EntityStoreRow` (`ADR-021`/`ADR-029`) is unaffected by this query — it
stays a valid-time-corrected view of "what's true now." System-time
playback is a second, parallel way to view the *same* history, computed
on demand (`ADR-068` v1), never a replacement for the authoritative fold.

## Sequence diagram — GDPR erasure for a withdrawn subject

```plantuml
@startuml Erasure_Sequence
autonumber
actor "Site Coordinator" as coordinator
actor "Data Protection Officer\n(dpo-1)" as dpo
participant "PublishEndpoint\n(Inbox)" as inbox
database "Event Log" as eventLog
participant "IErasureKeyStore\n(ADR-057)" as keyStore
actor "Any subsequent reader" as reader
participant "GraphQL Gateway" as gateway

coordinator -> inbox: POST /publish/ConsentWithdrawn\n{ payload: { SubjectId: "S-0077", WithdrawnAt: "2026-07-28T00:00:00Z",\n  Reason: "subject withdrew consent" } }
inbox -> eventLog: INSERT StoredEvent (ConsentWithdrawn)
note right of inbox
  Retained FOREVER, per ICH-GCP -- the withdrawal
  itself is a real trial event, structurally
  identical to any other, and is never itself
  a target of the erasure below.
end note
inbox --> coordinator: 202

dpo -> inbox: POST /publish/EntityErasureRequested\n{ payload: { EntityId: "trial1:Patient:S-0077",\n  RequestedAt: "2026-07-29T00:00:00Z",\n  Reason: "subject withdrew consent, GDPR Art. 17 request" } }
inbox -> eventLog: INSERT StoredEvent (EntityErasureRequested)\nActorId: "dpo-1" -- hash-chained like any other event (ADR-057)
inbox -> keyStore: DestroyKey(AppId: "trial1", EntityId: "trial1:Patient:S-0077")
keyStore -> keyStore: irreversible key destruction\n(Key Vault purge / KMS scheduled deletion / Vault key deletion)
inbox --> dpo: 202
note right of keyStore
  Destroys the DEK, never touches StoredEvent.Payload
  or ADR-019's hash chain -- ADR-057's own governing
  rule. The FACT that erasure happened is itself
  preserved forever (README.md's "never lose data").
end note

... subsequently ...
reader -> gateway: QUERY patient(entityId: "trial1:Patient:S-0077")
gateway -> gateway: attempt decrypt of LegalName/DateOfBirth\nvia IErasureKeyStore -- DEK no longer exists
gateway --> reader: 200 { SubjectId: "S-0077", SiteId: "04-221",\n  EnrollmentStatus: "Withdrawn",\n  LegalName: { "erased": true },\n  DateOfBirth: { "erased": true } }
note right of reader
  EnrollmentStatus/SiteId/ScreeningDate are NOT
  x-masking-classified PHI -- they remain fully
  readable regardless of claims. Only fields whose
  erasureScope resolves to this EntityId's destroyed
  DEK become "erased": true (ADR-057) -- distinct from
  "masked": a claim can never restore this.
end note
@enduml
```

The retention-vs-erasure tension this domain's `../README.md` names is
resolved exactly as stated there: the record's *structure* (its
existence, its non-PHI clinical fields, the fact and timing of
withdrawal and erasure themselves) survives forever, satisfying
ICH-GCP's retention requirement; only the subject's *identifying* content
becomes permanently unrecoverable, satisfying GDPR Art. 17. If `S-0077`
had ever appeared as a `Signature.SignerId` on some event (a countersigned
CRF, say), that signature would remain fully attributable regardless —
`ADR-066`'s categorical exemption (GDPR Art. 17(3)(b)/(e)) — though this
particular scenario doesn't exercise that path, since `S-0077` here is
the consent *subject*, never a signer.

## Data model (ER diagram)

```plantuml
@startuml ExportErasure_ER
hide circle
skinparam linetype ortho

entity "StoredEvent" as event {
  * SequenceNumber : bigint <<PK>>
  --
  EventId : uuid <<unique>>
  EntityId : string
  ' trial1:Patient:S-0077 (ConsentWithdrawn, EntityErasureRequested);
  ' trial1:AdverseEvent:ae-1042 / trial1:Patient:S-0091 (exported/played back)
  EventType : string
  Payload : text
  ' classified fields stored as ciphertext once a DEK exists (ADR-057)
  ChainHash : string
  LateArrivalFlag : bool?
  ' relevant only for playback's arrival-order fold (ADR-068)
}

entity "EntityErasureKey\n(ADR-057)" as erasureKey {
  * EntityId : string <<PK>>
  --
  AppId : string
  WrappedDek : bytes
  ' never the key material itself -- lives only in IErasureKeyStore
  KeyStoreBackendKey : string
  ' which registered IErasureKeyStore backend holds this entity's DEK
  CreatedAt : datetimeoffset
  DestroyedAt : datetimeoffset?
}

entity "EntityStoreRow\n(Patient)" as entityStore {
  * EntityId : string <<PK>>
  --
  Data : text
  ' LegalName/DateOfBirth render {"erased": true} once DestroyedAt is set
}

entity "AccessLogEntry\n(ADR-045, full shape in ../../../data/access-log.md)" as accessLog {
  * SequenceNumber : bigint <<PK>>
  --
  ReaderActorId : string
  EntityId : string
  ' the export/playback target, logged like any other read
  CredentialKind : enum {Authoritative, Attested}
}

event ..> erasureKey : "EntityId -- the DEK this entity's\nclassified fields were encrypted\nunder (ADR-057)"
erasureKey ..> entityStore : "DestroyedAt set --\nsubsequent reads of classified\nfields render erased: true"
event ..> accessLog : "an export or playback query writes\none AccessLogEntry per read (ADR-045),\nlogically, not a DB FK"

note right of erasureKey
  ExportManifest / SystemTimePlayback results are
  produced ARTIFACTS, not persisted rows -- shown as
  C# DTOs below, not in this ER diagram, since neither
  is a new database table (ADR-068's own "v1 computes
  this on demand" decision for playback; an export
  bundle is likewise never stored server-side).
end note
@enduml
```

Full column lists are in
[`../../../data/event-log.md`](../../../data/event-log.md),
[`../../../data/entity-store.md`](../../../data/entity-store.md)
(`EntityErasureKey`), and
[`../../../data/access-log.md`](../../../data/access-log.md) — this
diagram shows only what export, playback, and erasure actually touch.

```csharp
// ConsentWithdrawn payload -- EntityIdField "$.SubjectId" (ADR-021).
// Retained forever (ICH-GCP) -- never itself a target of erasure.
public class ConsentWithdrawnPayload
{
    public string SubjectId { get; set; } = default!;
    public string WithdrawnAt { get; set; } = default!;
    public string Reason { get; set; } = default!;
}

// EntityErasureRequested payload -- a reserved event type (ADR-057,
// reusing ADR-020's reservation pattern). Publishing this destroys the
// target EntityId's DEK; it never touches StoredEvent.Payload or the
// hash chain of any event.
public class EntityErasureRequestedPayload
{
    public string EntityId { get; set; } = default!;   // "trial1:Patient:S-0077"
    public string RequestedAt { get; set; } = default!;
    public string Reason { get; set; } = default!;
}

// ExportManifest -- a produced ARTIFACT (ADR-068), never a persisted
// database row; accompanies the NDJSON bundle.
public class ExportManifest
{
    public string[] EntityIdsExported { get; set; } = default!;
    public string[] EventTypeDefinitionsReferenced { get; set; } = default!; // for upcast-correctness on import
    public string ManifestHash { get; set; } = default!;   // SHA-256 over ordered ChainHash values + export metadata (ADR-019/ADR-068)
    public string ExportedByActorId { get; set; } = default!;
    public string ExportedAt { get; set; } = default!;
    public string FrameworkVersion { get; set; } = default!; // ADR-062's SemVer -- "matched version reads its own bundles" (ADR-068 §4)
}

// SystemTimePlaybackResult -- a produced result (ADR-068), computed on
// demand, not stored.
public class SystemTimePlaybackResult
{
    public string EntityId { get; set; } = default!;
    public long AsOfSequenceNumber { get; set; }
    public string ReconstructedDataJson { get; set; } = default!;
    public bool LateArrivalCorrectionShown { get; set; }
}
```

## State machine — a subject's data-rights lifecycle

```plantuml
@startuml SubjectRights_State
[*] --> Active : PatientScreened / InformedConsentCaptured / ConsentApproval\n(see patient-enrollment-and-informed-consent.md)

Active --> Active : exported (ADR-068) or played back (ADR-068) --\na READ, never changes this lifecycle state
Active --> Withdrawn : ConsentWithdrawn published\n(retained forever, ICH-GCP)

Withdrawn --> ErasureRequested : EntityErasureRequested published\n(ADR-057) -- itself hash-chained,\nretained forever
ErasureRequested --> Erased : IErasureKeyStore destroys the DEK\n(irreversible)

Erased : classified PHI fields render\n{"erased": true} on every subsequent read
Erased : non-PHI structural/clinical fields\nremain fully readable (ICH-GCP retention)
Erased : the EVENTS themselves, and the fact\nof erasure, are retained forever
Erased --> [*]
@enduml
```

The `Active --> Active` self-loop is deliberate: export and playback are
reads, not state transitions — they never move a subject any closer to
withdrawal or erasure, and can happen at any point in this lifecycle,
including after `Erased` (an export of an erased subject's record would
simply render `erased: true` for the same fields an ordinary query
would).

## Salt (UI mockup) — export/playback flow and erasure flow, each its own short screen sequence

### Export flow — Screen 1: export/playback request form

```plantuml
@startsalt
{
  { "Trial Data Export -- Sponsor Auditor (auditor-3)" }
  ..
  { "Entity ID" | "^trial1:AdverseEvent:ae-1042^" }
  [ Export Lineage Bundle ]
  ..
  { "System-Time Playback" }
  { "Entity ID" | "^trial1:Patient:S-0091^" }
  { "As of SequenceNumber" | "^48812^" | [<] [Play] [>] }
}
@endsalt
```

Clicking **Export Lineage Bundle** returns the NDJSON bundle + manifest
directly on this screen (a produced artifact, never a second page).
Clicking **Play** on the System-Time Playback control instead navigates to
Screen 2, the reconstruction viewer for the chosen `asOfSequenceNumber`.

### Export flow — Screen 2: bitemporal playback viewer

```plantuml
@startsalt
{
  { "System-Time Playback -- trial1:Patient:S-0091 @ SequenceNumber 48812" }
  ..
  { "EnrollmentStatus" | "Enrolled" }
  { "Last vitals sample" | "flagged LateArrival -- recovered in place at seq 48810" }
  ..
  [<] [ Play ] [>]
  "Scrubbing to seq 48809 hides this correction -- it lands exactly\n at 48810, never smoothed away (ADR-068)"
}
@endsalt
```

This viewer shows "what we knew as of `SequenceNumber` 48812" — folding
events in arrival order, the literal opposite of the authoritative fold's
own valid-time-corrected rule — with the late-arriving correction visibly
landing in place rather than smoothed into the record's current shape.

### Erasure flow — Screen 1: subject-rights / erasure request

```plantuml
@startsalt
{
  { "Subject Rights -- Data Protection Officer (dpo-1)" }
  ..
  | Subject | Status        | Withdrawn on | Erasure requested   |
  | S-0077  | Withdrawn     | 2026-07-28   | [ Request Erasure ] |
  | S-0091  | Enrolled      | --           | (not eligible)      |
  ..
  "! Requesting erasure is irreversible -- destroys the subject's\n  encryption key permanently. Structural trial records are retained."
  [ Confirm Erasure Request ]
}
@endsalt
```

`S-0091` (this domain's main continuity thread) shows as *not eligible*
for erasure — only a `Withdrawn` subject like `S-0077` can be requested.
Clicking **Confirm Erasure Request** publishes `EntityErasureRequested`
for `trial1:Patient:S-0077` and irreversibly destroys its `DEK`
(`ADR-057`); Screen 2 shows the result on a subsequent read.

### Erasure flow — Screen 2: confirmation, record intact but identifying fields erased

```plantuml
@startsalt
{
  { "trial1:Patient:S-0077 -- Record (post-erasure read)" }
  ..
  { "SubjectId" | "S-0077" } | { "SiteId" | "04-221" }
  { "EnrollmentStatus" | "Withdrawn" }
  { "LegalName" | "{ \"erased\": true }" }
  { "DateOfBirth" | "{ \"erased\": true }" }
}
@endsalt
```

The record's structure and non-PHI fields (`SubjectId`, `SiteId`,
`EnrollmentStatus`) remain fully readable forever, satisfying ICH-GCP's
retention requirement; only `LegalName`/`DateOfBirth` — the fields whose
`erasureScope` resolved to this now-destroyed `DEK` — render
`{"erased": true}`, distinct from `masked`: no claim, however privileged,
can ever restore this.

## Gherkin

```gherkin
Feature: Trial Data Export and Subject Rights
  As a sponsor auditor or regulator
  I want to export a patient's causal event chain with a verifiable chain-of-custody manifest
  And scrub through a patient's history exactly as it was known at any point in time, corrections included
  As a Data Protection Officer
  I want to permanently destroy a withdrawn subject's identifying data on request
  So that data can leave this system for litigation/regulatory review, or be erased for a withdrawn subject, without ever bypassing the same claims/masking/audit discipline every other read and write already follows

  # AppId "trial1" throughout (ADR-030). "S-0091" continues the same
  # patient enrolled in patient-enrollment-and-informed-consent.md and
  # reviewed in adverse-event-capture-and-review.md. "S-0077" is a
  # DIFFERENT, non-continuity subject, used only in this doc's erasure
  # scenarios, so S-0091's record is never itself erased.

  Background:
    Given the event type "ConsentWithdrawn" version 1 is registered with EntityIdField "$.SubjectId"
    And the event type "EntityErasureRequested" version 1 is a reserved event type (ADR-057) with EntityIdField "$.EntityId"
    And "auditor-3" holds claims ["export:lineage", "export:playback"]
    And "dpo-1" holds claim ["erasure:request"]
    And the adverse event "ae-1042" for subject "S-0091" is "accepted", per adverse-event-capture-and-review.md
    And subject "S-0077" is "Enrolled" at site "04-221", with classified fields LegalName and DateOfBirth encrypted under an EntityErasureKey (ADR-057)

  Scenario: An auditor without the export claim is denied
    When an authenticated caller lacking "export:lineage" queries "exportLineage(entityId: \"trial1:AdverseEvent:ae-1042\")"
    Then the response should be 403 for a missing "export:lineage" claim
    And no bundle should be produced

  Scenario: An auditor exports ae-1042's lineage bundle with a verifiable manifest
    When "auditor-3" queries "exportLineage(entityId: \"trial1:AdverseEvent:ae-1042\")"
    Then the response status should be 200 with an NDJSON bundle and a manifest.json
    And the manifest's ManifestHash should equal SHA-256 over the ordered ChainHash values of every exported event plus ExportedByActorId "auditor-3" and ExportedAt
    And an AccessLogEntry should be written recording "auditor-3"'s read (ADR-045)
    # No bypass: any field auditor-3 lacks the claim for would still
    # render "masked", any erased field would still render "erased":
    # true, inside the exported bundle (ADR-068's own no-bypass rule).

  Scenario: An auditor scrubs system-time playback and sees a late-arriving correction recovered in place
    Given a TelemetrySample-linked event for "trial1:Patient:S-0091" arrived out of order and was recorded with LateArrivalFlag true at SequenceNumber 48810
    When "auditor-3" queries "systemTimePlayback(entityId: \"trial1:Patient:S-0091\", asOfSequenceNumber: 48812)"
    Then the reconstruction should show the correction landing exactly at SequenceNumber 48810, not smoothed away
    And the same query with asOfSequenceNumber 48809 should NOT yet show that correction
    # The literal opposite of EntityStoreRow's valid-time-corrected fold
    # (ADR-021/029) -- this is what the system actually saw, in the
    # order it actually saw it (ADR-068).

  Scenario: A withdrawn subject's consent withdrawal is retained forever, never itself erased
    When a coordinator publishes "ConsentWithdrawn" for "S-0077" with body { "SubjectId": "S-0077", "WithdrawnAt": "2026-07-28T00:00:00Z", "Reason": "subject withdrew consent" }
    Then the response status should be 202
    And the ConsentWithdrawn event should remain fully readable and hash-chained forever, exactly like any other event
    # ICH-GCP's retention requirement applies to this event just as
    # much as to any clinical finding.

  Scenario: A Data Protection Officer requests erasure for the withdrawn subject, destroying the encryption key
    Given subject "S-0077" has withdrawn consent, per above
    When "dpo-1" publishes "EntityErasureRequested" with body { "EntityId": "trial1:Patient:S-0077", "RequestedAt": "2026-07-29T00:00:00Z", "Reason": "subject withdrew consent, GDPR Art. 17 request" }
    Then the response status should be 202
    And the EntityErasureRequested event itself should be retained and hash-chained forever
    And IErasureKeyStore should irreversibly destroy the DEK for ("trial1", "trial1:Patient:S-0077")

  Scenario: After erasure, PHI fields render erased while structural/clinical fields remain readable
    Given subject "S-0077"'s DEK has been destroyed, per above
    When any authenticated caller queries "patient(entityId: \"trial1:Patient:S-0077\")"
    Then LegalName and DateOfBirth should render { "erased": true }
    And EnrollmentStatus, SiteId, and WithdrawnAt should still render their real values
    # "erased" is distinct from "masked" (ADR-057) -- no claim, however
    # privileged, can ever restore an erased field; ICH-GCP's retention
    # requirement is satisfied by the record's surviving structure.

  Scenario: A caller without the erasure-request claim cannot destroy another subject's key
    When an authenticated caller lacking "erasure:request" attempts to publish "EntityErasureRequested" for "trial1:Patient:S-0091"
    Then the response should be 403 for a missing "erasure:request" claim
    And subject "S-0091"'s data-encryption key should remain intact
    # S-0091's own continuity is never put at risk by this scenario --
    # it exists specifically to show the claim gate holds, not to erase
    # the domain's main continuity thread.
```
