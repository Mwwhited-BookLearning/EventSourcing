using System.Text.Json.Nodes;
using EventStore.Projections.Abstractions;

namespace EventStore.Flows;

// One IProjection<PendingTask> per registered FlowDefinition (ADR-101). The
// flow's own AST walk IS the Project() body -- no new hosting mechanism,
// this runs inside the existing ProjectionHost<PendingTask> unchanged.
public sealed class FlowProjection : IProjection<PendingTask>
{
    private readonly FlowDefinition _flow;
    private readonly FlowInterpreter _interpreter;

    public FlowProjection(FlowDefinition flow)
    {
        _flow = flow;
        _interpreter = new FlowInterpreter(flow.Actions, flow.Conditions);
        EventTypes = new[] { flow.RaiserEventType }.Concat(flow.CollectResolverEventTypes()).Distinct().ToArray();
    }

    public string Name => _flow.Name;

    public IReadOnlyCollection<string> EventTypes { get; }

    // Legacy 2-arg member of IProjection<T>; ProjectionHost always calls the
    // 3-arg eventId-aware overload below for a FlowProjection (see
    // ProjectionHost.ApplyAsync), so this is unreachable in practice --
    // implemented for interface completeness only, not silently omitted.
    public string GetKey(string eventType, JsonNode payload) =>
        throw new NotSupportedException($"{nameof(FlowProjection)} requires the eventId-aware GetKey overload.");

    public string GetKey(string eventType, Guid eventId, JsonNode payload)
    {
        if (eventType == _flow.RaiserEventType)
            return eventId.ToString();

        var task = _flow.FindTaskDeclarationFor(eventType);
        return payload[task.CorrelatedBy]?.GetValue<string>()
            ?? throw new InvalidOperationException(
                $"Resolver event \"{eventType}\" for flow \"{_flow.Name}\" is missing its correlation field \"{task.CorrelatedBy}\".");
    }

    // The raiser event keeps whatever ChangeKind it's really registered
    // with (defer to the real registration, null); every resolver type is
    // forced Partial here, without touching that type's own real, unrelated
    // registration (e.g. authorityDecision is registered Full for its own
    // entity-fold purpose in VitalsSharedTypes.cs/MeridianSharedTypes.cs --
    // reusing that verbatim for this correlation join would silently wipe
    // every raiser field this flow's conditions still need).
    public ChangeKind? OverrideChangeKind(string eventType) =>
        eventType == _flow.RaiserEventType ? null : ChangeKind.Partial;

    public PendingTask? Project(string key, JsonNode mergedState)
    {
        var state = mergedState as JsonObject
            ?? throw new InvalidOperationException($"{nameof(FlowProjection)} requires an object-shaped merged snapshot.");

        if (_interpreter.Evaluate(_flow.Ast, state) is not FlowPausedAtTask paused)
            return null; // no open task for this key right now -- delete the row if one exists

        var entityId = state[_flow.EntityIdField.TrimStart('$', '.')]?.GetValue<string>() ?? key;

        return new PendingTask
        {
            Key = key,
            FlowName = _flow.Name,
            Description = paused.Task.Description,
            RequiredClaim = paused.Task.RequiredClaim,
            TriggeringEventId = key,
            AppId = _flow.AppId,
            EntityId = entityId,
            RaisedAt = DateTimeOffset.UtcNow,
        };
    }
}
