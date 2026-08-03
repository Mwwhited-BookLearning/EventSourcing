# Feature: Digital Evidence Acquisition and Chain-of-Custody Export

Context: this domain's own [`README.md`](../README.md#applicable-adrs)
calls out `ADR-019` and `ADR-068` as two of its strongest, most direct
fits, and this doc exercises both directly: `ADR-019`'s linear hash chain
(`ChainHash`) is what makes every acquisition and custody-transfer event
tamper-evident, and `ADR-068`'s three-part decision — lineage-scoped
export, bitemporal system-time playback, and the self-contained offline
player — is exercised end to end for a litigation-review handoff. Three
more `README.md`-listed ADRs round out the workflow: `ADR-032` (binary
attachments — the forensic image itself is attachment-shaped content,
this domain's highest-scoring fit of any candidate), `ADR-005` (event
lineage — a derived artifact traces causally to its source acquisition,
a literal DAG), and `ADR-045`/`ADR-066` (the read access audit log *is*
chain of custody; examiner sign-offs at each handoff are `ADR-066`'s
digital-sign-off target case). Envelope/data shapes referenced below
come from
[`../../../data/event-log.md`](../../../data/event-log.md)
(`StoredEvent`, `Signature`, `ChainHash`),
[`../../../data/streaming-and-attachments.md`](../../../data/streaming-and-attachments.md)
(`Attachment`, `AttachmentRef`), and
[`../../../data/access-log.md`](../../../data/access-log.md)
(`AccessLogEntry`).

This doc covers only what's specific to an evidence item's own
acquisition-through-litigation-export path. It deliberately does
**not** re-derive:
- Attachment upload/content-addressing/chunking mechanics in general
  (`POST /attachments`, `ContentHash` dedup, tiering) — that's
  `ADR-032`'s own decision record and
  [`../../../features/binary-attachments.md`](../../../features/binary-attachments.md).
  This doc only shows a disk image *as* an `AttachmentRef` on
  `EvidenceAcquired`, not the upload mechanism itself.
- Delegated, capped, entity-scoped access grants for opposing counsel or
  a secondary examiner (`ADR-043`/`ADR-044`) — a real secondary fit this
  domain's `README.md` names, but deliberately out of scope here to keep
  this doc's focus on acquisition/custody/export; see that ADR's own
  record for the grant mechanism itself.
- `RequiredSignature`/RFC 9470 step-up mechanics in general — this doc
  shows the one specific trigger (a custody-transfer handoff) that needs
  it, not the mechanism's full decision record (`ADR-066`).
- Masking/`x-masking` mechanics — a real but secondary fit here (evidence
  may contain PII/PHI needing redaction for discovery); the export
  mechanism below enforces it unchanged, but this doc doesn't re-explain
  the wrapper shape, covered by
  [`../../../features/masking.md`](../../../features/masking.md) and,
  for the domain that stress-tests it hardest, this domain's sibling
  government-case-management feature doc.

## Sequence diagram — acquisition, custody transfer, and derived-artifact lineage

```plantuml
@startuml Evidence_Acquisition_Custody_Sequence
autonumber
actor "Examiner" as examiner
participant "Attachment Upload\n(ADR-032)" as attachUpload
participant "Publish Endpoint\n(Inbox, ADR-023)" as inbox
database "Attachment Store" as attachStore
database "Event Log" as eventLog
actor "Analyst" as analyst

examiner -> attachUpload: POST /attachments\n(raw disk-image bytes, acquired through a write blocker)
attachUpload -> attachStore: store bytes, compute ContentHash\n(SHA-256, ADR-032)
attachStore --> attachUpload: ContentHash "sha256:9f3a..."
attachUpload --> examiner: 201 { contentHash }
examiner -> inbox: POST /publish/EvidenceAcquired\n{ payload: { EvidenceId: "ev-1", CaseNumber: "24-CR-118",\n  SourceDeviceDescription: "...", AcquisitionToolName: "...",\n  WriteBlockerUsed: true },\n  attachmentRef: { contentHash: "sha256:9f3a..." } } (ADR-032)
inbox -> eventLog: INSERT StoredEvent\n(EntityId "lab:Evidence:ev-1", ActorId = examiner (ADR-064),\nChainHash computed off prior SequenceNumber's, ADR-019)
inbox --> examiner: 202 { status: "received", entityId: "lab:Evidence:ev-1" }
== custody changes hands ==
examiner -> inbox: POST /publish/CustodyTransferred\n{ payload: { EvidenceId: "ev-1", FromActorId: "examiner-1",\n  ToActorId: "analyst-2", Reason: "handoff for analysis" } }\n(RequiredSignature configured, ADR-066)
alt caller's token satisfies RequiredSignature (acr/max_age)
  inbox -> eventLog: INSERT StoredEvent "CustodyTransferred"\n(Signature: { SignerId: "examiner-1", Meaning: "custody transfer",\nAcr: "urn:lab:acr:step-up" }, ADR-066)
  inbox --> examiner: 202 { status: "received" }
else caller's token does not satisfy RequiredSignature
  inbox --> examiner: 401 WWW-Authenticate: step-up required\n(acr_values, max_age -- RFC 9470, ADR-066)
  note right: rejected BEFORE storage -- the one exception to\npersist-everything for insufficient auth strength (ADR-066)
end
analyst -> inbox: POST /publish/ArtifactExtracted\n{ payload: { ArtifactId: "art-1", ArtifactType: "DeletedFileRecovery",\n  ExtractionToolName: "..." },\n  attachmentRef: { contentHash: "sha256:aa77..." },\n  parentEventIds: [EvidenceAcquired.EventId] } (ADR-005)
inbox -> eventLog: INSERT StoredEvent "ArtifactExtracted"\n(EntityId "lab:Artifact:art-1", EventParents -> EvidenceAcquired,\nParentValidationMode Strict -- parent must already exist, ADR-005)
@enduml
```

## Sequence diagram — litigation export and offline-player self-verification

```plantuml
@startuml Evidence_Litigation_Export_Sequence
autonumber
actor "Case Attorney / Paralegal" as attorney
participant "Lineage Export Resolver\n(ADR-068)" as exportResolver
participant "IEventLineageQueryProvider\n(ADR-005)" as lineageProvider
database "Event Log" as eventLog
database "Access Log" as accessLog
participant "Offline Player\n(vite-plugin-singlefile build, ADR-068)" as player

attorney -> exportResolver: exportLineage(entityId: "lab:Evidence:ev-1")
exportResolver -> lineageProvider: ancestors/descendants("lab:Evidence:ev-1")\n(cycle-safe traversal, ADR-005)
lineageProvider -> eventLog: SELECT StoredEvent ... via EventParents
lineageProvider --> exportResolver: [EvidenceAcquired, CustodyTransferred x2, ArtifactExtracted]
exportResolver -> accessLog: INSERT AccessLogEntry\n(Action: "export", ResourceRef: "lab:Evidence:ev-1",\nReaderActorId: attorney's ActorId, ADR-045)
note right: masking/claims enforcement applies identically to any\nother read -- no bypass for export (ADR-068's no-bypass rule)
exportResolver -> exportResolver: build NDJSON bundle + manifest\n(referenced EventTypeDefinitions, exported-by/exported-at,\nSHA-256 manifest hash over ordered ChainHash values, ADR-019/068)
exportResolver --> attorney: bundle.ndjson + manifest.json
attorney -> player: open offline-player.html\n(double-click, no server, no install)
player -> player: recompute ChainHash sequence and manifest hash\nfrom the embedded bundle data alone
alt every exported event's fields are unmasked in the bundle\n(exporting actor held every relevant claim)
  player --> attorney: "Fully independently verified" (pass)
else at least one field was masked because the EXPORTING\nactor themselves lacked its requiredClaim
  player --> attorney: "Verified except N masked fields --\nchain linkage intact" (partial, ADR-068's honest distinction)
  note right: PayloadHash/ChainHash were computed once, over the real\nstored bytes -- re-hashing a redacted field can't reproduce them;\nenvelope-level chain linkage between events is still fully checked
end
@enduml
```

## Data model (ER diagram)

```plantuml
@startuml Evidence_Custody_ER
hide circle
skinparam linetype ortho

entity "EvidenceAcquired" as acquired {
  * EventId : uuid <<PK>>
  --
  EntityId : string
  ' lab:Evidence:{EvidenceId}
  EvidenceId : string
  CaseNumber : string
  SourceDeviceDescription : string
  AcquisitionToolName : string
  WriteBlockerUsed : bool
  AttachmentRef : string
  ' ContentHash of the disk image (ADR-032)
}

entity "CustodyTransferred" as transfer {
  * EventId : uuid <<PK>>
  --
  EntityId : string
  FromActorId : string
  ToActorId : string
  Reason : string
  Signature : Signature
  ' Meaning "custody transfer", required (ADR-066)
}

entity "ArtifactExtracted" as artifact {
  * EventId : uuid <<PK>>
  --
  EntityId : string
  ' lab:Artifact:{ArtifactId}
  ArtifactType : string
  ExtractionToolName : string
  AttachmentRef : string
}

entity "AccessLogEntry" as accessLog {
  * SequenceNumber : bigint <<PK>>
  --
  ReaderActorId : string
  Action : string
  ' "query" | "export" | "download"
  ResourceRef : string
  ChainHash : string
}

acquired <.. transfer : "same EntityId --\nCustodyTransferred is a Partial\npatch on the evidence entity"
acquired <.. artifact : "parentEventIds\n(ADR-005 lineage DAG)"
acquired ..> accessLog : "every read of this entity,\nincluding the litigation export,\nis a logged ResourceRef (ADR-045)"

note bottom of transfer
  Chain of custody, structurally: each
  CustodyTransferred event is itself a
  hash-chained (ADR-019), signed (ADR-066)
  StoredEvent -- there is no separate
  "custody log" table.
end note
@enduml
```

Full `StoredEvent`/`Signature`/`Attachment`/`AttachmentRef` columns are
in [`../../../data/event-log.md`](../../../data/event-log.md) and
[`../../../data/streaming-and-attachments.md`](../../../data/streaming-and-attachments.md);
full `AccessLogEntry` columns are in
[`../../../data/access-log.md`](../../../data/access-log.md).

```csharp
// Payload shape for event type "EvidenceAcquired" v1
// (ChangeKind: Full, EntityIdField: "$.EvidenceId")
public class EvidenceAcquiredPayload
{
    public string EvidenceId { get; set; } = default!;         // -> EntityId "lab:Evidence:{EvidenceId}" (ADR-021)
    public string CaseNumber { get; set; } = default!;
    public string SourceDeviceDescription { get; set; } = default!;
    public string AcquisitionToolName { get; set; } = default!;
    public bool WriteBlockerUsed { get; set; }
}
// Envelope: ActorId = examiner (ADR-064, always populated, blocking);
// AttachmentRef.ContentHash points at the uploaded disk image bytes (ADR-032) --
// the image itself never rides inside Payload, keeping SchemaValidationService's
// parse cost independent of image size (the same reasoning ADR-031 applies to telemetry).

// Payload shape for event type "CustodyTransferred" v1
// (ChangeKind: Partial, EntityIdField: "$.EvidenceId", RequiredSignature configured -- ADR-066)
public class CustodyTransferredPayload
{
    public string FromActorId { get; set; } = default!;
    public string ToActorId { get; set; } = default!;
    public string Reason { get; set; } = default!;
}
// Envelope: Signature required (RequiredSignature.AcrValues/MaxAge, ADR-066) -- SignerId denormalizes
// ActorId, Meaning is required ("custody transfer"), Acr records the authentication context actually used;
// tamper evidence is ADR-019's existing ChainHash, no separate signature-integrity mechanism.

// Payload shape for event type "ArtifactExtracted" v1
// (ChangeKind: Partial, EntityIdField: "$.ArtifactId", ParentValidationMode Strict)
public class ArtifactExtractedPayload
{
    public string ArtifactId { get; set; } = default!;          // -> EntityId "lab:Artifact:{ArtifactId}" -- its OWN entity, not a patch on EvidenceAcquired's
    public string ArtifactType { get; set; } = default!;         // e.g. "DeletedFileRecovery", "DecryptedVolume", "TimelineEntry"
    public string ExtractionToolName { get; set; } = default!;
}
// Envelope: parentEventIds = [EvidenceAcquired.EventId] (ADR-005) -- the causal "derived from" link;
// AttachmentRef points at the extracted artifact's own binary content, independently content-addressed (ADR-032).
```

## Salt (UI mockup) — acquisition intake, the custody timeline, and the offline player's verdict

### Screen 1: Examiner's acquisition intake form

```plantuml
@startsalt
{
  { "Acquire Evidence -- Case 24-CR-118" }
  ..
  { "Disk image" | [Choose file...]  "disk-ev1.img (128 GB)" }
  { "Evidence ID" | "^ev-1^" }
  { "Source device" | "^Dell Latitude 5410, S/N ABC123^" }
  { "Acquisition tool" | "^FTK Imager 4.7^" }
  { "Write blocker used" | [X] }
  ..
  [ Upload and Publish EvidenceAcquired ] | [ Cancel ]
}
@endsalt
```

**Upload and Publish EvidenceAcquired** first runs the `POST
/attachments` upload from the first sequence diagram, computing the disk
image's `ContentHash` (`ADR-032`), then publishes `EvidenceAcquired`
carrying that hash as its `AttachmentRef`. The new `ChainHash` extends
the Event Log's existing chain (`ADR-019`) immediately, without waiting
for any later custody handoff. Once acquired, the item shows up on
Screen 2, the chain-of-custody timeline.

### Screen 2: Chain-of-custody timeline for one evidence item

```plantuml
@startsalt
{
  { "Evidence  ev-1  --  Case 24-CR-118  (lab:Evidence:ev-1)" }
  ..
  | SequenceNumber | Event              | Actor        | Signed?             |
  | 1042           | EvidenceAcquired   | examiner-1   | --                   |
  | 1043           | CustodyTransferred | examiner-1   | "custody transfer" ✓ |
  | 1058           | ArtifactExtracted  | analyst-2    | --                   |
  ..
  [ Export for litigation ] | [ Bitemporal playback ]
  ..
  { "Last export: 2026-06-02 -- Fully independently verified" }
}
@endsalt
```

Each row is one hash-chained `StoredEvent` from the timeline the first
sequence diagram builds — the `CustodyTransferred` row's signed checkmark
is only ever set once `ADR-066`'s RFC 9470 step-up challenge is
satisfied, exactly as that diagram's `alt` branch requires before
storage. Clicking **Export for litigation** runs the second sequence
diagram's lineage export end to end and hands the resulting bundle to
Screen 3, the offline player.

### Screen 3: Offline player's verification result

```plantuml
@startsalt
{
  { "Offline Player -- ev-1-export-2026-08-03.bundle" }
  ..
  { "Recomputing ChainHash sequence and manifest hash from embedded data..." }
  ..
  { "Result: Verified except 1 masked field -- chain linkage intact" }
  ..
  | Field masked                  | Reason                                  |
  | ArtifactExtracted.SourcePath  | exporting actor lacked its requiredClaim |
  ..
  [ View full event list ] | [ Close ]
}
@endsalt
```

This is the second sequence diagram's offline-player step, opened by
double-clicking `offline-player.html` with no server or install
(`ADR-068`) — no network round trip back to Screen 1 or 2 is involved.
The verdict shown here is the honest partial case: envelope-level chain
linkage is still fully re-verified, but one field's original
`PayloadHash`/`ChainHash` can't be re-derived because the exporting actor
themselves lacked that field's `requiredClaim` at export time, so the
bundle never carried the real bytes to re-hash.

## Gherkin

```gherkin
Feature: Digital Evidence Acquisition and Chain-of-Custody Export
  As a forensics lab
  I want acquisition, every custody handoff, and derived artifacts recorded as tamper-evident, signed events
  So that a litigation reviewer can export and independently re-verify the full chain of custody with no reliance on the lab's own testimony

  Background:
    Given the event type "EvidenceAcquired" version 1 is registered with ChangeKind "Full" and EntityIdField "$.EvidenceId"
    And the event type "CustodyTransferred" version 1 is registered with ChangeKind "Partial", EntityIdField "$.EvidenceId", and RequiredSignature { AcrValues: ["urn:lab:acr:step-up"], MaxAge: 300 }
    And the event type "ArtifactExtracted" version 1 is registered with ChangeKind "Partial", EntityIdField "$.ArtifactId", and ParentValidationMode "Strict"
    And "examiner-1" has acquired evidence "lab:Evidence:ev-1" with an uploaded disk image at ContentHash "sha256:9f3a..."

  Scenario: Acquiring evidence links the event to the uploaded disk image
    When "examiner-1" publishes "EvidenceAcquired" for EvidenceId "ev-1" with AttachmentRef ContentHash "sha256:9f3a..."
    Then the response status should be 202 with entityId "lab:Evidence:ev-1"
    And the stored event's AttachmentRef should resolve to the uploaded disk image
    And the stored event's ChainHash should extend the Event Log's existing chain (ADR-019)

  Scenario: A custody transfer with a sufficiently strong, recent authentication context is signed and stored
    Given "examiner-1"'s current token satisfies acr "urn:lab:acr:step-up" within the last 300 seconds
    When "examiner-1" publishes "CustodyTransferred" for EvidenceId "ev-1" handing off to "analyst-2"
    Then the response status should be 202
    And the stored event's Signature should have SignerId "examiner-1" and Meaning "custody transfer"

  Scenario: A custody transfer attempted with an insufficient authentication context is rejected before storage
    Given "examiner-1"'s current token does not satisfy acr "urn:lab:acr:step-up"
    When "examiner-1" attempts to publish "CustodyTransferred" for EvidenceId "ev-1" handing off to "analyst-2"
    Then the response should be 401 with a WWW-Authenticate step-up challenge naming "urn:lab:acr:step-up"
    And no "CustodyTransferred" event should be stored
    # The one publish outcome rejected before storage under ADR-023's otherwise persist-everything
    # posture -- insufficient authentication strength, not a content/schema problem (ADR-066).

  Scenario: Extracting an artifact links it to its source evidence, forming a lineage DAG
    When "analyst-2" publishes "ArtifactExtracted" for ArtifactId "art-1" with parentEventIds [ EvidenceAcquired.EventId for "ev-1" ]
    Then a new entity "lab:Artifact:art-1" should exist, distinct from "lab:Evidence:ev-1"
    And querying descendants of "lab:Evidence:ev-1" should include "lab:Artifact:art-1"

  Scenario: Exporting the evidence's lineage writes an access-log entry and a chain-derived manifest hash
    Given evidence "lab:Evidence:ev-1" has an EvidenceAcquired event, two CustodyTransferred events, and one ArtifactExtracted event in its lineage
    When "attorney-1" requests a lineage export for "lab:Evidence:ev-1"
    Then an AccessLogEntry should be written with Action "export" and ResourceRef "lab:Evidence:ev-1"
    And the export bundle's manifest hash should be a SHA-256 over the ordered ChainHash values of every exported event

  Scenario: The offline player fully verifies an export containing no masked fields
    Given every field in "lab:Evidence:ev-1"'s exported bundle was visible to the exporting actor
    When the offline player loads the bundle and recomputes its chain
    Then it should report "Fully independently verified"

  Scenario: The offline player reports partial verification when the exporting actor itself lacked a field's claim
    Given one field in "lab:Evidence:ev-1"'s exported bundle was masked because "attorney-1" lacked its requiredClaim
    When the offline player loads the bundle and recomputes its chain
    Then it should report "Verified except 1 masked field -- chain linkage intact"
    And it should not report a plain, undifferentiated pass
    # A masked field's original ChainHash/PayloadHash was computed over bytes the bundle no longer
    # carries -- the player can still confirm envelope-level chain linkage, just not re-derive that
    # one event's exact original hash from redacted content (ADR-068's stated, honest limitation).
```
