using EventStore.Domain.EventLog;

namespace EventStore.Archival;

// ADR-089 -- "the identical export format ADR-068's litigation export
// already uses, reused rather than inventing a second serialization."
// Literally reusing ADR-068's own ExportedEventLine/ExportManifest types
// (EventStore.LineageExport) doesn't fit here: that shape is EntityId-
// scoped (one manifest per single entity) and deliberately omits
// ParentEventIds/Signature (a litigation bundle carries the masked
// payload only, not envelope internals a viewer never needs) -- both of
// which THIS mechanism's own exit criterion ("re-verifying its own
// internal chain... confirms it's unaltered") genuinely needs to
// recompute PayloadHash/ChainHash exactly. So this reuses ADR-068's
// FORMAT CONVENTION (newline-delimited JSON, one record per line) with a
// new, sequence-range-scoped line shape complete enough for chain
// re-verification -- an honest partial reuse, not the ADR's literal
// types, per this repo's own "say when something is only partially
// borrowed" convention. ParentEventIds is carried explicitly here
// because EventParents' own live rows for an archived child are deleted
// as part of detaching it (ArchivalService) -- this is the only place
// that information survives afterward.
public record ArchivedEventLine(StoredEvent Event, List<Guid> ParentEventIds);

public record ArchivedEventLogBundle(List<ArchivedEventLine> Lines)
{
    public string ToNdjson() =>
        string.Join("\n", Lines.Select(l => System.Text.Json.JsonSerializer.Serialize(l)));

    public static ArchivedEventLogBundle ParseNdjson(string ndjson)
    {
        var lines = ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = lines
            .Select(line => System.Text.Json.JsonSerializer.Deserialize<ArchivedEventLine>(line) ?? throw new FormatException("archived segment bundle contains an invalid event line"))
            .ToList();
        return new ArchivedEventLogBundle(parsed);
    }
}

// Same "why not just reuse StoredEvent/an existing NDJSON type" reasoning
// as ArchivedEventLogBundle above, but for AccessLog's own independent
// chain -- AccessLogEntry (EventStore.Domain.AccessLog) already carries
// every field AccessLogEntryHash.Compute needs, with no separate
// parent-link table to fold in, so no wrapper type is needed here at
// all: the bundle is just a plain list of the entity itself.
public record ArchivedAccessLogBundle(List<Domain.AccessLog.AccessLogEntry> Lines)
{
    public string ToNdjson() =>
        string.Join("\n", Lines.Select(l => System.Text.Json.JsonSerializer.Serialize(l)));

    public static ArchivedAccessLogBundle ParseNdjson(string ndjson)
    {
        var lines = ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = lines
            .Select(line => System.Text.Json.JsonSerializer.Deserialize<Domain.AccessLog.AccessLogEntry>(line) ?? throw new FormatException("archived segment bundle contains an invalid access log entry line"))
            .ToList();
        return new ArchivedAccessLogBundle(parsed);
    }
}
