namespace EventStore.Domain.SchemaRegistry;

// null on EventTypeDefinition.RequiredSignature = no sign-off required;
// set = publish must satisfy an RFC 9470 step-up challenge first (ADR-066).
public class RequiredSignature
{
    public List<string> AcrValues { get; set; } = new();
    public int? MaxAge { get; set; }
}
