namespace EventStore.Domain.SchemaRegistry;

// "type:value" format, ADR-008. Multiple entries for the same Direction
// are OR-matched (ADR-050) -- holding any one satisfies the gate.
public class RequiredClaim
{
    public ClaimDirection Direction { get; set; }
    public string Claim { get; set; } = default!;
}

public enum ClaimDirection { Publish, Read }
