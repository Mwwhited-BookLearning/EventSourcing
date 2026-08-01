[← ADR index](../07-adrs.md)

# ADR-089: Event Log/`AccessLog` archival — detach a verified segment to `ADR-032`'s existing pluggable content store, no new interface

Status: Accepted

Context: `docs/10-open-questions.md` row 18 asked for the actual
*mechanism* for archiving an ever-growing Event Log/`AccessLog` beyond
`ADR-056`'s deliberately-deferred retention-window/cadence *policy* —
table partitioning, or some way to detach a hash-chain segment without
breaking verification. Direct design conversation resolved this
session: **as long as the archived content and its reference are
externalized from the primary store and support multiple providers
simultaneously, which physical tier/backend holds it doesn't matter** —
which is exactly the shape `ADR-032`'s `IAttachmentContentStore` already
provides. No new interface needed.

Decision:
- **Detach a verified, contiguous segment of `StoredEvent` (or
  `AccessLogEntry`) rows once past `ADR-056`'s deployment-configured
  retention window** — serialized as an NDJSON blob (the identical
  export format `ADR-068`'s litigation export already uses, reused
  rather than inventing a second serialization), written to a
  registered `IAttachmentContentStore` backend under an ordinary
  `ContentProviderKey`/`ContentProviderRef` pair. **No new interface**:
  an archived segment is just bytes, the same as an attachment's content
  — `ADR-032`'s existing multi-backend, keyed-simultaneously shape
  already satisfies "support multiple providers at the same time," and
  which specific backend/tier a deployment points it at (blob storage,
  a separate archive database, cold object storage) is exactly as
  provider-driven and framework-agnostic as attachment tiering already
  is — this ADR doesn't pick one, deliberately.
- **Chain verification survives by leaving a small checkpoint record
  behind, not a placeholder per archived row.** Detaching a segment
  removes the bulky row data but leaves one checkpoint row in the
  primary table: `{SequenceNumberRangeStart, SequenceNumberRangeEnd,
  ChainHashAtRangeEnd, ContentProviderKey, ContentProviderRef}`.
  Ordinary, ongoing verification of events appended *after* the archived
  segment needs only this checkpoint's `ChainHashAtRangeEnd` — it never
  touches archived data, so archiving has zero cost on the live
  verification path.
- **Full re-verification of an archived segment stays possible on
  demand** — fetch the NDJSON blob from its recorded provider/ref,
  recompute `ADR-019`'s chain from the segment's own start, confirm it
  lands on the checkpoint's recorded `ChainHashAtRangeEnd`. Same
  verification logic already used for the live chain, applied to fetched
  archived bytes instead of live rows — no second verification
  algorithm.
- **Table partitioning is not the framework's decided mechanism — it's
  one valid, deployment-chosen way to *implement* a backend**, not a
  competing design. A deployment could build an `IAttachmentContentStore`
  backend that itself detaches a database partition and stores it as a
  file; that's an implementation detail of *that* provider, invisible to
  this ADR's own mechanism.
- **`AccessLog` (`ADR-045`) gets identical treatment** — its own,
  independent hash chain detaches the same way, into the same pluggable
  store, with its own checkpoint row.
- **`ADR-056` still owns *when*; this ADR owns *how*** — the retention
  window/cadence remains a deployment-configured policy decision,
  unchanged. This ADR only supplies the mechanism that policy triggers.

Consequences:
- A small `ChainCheckpoint`-shaped table/entity is new — `docs/data/
  event-log.md` and `docs/data/access-log.md` gain it in this same pass,
  per this project's data-model-ownership convention.
- No new extensibility interface, no new `docs/extensibility-points.md`
  row — a genuine reuse, not a new seam.
- Resolves `docs/10-open-questions.md` row 18.
