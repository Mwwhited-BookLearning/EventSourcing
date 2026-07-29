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

## Browsable access: WebDAV, not a bespoke file API

Rather than inventing a bespoke "list/browse my attachments" API, expose
the attachment store through **WebDAV** (RFC 4918 — `PROPFIND` for
listing, `GET`/`PUT` for content, `MKCOL`/`COPY`/`MOVE` for organizing),
concretely via [NWebDav](../libraries/dotnet/nwebdav.md) rather than a
hand-rolled protocol implementation —
a real, decades-old standard every major OS already speaks natively
(Windows Explorer, macOS Finder, and most file managers can mount a
WebDAV URL directly, no bespoke client needed). This is the same
"adopt a real standard instead of a bespoke one" instinct this whole
design already applies everywhere else (`references.md`).

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
it would live in a client project (`ADR-033`'s MVVM client, queued, is
the natural home), built *on top of* the WebDAV surface above rather than
replacing it — noted here as a real, desirable extension point, not
designed further until `ADR-033` exists to hang it on.

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
