namespace EventStore.SchemaRegistry;

// The full registration content a receiving peer needs to fold a
// SchemaRegistered notification into its OWN EventTypeDefinitions table
// (docs/10-open-questions.md row 1, resolved this pass) -- everything
// AppendSchemaRegisteredAsync's own payload used to omit ({EventTypeName,
// Version} only). Mirrors EventTypeDefinition's own replicable fields, not
// RegisterEventTypeRequest's raw/unvalidated request shape -- the origin
// site already validated and resolved every value (parsed enums, a
// concrete Version number, built FilterableField rows); a receiving site
// applies this AS-IS via SchemaRegistryService.ApplyReplicatedRegistrationAsync,
// never re-validating or re-deriving anything from it. `EventTypeName`
// (not `Name`) matches SchemaRegisteredEventType's own already-registered
// schema property name exactly -- its EntityIdField ("$.EventTypeName")
// depends on this exact JSON key, unchanged since before this item.
public record ReplicatedSchemaRegistration(
    string AppId,
    string EventTypeName,
    int Version,
    string JsonSchema,
    DateTimeOffset RegisteredAt,
    string ParentValidationMode,
    string ChangeKind,
    string EntityIdField,
    string EntityType,
    string? UpcastFromPrevious,
    string? DowncastToPrevious,
    string RejectionBehavior,
    List<ReplicatedRequiredClaim> RequiredClaims,
    List<ReplicatedFilterableField> FilterableFields,
    ReplicatedRequiredSignature? RequiredSignature,
    ReplicatedExpectedResponse? ExpectedResponse);

public record ReplicatedRequiredClaim(string Direction, string Claim);

public record ReplicatedFilterableField(
    string JsonPath, string DataType, bool IsIndexed, string IndexKind, ReplicatedSearchableIndexConfig? SearchableConfig);

// ADR-096/097's own encrypted-search field shape -- replicated too, not
// just the plaintext-index fields, since skipping it would just leave a
// narrower version of the exact gap this item closes (a schema that
// "sort of" matches across peers).
public record ReplicatedSearchableIndexConfig(
    string IndexKind, string KeyScope, List<string>? BucketGranularities, string? Cardinality, bool AcknowledgeLeakageRisk);

public record ReplicatedRequiredSignature(List<string> AcrValues, int? MaxAge, bool EnableRfc3161Timestamp);

public record ReplicatedExpectedResponse(string ResponseEventType, TimeSpan Within);
