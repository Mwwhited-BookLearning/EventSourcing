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
    public string? UpcastFromPrevious { get; set; }
    public string? DowncastToPrevious { get; set; }
    public RejectionBehavior RejectionBehavior { get; set; } = RejectionBehavior.Annotate;
    public RequiredSignature? RequiredSignature { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }

    public List<FilterableField> FilterableFields { get; set; } = new();
}
