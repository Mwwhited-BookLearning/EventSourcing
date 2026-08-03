namespace EventStore.Domain.SchemaRegistry;

// One $select mapping: an output field in the derived type's own Payload,
// sourced from one declared source's field (ADR-007). Also drives the
// auto-composed JsonSchema at registration.
public class SelectField
{
    public string OutputField { get; set; } = default!;
    public string SourceType { get; set; } = default!;
    public string SourceField { get; set; } = default!;
}
