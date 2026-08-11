namespace EventStore.LineageExport;

// ADR-068 -- a produced artifact, never a persisted table (docs/features/
// lineage-export-and-playback.md's own ER diagram). EventTypeDefinitionsReferenced
// is a plain list of "{AppId}/{EventType}/v{Version}" identifiers -- informational
// (so an operator importing into a fresh environment knows which schemas
// they need to register first), not full schema definitions; ADR-068's own
// "so an importing environment can validate/upcast correctly even if its own
// registry hasn't seen that version" is satisfied by the operator registering
// from this list before import, not by this manifest auto-registering anything.
public record ExportManifest(
    string EntityId,
    List<string> EventTypeDefinitionsReferenced,
    string ManifestHash,
    string ExportedByActorId,
    DateTimeOffset ExportedAt,
    string FrameworkVersion,
    // ADR-086 ("RFC 3161 Trusted Timestamping") landed as a later
    // build-plan item this one named as a dependency for -- populated by
    // LineageExportService.ExportAsync whenever an ITimestampAuthorityClient
    // is configured (base64 TimeStampToken bytes over this manifest's own
    // ManifestHash directly, no re-hash), null only when no TSA is
    // registered for this deployment.
    string? Rfc3161Timestamp = null);

// One line of the NDJSON bundle body (after the manifest line) -- the
// exported StoredEvent's envelope plus its ALREADY-MASKED payload (masking
// happened once, at export time, per ADR-068's own no-bypass rule -- never
// re-applied on import or by the offline player).
public record ExportedEventLine(
    Guid EventId,
    string AppId,
    string EntityId,
    string EventType,
    int SchemaVersion,
    long SequenceNumber,
    string ChainHash,
    string PayloadHash,
    string Payload,
    DateTimeOffset OccurredAt,
    bool LateArrivalFlag);

public record LineageExportBundle(ExportManifest Manifest, List<ExportedEventLine> Events)
{
    // "NDJSON of the exported StoredEvents... plus a manifest" (ADR-068) --
    // line 1 is the manifest, every subsequent line is one event, in
    // SequenceNumber order. Genuinely newline-delimited, not a nested JSON
    // array, matching the ADR's own wording literally.
    public string ToNdjson()
    {
        var lines = new List<string> { System.Text.Json.JsonSerializer.Serialize(Manifest) };
        lines.AddRange(Events.Select(e => System.Text.Json.JsonSerializer.Serialize(e)));
        return string.Join("\n", lines);
    }

    public static LineageExportBundle ParseNdjson(string ndjson)
    {
        var lines = ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            throw new FormatException("empty bundle");

        var manifest = System.Text.Json.JsonSerializer.Deserialize<ExportManifest>(lines[0])
            ?? throw new FormatException("bundle's first line is not a valid manifest");
        var events = lines.Skip(1)
            .Select(line => System.Text.Json.JsonSerializer.Deserialize<ExportedEventLine>(line) ?? throw new FormatException("bundle contains an invalid event line"))
            .ToList();
        return new LineageExportBundle(manifest, events);
    }
}
