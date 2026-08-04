namespace EventStore.Domain.SchemaRegistry;

// Shape is the data-model authority: docs/data/schema-registry.md.
// Composite key (AppId, Name, Version) per ADR-030.
public class EventTypeDefinition
{
    public string AppId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public int Version { get; set; }
    public string JsonSchema { get; set; } = default!;
    public DateTimeOffset RegisteredAt { get; set; }
    public bool IsActive { get; set; }
    public ParentValidationMode ParentValidationMode { get; set; } = ParentValidationMode.Strict;
    public List<RequiredClaim> RequiredClaims { get; set; } = new();
    public ChangeKind ChangeKind { get; set; }
    public string EntityIdField { get; set; } = default!;
    // The logical entity this event type patches (ADR-021's EntityId format
    // is {appId}:{entityType}:{uniqueId}) -- distinct from Name/EventType:
    // OrderPlaced and OrderShipped are two different event types that must
    // still resolve to the SAME EntityType ("Order") to fold into one Entity
    // Store row. Defaults to this type's own normalized Name when not given
    // explicitly at registration -- correct for the common single-event-
    // type-per-entity case; a real registration-time choice, not a "no safe
    // default" field like EntityIdField/ChangeKind, since the default IS
    // actually safe here (every event type trivially patches "itself" unless
    // told otherwise).
    public string EntityType { get; set; } = default!;
    public string? UpcastFromPrevious { get; set; }
    public string? DowncastToPrevious { get; set; }
    public RejectionBehavior RejectionBehavior { get; set; } = RejectionBehavior.Annotate;
    public RequiredSignature? RequiredSignature { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }

    public List<FilterableField> FilterableFields { get; set; } = new();
}
