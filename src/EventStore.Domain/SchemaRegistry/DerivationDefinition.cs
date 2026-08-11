namespace EventStore.Domain.SchemaRegistry;

// Shape is the data-model authority: docs/data/schema-registry.md,
// "Derived/materialized event types (deferred, ADR-007)".
public class DerivationDefinition
{
    public string AppId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public List<string> Sources { get; set; } = new();
    public List<JoinCondition> JoinConditions { get; set; } = new();
    public List<SelectField> SelectFields { get; set; } = new();
    public JoinTriggerMode JoinTriggerMode { get; set; }
    public BackfillMode BackfillMode { get; set; }
    public bool BackfillThroughDerivedSources { get; set; }
    public TimeSpan PendingJoinTtl { get; set; }
    public int MaxHopCount { get; set; } = 5;
    public DateTimeOffset RegisteredAt { get; set; }
    public bool IsActive { get; set; }
}
