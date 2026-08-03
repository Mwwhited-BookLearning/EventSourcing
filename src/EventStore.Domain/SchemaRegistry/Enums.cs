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
