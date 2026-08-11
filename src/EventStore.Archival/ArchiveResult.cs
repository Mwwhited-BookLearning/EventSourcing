using EventStore.Domain.EventLog;

namespace EventStore.Archival;

public abstract record ArchiveResult
{
    public sealed record Archived(ChainCheckpoint Checkpoint) : ArchiveResult;

    // Nothing past the prior checkpoint (or SequenceNumber 1, for the very
    // first archival) exists up to throughSequenceNumber yet -- a no-op,
    // not an error.
    public sealed record NothingToArchive : ArchiveResult;

    // ADR-089's own "detach a VERIFIED... segment" -- never silently
    // archives a segment live verification has already found tampered.
    public sealed record SegmentNotVerified(long FirstDivergentSequenceNumber) : ArchiveResult;

    private ArchiveResult() { }
}
