namespace EventStore.Interchange;

// ADR-072 -- opt-in, the same posture ADR-077's own FeatureFlags provider
// established: most deployments never need an HL7v2/MLLP listener at all,
// so this defaults OFF rather than binding a TCP port unconditionally.
public class Hl7V2MllpOptions
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 2575; // the conventional default MLLP port real integration engines (Mirth, Rhapsody) also default to
    public string AppId { get; set; } = default!; // which AppId every message received on this listener publishes against
}
