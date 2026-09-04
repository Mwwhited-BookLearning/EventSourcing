namespace EventStore.Domain.SchemaRegistry;

public enum ChangeKind
{
    Full,    // this event type's payload replaces everything known about its key
    Partial  // this event type's payload merges onto existing state (Optional<T>-wrapped per property, ADR-022)
}

public enum ParentValidationMode
{
    Strict,     // publish is rejected (400) if any parentEventId does not resolve to a stored event
    Permissive  // dangling/forward parentEventId references are accepted and stored as unresolved
}

public enum RejectionBehavior
{
    Annotate,   // default -- a rejected event stays as originally published, flagged via AuthorityStatus only (ADR-035)
    Compensate  // a rejected event triggers a compensating patch, per-type opt-in
}

public enum FilterableFieldType { String, Number, Boolean, DateTimeOffset }

// ADR-096 -- which mechanism a filter predicate against this field compiles
// to. PlaintextExpression is the default and every FilterableField
// registered before this ADR -- completely unchanged json_extract/->>/
// JSON_VALUE behavior. The other two only ever apply to a field whose
// schema also declares x-masking-searchable. A third kind, OrderRevealing
// (ADR-097), was built, benchmarked, and removed as an adopted feature,
// 2026-09-04 -- see ADR-097's own additive note; the real, working
// implementation lives on as a reference in
// spikes/order-revealing-encryption/, not wired into this enum anymore.
public enum FilterableFieldIndexKind
{
    PlaintextExpression,   // default -- today's json_extract/->>/JSON_VALUE mechanism, unchanged
    EncryptedBlindIndex,   // ADR-096 -- eq comparisons route to EncryptedFieldIndexEntry.Token
    EncryptedRangeBucket,  // ADR-096 -- gt/gte/lt/lte comparisons narrow via EncryptedFieldIndexEntry.Token bucket lookups, then an exact decrypt-and-compare step (IEncryptedPredicateEvaluator, ADR-098)
}

// ADR-096 -- x-masking-searchable's own indexKind value, distinct from
// FilterableFieldIndexKind: this is what a schema author declares; that
// enum is what SchemaRegistryService derives it into for query routing.
public enum SearchableIndexKind { Equality, Range }

// ADR-096 -- Shared: one HMAC key per (AppId, EventTypeName, FieldJsonPath),
// enables real cross-entity search, cleaned up by deleting index rows on
// erasure. PerEntity: token derived from the entity's own DEK, destroyed
// automatically alongside it (true crypto-shredding), but only ever
// answers "does this one known entity have value V."
public enum SearchIndexKeyScope { Shared, PerEntity }

// ADR-096 -- required on a Range-kind field; drives the cardinality-aware
// registration guardrail (a Low-cardinality classified field needs an
// explicit acknowledgeLeakageRisk; a High-cardinality one doesn't).
public enum FieldCardinality { Low, High }

public enum JoinTriggerMode
{
    FireOnce,             // wait for one event per source per join key, emit once, key closes (ADR-007)
    ContinuousEnrichment  // any new arrival on any source re-emits, joined against the current latest state of the others (ADR-007)
}

public enum BackfillMode
{
    FromHistory, // the derivation worker starts by tailing each source from SequenceNumber 0
    FromNow      // the derivation worker starts tailing each source from its SequenceNumber as of registration
}
