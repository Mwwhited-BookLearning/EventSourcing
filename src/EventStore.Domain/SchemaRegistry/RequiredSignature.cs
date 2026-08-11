namespace EventStore.Domain.SchemaRegistry;

// null on EventTypeDefinition.RequiredSignature = no sign-off required;
// set = publish must satisfy an RFC 9470 step-up challenge first (ADR-066).
public class RequiredSignature
{
    public List<string> AcrValues { get; set; } = new();
    public int? MaxAge { get; set; }

    // ADR-086 -- opt-in per event type, the same configuration surface
    // RequiredSignature itself already uses (never a global switch): true
    // means PublishService also obtains an RFC 3161 TimeStampToken and
    // stores it on the resulting Signature.RFC3161Timestamp. Not every
    // Signature-requiring event type needs third-party-verifiable timing.
    public bool EnableRfc3161Timestamp { get; set; }
}
