[← Data model index](../02-data-model.md)

# Access Log

A **sixth**, independent append-only store — not a materialized view of
anything, not derived from the Event Log, and deliberately not folded
into `EventStoreContext` or `ProjectionsDbContext`. This is the record
of *reads*, not writes: who accessed what, when, under what trust basis
(`ADR-045`).

```csharp
public class AccessLogEntry
{
    public long SequenceNumber { get; set; }             // own append-only sequence, independent of StoredEvent's
    public string ReaderActorId { get; set; } = default!;
    public string ReaderTrustBasis { get; set; } = default!; // "Authoritative" | "Attested" (ADR-045)
    public Guid? GrantRef { get; set; }                   // set when ReaderTrustBasis=Attested via an ADR-043 delegated grant specifically
    public string ViewAccessed { get; set; } = default!;   // "Authoritative" | "Live" (ADR-042) -- which of the two Entity Store views was read
    public string ResourceRef { get; set; } = default!;    // EntityId / AttachmentRef / channel+position / etc. -- deliberately a plain string, not a typed FK, since it can point at any of five different resource kinds
    public string Action { get; set; } = default!;         // "query" | "stream" | "download" | ...
    public DateTimeOffset AccessedAt { get; set; }
    public string ChainHash { get; set; } = default!;       // SHA-256(prior ChainHash || entry fields || SequenceNumber) -- ADR-019's primitive, its OWN independent chain
}
```

## Why this is its own store, not a row in an existing one

Same reasoning `ADR-031`/`ADR-032` already used for streaming
channels/attachments: a genuinely different volume profile (reads
vastly outnumber writes) and a different reader (an auditor, not a
domain consumer or a projection) earn a separate store, not a shared
one. Mixing high-frequency access records into `StoredEvent` would also
mean every business-event reader has to filter noise it never cares
about — the same argument that already keeps CQRS read models out of
`EventStoreContext` (`entity-store.md`).

## Tamper evidence — its own chain, not a shared one

`ChainHash` here uses the exact SHA-256 chaining formula `ADR-019`
already established for the Event Log, but computes an **independent**
chain — a different append source (every read endpoint, not
`EventAppender`) with no reason to couple its tamper-evidence to the
Event Log's own. Verifying `AccessLog`'s integrity means replaying
*this* chain from `SequenceNumber = 1`, separately from verifying the
Event Log's.

## Retention

Never deleted by default, consistent with this design's governing
"never lose or corrupt data" principle — see `ADR-045`'s citation of
HIPAA's six-year audit-log retention minimum as the kind of real-world
requirement this default already exceeds without a bespoke policy.

## What writes here

Every read surface: GraphQL queries against the authoritative Entity
Store or the Live View (`ADR-037`/`ADR-042`), WebDAV/attachment
retrieval (`ADR-032`), streaming channel playback (`ADR-031`), and
ticket-authenticated headerless access (`ADR-040`) — each calls the
access-logging step explicitly in its own composition (`ADR-041`), not
via a reflection-based interceptor.
