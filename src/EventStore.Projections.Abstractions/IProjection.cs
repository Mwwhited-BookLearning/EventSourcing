using System.Text.Json.Nodes;

namespace EventStore.Projections.Abstractions;

// docs/09-cqrs-read-models.md's own sketch, verbatim -- a projection author
// never sees raw events, ChangeKind, or merge logic; ProjectionHost handles
// all of that before calling Project. This project has no dependency on
// anything else in this repo -- a projection author's only reference.
//
// ADR-101 added the three members below, as default-interface-method/
// nullable-widening additions -- source- and binary-compatible for the one
// existing implementer (OrderSummaryProjection, which never returns null and
// never needs the new overloads, so it keeps compiling and behaving
// identically).
public interface IProjection<TReadModel> where TReadModel : class
{
    string Name { get; }
    IReadOnlyCollection<string> EventTypes { get; }

    string GetKey(string eventType, JsonNode payload);

    // ADR-101: a projection whose key for one event type isn't derivable
    // from that event's own payload (e.g. a raiser event keyed by its own
    // EventId, since there's no other stable field a later resolver event
    // could correlate against) overrides this instead of the 2-arg member
    // above. Defaults to ignoring eventId entirely, i.e. every existing
    // projection's behavior is unchanged.
    string GetKey(string eventType, Guid eventId, JsonNode payload) => GetKey(eventType, payload);

    // ADR-101: lets a projection force a specific ChangeKind for one of its
    // own EventTypes without touching that event type's real, unrelated
    // global registration (e.g. EventStore.Flows.FlowProjection forces
    // Partial for a resolver event type like authorityDecision, which is
    // registered Full for its own entity-fold purpose elsewhere). Null
    // (the default) means "defer to the real registration," unchanged for
    // every existing projection.
    ChangeKind? OverrideChangeKind(string eventType) => null;

    // mergedState is the CURRENT, fully-merged snapshot for this key, after
    // ProjectionHost has already applied this event per its ChangeKind
    // (Full replace or Partial merge-patch, ADR-016) -- already done.
    // Nullable as of ADR-101: null means "no row for this key right now,
    // delete one if it exists" (EventStore.Flows.FlowProjection's own use --
    // a task that's been resolved has nothing to project anymore).
    // OrderSummaryProjection never returns null, so it is unaffected.
    TReadModel? Project(string key, JsonNode mergedState);
}
