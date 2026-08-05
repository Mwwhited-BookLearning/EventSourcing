namespace EventStore.Domain.AccessLog;

// ADR-045 -- a new, separate, append-only store: deliberately not mixed into
// StoredEvent/the Event Log (reads vastly outnumber writes, a different
// reader than a domain consumer), the same reasoning ADR-031/032 already
// apply to streaming channels/attachments. Its own independent hash chain
// (AccessLogEntryHash/AccessLogAppender), never coupled to the Event Log's.
public class AccessLogEntry
{
    public long SequenceNumber { get; set; } // own append-only sequence, independent of StoredEvent's
    public string ReaderActorId { get; set; } = default!;
    public string ReaderTrustBasis { get; set; } = default!; // "Authoritative" | "Attested"
    public Guid? GrantRef { get; set; } // set when ReaderTrustBasis=Attested via an ADR-043 delegated grant specifically
    public string ViewAccessed { get; set; } = default!; // "Authoritative" | "Live" (ADR-042) -- which of the two views was read
    public string ResourceRef { get; set; } = default!; // EntityId / AttachmentRef / channel+position / etc.
    public string Action { get; set; } = default!; // "query" | "stream" | "download" | "reveal" | ...
    public DateTimeOffset AccessedAt { get; set; }
    public string ChainHash { get; set; } = default!;
}
