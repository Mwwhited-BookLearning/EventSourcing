namespace EventStore.Domain.EventLog;

// Set only when EventTypeDefinition.RequiredSignature is configured (ADR-066).
public class Signature
{
    public string SignerId { get; set; } = default!;
    public DateTimeOffset SignedAt { get; set; }
    public string Meaning { get; set; } = default!;
    public string Acr { get; set; } = default!;
    public byte[]? RFC3161Timestamp { get; set; }
}
