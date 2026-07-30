[← ADR index](../07-adrs.md)

# ADR-032: Binary attachments — content-addressed, linked to an entity or event

Status: Accepted

Context: A genuinely different shape of data from either the event log
or `ADR-031`'s streaming channels: a **discrete** binary object — a
supporting document, a scanned form, a photo — uploaded once (or a
handful of times), not sampled continuously. Neither the event log
(JSON-Schema-validated, not built for large opaque blobs in `Payload`)
nor a streaming channel (a sequence, not a one-off object) is the right
home for this. This is also the first genuine use case in this design
for **content-addressable storage** — a concept the innovation-sprint
review earlier in this project's history considered and explicitly
marked reference-only, for lack of a real need at the time (`Payload` is
small JSON, no large-object storage problem existed). That reasoning no
longer holds once arbitrary-sized binary uploads are a real feature.

Decision:
- **`Attachment`: content-addressed by construction.** `ContentHash`
  (SHA-256 of the raw bytes — the same primitive `ADR-011`/`ADR-019`
  already use, not a new algorithm) is the primary reference; uploading
  identical bytes twice is naturally deduplicated (a second upload with a
  matching hash reuses the existing stored object rather than storing a
  second copy) — the same idempotency-by-content-equality reasoning
  `ADR-011` already applies to events, now applied to blobs.
- **Storage is out-of-band from the event log**, the same posture
  `ADR-031` already takes for streaming channels and for the same
  reason: this design's three EF Core providers (`ADR-001`) are an
  acceptable v1 engine (a plain `(ContentHash, Bytes, MimeType, SizeBytes,
  UploadedAt)` table) but real deployments — especially anything at
  meaningful file-size or volume — would more naturally use object/blob
  storage behind a CDN, exactly the caveat `ADR-031` already states for
  `Media` channels. Recorded here rather than silently assumed either way.
- **`AttachmentRef` is envelope metadata, linking an attachment to an
  `EntityId` and/or a specific `EventId`** — both are supported,
  independently: an attachment can belong to an entity generally (a
  patient's supporting documents overall) or to one specific event
  narrowly (the document that justified *this* particular decision), or
  both. This is the fourth field in the same family as `parentEventIds`
  (`ADR-005`), `MaterializationOfEventId` (`ADR-027`), and
  `TelemetryPointer` (`ADR-031`) — a distinct relationship, its own
  field, never conflated with the others: `parentEventIds` answers
  causal derivation, `MaterializationOfEventId` answers "reshaped copy
  of," `TelemetryPointer` answers "position in a signal," and
  `AttachmentRef` answers "supporting document for."
- **Upload is a two-step handoff, not embedded in a publish request**:
  `POST /attachments` (raw bytes, returns `ContentHash`) followed by an
  ordinary domain-event publish carrying that hash in `AttachmentRef` —
  keeps large binary payloads out of the JSON envelope `SchemaValidationService`
  (`05-schema-registry-and-spec-generation.md`) already has to parse for
  every publish, the same "don't make the common case pay for the rare
  large one" reasoning `ADR-031` already applies to telemetry.
- **Never deleted, same as everything else in this design** — an
  attachment linked from a rejected/superseded event (`ADR-023`) is not
  purged; if a real deletion requirement ever surfaces, it's the same
  deliberately-unsolved, separate problem `ADR-009`'s closing note
  already names for regulated event data, not newly invented here.

## Access paths, matched to standards individually — not one protocol for everything

Rather than reaching for one protocol to cover every way an attachment
gets touched, each real access path is matched to whichever existing
standard already fits it — several were already fully solved by
mechanisms this design had adopted for other reasons, before WebDAV
ever entered the picture:

- **Upload** — plain `POST /attachments` (raw bytes, returns
  `ContentHash`), per the Decision above. No protocol beyond ordinary
  HTTP needed.
- **Fetch by reference, with random/seekable access** — plain `GET`
  against a content-addressed URL, using `ADR-031`'s existing HTTP
  Range-request support (RFC 7233). Also already fully solved, no new
  protocol.
- **Browse/list what's available for an entity** — a GraphQL query
  against the owning entity (`ADR-037`): `entity(id) { attachments {
  contentHash, filename, mimeType, sizeBytes } }`. GraphQL's own nested-
  field resolution already *is* the right standard for "list the
  children of a resource" — no separate browsing API, WebDAV or
  otherwise, needed for this either.
- **OS-native file-manager mounting** (Explorer/Finder browsing a
  WebDAV URL like a network drive) — the one access path that genuinely
  has no equivalent among what's already adopted. **Decided: skipped,
  not built.** [The WebDAV library comparison](../comparisons/webdav-library.md)
  found no clean library (NWebDav archived, `Dav.AspNetCore.Server`
  thin, IT Hit commercial), and — now that the other three access
  paths are confirmed already served without it — this was only ever a
  "nice to have" mounting convenience, not a real gap. Explicitly not
  hand-rolled either: implementing RFC 4918 correctly (locking
  semantics, the XML property model, every required method) from
  scratch to avoid a library trade-off would cost more than the
  convenience is worth for a capability nothing else in this design
  depends on. Revisit only if a real, specific need for native
  mounting shows up — not preemptively, and not by default.

- **A virtual hierarchy, not a real folder tree.** Attachments are
  content-addressed (above), not stored at a path — WebDAV's collection/
  resource model is *projected* on top: `/dav/{appId}/{entityType}/
  {entityId}/` as a collection, with each linked `AttachmentRef` appearing
  as a resource named from its declared filename (falling back to
  `ContentHash` if none was given). `PUT` to a path is the WebDAV-native
  way to upload — internally it's still the same content-addressed store
  and the same `AttachmentRef` linking `ADR-032`'s Decision already
  established, just reached through a familiar file-manager UX instead
  of a raw HTTP API call.
- **`GET` reuses `ADR-031`'s Range-request support** — the same
  seekable-retrieval reasoning applies to a large PDF or image as to a
  media chunk; not a second mechanism.
- **Locking (`LOCK`/`UNLOCK`) is not implemented in v1** — attachments are
  never mutated once uploaded (same "never delete, only append" posture
  as everything else in this design), so WebDAV's lost-update-prevention
  machinery has no real conflict to guard against here; a client that
  sends `LOCK` gets a straightforward, uncontested grant, not real
  cooperative locking semantics.

**A true OS-level virtual file system (mount attachments as a drive that
looks and feels like OneDrive/Google Drive/iCloud — on-demand hydration,
placeholder files, offline sync) is a genuinely different, heavier
technology** (Windows Cloud Filter API, macOS File Provider extension) —
platform-specific client shell integration, not a server API. This is
explicitly **out of the core engine's scope**, consistent with `ADR-030`:
it would live in a client project (`ADR-039`'s MVVM client is the
natural home), built *on top of* the WebDAV surface above rather than
replacing it — noted here as a real, desirable extension point, not
designed further here.

## Standalone attachments and direct permissions

An attachment doesn't have to be linked to an event or entity at all —
`AttachmentRef.EntityId`/`EventId` are both optional. A work guide, a
form template, an instruction manual: real, common cases where a
document is a first-class thing in its own right, not supporting
material for some other business fact. For these, "inherit the linked
event's `RequiredReadClaim`" (above) has nothing to inherit *from* —
**an attachment (or a registered attachment *type*, the same way an
event type declares its own claims) can carry a direct
`RequiredReadClaim`/`RequiredPublishClaim` of its own**, reusing
`ADR-008`'s exact claim-string shape rather than inventing a second
claims model. Precedence, stated explicitly rather than left implicit:
a direct claim on the attachment always governs if set (even when a
link exists — a public instruction manual attached to a sensitive
event, or the reverse, are both real cases); absent a direct claim, an
attachment linked to an event/entity inherits that link's claim; absent
both, no additional restriction beyond normal auth — the same
"`null` = unrestricted" default `ADR-008`'s claims already use
everywhere else.

Consequences:
- Deduplication by `ContentHash` means an attachment is many-to-many with
  events/entities by construction, not by extra design effort — the same
  bytes referenced from two different entities is just two `AttachmentRef`
  rows pointing at one stored object, not two copies.
- No masking (`ADR-009`) or upcasting (`ADR-018`) applies to attachment
  bytes — those are payload-shape concerns; an attachment is opaque
  content with a `MimeType`, not a schema-validated structure. Access
  control is scope/claim-based only (a new `attachments:read` scope,
  `ADR-006`'s pattern, plus optionally reusing an owning event's
  `RequiredReadClaim` if the attachment should inherit its linked event's
  visibility — not designed further here).
- Like `ADR-031`'s streaming channels, this is a second (now third)
  storage concern living alongside the event log rather than inside it —
  a deliberate, repeated pattern in this design now: keep the event log
  fast and simple for what it's good at, and give genuinely different
  data shapes (signals, media, blobs) their own purpose-built, clearly
  linked, out-of-band home rather than stretching one table to fit
  everything.
- **Hot/cool/cold storage tiering is a real cost-reduction opportunity
  here — and specifically here, not for the Event Log/Entity Store** —
  raised this session: a structured, small `StoredEvent` row has no
  natural "temperature" (access pattern doesn't vary enough by age to
  matter); a large binary attachment genuinely does. **Resolved as a
  real, pluggable mechanism, not left as a pure deployment-config
  afterthought**: `IAttachmentContentStore` is a new keyed, multi-backend
  seam — the exact same Strategy-pattern/keyed-DI shape `ADR-057`'s
  `IErasureKeyStore` already established, applied here to content
  storage instead of encryption keys. Multiple backends (Azure Blob
  Hot/Cool/Cold/Archive tiers, S3 Standard/Infrequent-Access/Glacier, a
  local dev store) can be registered simultaneously, each under its own
  key. `Attachment` gains two fields alongside the stable, unaffected
  `ContentHash`: **`ContentProviderKey`** (which registered backend
  currently holds the bytes) and **`ContentProviderRef`** (an opaque,
  provider-specific locator — a blob path/key within that backend).
  `ContentHash` stays the one stable, content-addressed identity a
  caller ever references; `ContentProviderKey`/`ContentProviderRef` are
  purely internal routing info the read path uses to fetch from the
  right backend, invisible to any consumer. **A background mover**,
  the same "internal follower" shape `ADR-007`/`ADR-015`/`ADR-027`/
  `ADR-031`'s derivation/materialization/projection workers already use
  — reads attachments past a configurable age/access-pattern threshold
  and re-writes them to a colder-tier backend, updating
  `ContentProviderKey`/`ContentProviderRef` once the move completes. No
  new mechanism family invented — a keyed-DI seam plus a background
  follower, both already-established shapes, applied to a new resource
  type. What *stays* a deployment/policy choice, consistent with
  `ADR-056`'s retention-window treatment: which backends are registered,
  and the actual age/access-pattern threshold that triggers a move.
- **Content-defined chunking + per-chunk fingerprinting, for large
  attachments specifically — real prior art checked before designing,
  not invented.** `ContentHash` alone dedups a whole attachment against
  an identical one; it does nothing for two large attachments that
  mostly overlap, or for a peer/offline client that already has most of
  an attachment and only needs the changed part. Verified real
  precedent: restic, Borg Backup, and casync all converge on the same
  shape — a rolling hash (Buzhash or a Rabin fingerprint) finds
  **content-defined** chunk boundaries (so an edit shifts only nearby
  chunks, not everything downstream, unlike fixed-size blocking), and
  each resulting chunk gets its own cryptographic fingerprint (SHA-256
  in restic/casync — the same primitive this design already uses
  everywhere else). Checked and deliberately **not** adopting
  BitTorrent's fixed-size-piece approach instead: fixed pieces only work
  well when both sides already agree on identical byte offsets, and
  don't survive insertions/deletions without re-hashing everything
  after the edit point — exactly the problem content-defined chunking
  exists to avoid. **`Attachment` gains an optional `ChunkIndex`** — a
  linear list of `(ChunkHash, Offset, Length)` tuples, the same naming
  shape casync's own chunk index uses — populated only above a
  configurable size threshold (chunking a small scanned form or photo
  is pure overhead with no benefit; restic/Borg/casync all chunk
  everything by default because backup archives are typically large,
  which doesn't hold for every attachment here). Two real benefits,
  both already-needed capabilities gaining a mechanism rather than a
  new requirement invented to justify one: **sub-file dedup** (two
  large attachments sharing a common section store the shared chunks
  once, extending `ContentHash`'s whole-object dedup down to chunk
  granularity) and **partial sync** — `ADR-033`'s peer-sync mesh and
  `ADR-069`'s offline-client reconnect scenario can diff two
  `ChunkIndex`es and transfer only chunks the destination doesn't
  already have, rather than the whole object again, with a natural
  resume point if a transfer is interrupted mid-way. `ADR-031`'s
  `Media` channels are a natural second beneficiary of the identical
  mechanism (already delivered in codec-framed chunks, just not yet
  individually fingerprinted/indexed) — not designed further here,
  flagged as the same opportunity, not duplicated into a second
  mechanism.

**Compliance note** (a proving-ground compliance review, this session):
content-addressing gives this mechanism real evidentiary value beyond
convenience — `ContentHash` is itself a tamper-evidence proof for
biobanking's specimen imaging/lab reports (`ISO 20387`) and digital
forensics' evidence attachments (`ISO/IEC 27037`, US Federal Rules of
Evidence 901/902) without any extra mechanism — the content hash a
verifier recomputes either matches what was originally stored or it
doesn't.
