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
