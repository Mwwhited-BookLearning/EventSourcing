using System.Security.Claims;
using System.Text.Json.Nodes;
using EventStore.Abstractions;
using EventStore.Domain.EventLog;
using EventStore.Domain.SchemaRegistry;
using EventStore.Masking;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;

namespace EventStore.LineageExport;

public enum LineageExportRootCheck { NotFound, Forbidden, Ok }

// ADR-068 -- lineage-scoped event export, walking the SAME
// IEventLineageQueryProvider/CycleGuard traversal machinery event-chains.md
// already documents, through the exact same RequiredClaims/masking/read-
// audit enforcement any other read goes through. No bypass, no new
// authorization primitive -- this class only ever reads what the caller
// could already see via the ordinary Lineage API, one call at a time.
public class LineageExportService(
    EventStoreContext db, IEventLineageQueryProvider lineageQueryProvider, SchemaRegistryService schemaRegistry, IPayloadMasker payloadMasker,
    // ADR-086 -- optional: not every deployment configures a TSA. Unlike
    // Signature's per-event-type opt-in, an export always gets timestamped
    // when a TSA IS configured (ADR-086's own Decision text names no
    // separate opt-in for this consumer).
    ITimestampAuthorityClient? timestampAuthorityClient = null)
{
    // Unlike event-chains.md's own per-EventId Lineage API, ADR-068's export
    // starts from an EntityId -- an entity can fold from more than one event
    // over its lifetime, so every event directly stamped with this EntityId
    // is a root, not just the first. "Root not visible" means NONE of them
    // are -- the same all-or-nothing framing the ADR's own sequence diagram
    // uses ("the starting entity's own root event(s) visible to caller?").
    public async Task<LineageExportRootCheck> CheckRootAsync(string entityId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var rootEvents = await db.Events.AsNoTracking().Where(e => e.EntityId == entityId).ToListAsync(ct);
        if (rootEvents.Count == 0)
            return LineageExportRootCheck.NotFound;

        var claimsByType = await schemaRegistry.GetActiveClaimsByNamesAsync(rootEvents.Select(e => e.EventType).Distinct().ToList(), ct);
        var anyVisible = rootEvents.Any(e => IsVisible(e.EventType, claimsByType, user));
        return anyVisible ? LineageExportRootCheck.Ok : LineageExportRootCheck.Forbidden;
    }

    public async Task<LineageExportBundle> ExportAsync(string entityId, ClaimsPrincipal user, string exportedByActorId, CancellationToken ct = default)
    {
        var rootEvents = await db.Events.AsNoTracking().Where(e => e.EntityId == entityId).ToListAsync(ct);

        // Union the ancestor/descendant closure of EVERY root event stamped
        // with this EntityId -- one entity, potentially several independent
        // publish events over its lifetime, all treated as starting points
        // for the SAME traversal (ADR-005's existing cycle-safe machinery,
        // unchanged, called once per root).
        var closureIds = new HashSet<Guid>(rootEvents.Select(e => e.EventId));
        foreach (var root in rootEvents)
        {
            closureIds.UnionWith(await lineageQueryProvider.GetAncestorEventIdsAsync(db, root.EventId, ct));
            closureIds.UnionWith(await lineageQueryProvider.GetDescendantEventIdsAsync(db, root.EventId, ct));
        }

        var candidates = await db.Events.AsNoTracking()
            .Where(e => closureIds.Contains(e.EventId))
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);
        var claimsByType = await schemaRegistry.GetActiveClaimsByNamesAsync(candidates.Select(e => e.EventType).Distinct().ToList(), ct);

        // A node the caller can't see is dropped from the export set entirely
        // -- the same "never expand past a restricted node" rule event-chains.md's
        // own Lineage API already applies (ADR-008); here it also means the
        // restricted event's OWN payload never appears in the bundle at all,
        // not merely masked.
        var visibleEvents = candidates.Where(e => IsVisible(e.EventType, claimsByType, user)).ToList();

        var exportedLines = new List<ExportedEventLine>();
        var referencedDefinitions = new SortedSet<string>();
        foreach (var ev in visibleEvents)
        {
            var maskedPayload = await MaskPayloadAsync(ev, user, ct);
            exportedLines.Add(new ExportedEventLine(
                ev.EventId, ev.AppId, ev.EntityId, ev.EventType, ev.SchemaVersion, ev.SequenceNumber,
                ev.ChainHash, ev.PayloadHash, maskedPayload, ev.OccurredAt, ev.LateArrivalFlag));
            referencedDefinitions.Add($"{ev.AppId}/{ev.EventType}/v{ev.SchemaVersion}");
        }

        var exportedAt = DateTimeOffset.UtcNow;
        var manifestHash = ManifestHash.Compute(exportedLines.Select(e => e.ChainHash), exportedByActorId, exportedAt);
        // ADR-086 -- "an RFC 3161 timestamp over its own manifest hash":
        // ManifestHash is already a SHA-256 hex digest, so its hex-decoded
        // bytes are submitted AS the RFC 3161 message imprint directly
        // (algorithm SHA-256), not re-hashed a second time -- the literal
        // opposite of PublishService's "a hash OF ChainHash" wording for
        // Signature, which explicitly asks for a second hash. Null when no
        // TSA is configured -- not every deployment needs this, and this
        // was correctly left null until this item existed to populate it.
        string? rfc3161Timestamp = null;
        if (timestampAuthorityClient is not null)
        {
            var manifestHashBytes = Convert.FromHexString(manifestHash);
            var tokenBytes = await timestampAuthorityClient.TimestampHashAsync(manifestHashBytes, ct);
            rfc3161Timestamp = Convert.ToBase64String(tokenBytes);
        }
        var manifest = new ExportManifest(entityId, referencedDefinitions.ToList(), manifestHash, exportedByActorId, exportedAt, FrameworkVersion.Current, rfc3161Timestamp);

        return new LineageExportBundle(manifest, exportedLines);
    }

    // ADR-068 -- import preserves provenance rather than presenting a copy
    // as organic: every event gets a FRESH SequenceNumber/ChainHash in this
    // environment's own log (it IS a new append here), while
    // OriginalSequenceNumber/OriginalChainHash/ImportedFrom travel as new
    // envelope metadata recording where it actually came from. Manifest
    // hash is reverified from the bundle's OWN contents before any write --
    // a tampered or truncated bundle is rejected outright, nothing partially
    // applied.
    public async Task<int> ImportAsync(LineageExportBundle bundle, string importedFrom, CancellationToken ct = default)
    {
        var recomputedHash = ManifestHash.Compute(bundle.Events.Select(e => e.ChainHash), bundle.Manifest.ExportedByActorId, bundle.Manifest.ExportedAt);
        if (recomputedHash != bundle.Manifest.ManifestHash)
            throw new InvalidOperationException("manifest hash does not match the bundle's own contents -- rejected before any write");

        var ordered = bundle.Events.OrderBy(e => e.SequenceNumber).ToList();
        foreach (var line in ordered)
        {
            var storedEvent = new StoredEvent
            {
                EventId = line.EventId,
                AppId = line.AppId,
                EntityId = line.EntityId,
                EventType = line.EventType,
                SchemaVersion = line.SchemaVersion,
                Payload = line.Payload, // exactly as exported -- masked/erased branches included verbatim (ADR-068's own "never re-masked on import" rule)
                PayloadHash = line.PayloadHash, // the ORIGINAL hash, preserved -- this environment's own ChainHash below is freshly computed by EventAppender, chained onto ITS OWN prior tail (it IS a new append here)
                ChainHash = "", // computed by EventAppender, once this row's own SequenceNumber is known
                Status = "received", // Router's own next tick folds it, exactly like any ordinary publish -- ADR-068 doesn't ask for a bespoke fold path
                OccurredAt = line.OccurredAt,
                LateArrivalFlag = line.LateArrivalFlag,
                ActorId = "system:lineage-import",
                OriginalSequenceNumber = line.SequenceNumber,
                OriginalChainHash = line.ChainHash,
                ImportedFrom = importedFrom,
            };
            await EventAppender.AppendAsync(db, storedEvent, [], ct);
        }

        return ordered.Count;
    }

    private static bool IsVisible(string eventType, IReadOnlyDictionary<string, IReadOnlyList<RequiredClaim>> claimsByType, ClaimsPrincipal user) =>
        !claimsByType.TryGetValue(eventType, out var claims) || RequiredClaimEvaluator.HasAny(claims, ClaimDirection.Read, user);

    private async Task<string> MaskPayloadAsync(StoredEvent ev, ClaimsPrincipal user, CancellationToken ct)
    {
        var definition = await schemaRegistry.GetVersionAsync(ev.AppId, ev.EventType, ev.SchemaVersion, ct);
        if (definition is null)
            return ev.Payload; // no declared schema to mask against -- nothing classified, render as-is

        var schemaNode = JsonNode.Parse(definition.JsonSchema);
        var payloadNode = JsonNode.Parse(ev.Payload);
        var masked = await payloadMasker.MaskAsync(schemaNode!, payloadNode, ev.EntityId, claim => RequiredClaimEvaluator.HasClaim(user, claim), ct);
        return masked?.ToJsonString() ?? "null";
    }
}
