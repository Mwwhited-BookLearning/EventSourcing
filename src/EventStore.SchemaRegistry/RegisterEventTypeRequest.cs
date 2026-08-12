namespace EventStore.SchemaRegistry;

// Request shape per docs/features/schema-registry.md's sequence diagram,
// extended with `requiredClaims` (ADR-050's generalized list -- accepted and
// format-validated per docs/08-build-plan.md's "Schema Registry" item, not
// yet enforced). `AppId` is an explicit, required request field -- the
// feature doc's own "normally resolved from the caller's
// `registry:admin:{appId}` scope" framing never actually landed even after
// "Auth + Orchestration" and "Multi-Tenancy" both shipped; every RegisterAsync
// caller still supplies it explicitly, unchanged.
public record RegisterEventTypeRequest(
    string AppId,
    string JsonSchema,
    List<FilterableFieldRequest> FilterableFields,
    string ChangeKind,
    string? EntityIdField,
    string? ParentValidationMode,
    List<RequiredClaimRequest>? RequiredClaims,
    string? UpcastFromPrevious,
    string? DowncastToPrevious,
    string? EntityType = null,
    string? RejectionBehavior = null, // Annotate | Compensate -- ADR-035, "Non-Authoritative Capture"; null keeps EventTypeDefinition's own Annotate default
    // ADR-066 -- null (the default) means no sign-off required, completely
    // unaffected; set means a publish targeting this type must satisfy an
    // RFC 9470 step-up challenge first.
    RequiredSignatureRequest? RequiredSignature = null,
    // ADR-094 -- null (the default) means no tracked response expected,
    // completely unaffected; set means a ResponseEventType event carrying a
    // matching RespondsToEventId is expected within Within.
    ExpectedResponseRequest? ExpectedResponse = null);

public record FilterableFieldRequest(string JsonPath, string DataType, bool IsIndexed);

public record RequiredClaimRequest(string Direction, string Claim);

public record RequiredSignatureRequest(List<string> AcrValues, int? MaxAge, bool EnableRfc3161Timestamp = false);

public record ExpectedResponseRequest(string ResponseEventType, TimeSpan Within);
