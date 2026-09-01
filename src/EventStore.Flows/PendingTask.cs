namespace EventStore.Flows;

// The read model the whole "task list" feature is: "just a query... fed
// from events like everything else" (direct request, ADR-101) -- a row
// exists for exactly as long as FlowInterpreter's stateless AST walk
// currently reaches an unresolved task node for that key. No separate
// "flow instance" state exists anywhere; row absence IS "resolved."
public class PendingTask
{
    public string Key { get; set; } = default!;
    public string FlowName { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string? RequiredClaim { get; set; }
    public string TriggeringEventId { get; set; } = default!;
    public string AppId { get; set; } = default!;
    public string EntityId { get; set; } = default!;
    public DateTimeOffset RaisedAt { get; set; }
}
