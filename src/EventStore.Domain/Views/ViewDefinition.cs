namespace EventStore.Domain.Views;

// Shape is the data-model authority: docs/data/schema-registry.md's "Entity
// view definitions (ADR-039)" section. Follows the exact same content-
// addressed, versioned, hashed shape EventTypeDefinition already established
// for schemas -- a second application of that pattern, not a bespoke third
// shape. No AppId -- ViewDefinitions are shared across every application by
// EntityType name alone, per the data-model doc's own key (EntityType,
// Version, ViewKind), not (AppId, EntityType, Version, ViewKind).
public class ViewDefinition
{
    public string EntityType { get; set; } = default!;
    public int Version { get; set; }
    public ViewKind ViewKind { get; set; }
    public List<int> CompatibleSchemaVersions { get; set; } = [];
    public string TemplateContent { get; set; } = default!;
    public string Hash { get; set; } = default!;
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
}

public enum ViewKind { List, Detail, Edit, Custom }
