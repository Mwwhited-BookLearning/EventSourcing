namespace EventStore.SchemaRegistry;

// Duplicated from EventStore.Inbox.OriginIdOptions, not referenced --
// EventStore.Inbox depends on EventStore.SchemaRegistry (ADR-033/090), so a
// reference the other way would cycle (the same "duplication over
// reference" precedent AppendSchemaRegisteredAsync's own hardcoded "local"
// literal already established, now made a real, per-site-configurable
// value instead). Binds to the SAME "OriginId" configuration section every
// Host Program.cs already configures OriginIdOptions from (AppHost.cs's
// OriginId__OriginId env var) -- two independent types bound to one
// section resolve to the same real value, no shared assembly needed.
public class SchemaRegistryOriginIdOptions
{
    public const string Default = "local";
    public string OriginId { get; set; } = Default;
}
