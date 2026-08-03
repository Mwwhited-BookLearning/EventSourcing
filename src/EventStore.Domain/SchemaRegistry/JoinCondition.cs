namespace EventStore.Domain.SchemaRegistry;

// One conjunct of $on's pairwise-equality mini-grammar (ADR-007).
// LeftSource/RightSource must each name one of the owning
// DerivationDefinition's Sources.
public class JoinCondition
{
    public string LeftSource { get; set; } = default!;
    public string LeftField { get; set; } = default!;
    public string RightSource { get; set; } = default!;
    public string RightField { get; set; } = default!;
}
