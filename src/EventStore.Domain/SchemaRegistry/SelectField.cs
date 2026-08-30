namespace EventStore.Domain.SchemaRegistry;

// One $select mapping: an output field in the derived type's own Payload
// (ADR-007). Also drives the auto-composed JsonSchema at registration.
// Two mutually-exclusive shapes: a straight 1:1 rename/copy from one
// declared source's field (SourceType/SourceField set, Expression null),
// or a calculated field (Expression set, SourceType/SourceField null) --
// TODO.md's "Calculated fields" extension of this same mechanism, not a
// second parallel one. Expression is engine-agnostic text evaluated via
// the already-registered IUpcastExpressionEvaluator (ADR-053), the same
// seam UpcastFromPrevious expressions use, with "event" bound to an object
// keyed by each declared source's lowercased name (e.g.
// "event.orderline.Quantity * event.orderline.UnitPrice").
public class SelectField
{
    public string OutputField { get; set; } = default!;
    public string? SourceType { get; set; }
    public string? SourceField { get; set; }
    public string? Expression { get; set; }
}
