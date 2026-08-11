using System.Text.Json.Nodes;

namespace EventStore.Projections.Abstractions;

// docs/09-cqrs-read-models.md's own sketch, verbatim -- a projection author
// never sees raw events, ChangeKind, or merge logic; ProjectionHost handles
// all of that before calling Project. This project has no dependency on
// anything else in this repo -- a projection author's only reference.
public interface IProjection<TReadModel> where TReadModel : class
{
    string Name { get; }
    IReadOnlyCollection<string> EventTypes { get; }

    string GetKey(string eventType, JsonNode payload);

    // mergedState is the CURRENT, fully-merged snapshot for this key, after
    // ProjectionHost has already applied this event per its ChangeKind
    // (Full replace or Partial merge-patch, ADR-016) -- already done.
    TReadModel Project(string key, JsonNode mergedState);
}
