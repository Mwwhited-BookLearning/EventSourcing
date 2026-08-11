namespace EventStore.Domain.FeatureFlags;

// ADR-077 -- folded from the reserved FeatureFlagSet event type (ADR-067's
// control-plane-actions-as-reserved-events pattern), never written
// directly. See docs/data/schema-registry.md's own "Feature flag state"
// section for the full write/read-split reasoning.
public class FeatureFlagState
{
    public string AppId { get; set; } = default!;   // part of the composite key (ADR-030/ADR-075 -- one tenant's flags never affect another's)
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;    // JSON-encoded -- a flag value isn't always boolean
    public long LastAppliedSequenceNumber { get; set; } // watermark into the FeatureFlagSet event stream this row is folded from
}
