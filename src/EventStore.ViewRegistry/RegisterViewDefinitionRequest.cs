namespace EventStore.ViewRegistry;

// Mirrors EventStore.SchemaRegistry.RegisterEventTypeRequest's shape --
// ADR-039's own "exact same content-addressed, versioned, hashed shape" as
// EventTypeDefinition, applied to view templates instead of schemas. No
// AppId (docs/data/schema-registry.md's own key for ViewDefinition has none
// -- shared across every application by EntityType name alone).
public record RegisterViewDefinitionRequest(
    string EntityType,
    string ViewKind, // List | Detail | Edit | Custom
    List<int> CompatibleSchemaVersions,
    string TemplateContent);
