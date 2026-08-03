namespace EventStore.SchemaRegistry;

// Request shape per docs/features/schema-registry.md's sequence diagram,
// extended with `requiredClaims` (ADR-050's generalized list -- accepted and
// format-validated per docs/08-build-plan.md's "Schema Registry" item, not
// yet enforced). `AppId` is a temporary, explicit request field: the feature
// doc says it's normally resolved from the caller's `registry:admin:{appId}`
// scope, but "Auth + Orchestration" (which adds that scope check) hasn't
// landed yet -- 08-build-plan.md's own text says to "accept requests
// unauthenticated for now." This field is removed once that item lands.
public record RegisterEventTypeRequest(
    string AppId,
    string JsonSchema,
    List<FilterableFieldRequest> FilterableFields,
    string ChangeKind,
    string? EntityIdField,
    string? ParentValidationMode,
    List<RequiredClaimRequest>? RequiredClaims,
    string? UpcastFromPrevious,
    string? DowncastToPrevious);

public record FilterableFieldRequest(string JsonPath, string DataType, bool IsIndexed);

public record RequiredClaimRequest(string Direction, string Claim);
