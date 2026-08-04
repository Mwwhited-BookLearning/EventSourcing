[← Data model index](../02-data-model.md)

# Event Log

```csharp
public class StoredEvent
{
    public long SequenceNumber { get; set; }   // global monotonic order, identity column -- ARRIVAL order at this store, not logical order (ADR-029)
    public string? OriginId { get; set; }       // which site/peer this event originated at, in a multi-site mesh -- null/local-site-implied for a single-site deployment (ADR-033; propagated to this file, ADR-090). NOT related to TelemetryChannel.Origin (raw-source-vs-derived, ADR-031, docs/data/streaming-and-attachments.md) -- both use the word "Origin" for unrelated concepts; disambiguated explicitly, not renamed
    public string? LogicalClock { get; set; }   // hybrid logical clock value assigned at the origin site, for cross-site ordering (ADR-033; propagated to this file, ADR-090)
    public Guid EventId { get; set; }          // unique — client-supplied for idempotent retries, or server-generated (ADR-011); plays the "CorrelationId" role too
    public string EntityId { get; set; } = default!;   // {appId}:{entityType}:{uniqueId} — required (ADR-021); supersedes the old optional StreamId
    public string EventType { get; set; } = default!;  // normalized lowercase
    public int SchemaVersion { get; set; }
    public EventKind EventKind { get; set; } = EventKind.Original; // Original | UpcastMaterialization (ADR-027)
    public Guid? MaterializationOfEventId { get; set; } // set only when EventKind = UpcastMaterialization — the original's EventId (ADR-027)
    public long? ExpectedVersion { get; set; }          // Entity Store Version this patch was based on — optional, enables conflict detection (ADR-024)
    public string Payload { get; set; } = default!;    // JSON text; known properties typed, unknown routed to Extensions at fold time (ADR-022)
    public string PayloadHash { get; set; } = default!; // hash of {EventType, Payload, sorted parentEventIds} -- ADR-011
    public string ChainHash { get; set; } = default!;    // SHA-256(prior ChainHash || PayloadHash || SequenceNumber) -- ADR-019
    public string Status { get; set; } = default!;      // received | processing | applied | rejected — transport-level only (ADR-023)
    public string? SchemaStatus { get; set; }           // unknown | invalid | conformant — advisory, never gates Status (ADR-023)
    public bool ConflictFlag { get; set; }              // set by the fold step if a concurrent conflicting patch was detected (ADR-024)
    public bool LateArrivalFlag { get; set; }           // set by the fold step if OccurredAt was behind the entity/property's high-water mark (ADR-029)
    public DateTimeOffset OccurredAt { get; set; }      // CLIENT-DECLARED logical occurrence time, not server receipt time (ADR-029) — load-bearing for fold order
    public string ActorId { get; set; } = default!;      // verified caller identity (sub, or iss+sub per ADR-047) -- ALWAYS populated, blocking, not advisory (ADR-064) -- distinct from AttestedActorId below
    public string? AttestedActorId { get; set; }        // self-attested submitter identity — advisory, never gates Status (ADR-035) -- a CLAIM, not a verified fact; never conflated with ActorId above
    public string? AttestedClaims { get; set; }          // JSON — structured capability/delegation claims (e.g. a UCAN invocation, ADR-036); references the attestation schema-registry entry
    public string AuthorityStatus { get; set; } = "accepted"; // unattested | pending_review | accepted | rejected — advisory trust axis, independent of SchemaStatus (ADR-035). Defaults to "accepted" for an ordinary authenticated publish (ADR-006 already verified identity/permission); only starts unattested/pending_review when the publish itself declares AttestedClaims or an explicit review-pending marker (ADR-042)
    public Guid? AuthorityDecisionRef { get; set; }      // denormalized back-pointer to the authorityDecision event that last set AuthorityStatus, set by the fold step (ADR-035)
    public string? TelemetryPointer { get; set; }        // JSON-serialized List<TelemetryPointerEntry> (ADR-081, generalized from a single object) -- {ChannelId, ThreadId?, FromTimestamp, ToTimestamp?} per entry; one entry for an ordinary single-channel detection, multiple for a correlated multi-channel one. A distinct envelope field from parentEventIds/MaterializationOfEventId/AttachmentRef (ADR-031)
    public Signature? Signature { get; set; }             // set only when EventTypeDefinition.RequiredSignature is configured -- a sixth distinct relationship-shaped envelope field (ADR-066)
    public long? OriginalSequenceNumber { get; set; }      // set only on an event imported via ADR-068's lineage-export bundle format -- this environment's own SequenceNumber/ChainHash above are freshly computed (it IS a new append here); these three fields record provenance, never presented as if organically published here (ADR-068)
    public string? OriginalChainHash { get; set; }         // the exporting environment's own ChainHash for this event, at export time (ADR-068)
    public string? ImportedFrom { get; set; }              // identifies the exporting environment (ADR-068) -- a seventh distinct relationship-shaped envelope field, answering "where did this event actually originate" as opposed to OriginId (ADR-033/090, which peer/site in THIS deployment's own multi-site mesh)
    public Guid? RespondsToEventId { get; set; }            // optional on any publish -- the EventId this event is a reply to (Correlation Identifier pattern, Hohpe & Woolf). An eighth distinct relationship-shaped envelope field, answering "which prior event does this one satisfy a declared response expectation for" -- not existence-validated at publish time, unlike parentEventIds (ADR-094)
}

// Left behind in the primary table when a segment of StoredEvent rows is
// detached/archived to an externalized IAttachmentContentStore backend
// (ADR-089) -- lets ongoing chain verification for events appended after
// the archived segment proceed without ever touching archived data.
public class ChainCheckpoint
{
    public long SequenceNumberRangeStart { get; set; }
    public long SequenceNumberRangeEnd { get; set; }
    public string ChainHashAtRangeEnd { get; set; } = default!;
    public string ContentProviderKey { get; set; } = default!; // which registered IAttachmentContentStore backend holds the archived segment (ADR-032)
    public string ContentProviderRef { get; set; } = default!; // opaque, provider-specific locator for the segment's NDJSON blob
}

public class Signature
{
    public string SignerId { get; set; } = default!;  // denormalized copy of ActorId above, kept explicit rather than implied (ADR-066)
    public DateTimeOffset SignedAt { get; set; }
    public string Meaning { get; set; } = default!;    // required -- e.g. "reviewed", "approved", "authorship" (21 CFR Part 11 §11.50)
    public string Acr { get; set; } = default!;         // the acr claim the signing token actually carried, for audit
    public byte[]? RFC3161Timestamp { get; set; }        // optional -- a TSA's TimeStampToken over this event's ChainHash, independently verifiable proof this signature existed at or before a given time (ADR-086); enabled per event type alongside RequiredSignature, not global
}

public enum EventKind
{
    Original,             // every event published today — subject to normal fold
    UpcastMaterialization  // a persisted upcast result (ADR-027) — never folded; a parallel, optional-to-consume record
}

public class EventParent
{
    public Guid ChildEventId { get; set; }   // always resolves to a StoredEvent — the child is being inserted in the same publish
    public Guid ParentEventId { get; set; }  // may NOT resolve to a StoredEvent if the child's event type is Permissive
}
```

## Event lineage (parent/child DAG)

An event may declare one or more **parent events** — of any event type — that
it is causally derived from. This is envelope metadata, recorded in
`EventParents`, and is deliberately kept out of `Payload`: it is never part of
the registered JSON Schema, so it can't collide with schema validation or
`additionalProperties` rules.

- `parentEventIds` is optional on publish. Omitted or empty means an **origin
  event** with no parents.
- Whether a referenced parent must already exist is controlled per event type
  by `EventTypeDefinition.ParentValidationMode`, set at schema registration
  (default `Strict`).
- Under `Strict`, combined with the append-only, monotonically increasing
  `SequenceNumber`, the parent graph is **acyclic by construction**: a parent
  must already have a lower `SequenceNumber` than any child referencing it.
- Under `Permissive`, that guarantee does not hold: event A can be published
  referencing a not-yet-existing event X as a parent, and X can later be
  published referencing A as *its* parent (A already exists by then, so this
  passes validation even under Strict). The result is a 2-cycle. Any code that
  walks the DAG (see `../03-api-contracts.md`, Lineage API) must be cycle-safe
  unconditionally — it cannot assume acyclicity just because most event types
  use `Strict`. See `ADR-005`.
- `EventParents` is also the mechanism a future derived/materialized event
  type (deferred — see `ADR-007`) would use to record which source events it
  was computed from: no schema change would be needed here to support that
  later. It is deliberately **not** reused for the original→materialization
  link `ADR-027` introduces (`MaterializationOfEventId` is its own field) —
  lineage answers "what is this causally derived from," a different question
  from "what is this a re-shaped copy of."

## Expected-response tracking (`ADR-094`)

`RespondsToEventId` is envelope metadata, kept out of `Payload`, the same
reasoning `ADR-005` established for `parentEventIds`. Any publish may set
it, naming the `EventId` of the event this one is a reply to — it is
**not** existence-validated at publish time (no `ParentValidationMode`-
style Strict/Permissive fork), so a `RespondsToEventId` naming an
`EventId` that doesn't resolve is simply a response correlating to
nothing findable, never a rejected publish.

Setting `RespondsToEventId` alone does nothing beyond record the
relationship — tracking only activates when the *request* event's own
type declares `EventTypeDefinition.ExpectedResponse` (`schema-
registry.md`). `ExpectedResponseTracker` (`schema-registry.md`) is the
durable state a background `ExpectedResponseWatcher` maintains per
tracked request event, and the reserved `ExpectedResponseMissing` event
is what gets published, through the ordinary publish path, if a matching
response doesn't arrive within the declared window — see `ADR-094` for
the full mechanism. `ExpectedResponseMissing` itself sets
`RespondsToEventId` back at the original request, so a request event's
children, its actual response (if any), and its missing-response
escalation (if any) all resolve through this one field rather than a
second mechanism.

## Publish idempotency (`ADR-011`)

`PayloadHash` (a hash of `{EventType, Payload, sorted parentEventIds}`) is
stored on every `StoredEvent`, whether or not the publisher supplied their
own `eventId`. It's only ever consulted after the existing unique index on
`EventId` finds a match — a publisher who supplies the same `eventId`
again with an identical hash gets the original response replayed with no
new write; a matching `eventId` with a *different* hash is `409 Conflict`.
A publisher who never supplies `eventId` gets no idempotency guarantee, by
design — this is opt-in, not automatic dedup by content alone (two
genuinely different events that happen to share identical content would
otherwise be wrongly merged).

## Tamper evidence (`ADR-019`)

`ChainHash` is a *different* guarantee from `PayloadHash` above, computed
from it: every `StoredEvent` chains its `PayloadHash` and `SequenceNumber`
onto the immediately preceding event's `ChainHash`, so altering any past
row breaks every `ChainHash` after it — detectable by replaying the chain
from `SequenceNumber = 1`, not just comparing one row to itself. See
`ADR-019` for why this is a linear chain, not a full Merkle tree.

## Event upcasting (`ADR-018`) and materialization (`ADR-027`)

`StoredEvent.SchemaVersion` (above) is what makes a version-spanning
`mode=replay` (`ADR-010`) possible to reconcile at read time: an optional
`upcastFromPrevious` expression list, set per version
(`>= 2`), reshapes an old-shaped payload forward, version by version, so
every consumer sees the current shape regardless of which version
originally validated a given row. Originally specified as an OData
`compute()` expression list (`ADR-018`); `ADR-037` moved this off OData
entirely, and `ADR-053` made the evaluator itself pluggable behind
`IUpcastExpressionEvaluator`, CEL by default. Deliberately not a general
transform language — an upcast mapping is always many-source-fields-to-one-
destination-field or one-to-one, never one-to-many or many-to-many, so
this expression-list shape is already sufficient. `Payload`
itself is never rewritten — see `ADR-018` and `../06-solution-structure.md`
for the transform mechanics.

`EventKind`/`MaterializationOfEventId` (above) exist because that upcast
result no longer has to be recomputed on every read: once a lagging
publish's live validation succeeds (`ADR-020`) or a background
reconciler catches up an older backlog, the upcasted result is persisted
as its own `UpcastMaterialization` event — but it is **never folded**;
the fold step (`../02-data-model.md`'s Entity Store) continues to consume
only `Original` events, applying `UpcastChain` live exactly as `ADR-018`
already specifies. See `ADR-027` for why this split is what keeps the
design safe from double-applying the same logical change.

## Downcast on retrieval (`ADR-028`)

The reverse direction — current data, served in an older shape a
specific consumer still expects — is deliberately **not** stored here at
all. `downcastToPrevious` (on `EventTypeDefinition`, see
`schema-registry.md`) is applied read-time only, walked backward hop by
hop from an entity's actual current shape, only when a consumer
explicitly asks for an older version. Unlike upcasting, there is no
bounded "the" target to materialize — see `ADR-028` for why persisting it
would be unbounded, wasted work.

## Publish-time upcast validation and the reserved `EventUpcastFailed` type (`ADR-020`)

Publish now declares a required `SchemaVersion` up front (not implicitly
"whichever is active") and, if that version is behind the active one, is
upcast-validated live against the caller's real payload before responding.
On failure, `EventUpcastFailed` — the first event type in this design an
operator never registers, reserved at the platform level rather than via
`PUT /registry/{event-type}` — is stored in the original event's place,
carrying the original `EventType`/`SchemaVersion`/`Payload` verbatim plus
which upcast hop failed and why. See `ADR-020` for the full mechanics.
